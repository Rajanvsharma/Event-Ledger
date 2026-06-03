using AccountService.Data;
using AccountService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Controllers;

[ApiController]
[Route("accounts")]
public class AccountsController(AccountDbContext db, ILogger<AccountsController> logger) : ControllerBase
{
    [HttpPost("{accountId}/transactions")]
    public async Task<IActionResult> ApplyTransaction(string accountId, [FromBody] ApplyTransactionRequest request)
    {
        var traceId = Request.Headers["X-Trace-Id"].FirstOrDefault() ?? "unknown";
        logger.LogInformation("Applying transaction {EventId} for account {AccountId} traceId={TraceId}",
            request.EventId, accountId, traceId);

        if (request.AccountId != accountId)
            return BadRequest(new { error = "AccountId mismatch" });

        var existing = await db.Transactions.FirstOrDefaultAsync(t => t.EventId == request.EventId);
        if (existing != null)
        {
            logger.LogInformation("Duplicate transaction {EventId} ignored traceId={TraceId}", request.EventId, traceId);
            return Ok(new { message = "duplicate", eventId = request.EventId });
        }

        var tx = new Transaction
        {
            EventId = request.EventId,
            AccountId = accountId,
            Type = request.Type,
            Amount = request.Amount,
            Currency = request.Currency,
            EventTimestamp = request.EventTimestamp,
            ReceivedAt = DateTime.UtcNow
        };

        db.Transactions.Add(tx);
        await db.SaveChangesAsync();

        logger.LogInformation("Transaction {EventId} applied traceId={TraceId}", request.EventId, traceId);
        return Ok(new { message = "applied", eventId = request.EventId });
    }

    [HttpGet("{accountId}/balance")]
    public async Task<IActionResult> GetBalance(string accountId)
    {
        var traceId = Request.Headers["X-Trace-Id"].FirstOrDefault() ?? "unknown";
        logger.LogInformation("Getting balance for account {AccountId} traceId={TraceId}", accountId, traceId);

        var transactions = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .ToListAsync();

        var credits = transactions.Where(t => t.Type == "CREDIT").Sum(t => t.Amount);
        var debits = transactions.Where(t => t.Type == "DEBIT").Sum(t => t.Amount);
        var balance = credits - debits;

        return Ok(new
        {
            accountId,
            balance,
            currency = transactions.FirstOrDefault()?.Currency ?? "USD",
            transactionCount = transactions.Count
        });
    }

    [HttpGet("{accountId}")]
    public async Task<IActionResult> GetAccount(string accountId)
    {
        var traceId = Request.Headers["X-Trace-Id"].FirstOrDefault() ?? "unknown";
        logger.LogInformation("Getting account {AccountId} traceId={TraceId}", accountId, traceId);

        var transactions = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.EventTimestamp)
            .Take(20)
            .ToListAsync();

        var allTx = await db.Transactions.Where(t => t.AccountId == accountId).ToListAsync();
        var credits = allTx.Where(t => t.Type == "CREDIT").Sum(t => t.Amount);
        var debits = allTx.Where(t => t.Type == "DEBIT").Sum(t => t.Amount);

        return Ok(new
        {
            accountId,
            balance = credits - debits,
            transactionCount = allTx.Count,
            recentTransactions = transactions.Select(t => new
            {
                t.EventId,
                t.Type,
                t.Amount,
                t.Currency,
                t.EventTimestamp
            })
        });
    }

    [HttpGet("/health")]
    public async Task<IActionResult> Health()
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            return Ok(new { status = "healthy", service = "account-service", database = "connected", timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "unhealthy", service = "account-service", database = "disconnected" });
        }
    }
}
