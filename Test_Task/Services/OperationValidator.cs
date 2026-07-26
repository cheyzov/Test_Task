using System.Globalization;
using System.Text.RegularExpressions;
using Test_Task.Contracts;

namespace Test_Task.Services;

public static partial class OperationValidator
{
    [GeneratedRegex("^[0-9]+(?:\\.[0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    public static IReadOnlyDictionary<string, string[]> ValidateCreate(CreateOperationRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var operationId = request.OperationId?.Trim();
        var amountText = request.Amount?.Trim();
        var currency = request.Currency?.Trim().ToUpperInvariant();
        var description = request.Description?.Trim();

        if (string.IsNullOrWhiteSpace(operationId))
        {
            Add(errors, nameof(request.OperationId), "Поле operationId обязательно.");
        }
        else if (operationId.Length > 200)
        {
            Add(errors, nameof(request.OperationId), "Длина operationId не должна превышать 200 символов.");
        }

        if (string.IsNullOrWhiteSpace(amountText) || !AmountPattern().IsMatch(amountText) ||
            !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
        {
            Add(errors, nameof(request.Amount), "Сумма должна быть положительным числом не более чем с двумя знаками после запятой.");
        }

        if (currency != "RUB")
        {
            Add(errors, nameof(request.Currency), "Поддерживается только валюта RUB.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            Add(errors, nameof(request.Description), "Поле description обязательно.");
        }
        else if (description.Length > 1000)
        {
            Add(errors, nameof(request.Description), "Длина description не должна превышать 1000 символов.");
        }

        return errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, string[]> ValidateReceipt(ProviderReceiptRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.OperationId))
        {
            Add(errors, nameof(request.OperationId), "Поле operationId обязательно.");
        }

        if (string.IsNullOrWhiteSpace(request.ProviderPaymentId))
        {
            Add(errors, nameof(request.ProviderPaymentId), "Поле providerPaymentId обязательно.");
        }

        var result = request.Result?.Trim().ToUpperInvariant();
        if (result is not ("COMPLETED" or "REJECTED"))
        {
            Add(errors, nameof(request.Result), "Поле result должно иметь значение COMPLETED или REJECTED.");
        }

        return errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}
