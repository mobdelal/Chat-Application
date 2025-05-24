// Services/LocalFileStorageService.cs
using Application.FilesSR;
using System;
using System.IO;
using System.Threading.Tasks;
// Remove using Microsoft.AspNetCore.Hosting;

public class LocalFileStorageService : IFileStorageService
{
    // Remove _webHostEnvironment as it's no longer used for pathing

    // Base URL for accessing files from the frontend.
    // This assumes your web server serves wwwroot directly at the root.
    private readonly string _baseUrl;

    // We'll define the base directory here or inject it if it's configurable
    private readonly string _baseUploadDirectory;

    public LocalFileStorageService() // Remove IWebHostEnvironment from constructor
    {
        // Construct base URL for frontend access
        _baseUrl = "/";

        // Define the base upload directory. This mirrors your CreateChatAsync logic.
        // It's good practice to make this configurable (e.g., via appsettings.json)
        // for different environments (dev/prod). For now, hardcoding as per your example.
        _baseUploadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    public async Task<string> SaveFileAsync(byte[] fileBytes, string fileName, string contentType, string folderName = "attachments")
    {
        if (fileBytes == null || fileBytes.Length == 0)
        {
            throw new ArgumentNullException(nameof(fileBytes), "File bytes cannot be null or empty.");
        }
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentNullException(nameof(fileName), "File name cannot be null or empty.");
        }

        // Combine the base upload directory with the specific folder for attachments
        string uploadsFolder = Path.Combine(_baseUploadDirectory, folderName);

        // Ensure the directory exists
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        // Generate a unique file name to avoid collisions
        string uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(fileName);
        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

        try
        {
            await File.WriteAllBytesAsync(filePath, fileBytes);

            // Construct the URL path that the frontend will use.
            // This is relative to your wwwroot.
            return $"{_baseUrl}{folderName}/{uniqueFileName}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving file: {ex.Message}");
            throw new Exception($"Failed to save file: {fileName}", ex);
        }
    }
}