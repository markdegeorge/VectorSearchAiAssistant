using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Vectorize.Services;
using Vectorize.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.IO;
using System.Security.Cryptography;
using System.Text;
using Vectorize.Utilities;
using System.Security.Cryptography.X509Certificates;

namespace Vectorize
{
    public class FireflyMessageChangeFeed
    {

        private readonly OpenAiService _openAI;
        private readonly MongoDbService _mongo;
        // For demo purposes, create a list of valid Targets that we will process and search against
        // This will eventually become a query against FF groups that we want to search
        //                                                        "IPI 2022",                             "AI-Training"                           "Training" - For test only!             "Tech Internal" - For test only!
        private List<string> _validTargetIds = new List<string> { "fbe19f9d-b724-4b90-9fdb-6e3f1c5f385b", "2f140643-c423-4e02-b97b-1f5595f70ae1", "d604573c-1a66-4532-8ef7-366211a5b6b6", "df421bad-ab41-44d5-a7ac-5e3095b3919f" };

        public FireflyMessageChangeFeed(OpenAiService openAi, MongoDbService mongoDb) 
        { 
            _mongo = mongoDb;
            _openAI = openAi;
        
        }

        [FunctionName("FireflyMessageChangeFeed")]
        public async Task Run(
            [CosmosDBTrigger(
                databaseName: "fireflytestDB",
                containerName: "message",
                StartFromBeginning = true,
                Connection = "FireflyDBConnection",
                LeaseContainerName = "leases",
                CreateLeaseContainerIfNotExists = true)]IReadOnlyList<FireflyMessage> input,
            ILogger logger)
        {

            if (input != null && input.Count > 0)
            {
                logger.LogInformation("Processing " + input.Count + " Firefly Message(s)");

                foreach (FireflyMessage item in input)
                {
                    // Make sure that the message has a transcript
                    if (item.Transcript == null || item.Transcript == string.Empty)
                    {
                        logger.LogInformation("Skipping message with no transcript. Message Id: " + item.Id);
                        continue;
                    }

                    // Make sure that the message has at least one Target within the valid targets list
                    if (item.Targets == null || item.Targets.Count() == 0 || !item.Targets.Any(x => x.Id != null && _validTargetIds.Contains(x.Id)))
                    {
                        logger.LogInformation("Skipping message with no valid target. Message Id: " + item.Id);
                        continue;
                    }

                    await GenerateTranscriptVector(item, logger);
                }
                
            }
        }

