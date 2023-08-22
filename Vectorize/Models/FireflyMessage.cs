using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using Newtonsoft.Json;

namespace Vectorize.Models
{
    
    public class FireflyMessage
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string From { get; set; }
        public string Type { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime SentDate { get; set; }
        public string Subject { get; set; }
        public string RecordingType { get; set; }
        public string RecordingDuration { get; set; }
        public string Status { get; set; }
        public string IncomingLocation { get; set; }
        public string AssetName { get; set; }
        public string StreamingUrl { get; set; }
        public string DownloadUrl { get; set; }
        public string Transcript { get; set; }
        public string Summary { get; set; }
        public string ThumbnailUrl { get; set; }
        public Target[] Targets { get; set; } = Array.Empty<Target>();
        public Viewer[] Viewers { get; set; } = Array.Empty<Viewer>();
        public string ParentId { get; set; }
        public bool AIEnabled { get; set; }
        public bool Is4K { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class Viewer {
        public string Type { get; set; }
        public string Name { get; set; }
        public string UserId { get; set; }
        public bool HasViewed { get; set; }
        public DateTime DateViewed { get; set; }
    }
    public class Target
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }
        public string WorkItemType { get; set; }
        public string AssignToUser { get; set; }

    }
}
