namespace Models
{
    public class FileAttachment
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public Message? Message { get; set; }
        public string FileUrl { get; set; } = null!;
        public string FileType { get; set; } = null!;
    }
}
