using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Route
{
    /// <summary>
    /// Full projection model for Route Management table showing active assignments.
    /// </summary>
    public class RouteManagementItemModel
    {
        public int RouteId { get; set; }
        public string RouteCode { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int ActiveDeliveryBoyCount { get; set; }
        public int ActiveCustomerCount { get; set; }
    }

    /// <summary>
    /// Input model for creating or updating a geographical delivery route.
    /// </summary>
    public class RouteFormModel
    {
        public int RouteId { get; set; }

        [Required(ErrorMessage = "Route code is required.")]
        [StringLength(20, ErrorMessage = "Route code cannot exceed 20 characters.")]
        [Display(Name = "Route Code")]
        public string RouteCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Route name is required.")]
        [StringLength(100, ErrorMessage = "Route name cannot exceed 100 characters.")]
        [Display(Name = "Route Name")]
        public string RouteName { get; set; } = string.Empty;

        [StringLength(250, ErrorMessage = "Description cannot exceed 250 characters.")]
        [Display(Name = "Coverage Description")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// Result payload for Route management operations.
    /// </summary>
    public class RouteOperationResult
    {
        public bool Succeeded { get; set; }
        public int RouteId { get; set; }
        public string? RouteCode { get; set; }
        public string? RouteName { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }

        public static RouteOperationResult Success(int routeId, string? routeCode, string? routeName, string message) => new()
        {
            Succeeded = true,
            RouteId = routeId,
            RouteCode = routeCode,
            RouteName = routeName,
            Message = message
        };

        public static RouteOperationResult Failure(string errorMessage) => new()
        {
            Succeeded = false,
            ErrorMessage = errorMessage
        };
    }

    /// <summary>
    /// Page view model for Admin Route Management (/Route/Index).
    /// </summary>
    public class RouteIndexViewModel
    {
        public IReadOnlyList<RouteManagementItemModel> Routes { get; set; } = Array.Empty<RouteManagementItemModel>();
        public RouteFormModel CreateForm { get; set; } = new();
        public int TotalRoutes => Routes.Count;
        public int ActiveRoutes { get; set; }
        public int InactiveRoutes { get; set; }
        public int TotalAssignedCustomers { get; set; }
        public int TotalAssignedAgents { get; set; }
    }
}
