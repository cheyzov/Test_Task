namespace Test_Task.Models;

public sealed class OperationEvent
{
    public long EventId { get; set; }

    public required string OperationId { get; set; }

    public OperationStatus? FromStatus { get; set; }

    public OperationStatus ToStatus { get; set; }

    public required string Type { get; set; }

    public required string Message { get; set; }

    public DateTime OccurredAt { get; set; }

    public PaymentOperation Operation { get; set; } = null!;
}
