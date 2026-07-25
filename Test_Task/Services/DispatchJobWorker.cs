using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Test_Task.Models;

namespace Test_Task.Services;

public sealed class DispatchJobWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<DispatchJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Dispatch job worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextJob(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected error while processing dispatch jobs");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Dispatch job worker stopped");
    }

    private async Task<bool> ProcessNextJob(CancellationToken cancellationToken)
    {
        DispatchAttempt? attempt;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestTaskDbContext>();
            var now = DateTime.UtcNow;
            var job = await db.PaymentDispatches
                .Include(x => x.Operation)
                .Where(x => x.Operation.Status == OperationStatus.PROCESSING &&
                            (x.NextAttemptAt == null || x.NextAttemptAt <= now))
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (job is null)
            {
                return false;
            }

            job.AttemptCount++;
            job.LastAttemptAt = now;
            job.NextAttemptAt = now.Add(Backoff(job.AttemptCount));
            job.LastError = null;
            await db.SaveChangesAsync(cancellationToken);

            attempt = new DispatchAttempt(
                job.OperationId,
                job.RequestBody,
                job.AttemptCount);
        }

        try
        {
            var providerUrl = configuration["PROVIDER_URL"] ?? configuration["Provider:Url"];
            if (string.IsNullOrWhiteSpace(providerUrl))
            {
                throw new InvalidOperationException("Provider URL is not configured.");
            }

            var endpoint = new Uri($"{providerUrl.TrimEnd('/')}/payments");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(attempt.RequestBody, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Idempotency-Key", attempt.OperationId);
            request.Headers.Add("X-Correlation-ID", attempt.OperationId);

            var client = httpClientFactory.CreateClient("provider");
            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var providerPaymentId = ReadProviderPaymentId(responseBody);
                await MarkSucceeded(attempt.OperationId, providerPaymentId, cancellationToken);
                logger.LogInformation(
                    "Provider accepted operation {OperationId} on attempt {Attempt}",
                    attempt.OperationId,
                    attempt.AttemptCount);
            }
            else
            {
                await MarkFailed(attempt.OperationId, $"Provider returned {(int)response.StatusCode}: {responseBody}", cancellationToken);
                logger.LogWarning(
                    "Provider rejected operation {OperationId} on attempt {Attempt} with status {StatusCode}",
                    attempt.OperationId,
                    attempt.AttemptCount,
                    (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await MarkFailed(attempt.OperationId, exception.Message, cancellationToken);
            logger.LogWarning(exception, "Provider request failed for operation {OperationId}", attempt.OperationId);
        }

        return true;
    }

    private async Task MarkSucceeded(string operationId, string? providerPaymentId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestTaskDbContext>();
        var job = await db.PaymentDispatches.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        job.NextAttemptAt = null;
        job.LastError = null;

        if (providerPaymentId is not null)
        {
            var operation = await db.Operations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
            operation.ProviderPaymentId ??= providerPaymentId;
            operation.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailed(string operationId, string error, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestTaskDbContext>();
        var job = await db.PaymentDispatches.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        job.LastError = error.Length > 2000 ? error[..2000] : error;
        job.NextAttemptAt = DateTime.UtcNow.Add(Backoff(job.AttemptCount));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Min(30, Math.Pow(2, Math.Min(attempt - 1, 5)));
        return TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble());
    }

    private static string? ReadProviderPaymentId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.TryGetProperty("providerPaymentId", out var property)
            ? property.GetString()
            : null;
    }

    private sealed record DispatchAttempt(
        string OperationId,
        string RequestBody,
        int AttemptCount);
}
