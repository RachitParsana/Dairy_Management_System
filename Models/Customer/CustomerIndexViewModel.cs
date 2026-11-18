using System.Collections.Generic;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// ViewModel for the Customer Management directory page.
    /// Supports filter state, continuous scrolling data, statistics, and dropdown lookups.
    /// </summary>
    public class CustomerIndexViewModel
    {
        public CustomerFilterCriteria Criteria { get; set; } = new();
        public IReadOnlyList<CustomerListItemViewModel> Customers { get; set; } = new List<CustomerListItemViewModel>();
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public int VacationTodayCount { get; set; }

        public IReadOnlyList<RouteLookupItem> Routes { get; set; } = new List<RouteLookupItem>();
        public IReadOnlyList<DeliveryBoyLookupItem> DeliveryBoys { get; set; } = new List<DeliveryBoyLookupItem>();
        public IReadOnlyList<string> Areas { get; set; } = new List<string>();
        public IReadOnlyList<MilkCategoryLookupItem> Categories { get; set; } = new List<MilkCategoryLookupItem>();

        public string? SearchTerm => Criteria.SearchTerm;
        public string? SelectedStatus => Criteria.Status;
        public int? SelectedRouteId => Criteria.RouteId;
        public int? SelectedDeliveryBoyId => Criteria.DeliveryBoyId;
        public string? SelectedArea { get; set; }

        public IReadOnlyList<RouteLookupItem> RouteList => Routes;
        public IReadOnlyList<DeliveryBoyLookupItem> DeliveryBoyList => DeliveryBoys;
        public IReadOnlyList<string> AreaList => Areas;
    }
}
