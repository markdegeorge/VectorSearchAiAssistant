using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using Newtonsoft.Json;

namespace Vectorize.Models
{
    
    public class FireflyMessageData
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string id { get; set; }
        public string messageId { get; set; }
        public List<MessageTargets> messageTargets { get; set; }
    }

    public class MessageTargets
    {
        public string messageId { get; set; }
        public List<MessageTarget> targets { get; set; }
    }

    public class MessageTarget
    {
        public string id { get; set; }
        public string name { get; set; }
    }
}