        public async Task GenerateTranscriptVector(FireflyMessage message, ILogger logger)
        {

            try
            {
                // Get the transcript checksum
                var transcriptChecksum = Checksum.GetChecksum(message.Transcript);

                // Check the database to see if there is a matching transcript checksum (_id)
                var existingMessageData = await _mongo.GetMessageData(transcriptChecksum, logger);

                // If there is a matching transcript checksum, then we need to check the message Ids to see if they match
                if (existingMessageData != null)
                {
                    // If the message Ids match, then we can skip this message
                    if (existingMessageData.GetValue("messageId").AsString == message.Id)
                    {
                        logger.LogInformation("Skipping message with matching transcript checksum and message Id. Message Id: " + message.Id);
                        return;
                    }
                    else
                    {
                        // Check to see if this messageId is already in the messageTargets array
                        var existingMessageTargets = existingMessageData.GetValue("messageTargets").AsBsonArray;
                        foreach (var existingMessageTarget in existingMessageTargets)
                        {
                            if (existingMessageTarget["messageId"].AsString == message.Id)
                            {
                                logger.LogInformation("Skipping message with matching transcript checksum and message Id and in Targets. Message Id: " + message.Id);
                                return;
                            }
                        }

                        // TODO: We need to process the targets to see if they are different and update the message data and vectors accordingly
                        logger.LogWarning("Warning: Found message with matching transcript checksum and different message Id. Message Id: " + message.Id);

                        // For now, update the existingMessageData to include the new messageTargets so we can see which messages need further processing
                        var newTargetIds = message.Targets.Where(x => x.Id != null && _validTargetIds.Contains(x.Id)).Select(x => x.Id).ToList();
                        var newMessageTarget = new BsonDocument
                        {
                            { "messageId", message.Id },
                            { "targetIds", new BsonArray(newTargetIds) }
                        };

                        existingMessageData["messageTargets"].AsBsonArray.Add(newMessageTarget);
                        await _mongo.UpsertMessageData(existingMessageData, logger);

                        return;
                    }
                }

                // Create the message data object
                var messageData = new FireflyMessageData
                {
                    id = transcriptChecksum,
                    messageId = message.Id,
                };

                // Retrieve the valid targets from the document
                var targetIds = message.Targets.Where(x => x.Id != null && _validTargetIds.Contains(x.Id)).Select(x => x.Id).ToList();
                messageData.messageTargets = new List<MessageTargets>()
                {
                    new MessageTargets
                    {
                        messageId = message.Id, targetIds = targetIds
                    }
                };

                //
                // Save off the message data to Mongo
                //
                BsonDocument dataDocument = messageData.ToBsonDocument();
                await _mongo.UpsertMessageData(dataDocument, logger);

                // Create the message vector object
                var vector = new FireflyMessageVector
                {
                    id = message.Id,
                    messageId = message.Id,
                }; 

                // Delete any existing vectors for this message id if they exist
                await _mongo.DeleteMessageVectors(message.Id, targetIds, logger);

                // TODO: Something to play with is tokenizing the subjects separately from the transcript and see the results
                // First we process the messages subject if it exists
                //if (message.Subject != null && message.Subject != string.Empty)
                //{
                //    vector.vectorType = nameof(message.Subject);
                //    vector.vectorText = message.Subject;
                //    vector.chunkSequence = 0;
                //    vector.id = $"{message.Id}-{vector.chunkSequence}";

                //    // TODO: Remove after testing
                //    // Create a default vector float array for the subject
                //    //vector.vector = new float[1536];

                //    //Get the embeddings from OpenAI
                //    vector.vector = await _openAI.GetEmbeddingsAsync($"{{video subject: \"{vector.vectorText}\" }}", logger);

                //    //Save to Mongo
                //    BsonDocument document = vector.ToBsonDocument();
                //    await _mongo.InsertVector(document, targetIds, logger);

                //    logger.LogInformation("Saved Subject vector for Firefly Message Id: " + message.Id);
                //}

                // Now we process the message transcript and chunk if necessary
                vector.vectorType = nameof(message.Transcript);

                // Loop through all of the chunks generated from the transcript
                foreach (var chunk in ChunkTranscript(message.Transcript))
                {
                    // TODO: Something to play with is tokenizing the subjects separately from the transcript and see the results
                    // Create a new object with only the message.Subject and message.Transcript properties for the OpenAI request
                    var newObject = new
                    {
                        subject = message.Subject,
                        transcript = chunk
                    };

                    vector.vectorText = JObject.FromObject(newObject).ToString();
                    vector.chunkSequence++;
                    vector.id = $"{message.Id}-{vector.chunkSequence}";

                    // TODO: Remove after testing
                    // Create a default vector float array for the subject
                    //vector.vector = new float[1536];

                    //Get the embeddings from OpenAI
                    vector.vector = await _openAI.GetEmbeddingsAsync(vector.vectorText, logger);
                    //vector.vector = await _openAI.GetEmbeddingsAsync($"{{ video transcript: \"{vector.vectorText}\" }}", logger);

                    //Save to Mongo
                    BsonDocument document = vector.ToBsonDocument();
                    await _mongo.InsertVector(document, targetIds, logger);
                }

                logger.LogInformation($"Saved {vector.chunkSequence} Transcript vector(s) for Firefly Message Id: " + message.Id);
            }
            catch (Exception x)
            {
                logger.LogError($"Exception while generating vector for message Id [{message.Id}]: " + x.Message);
            }

        }

        public List<string> ChunkTranscript(string transcript, int threshold = 1023) // threshold = azure limit / 2
        {
            var chunks = new List<string>();

            // Check if we even need to chunk the transcript
            if (transcript.Length <= threshold)
            {
                chunks.Add(transcript);
                return chunks;
            }
            
            var sentences = transcript.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries); // Splitting by period for simplicity
            var currentChunk = new List<string>();
            var wordCount = 0;

            foreach (var sentence in sentences)
            {
                var words = sentence.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); // Split sentence into words
                
                foreach (var word in words)
                {
                    currentChunk.Add(word);
                    wordCount++;

                    if (wordCount >= threshold && word == words.Last())
                    {
                        chunks.Add(string.Join(" ", currentChunk));
                        currentChunk.Clear();
                        wordCount = 0;
                    }
                }
            }

            if (currentChunk.Any()) // Any words left in the last chunk
            {
                chunks.Add(string.Join(" ", currentChunk));
            }

            return chunks;
        }
    }
}


