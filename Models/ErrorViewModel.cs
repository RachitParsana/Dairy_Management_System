namespace Dairy_Management_System.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public int? StatusCode { get; set; }

        public string? StatusMessage { get; set; }

        public string? StatusDescription { get; set; }
    }
}
