using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Vectorize.Services;
using Vectorize.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.IO;

namespace Vectorize
{
    public class FireflyMessageChangeFeed
    {

        private readonly OpenAiService _openAI;
        private readonly MongoDbService _mongo;

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
                logger.LogInformation("Generating embeddings for " + input.Count + " Firefly Messages");

                foreach (FireflyMessage item in input)
                {
                    await GenerateTranscriptVector(item, logger);
                }
                
            }
        }

        public async Task GenerateTranscriptVector(FireflyMessage message, ILogger logger)
        {

            try
            {
                // First we process the messages subject
                var vector = new FireflyMessageVector
                {
                    id = message.Id,
                    userId = message.UserId,
                    vectorType = nameof(message.Subject),
                    vectorText = message.Subject
                };
                
                //Get the embeddings from OpenAI
                vector.vector = await _openAI.GetEmbeddingsAsync(vector.vectorText, logger);

                //Save to Mongo
                BsonDocument document = vector.ToBsonDocument();
                await _mongo.InsertVector(document, logger);

                // Now we process the message transcript and chunk if necessary
                vector.vectorType = nameof(message.Transcript);

                // Loop through all of the chunks generated from the transcript
                foreach (var chunk in ChunkTranscript(message.Transcript))
                {
                    vector.vectorText = chunk;

                    //Get the embeddings from OpenAI
                    vector.vector = await _openAI.GetEmbeddingsAsync(vector.vectorText, logger);

                    //Save to Mongo
                    document = vector.ToBsonDocument();
                    await _mongo.InsertVector(document, logger);
                }

                logger.LogInformation("Saved vector for Firefly Message: " + message.Subject);
            }
            catch (Exception x)
            {
                logger.LogError("Exception while generating vector for message subject [" + message.Subject + "]: " + x.Message);
            }

        }

        public List<string> ChunkTranscript(string transcript, int threshold = 1000)
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
