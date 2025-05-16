namespace DTOs.MessageDTOs
{
    public class SendMessageDTO
    {
        public int ChatId { get; set; }
        public int SenderId { get; set; }
        public string? Content { get; set; }
        public List<FileAttachmentCreateDTO> Attachments { get; set; } = new();
    }
}
