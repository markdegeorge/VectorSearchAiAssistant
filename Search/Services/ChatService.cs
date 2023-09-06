﻿using Azure.AI.OpenAI;
using Search.Constants;
using Search.Models;

namespace Search.Services;

public class ChatService
{
    /// <summary>
    /// All data is cached in the _sessions List object.
    /// </summary>
    /// 

    public string SelectedUser { get; set; } 
    
    private static List<Session> _sessions = new();
    private static List<MessageTarget> _availableMessageTargets = new();

    private readonly CosmosDbService _cosmosDbService;
    private readonly OpenAiService _openAiService;
    private readonly MongoDbService _mongoDbService;
    private readonly int _maxConversationBytes;

    public ChatService(CosmosDbService cosmosDbService, OpenAiService openAiService, MongoDbService mongoDbService)
    {
        _cosmosDbService = cosmosDbService;
        _openAiService = openAiService;
        _mongoDbService = mongoDbService;

        _maxConversationBytes = openAiService.MaxConversationBytes;

    }

    /// <summary>
    /// Returns list of target ids and names for left-hand nav to bind to
    /// </summary>
    public async Task<List<MessageTarget>> GetAvailableTargetsAsync()
    {
        return _availableMessageTargets = await _mongoDbService.GetVectorSearchTargetsAsync();
    }

    /// <summary>
    /// Returns list of chat session ids and names for left-hand nav to bind to (display Name and ChatSessionId as hidden)
    /// </summary>
    public async Task<List<Session>> GetAllChatSessionsAsync()
    {
        return _sessions = await _cosmosDbService.GetSessionsAsync(SelectedUser);
    }

    /// <summary>
    /// Returns the chat messages to display on the main web page when the user selects a chat from the left-hand nav
    /// </summary>
    public async Task<List<Message>> GetChatSessionMessagesAsync(string? sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        List<Message> chatMessages = new();

        if (_sessions.Count == 0)
        {
            return Enumerable.Empty<Message>().ToList();
        }

        int index = _sessions.FindIndex(s => s.SessionId == sessionId);

        if (_sessions[index].Messages.Count == 0)
        {
            // Messages are not cached, go read from database
            chatMessages = await _cosmosDbService.GetSessionMessagesAsync(sessionId);

            // Cache results
            _sessions[index].Messages = chatMessages;
        }
        else
        {
            // Load from cache
            chatMessages = _sessions[index].Messages;
        }

        return chatMessages;
    }

    /// <summary>
    /// User creates a new Chat Session.
    /// </summary>
    public async Task CreateNewChatSessionAsync(string userId)
    {
        Session session = new(userId);

        _sessions.Add(session);

        await _cosmosDbService.InsertSessionAsync(session);

    }

    /// <summary>
    /// Rename the Chat Ssssion from "New Chat" to the summary provided by OpenAI
    /// </summary>
    public async Task RenameChatSessionAsync(string? sessionId, string newChatSessionName)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        int index = _sessions.FindIndex(s => s.SessionId == sessionId);

        _sessions[index].Name = newChatSessionName;

