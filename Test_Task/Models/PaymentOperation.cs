namespace Test_Task.Models;

public sealed class PaymentOperation
{
    public required string OperationId { get; set; }

    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    public required string Description { get; set; }

    public OperationStatus Status { get; set; }

    public string? ProviderPaymentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<OperationEvent> Events { get; set; } = [];

    public PaymentDispatch? Dispatch { get; set; }

    public List<ProviderReceipt> Receipts { get; set; } = [];
}
