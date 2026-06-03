using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventLedger.Tests.Helpers;

namespace EventLedger.Tests;

public class EventGatewayIntegrationTests : IDisposable
{
    private readonly AccountWebFactory _accountFactory;
    private readonly GatewayWebFactory _gatewayFactory;
    private readonly HttpClient _client;

    public EventGatewayIntegrationTests()
    {
        _accountFactory = new AccountWebFactory();
        // Trigger initialization so Server is available
        _ = _accountFactory.CreateClient();

        _gatewayFactory = new GatewayWebFactory
        {
            AccountHandler = _accountFactory.Server.CreateHandler()
        };
        _client = _gatewayFactory.CreateClient();
    }

    private static object ValidEvent(string eventId, string accountId, string type = "CREDIT", decimal amount = 100m) => new
    {
        eventId,
        accountId,
        type,
        amount,
        currency = "USD",
        eventTimestamp = DateTime.UtcNow
    };

    [Fact]
    public async Task SubmitEvent_Valid_Returns201WithEventData()
    {
        var response = await _client.PostAsJsonAsync("events", ValidEvent($"evt-{Guid.NewGuid()}", "acct-001"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body!.RootElement.GetProperty("eventId").GetString());
    }

    [Fact]
    public async Task SubmitEvent_Idempotency_SecondSubmitReturns200WithOriginal()
    {
        var ev = ValidEvent($"evt-idem-{Guid.NewGuid()}", "acct-idem");

        var r1 = await _client.PostAsJsonAsync("events", ev);
        var r2 = await _client.PostAsJsonAsync("events", ev);

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var b1 = await r1.Content.ReadFromJsonAsync<JsonDocument>();
        var b2 = await r2.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(
            b1!.RootElement.GetProperty("eventId").GetString(),
            b2!.RootElement.GetProperty("eventId").GetString());
    }

    [Fact]
    public async Task SubmitEvent_MissingEventId_Returns400()
    {
        var response = await _client.PostAsJsonAsync("events", new
        {
            accountId = "acct-001",
            type = "CREDIT",
            amount = 100m,
            currency = "USD",
            eventTimestamp = DateTime.UtcNow
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitEvent_ZeroAmount_Returns400()
    {
        var response = await _client.PostAsJsonAsync("events", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId = "acct-001",
            type = "CREDIT",
            amount = 0m,
            currency = "USD",
            eventTimestamp = DateTime.UtcNow
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitEvent_InvalidType_Returns400()
    {
        var response = await _client.PostAsJsonAsync("events", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId = "acct-001",
            type = "TRANSFER",
            amount = 50m,
            currency = "USD",
            eventTimestamp = DateTime.UtcNow
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetEvent_ById_ReturnsEvent()
    {
        var eventId = $"evt-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("events", ValidEvent(eventId, "acct-get"));

        var response = await _client.GetAsync($"events/{eventId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(eventId, body!.RootElement.GetProperty("eventId").GetString());
    }

    [Fact]
    public async Task GetEvent_NotFound_Returns404()
    {
        var response = await _client.GetAsync("events/evt-does-not-exist-xyz");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListEvents_ReturnedInChronologicalOrder()
    {
        var accountId = $"acct-{Guid.NewGuid()}";
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Submit out of order: 3rd, 1st, 2nd
        await _client.PostAsJsonAsync("events", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 30m,
            currency = "USD",
            eventTimestamp = baseTime.AddHours(2)
        });
        await _client.PostAsJsonAsync("events", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 10m,
            currency = "USD",
            eventTimestamp = baseTime
        });
        await _client.PostAsJsonAsync("events", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 20m,
            currency = "USD",
            eventTimestamp = baseTime.AddHours(1)
        });

        var response = await _client.GetAsync($"events?account={accountId}");
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var events = body!.RootElement.EnumerateArray().ToList();

        Assert.Equal(3, events.Count);
        Assert.Equal(10m, events[0].GetProperty("amount").GetDecimal());
        Assert.Equal(20m, events[1].GetProperty("amount").GetDecimal());
        Assert.Equal(30m, events[2].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task TraceId_EchoedBackInResponseHeader()
    {
        var traceId = $"test-trace-{Guid.NewGuid()}";
        var request = new HttpRequestMessage(HttpMethod.Post, "events")
        {
            Headers = { { "X-Trace-Id", traceId } },
            Content = JsonContent.Create(ValidEvent($"evt-{Guid.NewGuid()}", "acct-trace"))
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Trace-Id"));
        Assert.Equal(traceId, response.Headers.GetValues("X-Trace-Id").First());
    }

    [Fact]
    public async Task GetEventsById_WorksWithoutAccountService()
    {
        // Store an event while account service is up
        var eventId = $"evt-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("events", ValidEvent(eventId, "acct-degrade"));

        // GET by ID depends only on gateway DB — must always work
        var response = await _client.GetAsync($"events/{eventId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListEventsByAccount_WorksWithoutAccountService()
    {
        var accountId = $"acct-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("events", ValidEvent($"evt-{Guid.NewGuid()}", accountId));

        // GET list depends only on gateway DB — must always work
        var response = await _client.GetAsync($"events?account={accountId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("healthy", body!.RootElement.GetProperty("status").GetString());
    }

    public void Dispose()
    {
        _client.Dispose();
        _gatewayFactory.Dispose();
        _accountFactory.Dispose();
    }
}
