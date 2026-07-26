using Microsoft.AspNetCore.Mvc;
using Test_Task.Contracts;
using Test_Task.Services;

namespace Test_Task.Controllers;

[ApiController]
[Route("receipts")]
public sealed class ReceiptsController(ReceiptService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Receive(ProviderReceiptRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ReceiveAsync(request, cancellationToken);
        if (!result.IsValid)
        {
            return this.ToValidationProblem(result.Errors);
        }

        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Conflict
            ? Conflict(new { message = "providerPaymentId не соответствует операции или уже привязан к другой операции." })
            : NoContent();
    }

}
