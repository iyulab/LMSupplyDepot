#pragma warning disable OPENAI001 // Suppress experimental API warnings
using OpenAI;
using OpenAI.VectorStores;
using System.ClientModel.Primitives;

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
        Console.WriteLine($"Creating vector store: {storeName}");

        try
        {
            // Create the options with file IDs properly added
            var options = new VectorStoreCreationOptions
            {
                Name = storeName
            };

            // Add each file ID to the collection
            foreach (string fileId in fileIds)
            {
                options.FileIds.Add(fileId);
            }

            // Create the vector store
            var operation = await _vectorStoreClient.CreateVectorStoreAsync(
                waitUntilCompleted: false,
                vectorStore: options);

            // Get the current value of the operation
            var vectorStore = operation.Value;
            if (vectorStore != null)
            {
                Console.WriteLine($"Vector store created successfully. ID: {vectorStore.Id}");
                return vectorStore;
            }
            else
            {
                throw new InvalidOperationException("Failed to create vector store - operation returned null");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating vector store: {ex.Message}");
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
        Console.WriteLine($"Waiting for vector store {vectorStoreId} to be processed...");

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
                    Console.WriteLine("Vector store not found or result is null");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    continue;
                }

                // Check for Completed status
                if (vectorStore.Status == VectorStoreStatus.Completed)
                {
                    Console.WriteLine("Vector store processing completed successfully!");
                    return vectorStore;
                }

                // Check for Expired status - can use this instead of "Failed"
                if (vectorStore.Status == VectorStoreStatus.Expired)
                {
                    Console.WriteLine($"Vector store processing expired");
                    throw new Exception($"Vector store processing expired");
                }

                Console.WriteLine($"Vector store status: {vectorStore.Status}. Waiting {delaySeconds} seconds...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking vector store status: {ex.Message}");
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
            Console.WriteLine($"Error listing vector stores: {ex.Message}");
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
                Console.WriteLine($"Delete operation for vector store {vectorStoreId} returned null result");
                return false;
            }

            Console.WriteLine($"Vector store {vectorStoreId} deleted successfully");
            return result.Value.Deleted;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting vector store {vectorStoreId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Adds a file to an existing vector store
    /// </summary>
        public async Task<VectorStoreFileAssociation> AddFileToVectorStoreAsync(string vectorStoreId, string fileId)
    {
        Console.WriteLine($"Adding file {fileId} to vector store {vectorStoreId}");

        try
        {
            // Use the AddFileToVectorStore method which waits for the operation to complete
            var operation = await _vectorStoreClient.AddFileToVectorStoreAsync(
                vectorStoreId,
                fileId,
                waitUntilCompleted: true);

            // Get the file association result
            var fileAssociation = operation.Value ?? throw new InvalidOperationException($"Failed to add file {fileId} to vector store {vectorStoreId} - operation returned null");
            Console.WriteLine($"File {fileId} added to vector store {vectorStoreId}");
            return fileAssociation;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding file to vector store: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Removes a file from a vector store
    /// </summary>
        public async Task<bool> RemoveFileFromVectorStoreAsync(string vectorStoreId, string fileId)
    {
        Console.WriteLine($"Removing file {fileId} from vector store {vectorStoreId}");

        try
        {
            // Use the RemoveFileFromStore method
            var result = await _vectorStoreClient.RemoveFileFromStoreAsync(vectorStoreId, fileId);
            if (result?.Value == null)
            {
                Console.WriteLine($"Remove operation for file {fileId} from vector store {vectorStoreId} returned null result");
                return false;
            }

            Console.WriteLine($"File {fileId} removed from vector store {vectorStoreId}: {result.Value.Removed}");
            return result.Value.Removed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing file from vector store: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Lists all files in a vector store
    /// </summary>
            public List<VectorStoreFileAssociation> ListVectorStoreFiles(string vectorStoreId, int? limit = null, string? status = null)
    {
        try
        {
            // Create options for filtering if needed
            var options = new VectorStoreFileAssociationCollectionOptions
            {
                PageSizeLimit = limit
            };

            // If status is provided, add it as a filter
            if (!string.IsNullOrEmpty(status))
            {
                options.Filter = new VectorStoreFileStatusFilter(status);
            }

            // Use the GetFileAssociations method
            var filesCollection = _vectorStoreClient.GetFileAssociations(vectorStoreId, options);
            if (filesCollection == null)
            {
                return [];
            }

            // Convert collection to list
            return [.. filesCollection];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing vector store files: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Adds multiple files to a vector store as a batch
    /// </summary>
        public async Task<VectorStoreBatchFileJob> AddFilesToVectorStoreAsync(string vectorStoreId, IEnumerable<string> fileIds)
    {
        Console.WriteLine($"Adding {fileIds.Count()} files to vector store {vectorStoreId} as a batch");

        try
        {
            // Create a batch file job
            var operation = await _vectorStoreClient.CreateBatchFileJobAsync(
                vectorStoreId,
                fileIds,
                waitUntilCompleted: false);

            // Get the initial batch job information
            var batchJob = operation.Value ?? throw new InvalidOperationException($"Failed to add files to vector store {vectorStoreId} - operation returned null");
            Console.WriteLine($"Batch file job created. ID: {batchJob.BatchId}");
            return batchJob;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding files to vector store as batch: {ex.Message}");
            throw;
        }
    }
}