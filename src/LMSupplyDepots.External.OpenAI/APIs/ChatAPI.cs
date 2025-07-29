using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Diagnostics;
using System.Text;

namespace LMSupplyDepots.External.OpenAI.APIs;

/// <summary>
/// Manages chat operations with OpenAI
/// </summary>
public class ChatAPI
{
    private readonly ChatClient _chatClient;

    /// <summary>
    /// Initializes a new instance of the ChatAPI class
    /// </summary>
    public ChatAPI(OpenAIClient client, string model = "gpt-4o")
    {
        _chatClient = client.GetChatClient(model);
    }

    /// <summary>
    /// Sends a single message to the model and returns the response
    /// </summary>
    public async Task<string> SendMessageAsync(string message, string? systemPrompt = null)
    {
        Debug.WriteLine($"Sending message to OpenAI: \"{message}\"");

        try
        {
            // Create messages for the conversation
            var messages = new List<ChatMessage>();

            // Add system message if provided
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new SystemChatMessage(systemPrompt));
            }

            // Add user message
            messages.Add(new UserChatMessage(message));

            // Send the request
            ClientResult<ChatCompletion> response = await _chatClient.CompleteChatAsync(messages);

            // Extract the response text
            string responseText = response.Value.Content[0].Text;
            Debug.WriteLine($"Response received from OpenAI");

            return responseText;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error sending message: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Conducts a conversation with the model using a list of messages
    /// </summary>
    public async Task<string> SendConversationAsync(List<(string role, string content)> messages)
    {
        Debug.WriteLine($"Sending conversation with {messages.Count} messages to OpenAI");

        try
        {
            // Convert messages to ChatMessage list
            var chatMessages = ConvertToChatMessages(messages);

            // Send the request
            ClientResult<ChatCompletion> response = await _chatClient.CompleteChatAsync(chatMessages);

            // Extract the response text
            string responseText = response.Value.Content[0].Text;
            Debug.WriteLine($"Response received from OpenAI");

            return responseText;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error sending conversation: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Streams a chat response from the model
    /// </summary>
    public async Task<string> StreamMessageAsync(string message, string? systemPrompt = null, Action<string>? onUpdate = null)
    {
        Debug.WriteLine($"Streaming message to OpenAI: \"{message}\"");

        try
        {
            // Create messages for the conversation
            var messages = new List<ChatMessage>();

            // Add system message if provided
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new SystemChatMessage(systemPrompt));
            }

            // Add user message
            messages.Add(new UserChatMessage(message));

            // Send the request and process the streaming response
            return await ProcessStreamingResponseAsync(messages, onUpdate);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error streaming message: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Streams a conversation with the model using a list of messages
    /// </summary>
    public async Task<string> StreamConversationAsync(List<(string role, string content)> messages, Action<string>? onUpdate = null)
    {
        Debug.WriteLine($"Streaming conversation with {messages.Count} messages to OpenAI");

        try
        {
            // Convert messages to ChatMessage list
            var chatMessages = ConvertToChatMessages(messages);

            // Send the request and process the streaming response
            return await ProcessStreamingResponseAsync(chatMessages, onUpdate);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error streaming conversation: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Converts a list of message tuples to ChatMessage list
    /// </summary>
    private static List<ChatMessage> ConvertToChatMessages(List<(string role, string content)> messages)
    {
        var chatMessages = new List<ChatMessage>();

        foreach (var (role, content) in messages)
        {
            ChatMessage message = role.ToLower() switch
            {
                "system" => new SystemChatMessage(content),
                "assistant" => new AssistantChatMessage(content),
                _ => new UserChatMessage(content)
            };
            chatMessages.Add(message);
        }

        return chatMessages;
    }

    /// <summary>
    /// Processes a streaming response from the OpenAI API
    /// </summary>
    private async Task<string> ProcessStreamingResponseAsync(List<ChatMessage> messages, Action<string>? onUpdate)
    {
        // Create the streaming request
        AsyncCollectionResult<StreamingChatCompletionUpdate> streamingResponse = 
            _chatClient.CompleteChatStreamingAsync(messages);

        var fullResponse = new StringBuilder();

        // Process each chunk as it arrives
        await foreach (StreamingChatCompletionUpdate update in streamingResponse)
        {
            if (update.ContentUpdate.Count > 0)
            {
                string chunk = update.ContentUpdate[0].Text;
                if (!string.IsNullOrEmpty(chunk))
                {
                    fullResponse.Append(chunk);
                    onUpdate?.Invoke(chunk);
                }
            }
        }

        string completeResponse = fullResponse.ToString();
        Debug.WriteLine($"Streaming response completed");

        return completeResponse;
    }
}