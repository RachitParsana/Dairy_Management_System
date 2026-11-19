namespace Dairy_Management_System.Models.Route
{
    /// <summary>
    /// Minimal projection model representing an active route for application dropdowns and filtering.
    /// Independent of customer assignments.
    /// </summary>
    public class RouteLookupModel
    {
        public int RouteId { get; set; }
        public string RouteCode { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
    }
}
