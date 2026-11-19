using System;
using System.Collections.Generic;
using Dairy_Management_System.Models.Route;

namespace Dairy_Management_System.Models.DeliveryBoy
{
    /// <summary>
    /// View model for the Delivery Boy staff directory.
    /// Provides authoritative system-wide KPI metrics alongside filtered pagination.
    /// </summary>
    public class DeliveryBoyIndexViewModel
    {
        public DeliveryBoyFilterCriteria Criteria { get; set; } = new();
        public IReadOnlyList<DeliveryBoyListItemDto> DeliveryBoys { get; set; } = new List<DeliveryBoyListItemDto>();
        public IReadOnlyList<RouteLookupModel> AvailableRoutes { get; set; } = new List<RouteLookupModel>();

        // System-wide authoritative KPI metrics (independent of active filter/page)
        public int TotalEmployees { get; set; }
        public int ActiveEmployees { get; set; }
        public int InactiveEmployees { get; set; }
        public int CommissionEmployees { get; set; }
        public int FixedSalaryEmployees { get; set; }

        // Backward-compatible aliases
        public int ActiveCount => ActiveEmployees;
        public int InactiveCount => InactiveEmployees;
        public int CommissionCount => CommissionEmployees;
        public int FixedSalaryCount => FixedSalaryEmployees;

        // Filtered pagination metrics
        public int TotalCount { get; set; } // Total records matching current search/filter criteria
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / (PageSize > 0 ? PageSize : 10));
        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;
    }
}
