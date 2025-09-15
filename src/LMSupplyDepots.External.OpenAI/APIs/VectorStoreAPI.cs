#pragma warning disable OPENAI001 // Suppress experimental API warnings
using OpenAI;
using OpenAI.VectorStores;
using System.ClientModel;
using System.Diagnostics;

namespace LMSupplyDepots.External.OpenAI.APIs;

/// <summary>
/// Manages vector store operations with OpenAI
/// </summary>
public class VectorStoreAPI
{
    private readonly VectorStoreClient _vectorStoreClient;

    /// <summary>
    /// Initializes a new instance of the VectorStoreManager class
    /// </summary>
    public VectorStoreAPI(OpenAIClient client)
    {
        _vectorStoreClient = client.GetVectorStoreClient();
    }

    /// <summary>
    /// Creates a vector store from the uploaded files
    /// </summary>
    public async Task<VectorStore> CreateVectorStoreAsync(IEnumerable<string> fileIds, string? name = null)
    {
        var storeName = name ?? $"vectorstore-{DateTime.UtcNow:yyyyMMddHHmmss}";
        Debug.WriteLine($"Creating vector store: {storeName}");

        try
        {
            // Create the options with file IDs
            var options = new VectorStoreCreationOptions
            {
                Name = storeName
            };

            // Add each file ID to the collection
            foreach (string fileId in fileIds)
            {
                options.FileIds.Add(fileId);
            }

            // Use the actual OpenAI SDK method signature
            var result = await _vectorStoreClient.CreateVectorStoreAsync(options);
            var vectorStore = result.Value;

            if (vectorStore != null)
            {
                Debug.WriteLine($"Vector store created successfully. ID: {vectorStore.Id}");
                return vectorStore;
            }
            else
            {
                throw new InvalidOperationException("Failed to create vector store - operation returned null");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating vector store: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Waits for a vector store to be processed and ready for use
    /// </summary>
    /// <exception cref="TimeoutException">Thrown when the processing takes too long</exception>
    /// <exception cref="Exception">Thrown when the processing fails</exception>
    public async Task<VectorStore> WaitForVectorStoreProcessingAsync(
        string vectorStoreId,
        int maxAttempts = 30,
        int delaySeconds = 5)
    {
        Debug.WriteLine($"Waiting for vector store {vectorStoreId} to be processed...");

        int attempts = 0;

        while (attempts < maxAttempts)
        {
            attempts++;

            try
            {
                // Get the vector store directly
                var vectorStoreResult = _vectorStoreClient.GetVectorStore(vectorStoreId);
                var vectorStore = vectorStoreResult.Value;

                if (vectorStore == null)
                {
                    Debug.WriteLine("Vector store not found or result is null");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    continue;
                }

                // Check for Completed status
                if (vectorStore.Status == VectorStoreStatus.Completed)
                {
                    Debug.WriteLine("Vector store processing completed successfully!");
                    return vectorStore;
                }

                // Check for Expired status - can use this instead of "Failed"
                if (vectorStore.Status == VectorStoreStatus.Expired)
                {
                    Debug.WriteLine($"Vector store processing expired");
                    throw new Exception($"Vector store processing expired");
                }

                Debug.WriteLine($"Vector store status: {vectorStore.Status}. Waiting {delaySeconds} seconds...");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking vector store status: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }

        throw new TimeoutException($"Vector store processing timed out after {maxAttempts} attempts");
    }

    /// <summary>
    /// Lists all vector stores
    /// </summary>
    /// <returns>Collection of vector store information</returns>
    public List<VectorStore> ListVectorStores()
    {
        try
        {
            // Use the GetVectorStores method and convert to list
            var vectorStoresCollection = _vectorStoreClient.GetVectorStores();
            if (vectorStoresCollection == null)
            {
                return [];
            }

            // Convert collection result to list
            var storesList = vectorStoresCollection.ToList();
            return storesList;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error listing vector stores: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Deletes a vector store from OpenAI
    /// </summary>
    public async Task<bool> DeleteVectorStoreAsync(string vectorStoreId)
    {
        try
        {
            var result = await _vectorStoreClient.DeleteVectorStoreAsync(vectorStoreId);
            if (result?.Value == null)
            {
                Debug.WriteLine($"Delete operation for vector store {vectorStoreId} returned null result");
                return false;
            }

            Debug.WriteLine($"Vector store {vectorStoreId} deleted successfully");
            return result.Value.Deleted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting vector store {vectorStoreId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Adds a file to an existing vector store
    /// </summary>
    public async Task<VectorStoreFile> AddFileToVectorStoreAsync(string vectorStoreId, string fileId)
    {
        Debug.WriteLine($"Adding file {fileId} to vector store {vectorStoreId}");

        try
        {
            var result = await _vectorStoreClient.CreateFileAsync(vectorStoreId, fileId);
            return result.Value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding file to vector store: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Removes a file from a vector store
    /// </summary>
    public async Task<bool> RemoveFileFromVectorStoreAsync(string vectorStoreId, string fileId)
    {
        Debug.WriteLine($"Removing file {fileId} from vector store {vectorStoreId}");

        try
        {
            var result = await _vectorStoreClient.DeleteFileAsync(vectorStoreId, fileId);
            if (result?.Value == null)
            {
                Debug.WriteLine($"Remove operation for file {fileId} from vector store {vectorStoreId} returned null result");
                return false;
            }

            Debug.WriteLine($"File {fileId} removed from vector store {vectorStoreId}: {result.Value.Deleted}");
            return result.Value.Deleted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing file from vector store: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Lists all files in a vector store
    /// </summary>
    public List<VectorStoreFile> ListVectorStoreFiles(string vectorStoreId, int? limit = null, string? status = null)
    {
        try
        {
            // Use simple call
            var filesCollection = _vectorStoreClient.GetVectorStoreFiles(vectorStoreId);

            if (filesCollection == null)
            {
                return [];
            }

            // Convert collection to list and apply filtering
            var filesList = filesCollection.ToList();

            if (limit.HasValue)
            {
                filesList = filesList.Take(limit.Value).ToList();
            }

            return filesList;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error listing vector store files: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Adds multiple files to a vector store individually
    /// </summary>
    public async Task<List<VectorStoreFile>> AddFilesToVectorStoreAsync(string vectorStoreId, IEnumerable<string> fileIds)
    {
        Debug.WriteLine($"Adding {fileIds.Count()} files to vector store {vectorStoreId}");

        try
        {
            var results = new List<VectorStoreFile>();
            foreach (var fileId in fileIds)
            {
                var result = await AddFileToVectorStoreAsync(vectorStoreId, fileId);
                results.Add(result);
            }
            return results;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding files to vector store: {ex.Message}");
            throw;
        }
    }
}