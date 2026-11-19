using System;

namespace Dairy_Management_System.Models.Compensation
{
    public class CompensationFilterCriteria
    {
        public byte Month { get; set; } = (byte)DateTime.Today.Month;
        public short Year { get; set; } = (short)DateTime.Today.Year;
        public string? Status { get; set; } = "All"; // 'All', 'Draft', 'Finalized', 'Paid'
        public string? CompensationType { get; set; } = "All"; // 'All', 'Fixed Salary', 'Commission'
        public int? DeliveryBoyId { get; set; }
    }
}
