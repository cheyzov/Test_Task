using System.Globalization;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Test_Task.Models;

namespace Test_Task.Controllers;

[ApiController]
[Route("operations")]
public sealed class OperationsController(TestTaskDbContext db) : ControllerBase
{
    private static readonly Regex AmountPattern =
        new("^[0-9]+(?:\\.[0-9]{1,2})?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [HttpPost]
    [ProducesResponseType(typeof(OperationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateOperationRequest request, CancellationToken cancellationToken)
    {
        var operationId = request.OperationId?.Trim();
        var currency = request.Currency?.Trim().ToUpperInvariant();
        var description = request.Description?.Trim();

        if (string.IsNullOrWhiteSpace(operationId))
        {
            ModelState.AddModelError(nameof(request.OperationId), "operationId is required.");
        }
        else if (operationId.Length > 200)
        {
            ModelState.AddModelError(nameof(request.OperationId), "operationId must be no longer than 200 characters.");
        }

        var amount = 0m;
        if (string.IsNullOrWhiteSpace(request.Amount) || !AmountPattern.IsMatch(request.Amount.Trim()) ||
            !decimal.TryParse(request.Amount.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount) ||
            amount <= 0)
        {
            ModelState.AddModelError(nameof(request.Amount), "amount must be a positive decimal with no more than two fractional digits.");
        }

        if (currency != "RUB")
        {
            ModelState.AddModelError(nameof(request.Currency), "Only RUB currency is supported.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            ModelState.AddModelError(nameof(request.Description), "description is required.");
        }
        else if (description.Length > 1000)
        {
            ModelState.AddModelError(nameof(request.Description), "description must be no longer than 1000 characters.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (await db.Operations.AnyAsync(x => x.OperationId == operationId, cancellationToken))
        {
            return Conflict(new { message = "An operation with this operationId already exists." });
        }

        var now = DateTime.UtcNow;
        var operation = new PaymentOperation
        {
            OperationId = operationId!,
            Amount = amount,
            Currency = currency!,
            Description = description!,
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
            Message = "Operation created",
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
            if (await db.Operations.AnyAsync(x => x.OperationId == operation.OperationId, cancellationToken))
            {
                return Conflict(new { message = "An operation with this operationId already exists." });
            }

            throw;
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = operation.OperationId },
            ToResponse(operation));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(OperationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
    {
        var operation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == id, cancellationToken);
        return operation is null ? NotFound() : Ok(ToResponse(operation));
    }

    [HttpGet("{id}/events")]
    [ProducesResponseType(typeof(IReadOnlyList<OperationEventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvents(string id, CancellationToken cancellationToken)
    {
        var operationExists = await db.Operations
            .AsNoTracking()
            .AnyAsync(x => x.OperationId == id, cancellationToken);

        if (!operationExists)
        {
            return NotFound();
        }

        var events = await db.OperationEvents
            .AsNoTracking()
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

        return Ok(events);
    }

    [HttpPost("{id}/submit")]
    [ProducesResponseType(typeof(OperationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(OperationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submit(string id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var operation = await db.Operations
            .SingleOrDefaultAsync(x => x.OperationId == id, cancellationToken);

        if (operation is null)
        {
            return NotFound();
        }

        if (operation.Status != OperationStatus.CREATED)
        {
            await transaction.CommitAsync(cancellationToken);
            return Ok(ToResponse(operation));
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
            Message = "Operation submitted",
            OccurredAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Accepted(ToResponse(operation));
    }

    private static OperationResponse ToResponse(PaymentOperation operation) => new(
        operation.OperationId,
        operation.Amount.ToString("0.00", CultureInfo.InvariantCulture),
        operation.Currency,
        operation.Description,
        operation.Status.ToString(),
        operation.ProviderPaymentId);
}

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
