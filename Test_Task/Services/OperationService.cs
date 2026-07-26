using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Test_Task.Contracts;
using Test_Task.Models;

namespace Test_Task.Services;

public sealed class OperationService(TestTaskDbContext db)
{
    public async Task<CreateOperationResult> CreateAsync(
        CreateOperationRequest request,
        CancellationToken cancellationToken)
    {
        var errors = OperationValidator.ValidateCreate(request);
        if (errors.Count > 0)
        {
            return CreateOperationResult.Invalid(errors);
        }

        var operationId = request.OperationId!.Trim();
        if (await db.Operations.AnyAsync(x => x.OperationId == operationId, cancellationToken))
        {
            return CreateOperationResult.ConflictResult();
        }

        var now = DateTime.UtcNow;
        var operation = new PaymentOperation
        {
            OperationId = operationId,
            Amount = decimal.Parse(request.Amount!.Trim(), CultureInfo.InvariantCulture),
            Currency = request.Currency!.Trim().ToUpperInvariant(),
            Description = request.Description!.Trim(),
            Status = OperationStatus.CREATED,
            CreatedAt = now,
            UpdatedAt = now
        };
        operation.Events.Add(new OperationEvent
        {
            OperationId = operation.OperationId,
            FromStatus = null,
            ToStatus = OperationStatus.CREATED,
            Type = nameof(OperationStatus.CREATED),
            Message = "Операция создана",
            OccurredAt = now
        });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Operations.Add(operation);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await db.Operations.AnyAsync(x => x.OperationId == operationId, cancellationToken))
            {
                return CreateOperationResult.ConflictResult();
            }

            throw;
        }

        return CreateOperationResult.Created(OperationMapper.ToResponse(operation));
    }

    public async Task<OperationResponse?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        var operation = await db.Operations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperationId == id, cancellationToken);
        return operation is null ? null : OperationMapper.ToResponse(operation);
    }

    public async Task<IReadOnlyList<OperationEventResponse>?> GetEventsAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var operationExists = await db.Operations.AsNoTracking()
            .AnyAsync(x => x.OperationId == id, cancellationToken);
        if (!operationExists)
        {
            return null;
        }

        return await db.OperationEvents.AsNoTracking()
            .Where(x => x.OperationId == id)
            .OrderBy(x => x.EventId)
            .Select(x => new OperationEventResponse(
                x.EventId,
                x.Type,
                x.FromStatus == null ? null : x.FromStatus.Value.ToString(),
                x.ToStatus.ToString(),
                x.Message,
                x.OccurredAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubmitOperationResult> SubmitAsync(string id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var operation = await db.Operations.SingleOrDefaultAsync(x => x.OperationId == id, cancellationToken);
        if (operation is null)
        {
            return SubmitOperationResult.NotFoundResult();
        }

        if (operation.Status != OperationStatus.CREATED)
        {
            await transaction.CommitAsync(cancellationToken);
            return SubmitOperationResult.AlreadySubmitted(OperationMapper.ToResponse(operation));
        }

        var now = DateTime.UtcNow;
        operation.Status = OperationStatus.PROCESSING;
        operation.UpdatedAt = now;
        operation.Dispatch = new PaymentDispatch
        {
            OperationId = operation.OperationId,
            RequestBody = JsonSerializer.Serialize(new
            {
                operationId = operation.OperationId,
                amount = operation.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                currency = operation.Currency
            }),
            AttemptCount = 0,
            CreatedAt = now,
            NextAttemptAt = now
        };
        operation.Events.Add(new OperationEvent
        {
            OperationId = operation.OperationId,
            FromStatus = OperationStatus.CREATED,
            ToStatus = OperationStatus.PROCESSING,
            Type = nameof(OperationStatus.PROCESSING),
            Message = "Операция отправлена на обработку",
            OccurredAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return SubmitOperationResult.AcceptedResult(OperationMapper.ToResponse(operation));
    }
}

public sealed record CreateOperationResult(
    OperationResponse? Response,
    IReadOnlyDictionary<string, string[]> Errors,
    bool Conflict)
{
    public bool IsValid => Errors.Count == 0;

    public static CreateOperationResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors, false);

    public static CreateOperationResult ConflictResult() => new(null, new Dictionary<string, string[]>(), true);

    public static CreateOperationResult Created(OperationResponse response) => new(response, new Dictionary<string, string[]>(), false);
}

public sealed record SubmitOperationResult(OperationResponse? Response, bool NotFound, bool Accepted)
{
    public static SubmitOperationResult NotFoundResult() => new(null, true, false);

    public static SubmitOperationResult AlreadySubmitted(OperationResponse response) => new(response, false, false);

    public static SubmitOperationResult AcceptedResult(OperationResponse response) => new(response, false, true);
}
