using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Vectorize.Services
{
    public class MongoDbService
    {
        private readonly MongoClient? _client;
        private readonly IMongoDatabase? _database;
        private readonly IMongoCollection<BsonDocument> _vectorDataCollection;
        private readonly string? _vectorSearchCollectionPrefix;
        private readonly string _vectorFieldName = "vector";
        private readonly ILogger _logger;

        private List<string> _vectorSearchCollectionNames = new List<string>();

        public MongoDbService(string connection, string databaseName, string vectorDataCollectionName, string vectorSearchCollectionPrefix, ILogger logger)
        {
            _logger = logger;

            try
            {
                if (string.IsNullOrEmpty(connection))
                {
                    throw new ArgumentNullException(nameof(connection));
                }
                if (string.IsNullOrEmpty(databaseName))
                {
                    throw new ArgumentNullException(nameof(databaseName));
                }
                if (string.IsNullOrEmpty(vectorDataCollectionName))
                {
                    throw new ArgumentNullException(nameof(vectorDataCollectionName));
                }
                if (string.IsNullOrEmpty(vectorSearchCollectionPrefix))
                {
                    throw new ArgumentNullException(nameof(vectorSearchCollectionPrefix));
                }

                _client = new MongoClient(connection);
                _database = _client.GetDatabase(databaseName);
                _vectorDataCollection = _database.GetCollection<BsonDocument>(vectorDataCollectionName);

                string vectorIndexName = "vectorSearchIndex";
                _vectorSearchCollectionPrefix = vectorSearchCollectionPrefix;

                // Get a list of search collections that exist in the database
                // so we can assure that the vector search index exisis in each
                using (IAsyncCursor<BsonDocument> cursor = _database.ListCollections())
                {
                    var collectionNames = cursor.ToList().Where(x => x["name"].AsString.StartsWith(_vectorSearchCollectionPrefix)).Select(x => x["name"].AsString);
                    
                    logger.LogInformation($"Processing {collectionNames.Count()} collection(s) to check if vector search index exists.");

                    if (collectionNames != null)
                    {
                        foreach (var name in collectionNames)
                        {
                            _vectorSearchCollectionNames.Add(name);
                            logger.LogInformation($"Processing {name} collection to check if vector search index exists.");

                            //Find if vector index exists
                            using (IAsyncCursor<BsonDocument> indexCursor = _database.GetCollection<BsonDocument>(name).Indexes.List())
                            {
                                bool vectorIndexExists = indexCursor.ToList().Any(x => x["name"] == vectorIndexName);
                                if (!vectorIndexExists)
                                {
                                    logger.LogInformation($"Adding vector search index to collection {name}.");
                                    var command = BuildVectorSearchIndexCommand(name, _vectorFieldName);
                                    BsonDocument result = _database.RunCommand(command);
                                    if (result["ok"] != 1)
                                    {
                                        _logger.LogError("CreateIndex failed with response: " + result.ToJson());
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("MongoDbService Init failure: " + ex.Message);
            }
        }

        public async Task UpsertMessageData(BsonDocument document, ILogger logger)
        {

            if (!document.Contains("_id"))
            {
                logger.LogError("Document does not contain _id.");
                throw new ArgumentException("Document does not contain _id.");
            }

            var _idValue = document.GetValue("_id").AsString;

            try
            {
                var filter = Builders<BsonDocument>.Filter.Eq("_id", _idValue);
                var options = new ReplaceOptions { IsUpsert = true };
                await _vectorDataCollection.ReplaceOneAsync(filter, document, options);

                logger.LogInformation("Inserted new message into vector data collection");
            }
            catch (Exception ex)
            {
                //TODO: fix the logger. Output does not show up anywhere
                logger.LogError($"Exception: InsertMessageData(): {ex.Message}");
                throw;
            }

        }

        public async Task<BsonDocument> GetMessageData(string id, ILogger logger)
        {
            try
            {
                var filter = Builders<BsonDocument>.Filter.Eq("_id", id);
                var data = await _vectorDataCollection.FindAsync(filter);

                return data.FirstOrDefault();
            }
            catch (MongoException ex)
            {
                logger.LogError($"Exception: GetMessageData(): {ex.Message}");
                throw;

            }
        }

        public async Task InsertVector(BsonDocument document, List<string> targetIds, ILogger logger)
        {
            if (!document.Contains("_id"))
            {
                logger.LogError("InsertFireflyVector: Document does not contain _id field.");
                throw new ArgumentException("Document does not contain _id field.");
            }

            if (!document.Contains("messageId"))
            {
                logger.LogError("InsertFireflyVector: Document does not contain messageId field.");
                throw new ArgumentException("Document does not contain messageId field.");
            }

            if (!document.Contains("chunkSequence"))
            {
                logger.LogError("InsertFireflyVector: Document does not contain chunkSequence field.");
                throw new ArgumentException("Document does not contain chunkSequence field.");
            }

            try
            {
                var idValue = document.GetValue("_id").ToString();
                var messageIdValue = document.GetValue("messageId").ToString();
                //var messageTargets = document.GetValue("messageTargets").AsBsonArray;

                // Loop through and get the current message targets and build the collection names 
                //BsonArray? targets = null;
                //foreach (BsonDocument messageTarget in messageTargets)
                //{
                //    if (messageTarget.GetValue("messageId").AsString == messageIdValue)
                //    {
                //        targets = messageTarget.GetValue("targetIds").AsBsonArray;
                //    }
                //}

                if (targetIds == null || targetIds.Count == 0)
                {
                    logger.LogError($"InsertFireflyVector: Target ids are required for the current message id: {idValue}.");
                    throw new ArgumentException($"InsertFireflyVector: Target ids are required for the current message id: {idValue}.");
                }

                foreach (var targetId in targetIds)
                {
                    var collectionName = GetCollectionName(targetId);

                    // Get the collection
                    var collection = _database.GetCollection<BsonDocument>(collectionName);

                    // If the collection does not exist, create it and index it
                    if (!_vectorSearchCollectionNames.Contains(collectionName))
                    {
                        _vectorSearchCollectionNames.Add(collectionName);
                        logger.LogInformation($"Creating new collection: {collectionName} in MongoDB");
                        await _database.CreateCollectionAsync(collectionName);

                        collection = _database.GetCollection<BsonDocument>(collectionName);

                        // Create the index that will be used for vector search
                        var command = BuildVectorSearchIndexCommand(collectionName, _vectorFieldName);
                        BsonDocument result = await _database.RunCommandAsync(command);
                        if (result["ok"] != 1)
                        {
                            _logger.LogError($"CreateIndex failed for collection {collectionName} with response: {result.ToJson()}");
                            throw new ApplicationException($"CreateIndex failed for collection {collectionName} with response: {result.ToJson()}");
                        }
                    }

                    var filter = Builders<BsonDocument>.Filter.Eq("_id", idValue);
                    var options = new ReplaceOptions { IsUpsert = true };
                    await collection.ReplaceOneAsync(filter, document, options);

                    logger.LogInformation($"Inserted new Firefly vector into collection: {collectionName} of MongoDB");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Exception: InsertFireflyVector(): {ex.Message}");
                throw;
            }
        }

        public async Task DeleteVector(string id, List<string> targetIds, ILogger logger)
        {
            try
            {
                var filter = Builders<BsonDocument>.Filter.Eq("_id", id);

                foreach (var targetId in targetIds)
                {
                    var collectionName = GetCollectionName(targetId);

                    // Get the collection
                    var collection = _database.GetCollection<BsonDocument>(collectionName);

                    // If the collection exists, delete the document
                    if (_vectorSearchCollectionNames.Contains(collectionName))
                    {
                        await collection.DeleteOneAsync(filter);
                        logger.LogInformation($"Deleted vector with Id: {id} in collection: {collectionName}");
                    }
                }
            }
            catch (MongoException ex) 
            {
                logger.LogError($"Exception: DeleteFireflyVector(): {ex.Message}");
                throw;

            }
        }

        public async Task DeleteMessageVectors(string messageId, List<string> targetIds, ILogger logger)
        {

            try
            {
                var filter = Builders<BsonDocument>.Filter.Eq("messageId", messageId);

                foreach (var targetId in targetIds)
                {
                    var collectionName = GetCollectionName(targetId);

                    // Get the collection
                    var collection = _database.GetCollection<BsonDocument>(collectionName);

                    // If the collection exists, delete the document
                    if (_vectorSearchCollectionNames.Contains(collectionName))
                    {
                        await collection.DeleteOneAsync(filter);
                        logger.LogInformation($"Deleted vector(s) with message Id: {messageId} in collection: {collectionName}");
                    }
                }

            }
            catch (MongoException ex) 
            {
                logger.LogError($"Exception: DeleteFireflyMessageVector(): {ex.Message}");
                throw;

            }
        }


        
        //public async Task InsertVector(BsonDocument document, ILogger logger)
        //{

        //    if (!document.Contains("_id"))
        //    {
        //        logger.LogError("Document does not contain _id.");
        //        throw new ArgumentException("Document does not contain _id.");
        //    }

        //    string? _idValue = document.GetValue("_id").ToString();

        //    try
        //    {
        //        var filter = Builders<BsonDocument>.Filter.Eq("_id", _idValue);
        //        var options = new ReplaceOptions { IsUpsert = true };
        //        var collection = _database.GetCollection<BsonDocument>("vectors");
        //        await collection.ReplaceOneAsync(filter, document, options);

        //        logger.LogInformation("Inserted new vector into MongoDB");
        //    }
        //    catch (Exception ex)
        //    {
        //        //TODO: fix the logger. Output does not show up anywhere
        //        logger.LogError($"Exception: InsertVector(): {ex.Message}");
        //        throw;
        //    }

        //}

        //public async Task DeleteVector(string categoryId, string id, ILogger logger)
        //{

        //    try
        //    {

        //        var filter = Builders<BsonDocument>.Filter.And(
        //            Builders<BsonDocument>.Filter.Eq("categoryId", categoryId),
        //            Builders<BsonDocument>.Filter.Eq("_id", id));

        //        var collection = _database.GetCollection<BsonDocument>("vectors");
        //        await collection.DeleteOneAsync(filter);

        //    }
        //    catch (MongoException ex) 
        //    {
        //        logger.LogError($"Exception: DeleteVector(): {ex.Message}");
        //        throw;

        //    }
        //}

        private string GetCollectionName(string targetId)
        {
            return $"{_vectorSearchCollectionPrefix}-{targetId}";
        }

        // Build the vector search index command 
        private BsonDocumentCommand<BsonDocument> BuildVectorSearchIndexCommand(string collectionName, string vectorFieldName)
        {
            var command = new BsonDocumentCommand<BsonDocument>(
                new BsonDocument
                {
                    { "createIndexes", collectionName },
                    { "indexes", new BsonArray
                        {
                            new BsonDocument
                            {
                                { "name", "vectorSearchIndex" },
                                { "key", new BsonDocument { { vectorFieldName, "cosmosSearch" } } },
                                { "cosmosSearchOptions", new BsonDocument
                                    {
                                        { "kind", "vector-ivf" },
                                        { "numLists", 5 },
                                        { "similarity", "COS" },
                                        { "dimensions", 1536 }
                                    }
                                }
                            }
                        }
                    }
                }
            );

            return command;
        }

    }
}
