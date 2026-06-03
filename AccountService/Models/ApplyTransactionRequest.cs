namespace AccountService.Models;

public class ApplyTransactionRequest
{
    public string EventId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime EventTimestamp { get; set; }
}
