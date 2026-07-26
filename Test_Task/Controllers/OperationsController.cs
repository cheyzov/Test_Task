using Microsoft.AspNetCore.Mvc;
using Test_Task.Contracts;
using Test_Task.Services;

namespace Test_Task.Controllers;

[ApiController]
[Route("operations")]
public sealed class OperationsController(OperationService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(OperationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateOperationRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        if (!result.IsValid)
        {
            return this.ToValidationProblem(result.Errors);
        }

        if (result.Conflict)
        {
            return Conflict(new { message = "Операция с таким operationId уже существует." });
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Response!.OperationId }, result.Response);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(OperationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
    {
        var response = await service.GetByIdAsync(id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{id}/events")]
    [ProducesResponseType(typeof(IReadOnlyList<OperationEventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvents(string id, CancellationToken cancellationToken)
    {
        var events = await service.GetEventsAsync(id, cancellationToken);
        return events is null ? NotFound() : Ok(events);
    }

    [HttpPost("{id}/submit")]
    [ProducesResponseType(typeof(OperationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(OperationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submit(string id, CancellationToken cancellationToken)
    {
        var result = await service.SubmitAsync(id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Accepted ? Accepted(result.Response) : Ok(result.Response);
    }

}
