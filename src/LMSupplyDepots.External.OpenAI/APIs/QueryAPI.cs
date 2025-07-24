using OpenAI;
using OpenAI.Responses;
using System.Diagnostics;

namespace LMSupplyDepots.External.OpenAI.APIs;

/// <summary>
/// Manages query operations with OpenAI for retrieval augmented generation
/// </summary>
public class QueryAPI
{
    private readonly OpenAIResponseClient _responseClient;

    /// <summary>
    /// Initializes a new instance of the QueryManager class
    /// </summary>
            public QueryAPI(OpenAIClient client, string model = "gpt-4o-mini")
    {
        _responseClient = client.GetOpenAIResponseClient(model);
    }

    /// <summary>
    /// Queries the content of files using the Responses API with file search
    /// </summary>
        public async Task<OpenAIResponse> QueryFilesAsync(string vectorStoreId, string query)
    {
        Debug.WriteLine($"Querying files with: \"{query}\"");

        try
        {
            var fileSearchTool = ResponseTool.CreateFileSearchTool(vectorStoreIds: [vectorStoreId]);

            var response = await _responseClient.CreateResponseAsync(
                userInputText: query,
                new ResponseCreationOptions
                {
                    Tools = { fileSearchTool }
                });

            return response;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error querying files: {ex.Message}");
            throw;
        }
    }
}