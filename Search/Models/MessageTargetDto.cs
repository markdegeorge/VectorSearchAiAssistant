using MongoDB.Bson;

namespace Search.Models
{
    public class MessageTargetDto
    {
        public BsonValue Id { get; set; } = string.Empty;
        public BsonValue Name { get; set; } = string.Empty;
    }
}
