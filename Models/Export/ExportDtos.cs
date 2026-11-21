using System;

namespace Dairy_Management_System.Models.Export
{
    public class CustomerExportItemDto
    {
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public string Landmark { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public string DeliveryBoyName { get; set; } = string.Empty;
        public int DeliverySequence { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal OutstandingBalance { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class DailyDeliveryExportItemDto
    {
        public long DeliveryId { get; set; }
        public DateOnly EntryDate { get; set; }
        public string Shift { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public string DeliveryBoyName { get; set; } = string.Empty;
        public decimal TotalLiquidVolumeLitres { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal CommissionEarned { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MonthlyBillExportItemDto
    {
        public string BillNumber { get; set; } = string.Empty;
        public string MonthYear { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public decimal TotalLiquidMilkVolumeLitres { get; set; }
        public decimal CurrentMonthInvoiceAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal BalanceAmount { get; set; }
        public DateOnly? PaymentDueDate { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
    }

    public class PaymentExportItemDto
    {
        public string ReceiptNumber { get; set; } = string.Empty;
        public DateOnly PaymentDate { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string PaymentMode { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string ReceivedByAdmin { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class CompensationExportItemDto
    {
        public int CompensationId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public string MonthYear { get; set; } = string.Empty;
        public string CompensationType { get; set; } = string.Empty;
        public decimal TotalDeliveredLitres { get; set; }
        public decimal GrossRemuneration { get; set; }
        public decimal Adjustments { get; set; }
        public decimal NetPayable { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PaymentReference { get; set; } = string.Empty;
        public DateTime? PaidAt { get; set; }
    }
}
