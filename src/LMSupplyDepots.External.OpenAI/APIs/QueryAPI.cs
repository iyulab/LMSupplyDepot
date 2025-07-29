#pragma warning disable OPENAI001 // Suppress experimental API warnings
using OpenAI;
using OpenAI.Assistants;
using System.Diagnostics;

namespace LMSupplyDepots.External.OpenAI.APIs;

/// <summary>
/// Manages query operations with OpenAI for retrieval augmented generation
/// </summary>
public class QueryAPI
{
    private readonly AssistantClient _assistantClient;
    private readonly string _model;

    /// <summary>
    /// Initializes a new instance of the QueryAPI class
    /// </summary>
    public QueryAPI(OpenAIClient client, string model = "gpt-4o-mini")
    {
        _assistantClient = client.GetAssistantClient();
        _model = model;
    }

    /// <summary>
    /// Queries the content of files using the Assistant API with file search
    /// </summary>
    public async Task<QueryResult> QueryFilesAsync(string vectorStoreId, string query)
    {
        Debug.WriteLine($"Querying files with: \"{query}\"");

        try
        {
            // Create a temporary assistant with file search capabilities
            var assistant = await _assistantClient.CreateAssistantAsync(
                _model,
                new AssistantCreationOptions
                {
                    Name = "Temporary Query Assistant",
                    Instructions = "You are a helpful assistant that can search through files to answer questions.",
                    Tools = { new FileSearchToolDefinition() },
                    ToolResources = new ToolResources
                    {
                        FileSearch = new FileSearchToolResources
                        {
                            VectorStoreIds = { vectorStoreId }
                        }
                    }
                });

            // Create a thread
            var thread = await _assistantClient.CreateThreadAsync();

            // Add a message to the thread
            await _assistantClient.CreateMessageAsync(thread.Value.Id, MessageRole.User, [MessageContent.FromText(query)]);

            // Run the assistant
            var run = await _assistantClient.CreateRunAsync(thread.Value.Id, assistant.Value.Id);

            // Wait for completion
            while (run.Value.Status == RunStatus.InProgress || run.Value.Status == RunStatus.Queued)
            {
                await Task.Delay(1000);
                run = await _assistantClient.GetRunAsync(thread.Value.Id, run.Value.Id);
            }

            if (run.Value.Status != RunStatus.Completed)
            {
                throw new Exception($"Query failed with status: {run.Value.Status}");
            }

            // Get the assistant's response
            var messages = _assistantClient.GetMessages(thread.Value.Id);
            var assistantMessage = messages.FirstOrDefault(m => m.Role == MessageRole.Assistant);

            if (assistantMessage == null)
            {
                throw new Exception("No response from assistant");
            }

            // Extract response text
            var responseText = assistantMessage.Content.FirstOrDefault()?.Text ?? "";

            // Clean up: delete the temporary assistant and thread
            await _assistantClient.DeleteAssistantAsync(assistant.Value.Id);
            await _assistantClient.DeleteThreadAsync(thread.Value.Id);

            Debug.WriteLine($"Query completed successfully");

            return new QueryResult
            {
                ResponseText = responseText
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error querying files: {ex.Message}");
            throw;
        }
    }
}

/// <summary>
/// Result of a file query operation
/// </summary>
public class QueryResult
{
    /// <summary>
    /// The response text from the query
    /// </summary>
    public required string ResponseText { get; set; }

    /// <summary>
    /// Gets the output text (alias for ResponseText for backward compatibility)
    /// </summary>
    public string GetOutputText() => ResponseText;

    /// <summary>
    /// Gets file annotations for backward compatibility
    /// </summary>
    public List<string> FileAnnotations()
    {
        // Return empty list for now - file citations can be extracted from response text
        return new List<string>();
    }
}