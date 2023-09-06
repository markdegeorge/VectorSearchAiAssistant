using Newtonsoft.Json;

namespace Search.Models;

public record Session
{
    private const string _systemPrompt = @"
        You are an intelligent assistant for a video messaging system called Firefly. 
        You are designed to provide helpful answers to user questions about the information in the
        video transcripts provided in JSON format below.

        Instructions:
        - Only answer questions related to the information provided below,
        - Don't reference any external data not provided below.
        - If you're unsure of an answer, you can say ""I don't know"" or ""I'm not sure"" and recommend adjusting their search.

        Text of relevant information:";

    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; }

    public string Type { get; set; }

    public string UserId { get; set; }

    /// <summary>
    /// Partition key
    /// </summary>
    public string SessionId { get; set; }

    public string AISystemPrompt { get; set; }

    public float AITemperature { get; set; }

    public string TargetGroupId { get; set; }

    public int? TokensUsed { get; set; }

    public string Name { get; set; }

    [JsonIgnore]
    public List<Message> Messages { get; set; }

    public Session(string userId)
    {
        Id = Guid.NewGuid().ToString();
        Type = nameof(Session);
        UserId = userId;
        SessionId = Id;
        AISystemPrompt = _systemPrompt;
        AITemperature = 0.5f;
        TargetGroupId = string.Empty;
        TokensUsed = 0;
        Name = "New Chat";
        Messages = new List<Message>();
    }

    public void AddMessage(Message message)
    {
        Messages.Add(message);
    }

    public void UpdateMessage(Message message)
    {
        var match = Messages.Single(m => m.Id == message.Id);
        var index = Messages.IndexOf(match);
        Messages[index] = message;
    }
    public void DeleteMessages()
    {
        Messages.Clear();
    }
}