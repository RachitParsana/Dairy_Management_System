using System;
using System.Collections.Generic;
using Dairy_Management_System.Models.Customer;

namespace Dairy_Management_System.Models.Compensation
{
    public class CompensationIndexViewModel
    {
        public CompensationFilterCriteria Criteria { get; set; } = new();
        public IReadOnlyList<MonthlyCompensationListItemDto> Compensations { get; set; } = new List<MonthlyCompensationListItemDto>();
        public IReadOnlyList<DeliveryBoyLookupItem> AvailableDeliveryBoys { get; set; } = new List<DeliveryBoyLookupItem>();

        public int TotalEmployees { get; set; }
        public int DraftCount { get; set; }
        public int FinalizedCount { get; set; }
        public int PaidCount { get; set; }
        public decimal TotalGrossPayable { get; set; }
        public decimal TotalAdjustments { get; set; }
        public decimal TotalNetPayable { get; set; }

        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / (PageSize > 0 ? PageSize : 10));
        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;
    }
}
