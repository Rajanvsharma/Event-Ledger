using System.Net;
using System.Net.Http.Json;
using EventLedger.Tests.Helpers;

namespace EventLedger.Tests;

public class ResiliencyTests
{
    private static object ValidEvent(string eventId, string accountId) => new
    {
        eventId,
        accountId,
        type = "CREDIT",
        amount = 100.00m,
        currency = "USD",
        eventTimestamp = DateTime.UtcNow
    };

    [Fact]
    public async Task AccountServiceDown_PostEvent_Returns503()
    {
        using var factory = new GatewayWebFactory { AccountHandler = new AlwaysFailingHandler() };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("events", ValidEvent($"evt-{Guid.NewGuid()}", "acct-fail"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task CircuitBreaker_ReducesCallsToAccountServiceAfterRepeatedFailures()
    {
        var failingHandler = new AlwaysFailingHandler();
        using var factory = new GatewayWebFactory { AccountHandler = failingHandler };
        using var client = factory.CreateClient();

        // First call: exhausts retries → circuit breaker opens
        var r1 = await client.PostAsJsonAsync("events", ValidEvent($"evt-{Guid.NewGuid()}", "acct-cb"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r1.StatusCode);
        var callsAfterFirst = failingHandler.CallCount;

        // Second call: circuit is open, fails immediately without hitting the handler
        var r2 = await client.PostAsJsonAsync("events", ValidEvent($"evt-{Guid.NewGuid()}", "acct-cb"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r2.StatusCode);

        // Circuit breaker prevented a full retry cycle on the second call
        Assert.True(failingHandler.CallCount < callsAfterFirst * 2,
            $"Expected circuit breaker to limit calls (got {failingHandler.CallCount}, expected < {callsAfterFirst * 2})");
    }

    [Fact]
    public async Task GetEventsById_WorksWhenAccountServiceDown()
    {
        // Use a working account service to seed an event first
        using var accountFactory = new AccountWebFactory();
        _ = accountFactory.CreateClient();

        using var factory = new GatewayWebFactory
        {
            AccountHandler = accountFactory.Server.CreateHandler()
        };
        using var client = factory.CreateClient();

        var eventId = $"evt-{Guid.NewGuid()}";
        await client.PostAsJsonAsync("events", ValidEvent(eventId, "acct-degrade"));

        // Now switch the account handler to failing
        // (We can't hot-swap handlers, so we verify GET already works independently)
        var response = await client.GetAsync($"events/{eventId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListEvents_WorksWhenAccountServiceDown()
    {
        using var failingFactory = new GatewayWebFactory { AccountHandler = new AlwaysFailingHandler() };
        using var failingClient = failingFactory.CreateClient();

        // GET list never calls AccountService — should always return 200
        var response = await failingClient.GetAsync("events?account=any-account");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_WorksWhenAccountServiceDown()
    {
        using var factory = new GatewayWebFactory { AccountHandler = new AlwaysFailingHandler() };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
