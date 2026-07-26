using System.Data;
using Microsoft.EntityFrameworkCore;
using Test_Task.Contracts;
using Test_Task.Models;

namespace Test_Task.Services;

public sealed class ReceiptService(TestTaskDbContext db)
{
    public async Task<ReceiptResult> ReceiveAsync(
        ProviderReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var errors = OperationValidator.ValidateReceipt(request);
        if (errors.Count > 0)
        {
            return ReceiptResult.Invalid(errors);
        }

        var operationId = request.OperationId!.Trim();
        var providerPaymentId = request.ProviderPaymentId!.Trim();
        var result = request.Result!.Trim().ToUpperInvariant();

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var operation = await db.Operations.SingleOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);
        if (operation is null)
        {
            return ReceiptResult.NotFoundResult();
        }

        if (operation.ProviderPaymentId is not null && operation.ProviderPaymentId != providerPaymentId)
        {
            return ReceiptResult.ConflictResult();
        }

        var receiptAlreadyProcessed = await db.ProviderReceipts.AnyAsync(
            x => x.OperationId == operationId &&
                 x.ProviderPaymentId == providerPaymentId &&
                 x.Result == result,
            cancellationToken);
        if (receiptAlreadyProcessed)
        {
            await transaction.CommitAsync(cancellationToken);
            return ReceiptResult.Accepted();
        }

        var providerPaymentBelongsToAnotherOperation = await db.Operations.AnyAsync(
                x => x.ProviderPaymentId == providerPaymentId && x.OperationId != operationId,
                cancellationToken) ||
            await db.ProviderReceipts.AnyAsync(
                x => x.ProviderPaymentId == providerPaymentId && x.OperationId != operationId,
                cancellationToken);
        if (providerPaymentBelongsToAnotherOperation)
        {
            return ReceiptResult.ConflictResult();
        }

        var now = DateTime.UtcNow;
        operation.ProviderPaymentId ??= providerPaymentId;
        operation.UpdatedAt = now;

        var ignored = operation.Status is OperationStatus.COMPLETED or OperationStatus.REJECTED &&
                      operation.Status.ToString() != result;
        if (!ignored && operation.Status is not (OperationStatus.COMPLETED or OperationStatus.REJECTED))
        {
            var nextStatus = result == "COMPLETED" ? OperationStatus.COMPLETED : OperationStatus.REJECTED;
            var previousStatus = operation.Status;
            operation.Status = nextStatus;
            operation.Events.Add(new OperationEvent
            {
                OperationId = operation.OperationId,
                FromStatus = previousStatus,
                ToStatus = nextStatus,
                Type = nextStatus.ToString(),
                Message = request.Message?.Trim() ?? $"Квитанция провайдера: {nextStatus}",
                OccurredAt = request.OccurredAt == default ? now : request.OccurredAt
            });
        }

        db.ProviderReceipts.Add(new ProviderReceipt
        {
            ProviderPaymentId = providerPaymentId,
            OperationId = operation.OperationId,
            Result = result,
            Ignored = ignored,
            Message = request.Message?.Trim(),
            OccurredAt = request.OccurredAt == default ? now : request.OccurredAt,
            ReceivedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReceiptResult.Accepted();
    }
}

public sealed record ReceiptResult(
    IReadOnlyDictionary<string, string[]> Errors,
    bool NotFound,
    bool Conflict)
{
    public bool IsValid => Errors.Count == 0;

    public static ReceiptResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(errors, false, false);

    public static ReceiptResult NotFoundResult() => new(new Dictionary<string, string[]>(), true, false);

    public static ReceiptResult ConflictResult() => new(new Dictionary<string, string[]>(), false, true);

    public static ReceiptResult Accepted() => new(new Dictionary<string, string[]>(), false, false);
}
