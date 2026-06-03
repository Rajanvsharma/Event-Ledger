using System.ComponentModel.DataAnnotations;

namespace EventGateway.Models;

public class EventRequest
{
    [Required]
    public string EventId { get; set; } = string.Empty;

    [Required]
    public string AccountId { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(CREDIT|DEBIT)$", ErrorMessage = "Type must be CREDIT or DEBIT")]
    public string Type { get; set; } = string.Empty;

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }

    [Required]
    public string Currency { get; set; } = string.Empty;

    [Required]
    public DateTime EventTimestamp { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }
}
