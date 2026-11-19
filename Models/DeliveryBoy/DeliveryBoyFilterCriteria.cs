namespace Dairy_Management_System.Models.DeliveryBoy
{
    public class DeliveryBoyFilterCriteria
    {
        public string? SearchTerm { get; set; }
        public string? StatusFilter { get; set; } = "All"; // 'All', 'Active', 'Inactive'
        public string? CompensationType { get; set; } = "All"; // 'All', 'Fixed Salary', 'Commission'
        public int? RouteId { get; set; }
    }
}
