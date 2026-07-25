namespace Test_Task.Models;

public sealed class ProviderReceipt
{
    public long ReceiptId { get; set; }

    public required string ProviderPaymentId { get; set; }

    public required string OperationId { get; set; }

    public required string Result { get; set; }

    public string? Message { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime ReceivedAt { get; set; }

    public PaymentOperation Operation { get; set; } = null!;
}
