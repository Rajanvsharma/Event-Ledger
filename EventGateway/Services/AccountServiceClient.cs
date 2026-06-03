using System.Net.Http.Json;
using System.Text.Json;

namespace EventGateway.Services;

public class AccountServiceClient(HttpClient httpClient, ILogger<AccountServiceClient> logger)
{
    public async Task<bool> ApplyTransactionAsync(
        string accountId,
        object request,
        string traceId,
        CancellationToken ct = default)
    {
        httpClient.DefaultRequestHeaders.Remove("X-Trace-Id");
        httpClient.DefaultRequestHeaders.Add("X-Trace-Id", traceId);

        logger.LogInformation("Calling account service for account {AccountId} traceId={TraceId}", accountId, traceId);

        var response = await httpClient.PostAsJsonAsync($"accounts/{accountId}/transactions", request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Account service returned {Status} for account {AccountId} traceId={TraceId} body={Body}",
                response.StatusCode, accountId, traceId, body);
            return false;
        }

        logger.LogInformation("Account service applied transaction for account {AccountId} traceId={TraceId}", accountId, traceId);
        return true;
    }

    public async Task<JsonDocument?> GetBalanceAsync(string accountId, string traceId, CancellationToken ct = default)
    {
        httpClient.DefaultRequestHeaders.Remove("X-Trace-Id");
        httpClient.DefaultRequestHeaders.Add("X-Trace-Id", traceId);

        var response = await httpClient.GetAsync($"accounts/{accountId}/balance", ct);
        if (!response.IsSuccessStatusCode) return null;

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
