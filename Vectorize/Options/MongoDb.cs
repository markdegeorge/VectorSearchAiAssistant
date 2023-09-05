using Microsoft.Extensions.Logging;

namespace Vectorize.Options
{
    public record MongoDb
    {
        public string? Connection { get; set; }
        public string? DatabaseName { get; set; }
        public string? VectorDataCollectionName { get; set; }
        public string? VectorSearchCollectionPrefix { get; set; }

        public ILogger? Logger { get; set; }

    }
}
