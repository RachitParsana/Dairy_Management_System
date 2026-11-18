namespace Dairy_Management_System.Models.Customer
{
    public class RouteLookupItem
    {
        public int RouteId { get; set; }
        public string RouteCode { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
    }

    public class DeliveryBoyLookupItem
    {
        public int DeliveryBoyId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? VehicleNumber { get; set; }
        public bool IsActive { get; set; }
    }

    public class MilkCategoryLookupItem
    {
        public int CategoryId { get; set; }
        public string CategoryCode { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string UnitOfMeasure { get; set; } = "Litre";
        public decimal DefaultPrice { get; set; }
        public string ProductType { get; set; } = "Liquid";
        public bool IsActive { get; set; }
    }
}
