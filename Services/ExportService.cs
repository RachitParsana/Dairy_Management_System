using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Common;
using Dairy_Management_System.Models.Export;
using Dairy_Management_System.Repositories;

namespace Dairy_Management_System.Services
{
    public class ExportService : IExportService
    {
        private readonly IExportRepository _exportRepository;

        public ExportService(IExportRepository exportRepository)
        {
            _exportRepository = exportRepository ?? throw new ArgumentNullException(nameof(exportRepository));
        }

        public async Task<byte[]> GenerateCustomersCsvAsync(int? routeId, bool? isActive, CancellationToken ct = default)
        {
            var data = await _exportRepository.GetCustomersExportAsync(routeId, isActive, ct);

            var headers = new List<string>
            {
                "Customer Code", "Full Name", "Phone", "Address", "Area", "Landmark",
                "Route", "Delivery Boy", "Delivery Sequence", "Status", "Outstanding Balance", "Created At"
            };

            var rows = data.Select(c => (IReadOnlyList<string?>)new string?[]
            {
                c.CustomerCode,
                c.FullName,
                c.Phone,
                c.Address,
                c.Area,
                c.Landmark,
                c.RouteName,
                c.DeliveryBoyName,
                c.DeliverySequence.ToString(),
                c.Status,
                c.OutstandingBalance.ToString("F2"),
                c.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            });

            return CsvExportHelper.BuildCsv(headers, rows);
        }

        public async Task<byte[]> GenerateDailyDeliveriesCsvAsync(DateOnly fromDate, DateOnly toDate, int? routeId, string? shift, CancellationToken ct = default)
        {
            var data = await _exportRepository.GetDailyDeliveriesExportAsync(fromDate, toDate, routeId, shift, ct);

            var headers = new List<string>
            {
                "Delivery ID", "Entry Date", "Shift", "Customer Code", "Customer Name",
                "Route", "Delivery Boy", "Liquid Volume (L)", "Total Amount", "Status", "Commission Earned", "Created At"
            };

            var rows = data.Select(d => (IReadOnlyList<string?>)new string?[]
            {
                d.DeliveryId.ToString(),
                d.EntryDate.ToString("yyyy-MM-dd"),
                d.Shift,
                d.CustomerCode,
                d.CustomerName,
                d.RouteName,
                d.DeliveryBoyName,
                d.TotalLiquidVolumeLitres.ToString("F2"),
                d.TotalAmount.ToString("F2"),
                d.Status,
                d.CommissionEarned.ToString("F2"),
                d.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            });

            return CsvExportHelper.BuildCsv(headers, rows);
        }

        public async Task<byte[]> GenerateMonthlyBillsCsvAsync(byte month, short year, string? paymentStatus, int? routeId, CancellationToken ct = default)
        {
            var data = await _exportRepository.GetMonthlyBillsExportAsync(month, year, paymentStatus, routeId, ct);

            var headers = new List<string>
            {
                "Bill Number", "Billing Month", "Customer Code", "Customer Name", "Route",
                "Total Liquid Milk (L)", "Invoice Amount", "Paid Amount", "Balance Amount",
                "Payment Due Date", "Payment Status", "Published At"
            };

            var rows = data.Select(b => (IReadOnlyList<string?>)new string?[]
            {
                b.BillNumber,
                b.MonthYear,
                b.CustomerCode,
                b.CustomerName,
                b.RouteName,
                b.TotalLiquidMilkVolumeLitres.ToString("F2"),
                b.CurrentMonthInvoiceAmount.ToString("F2"),
                b.PaidAmount.ToString("F2"),
                b.BalanceAmount.ToString("F2"),
                b.PaymentDueDate?.ToString("yyyy-MM-dd") ?? "",
                b.PaymentStatus,
                b.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
            });

            return CsvExportHelper.BuildCsv(headers, rows);
        }

        public async Task<byte[]> GeneratePaymentsCsvAsync(DateOnly? fromDate, DateOnly? toDate, string? paymentMode, CancellationToken ct = default)
        {
            var data = await _exportRepository.GetPaymentsExportAsync(fromDate, toDate, paymentMode, ct);

            var headers = new List<string>
            {
                "Receipt Number", "Payment Date", "Customer Code", "Customer Name",
                "Payment Mode", "Reference Number", "Amount", "Received By", "Notes", "Created At"
            };

            var rows = data.Select(p => (IReadOnlyList<string?>)new string?[]
            {
                p.ReceiptNumber,
                p.PaymentDate.ToString("yyyy-MM-dd"),
                p.CustomerCode,
                p.CustomerName,
                p.PaymentMode,
                p.ReferenceNumber,
                p.Amount.ToString("F2"),
                p.ReceivedByAdmin,
                p.Notes,
                p.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            });

            return CsvExportHelper.BuildCsv(headers, rows);
        }

        public async Task<byte[]> GenerateCompensationCsvAsync(byte month, short year, CancellationToken ct = default)
        {
            var data = await _exportRepository.GetCompensationExportAsync(month, year, ct);

            var headers = new List<string>
            {
                "Compensation ID", "Delivery Boy", "Phone", "Route", "Month Year",
                "Compensation Type", "Total Delivered (L)", "Gross Remuneration", "Adjustments",
                "Net Payable", "Status", "Payment Reference", "Paid At"
            };

            var rows = data.Select(c => (IReadOnlyList<string?>)new string?[]
            {
                c.CompensationId.ToString(),
                c.DeliveryBoyName,
                c.Phone,
                c.RouteName,
                c.MonthYear,
                c.CompensationType,
                c.TotalDeliveredLitres.ToString("F2"),
                c.GrossRemuneration.ToString("F2"),
                c.Adjustments.ToString("F2"),
                c.NetPayable.ToString("F2"),
                c.Status,
                c.PaymentReference,
                c.PaidAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
            });

            return CsvExportHelper.BuildCsv(headers, rows);
        }
    }
}
