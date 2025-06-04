using OpenAI;
using OpenAI.Responses;
using System.Text;

namespace LMSupplyDepots.External.OpenAI.APIs;

/// <summary>
/// Manages chat operations with OpenAI
/// </summary>
public class ChatAPI
{
    private readonly OpenAIResponseClient _responseClient;

    /// <summary>
    /// Initializes a new instance of the ChatAPI class
    /// </summary>
            public ChatAPI(OpenAIClient client, string model = "gpt-4o")
    {
        _responseClient = client.GetOpenAIResponseClient(model);
    }

    /// <summary>
    /// Sends a single message to the model and returns the response
    /// </summary>
        public async Task<string> SendMessageAsync(string message, string? systemPrompt = null)
    {
        Console.WriteLine($"Sending message to OpenAI: \"{message}\"");

        try
        {
            // Create input items for the conversation
            var inputItems = new List<ResponseItem>();

            // Add system message if provided
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                inputItems.Add(ResponseItem.CreateSystemMessageItem(systemPrompt));
            }

            // Add user message
            inputItems.Add(ResponseItem.CreateUserMessageItem(message));

            // Send the request
            var response = await _responseClient.CreateResponseAsync(
                inputItems,
                new ResponseCreationOptions());

            // Extract the response text
            string responseText = response.Value.GetOutputText();
            Console.WriteLine($"Response received from OpenAI");

            return responseText;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Conducts a conversation with the model using a list of messages
    /// </summary>
    public async Task<string> SendConversationAsync(List<(string role, string content)> messages)
    {
        Console.WriteLine($"Sending conversation with {messages.Count} messages to OpenAI");

        try
        {
            // Convert messages to ResponseItems
            var inputItems = ConvertMessagesToResponseItems(messages);

            // Send the request
            var response = await _responseClient.CreateResponseAsync(
                inputItems,
                new ResponseCreationOptions());

            // Extract the response text
            string responseText = response.Value.GetOutputText();
            Console.WriteLine($"Response received from OpenAI");

            return responseText;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending conversation: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Streams a chat response from the model
    /// </summary>
            public async Task<string> StreamMessageAsync(string message, string? systemPrompt = null, Action<string>? onUpdate = null)
    {
        Console.WriteLine($"Streaming message to OpenAI: \"{message}\"");

        try
        {
            // Create input items for the conversation
            var inputItems = new List<ResponseItem>();

            // Add system message if provided
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                inputItems.Add(ResponseItem.CreateSystemMessageItem(systemPrompt));
            }

            // Add user message
            inputItems.Add(ResponseItem.CreateUserMessageItem(message));

            // Send the request and process the streaming response
            return await ProcessStreamingResponseAsync(inputItems, onUpdate);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error streaming message: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Streams a conversation with the model using a list of messages
    /// </summary>
        public async Task<string> StreamConversationAsync(List<(string role, string content)> messages, Action<string>? onUpdate = null)
    {
        Console.WriteLine($"Streaming conversation with {messages.Count} messages to OpenAI");

        try
        {
            // Convert messages to ResponseItems
            var inputItems = ConvertMessagesToResponseItems(messages);

            // Send the request and process the streaming response
            return await ProcessStreamingResponseAsync(inputItems, onUpdate);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error streaming conversation: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Converts a list of message tuples to ResponseItems
    /// </summary>
    private static List<ResponseItem> ConvertMessagesToResponseItems(List<(string role, string content)> messages)
    {
        var inputItems = new List<ResponseItem>();

        foreach (var (role, content) in messages)
        {
            ResponseItem item = role.ToLower() switch
            {
                "system" => ResponseItem.CreateSystemMessageItem(content),
                "assistant" => ResponseItem.CreateAssistantMessageItem(content),
                _ => ResponseItem.CreateUserMessageItem(content)
            };
            inputItems.Add(item);
        }

        return inputItems;
    }

    /// <summary>
    /// Processes a streaming response from the OpenAI API
    /// </summary>
        private async Task<string> ProcessStreamingResponseAsync(List<ResponseItem> inputItems, Action<string>? onUpdate)
    {
        // Create the streaming request
        var streamingResponse = _responseClient.CreateResponseStreamingAsync(
            inputItems,
            new ResponseCreationOptions());

        var fullResponse = new StringBuilder();

        // Process each chunk as it arrives
        await foreach (var update in streamingResponse)
        {
            // Process different update types based on the Type property
            if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
            {
                // Handle text delta updates
                string chunk = textDelta.Delta;
                if (!string.IsNullOrEmpty(chunk))
                {
                    fullResponse.Append(chunk);
                    onUpdate?.Invoke(chunk);
                }
            }
            // Add cases for other update types as needed if required in the future
        }

        string completeResponse = fullResponse.ToString();
        Console.WriteLine($"Streaming response completed");

        return completeResponse;
    }
}