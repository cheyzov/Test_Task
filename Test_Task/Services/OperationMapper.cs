using System.Globalization;
using Test_Task.Contracts;
using Test_Task.Models;

namespace Test_Task.Services;

public static class OperationMapper
{
    public static OperationResponse ToResponse(PaymentOperation operation) => new(
        operation.OperationId,
        operation.Amount.ToString("0.00", CultureInfo.InvariantCulture),
        operation.Currency,
        operation.Description,
        operation.Status.ToString(),
        operation.ProviderPaymentId);

}
