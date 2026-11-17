using System.Collections.Generic;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Standard operation result for Customer domain services.
    /// Provides status, entity identifiers, and user-facing error details.
    /// </summary>
    public class CustomerOperationResult
    {
        public bool Succeeded { get; set; }
        public int? CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? Message { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool RequiresConfirmation { get; set; }
        public string? ConfirmationPrompt { get; set; }

        public static CustomerOperationResult Success(int customerId, string customerCode, string? message = null) =>
            new() { Succeeded = true, CustomerId = customerId, CustomerCode = customerCode, Message = message };

        public static CustomerOperationResult NeedsConfirmation(int customerId, string prompt) =>
            new() { Succeeded = false, RequiresConfirmation = true, CustomerId = customerId, ConfirmationPrompt = prompt };

        public static CustomerOperationResult Failure(string error) =>
            new() { Succeeded = false, Errors = new List<string> { error } };

        public static CustomerOperationResult Failure(IEnumerable<string> errors) =>
            new() { Succeeded = false, Errors = new List<string>(errors) };
    }
}
