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
        public string messageId { get; set; }
        //public List<MessageTargets> messageTargets { get; set; }
        //public string transcriptChecksum { get; set; }
        public string vectorType { get; set; }
        public string vectorText { get; set; }
        public int chunkSequence { get; set; }
        public float[]? vector { get; set; }
    }

    //public class MessageTargets
    //{
    //    public string messageId { get; set; }
    //    public List<string> targetIds { get; set; }
    //}
}
