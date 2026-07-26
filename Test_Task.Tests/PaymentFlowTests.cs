using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Test_Task.Controllers;
using Test_Task.Contracts;
using Test_Task.Models;
using Test_Task.Services;
using Xunit;

namespace Test_Task.Tests;

public sealed class PaymentFlowTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public Task InitializeAsync()
    {
        _database = new TestDatabase();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Successful_flow_reaches_completed()
    {
        var id = await CreateAndSubmit();

        var result = await ReceiveReceipt(id, "provider-success", "COMPLETED");

        Assert.IsType<NoContentResult>(result);
        await AssertOperation(id, OperationStatus.COMPLETED, "provider-success", 3);
    }

    [Fact]
    public async Task Rejected_flow_reaches_rejected()
    {
        var id = await CreateAndSubmit();

        await ReceiveReceipt(id, "provider-rejected", "REJECTED");

        await AssertOperation(id, OperationStatus.REJECTED, "provider-rejected", 3);
    }

    [Fact]
    public async Task Parallel_submit_creates_one_dispatch_and_one_transition()
    {
        var id = await CreateOperation();
        var tasks = Enumerable.Range(0, 2).Select(_ => Submit(id)).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(x => x is AcceptedResult));
        Assert.Equal(1, results.Count(x => x is OkObjectResult));

        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.PaymentDispatches.CountAsync(x => x.OperationId == id));
        Assert.Equal(2, await db.OperationEvents.CountAsync(x => x.OperationId == id));
    }

    [Fact]
    public async Task Repeated_callback_returns_204_without_new_event()
    {
        var id = await CreateAndSubmit();

        var first = await ReceiveReceipt(id, "provider-repeat", "COMPLETED");
        var second = await ReceiveReceipt(id, "provider-repeat", "COMPLETED");

        Assert.IsType<NoContentResult>(first);
        Assert.IsType<NoContentResult>(second);
        await AssertOperation(id, OperationStatus.COMPLETED, "provider-repeat", 3);
    }

    [Fact]
    public async Task Late_opposite_callback_is_recorded_as_ignored()
    {
        var id = await CreateAndSubmit();

        await ReceiveReceipt(id, "provider-late", "COMPLETED");
        var late = await ReceiveReceipt(id, "provider-late", "REJECTED");

        Assert.IsType<NoContentResult>(late);
        await AssertOperation(id, OperationStatus.COMPLETED, "provider-late", 3);

        await using var db = _database.CreateContext();
        var ignoredReceipt = await db.ProviderReceipts.SingleAsync(x =>
            x.OperationId == id && x.ProviderPaymentId == "provider-late" && x.Result == "REJECTED");
        Assert.True(ignoredReceipt.Ignored);
    }

    [Fact]
    public async Task Callback_before_provider_response_is_final_and_not_resubmitted()
    {
        var id = await CreateAndSubmit();

        await ReceiveReceipt(id, "provider-early", "COMPLETED");

        await using var db = _database.CreateContext();
        var operation = await db.Operations.SingleAsync(x => x.OperationId == id);
        var job = await db.PaymentDispatches.SingleAsync(x => x.OperationId == id);
        Assert.Equal(OperationStatus.COMPLETED, operation.Status);
        Assert.Equal("provider-early", operation.ProviderPaymentId);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public async Task Lost_provider_response_is_retried_after_restart_with_same_body()
    {
        var id = await CreateAndSubmit();
        var lostResponse = new RecordingHandler(_ => throw new HttpRequestException("connection reset"));

        await RunWorkerOnce(lostResponse);
        await WaitForJob(id, job => job.LastError is not null);

        await using (var db = _database.CreateContext())
        {
            var job = await db.PaymentDispatches.SingleAsync(x => x.OperationId == id);
            job.NextAttemptAt = DateTime.UtcNow.AddMilliseconds(-1);
            await db.SaveChangesAsync();
        }

        var recovered = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new { providerPaymentId = "provider-recovered", status = "ACCEPTED" })
        }));
        await RunWorkerOnce(recovered);
        await WaitForOperation(id, operation => operation.ProviderPaymentId == "provider-recovered");

        await AssertOperation(id, OperationStatus.PROCESSING, "provider-recovered", 2);
        Assert.Equal(lostResponse.Bodies.Single(), recovered.Bodies.Single());
        Assert.Equal(id, recovered.Requests.Single().Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(id, recovered.Requests.Single().Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Rejected_provider_response_keeps_processing_and_schedules_retry()
    {
        var id = await CreateAndSubmit();
        var provider = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        await RunWorkerOnce(provider);
        await WaitForJob(id, job => job.LastError?.Contains("503") == true);

        await using var db = _database.CreateContext();
        var operation = await db.Operations.SingleAsync(x => x.OperationId == id);
        var job = await db.PaymentDispatches.SingleAsync(x => x.OperationId == id);
        Assert.Equal(OperationStatus.PROCESSING, operation.Status);
        Assert.Null(operation.ProviderPaymentId);
        Assert.True(job.NextAttemptAt > DateTime.UtcNow);
        Assert.Contains("503", job.LastError);
    }

    private async Task<string> CreateOperation()
    {
        var id = $"operation-{Guid.NewGuid():N}";
        await using var db = _database.CreateContext();
        var controller = new OperationsController(new OperationService(db));
        var result = await controller.Create(new CreateOperationRequest
        {
            OperationId = id,
            Amount = "100.00",
            Currency = "RUB",
            Description = "Test payment"
        }, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(result);
        return id;
    }

    private async Task<string> CreateAndSubmit()
    {
        var id = await CreateOperation();
        var result = await Submit(id);
        Assert.IsType<AcceptedResult>(result);
        return id;
    }

    private async Task<IActionResult> Submit(string id)
    {
        await using var db = _database.CreateContext();
        return await new OperationsController(new OperationService(db)).Submit(id, CancellationToken.None);
    }

    private async Task<IActionResult> ReceiveReceipt(string id, string providerPaymentId, string result)
    {
        await using var db = _database.CreateContext();
        return await new ReceiptsController(new ReceiptService(db)).Receive(new ProviderReceiptRequest
        {
            OperationId = id,
            ProviderPaymentId = providerPaymentId,
            Result = result,
            Message = $"Payment {result.ToLowerInvariant()}"
        }, CancellationToken.None);
    }

    private async Task RunWorkerOnce(RecordingHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_database);
        services.AddDbContext<TestTaskDbContext>(options => options.UseSqlite(_database.ConnectionString));
        services.AddHttpClient("provider").ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var worker = new DispatchJobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Provider:Url"] = "http://provider"
            }).Build(),
            NullLogger<DispatchJobWorker>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(cancellation.Token);
        await handler.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        cancellation.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    private async Task WaitForJob(string id, Func<PaymentDispatch, bool> predicate)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await using var db = _database.CreateContext();
            var job = await db.PaymentDispatches.SingleAsync(x => x.OperationId == id);
            if (predicate(job))
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Dispatch job did not reach the expected state.");
    }

    private async Task WaitForOperation(string id, Func<PaymentOperation, bool> predicate)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await using var db = _database.CreateContext();
            var operation = await db.Operations.SingleAsync(x => x.OperationId == id);
            if (predicate(operation))
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Operation did not reach the expected state.");
    }

    private async Task AssertOperation(string id, OperationStatus status, string providerPaymentId, int eventCount)
    {
        await using var db = _database.CreateContext();
        var operation = await db.Operations.SingleAsync(x => x.OperationId == id);
        Assert.Equal(status, operation.Status);
        Assert.Equal(providerPaymentId, operation.ProviderPaymentId);
        Assert.Equal(eventCount, await db.OperationEvents.CountAsync(x => x.OperationId == id));
    }
}

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _anchor;

    public TestDatabase()
    {
        ConnectionString = $"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
        _anchor = new SqliteConnection(ConnectionString);
        _anchor.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public string ConnectionString { get; }

    public TestTaskDbContext CreateContext()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return new TestTaskDbContext(new DbContextOptionsBuilder<TestTaskDbContext>()
            .UseSqlite(connection)
            .Options);
    }

    public ValueTask DisposeAsync() => _anchor.DisposeAsync();
}

internal sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
        try
        {
            return await handler(request);
        }
        finally
        {
            Completed.TrySetResult();
        }
    }
}
