using OpenAI;
using OpenAI.Files;
using System.Diagnostics;

namespace LMSupplyDepots.External.OpenAI.APIs;

/// <summary>
/// Manages file operations with OpenAI
/// </summary>
public class FileAPI
{
    private readonly OpenAIFileClient _fileClient;

    /// <summary>
    /// Initializes a new instance of the FileManager class
    /// </summary>
        public FileAPI(OpenAIClient client)
    {
        _fileClient = client.GetOpenAIFileClient();
    }

    /// <summary>
    /// Uploads a file to OpenAI
    /// </summary>
    public async Task<OpenAIFile> UploadFileAsync(string filePath)
    {
        Debug.WriteLine($"Uploading file: {Path.GetFileName(filePath)}");

        try
        {
            var fileResult = await _fileClient.UploadFileAsync(
                filePath,
                FileUploadPurpose.Assistants);

            // Access the Value property to get the actual OpenAIFile
            OpenAIFile file = fileResult.Value;

            Debug.WriteLine($"File uploaded successfully. ID: {file.Id}");
            return file;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error uploading file: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Uploads multiple files to OpenAI
    /// </summary>
    public async Task<List<OpenAIFile>> UploadFilesAsync(IEnumerable<string> filePaths)
    {
        var uploadedFiles = new List<OpenAIFile>();

        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                Debug.WriteLine($"File not found: {filePath}");
                continue;
            }

            var file = await UploadFileAsync(filePath);
            uploadedFiles.Add(file);
        }

        return uploadedFiles;
    }

    /// <summary>
    /// Lists all uploaded files
    /// </summary>
    /// <returns>Collection of file information</returns>
    public async Task<List<OpenAIFile>> ListFilesAsync()
    {
        try
        {
            // Use the GetFiles method which is public
            var filesResult = await _fileClient.GetFilesAsync();
            var files = filesResult.Value;
            return [.. files];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error listing files: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Deletes a file from OpenAI
    /// </summary>
    public async Task<bool> DeleteFileAsync(string fileId)
    {
        try
        {
            await _fileClient.DeleteFileAsync(fileId);
            Debug.WriteLine($"File {fileId} deleted successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting file {fileId}: {ex.Message}");
            return false;
        }
    }
}