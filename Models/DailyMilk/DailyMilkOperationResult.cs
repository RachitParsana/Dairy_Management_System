using System.Collections.Generic;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Standard operation result for Daily Milk domain services.
    /// Provides execution status, generated identifiers, and user-facing error or confirmation details.
    /// </summary>
    public class DailyMilkOperationResult
    {
        public bool Succeeded { get; set; }
        public long? BatchId { get; set; }
        public long? DeliveryId { get; set; }
        public int AffectedCount { get; set; }
        public string? Message { get; set; }
        public List<string> Errors { get; set; } = new();

        public static DailyMilkOperationResult Success(string? message = null) =>
            new() { Succeeded = true, Message = message };

        public static DailyMilkOperationResult SuccessBatch(long batchId, int affectedCount, string? message = null) =>
            new() { Succeeded = true, BatchId = batchId, AffectedCount = affectedCount, Message = message };

        public static DailyMilkOperationResult SuccessDelivery(long deliveryId, string? message = null) =>
            new() { Succeeded = true, DeliveryId = deliveryId, AffectedCount = 1, Message = message };

        public static DailyMilkOperationResult Failure(string error) =>
            new() { Succeeded = false, Errors = new List<string> { error } };

        public static DailyMilkOperationResult Failure(IEnumerable<string> errors) =>
            new() { Succeeded = false, Errors = new List<string>(errors) };
    }
}
