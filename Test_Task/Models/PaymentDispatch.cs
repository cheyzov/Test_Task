namespace Test_Task.Models;

public sealed class PaymentDispatch
{
    public required string OperationId { get; set; }

    public required string RequestBody { get; set; }

    public int AttemptCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public PaymentOperation Operation { get; set; } = null!;
}
