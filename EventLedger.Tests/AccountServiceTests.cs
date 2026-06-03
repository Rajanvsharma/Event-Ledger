using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventLedger.Tests.Helpers;

namespace EventLedger.Tests;

public class AccountServiceTests : IDisposable
{
    private readonly AccountWebFactory _factory;
    private readonly HttpClient _client;

    public AccountServiceTests()
    {
        _factory = new AccountWebFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task ApplyTransaction_ValidCredit_ReturnsOk()
    {
        var response = await _client.PostAsJsonAsync("accounts/acct-001/transactions", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId = "acct-001",
            type = "CREDIT",
            amount = 100.00m,
            currency = "USD",
            eventTimestamp = DateTime.UtcNow
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApplyTransaction_Duplicate_ReturnsDuplicateMessage()
    {
        var eventId = $"evt-dup-{Guid.NewGuid()}";
        var payload = new
        {
            eventId,
            accountId = "acct-dup",
            type = "CREDIT",
            amount = 100.00m,
            currency = "USD",
            eventTimestamp = DateTime.UtcNow
        };

        await _client.PostAsJsonAsync("accounts/acct-dup/transactions", payload);
        var r2 = await _client.PostAsJsonAsync("accounts/acct-dup/transactions", payload);

        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var body = await r2.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("duplicate", body!.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetBalance_CreditMinusDebit_IsCorrect()
    {
        var accountId = $"acct-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync($"accounts/{accountId}/transactions", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 500.00m,
            currency = "USD",
            eventTimestamp = DateTime.UtcNow.AddHours(-2)
        });

        await _client.PostAsJsonAsync($"accounts/{accountId}/transactions", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "DEBIT",
            amount = 200.00m,
            currency = "USD",
            eventTimestamp = DateTime.UtcNow.AddHours(-1)
        });

        var response = await _client.GetAsync($"accounts/{accountId}/balance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(300.00m, body!.RootElement.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task GetBalance_OutOfOrderArrival_BalanceIsStillCorrect()
    {
        var accountId = $"acct-{Guid.NewGuid()}";
        var now = DateTime.UtcNow;

        // Later event arrives first
        await _client.PostAsJsonAsync($"accounts/{accountId}/transactions", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "DEBIT",
            amount = 100.00m,
            currency = "USD",
            eventTimestamp = now
        });

        // Earlier event arrives second
        await _client.PostAsJsonAsync($"accounts/{accountId}/transactions", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 500.00m,
            currency = "USD",
            eventTimestamp = now.AddDays(-3)
        });

        var response = await _client.GetAsync($"accounts/{accountId}/balance");
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(400.00m, body!.RootElement.GetProperty("balance").GetDecimal());
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
        _factory.Dispose();
    }
}
