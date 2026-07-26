namespace Test_Task.Contracts;

public sealed class CreateOperationRequest
{
    public string? OperationId { get; init; }

    public string? Amount { get; init; }

    public string? Currency { get; init; }

    public string? Description { get; init; }
}

public sealed record OperationResponse(
    string OperationId,
    string Amount,
    string Currency,
    string Description,
    string Status,
    string? ProviderPaymentId);

public sealed record OperationEventResponse(
    long EventId,
    string Type,
    string? FromStatus,
    string ToStatus,
    string Message,
    DateTime OccurredAt);

public sealed class ProviderReceiptRequest
{
    public string? ProviderPaymentId { get; init; }

    public string? OperationId { get; init; }

    public string? Result { get; init; }

    public string? Message { get; init; }

    public DateTime OccurredAt { get; init; }
}
