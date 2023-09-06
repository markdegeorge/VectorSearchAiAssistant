namespace Search.Services
{
    using MongoDB.Bson;
    using MongoDB.Driver;
    using Search.Models;
    using System.Collections;
    using System.Text.Json;

    /// <summary>
    /// Service to access Azure Cosmos DB for Mongo vCore.
    /// </summary>
    public class MongoDbService
    {
        private readonly MongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly IMongoCollection<BsonDocument> _vectorDataCollection;
        private readonly string _vectorSearchCollectionPrefix;
        private readonly int _maxVectorSearchResults = default;
        private readonly ILogger _logger;

        /// <summary>
        /// Creates a new instance of the service.
        /// </summary>
        /// <param name="endpoint">Endpoint URI.</param>
        /// <param name="key">Account key.</param>
        /// <param name="databaseName">Name of the database to access.</param>
        /// <param name="collectionName">Names of the collection with vectors.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint, key, databaseName, or containerNames is either null or empty.</exception>
        /// <remarks>
        /// This constructor will validate credentials and create a service client instance.
        /// </remarks>
        public MongoDbService(string connection, string databaseName, string vectorDataCollectionName, string vectorSearchCollectionPrefix, string maxVectorSearchResults, ILogger logger)
        {
            ArgumentException.ThrowIfNullOrEmpty(connection);
            ArgumentException.ThrowIfNullOrEmpty(databaseName);
            ArgumentException.ThrowIfNullOrEmpty(vectorDataCollectionName);
            ArgumentException.ThrowIfNullOrEmpty(vectorSearchCollectionPrefix);
            ArgumentException.ThrowIfNullOrEmpty(maxVectorSearchResults);

            _logger = logger;

            _client = new MongoClient(connection);
            _database = _client.GetDatabase(databaseName);
            _vectorDataCollection = _database.GetCollection<BsonDocument>(vectorDataCollectionName);
            _vectorSearchCollectionPrefix = vectorSearchCollectionPrefix;
            _maxVectorSearchResults = int.TryParse(maxVectorSearchResults, out _maxVectorSearchResults) ? _maxVectorSearchResults: 10;
        }

        public async Task<string> VectorSearchAsync(string targetGroupId, float[] embeddings)
        {
            ArgumentException.ThrowIfNullOrEmpty(targetGroupId);

            List<string> retDocs = new List<string>();

            var resultDocuments = string.Empty;

            try 
            {
                var collectionName = GetCollectionName(targetGroupId);

                // Get the collection
                var collection = _database.GetCollection<BsonDocument>(collectionName);

                if (collection == null)
                {
                    _logger.LogError($"Collection {collectionName} not found.");
                    return resultDocuments;
                }

                //Search Mongo vCore collection for similar embeddings
                //Project the fields that are needed
                BsonDocument[] pipeline = new BsonDocument[]
                {
                    BsonDocument.Parse($"{{$search: {{cosmosSearch: {{ vector: [{string.Join(',', embeddings)}], path: 'vector', k: {_maxVectorSearchResults}}}, returnStoredSource:true}}}}"),
                    BsonDocument.Parse($"{{$project: {{vectorType: 1, vectorText: 1}}}}"),
                };

                // Combine the results into a single string
                List<BsonDocument> result = await collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
                resultDocuments = string.Join(Environment.NewLine + "-", result.Select(x => x.ToJson()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
            
            return resultDocuments;
        }

        public async Task<List<MessageTarget>> GetVectorSearchTargetsAsync()
        {
            var targets = new List<MessageTarget>();

            try
            {
                var messageTargets = await _vectorDataCollection.Aggregate()
                    .Unwind("messageTargets")
                    .Unwind("messageTargets.targets")
                    .Group(new BsonDocument
                    {
                        { "_id", "$messageTargets.targets._id" },
                        { "name", new BsonDocument("$first", "$messageTargets.targets.name") }
                    })
                    .ToListAsync();

                targets = messageTargets.Where(t => t["_id"] != BsonNull.Value).Select(t => new MessageTarget
                {
                    Id = t["_id"].AsString,
                    Name = t["name"].AsString
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }

            return targets;
        }
        private string GetCollectionName(string targetId)
        {
            return $"{_vectorSearchCollectionPrefix}-{targetId}";
        }

    }
}