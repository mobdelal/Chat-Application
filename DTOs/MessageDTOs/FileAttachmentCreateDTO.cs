using Microsoft.AspNetCore.Http;

namespace DTOs.MessageDTOs
{
    public class FileAttachmentCreateDTO
    {
        public string FileData { get; set; } = string.Empty; 
        public string FileUrl { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;

    }
}