        await _cosmosDbService.UpdateSessionAsync(_sessions[index]);
    }

    /// <summary>
    /// Rename the Chat Ssssion from "New Chat" to the summary provided by OpenAI
    /// </summary>
    public async Task UpdateChatSessionTargetGroupAsync(string? sessionId, string targetGroupId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        int index = _sessions.FindIndex(s => s.SessionId == sessionId);

        _sessions[index].TargetGroupId = targetGroupId;

        await _cosmosDbService.UpdateSessionAsync(_sessions[index]);
    }

    /// <summary>
    /// Delete user prompts in the chat session message list object and also delete in the data service.
    /// </summary>
    public async Task DeleteChatMessagesAsync(string sessionId)
    {
        int index = _sessions.FindIndex(s => s.SessionId == sessionId);

        _sessions[index].DeleteMessages();

        await _cosmosDbService.DeleteMessagesAsync(sessionId);
    }

    /// <summary>
    /// User deletes a chat session
    /// </summary>
    public async Task DeleteChatSessionAsync(string? sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        int index = _sessions.FindIndex(s => s.SessionId == sessionId);

        _sessions.RemoveAt(index);

        await _cosmosDbService.DeleteSessionAndMessagesAsync(sessionId);
    }

    /// <summary>
    /// Receive a prompt from a user, Vectorize it from _openAIService Get a completion from _openAiService
    /// </summary>
    public async Task<string> ChatCompletionAsync(string? sessionId, string userPrompt, string? targetGroupId, string? systemPrompt, float? temperture)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(targetGroupId);

        //Retrieve conversation, including latest prompt.
        //If you put this after the vector search it doesn't take advantage of previous information given so harder to chain prompts together.
        //However if you put this before the vector search it can get stuck on previous answers and not pull additional information. Worth experimenting
        //string conversation = GetChatSessionConversation(sessionId, userPrompt);


        //Get embeddings for user prompt.
        (float[] promptVectors, int vectorTokens) = await _openAiService.GetEmbeddingsAsync(sessionId, userPrompt);



        //Do vector search on prompt embeddings, return list of documents
        string retrievedDocuments = await _mongoDbService.VectorSearchAsync(targetGroupId, promptVectors);


        //Retrieve conversation, including latest prompt.
        string conversation = GetChatSessionConversation(sessionId, userPrompt);



        //Generate the completion to return to the user
        (string completion, int promptTokens, int responseTokens) = await _openAiService.GetChatCompletionAsync(sessionId, conversation, retrievedDocuments, systemPrompt, temperture);


        //Add to prompt and completion to cache, then persist in Cosmos as transaction 
        Message promptMessage = new Message(sessionId, nameof(Participants.User), promptTokens, userPrompt);
        Message completionMessage = new Message(sessionId, nameof(Participants.Assistant), responseTokens, completion);
        await AddPromptCompletionMessagesAsync(sessionId, promptMessage, completionMessage);


        return completion;
    }

    /// <summary>
    /// Get current conversation from newest to oldest up to max conversation tokens and add to the prompt
    /// </summary>
    private string GetChatSessionConversation(string sessionId, string userPrompt)
    {

        int? bytesUsed = 0;

        List<string> conversationBuilder = new List<string>();


        int index = _sessions.FindIndex(s => s.SessionId == sessionId);


        List<Message> messages = _sessions[index].Messages;

        //Start at the end of the list and work backwards
        for (int i = messages.Count - 1; i >= 0; i--)
        {

            bytesUsed += messages[i].Text.Length;

            if (bytesUsed > _maxConversationBytes)
                break;

            
            conversationBuilder.Add(messages[i].Text);

        }

        //Invert the chat messages to put back into chronological order and output as string.        
        string conversation = string.Join(Environment.NewLine, conversationBuilder.Reverse<string>());

        //Add the current userPrompt
        conversation += Environment.NewLine + userPrompt;

        return conversation;


    }

    public async Task<string> SummarizeChatSessionNameAsync(string? sessionId, string prompt)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        string response = await _openAiService.SummarizeAsync(sessionId, prompt);

        await RenameChatSessionAsync(sessionId, response);

        return response;
    }

    /// <summary>
    /// Add user prompt to the chat session message list object and insert into the data service.
    /// </summary>
    private async Task<Message> AddPromptMessageAsync(string sessionId, string promptText)
    {
        Message promptMessage = new(sessionId, nameof(Participants.User), default, promptText);

        int index = _sessions.FindIndex(s => s.SessionId == sessionId);

        _sessions[index].AddMessage(promptMessage);

        return await _cosmosDbService.InsertMessageAsync(promptMessage);
    }


    /// <summary>
    /// Add user prompt and AI assistance response to the chat session message list object and insert into the data service as a transaction.
    /// </summary>
    private async Task AddPromptCompletionMessagesAsync(string sessionId, Message promptMessage, Message completionMessage)
    {

        int index = _sessions.FindIndex(s => s.SessionId == sessionId);


        //Add prompt and completion to the cache
        _sessions[index].AddMessage(promptMessage);
        _sessions[index].AddMessage(completionMessage);


        //Update session cache with tokens used
        _sessions[index].TokensUsed += promptMessage.Tokens;
        _sessions[index].TokensUsed += completionMessage.Tokens;


        await _cosmosDbService.UpsertSessionBatchAsync(promptMessage, completionMessage, _sessions[index]);

    }

    /// <summary>
    /// Save settings for OpenAI
    /// </summary>
    public async Task SaveSettingsAsync(string? sessionId, string systemPrompt, float temperature)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        int index = _sessions.FindIndex(s => s.SessionId == sessionId);

        _sessions[index].AISystemPrompt = systemPrompt;
        _sessions[index].AITemperature = temperature;

        await _cosmosDbService.UpdateSessionAsync(_sessions[index]);
    }
}