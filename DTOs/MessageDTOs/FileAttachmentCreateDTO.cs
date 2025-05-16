using Microsoft.AspNetCore.Http;

namespace DTOs.MessageDTOs
{
    public class FileAttachmentCreateDTO
    {
        public IFormFile? FileData { get; set; } 
        public string FileUrl { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
    }
}
