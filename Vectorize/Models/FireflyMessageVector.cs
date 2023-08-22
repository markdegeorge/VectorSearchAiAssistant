using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using Newtonsoft.Json;

namespace Vectorize.Models
{
    
    public class FireflyMessageVector
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string id { get; set; }
        public string userId { get; set; }
        public string vectorType { get; set; }
        public string vectorText { get; set; }
        public float[]? vector { get; set; }
    }
}
