using System.Text.Json;
using EventGateway.Data;
using EventGateway.Models;
using EventGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventGateway.Controllers;

[ApiController]
public class EventsController(
    EventDbContext db,
    AccountServiceClient accountClient,
    ILogger<EventsController> logger) : ControllerBase
{
    [HttpPost("events")]
    public async Task<IActionResult> SubmitEvent([FromBody] EventRequest request)
    {
        var traceId = HttpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();

        logger.LogInformation("Received event {EventId} for account {AccountId} traceId={TraceId}",
            request.EventId, request.AccountId, traceId);

        var existing = await db.Events.FirstOrDefaultAsync(e => e.EventId == request.EventId);
        if (existing != null)
        {
            logger.LogInformation("Duplicate event {EventId} traceId={TraceId}", request.EventId, traceId);
            return StatusCode(200, ToResponse(existing));
        }

        var record = new EventRecord
        {
            EventId = request.EventId,
            AccountId = request.AccountId,
            Type = request.Type,
            Amount = request.Amount,
            Currency = request.Currency,
            EventTimestamp = request.EventTimestamp,
            ReceivedAt = DateTime.UtcNow,
            MetadataJson = request.Metadata != null ? JsonSerializer.Serialize(request.Metadata) : null
        };

        db.Events.Add(record);
        await db.SaveChangesAsync();

        var txPayload = new
        {
            eventId = request.EventId,
            accountId = request.AccountId,
            type = request.Type,
            amount = request.Amount,
            currency = request.Currency,
            eventTimestamp = request.EventTimestamp
        };

        try
        {
            var applied = await accountClient.ApplyTransactionAsync(request.AccountId, txPayload, traceId);
            if (!applied)
            {
                logger.LogWarning("Account service rejected transaction {EventId} traceId={TraceId}", request.EventId, traceId);
                return StatusCode(503, new { error = "Account service unavailable or rejected transaction", eventId = request.EventId });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Account service call failed for {EventId} traceId={TraceId}", request.EventId, traceId);
            return StatusCode(503, new { error = "Account service unavailable", eventId = request.EventId });
        }

        logger.LogInformation("Event {EventId} processed successfully traceId={TraceId}", request.EventId, traceId);
        return StatusCode(201, ToResponse(record));
    }

    [HttpGet("events/{id}")]
    public async Task<IActionResult> GetEvent(string id)
    {
        var traceId = HttpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
        var ev = await db.Events.FirstOrDefaultAsync(e => e.EventId == id);
        if (ev == null)
        {
            logger.LogWarning("Event {EventId} not found traceId={TraceId}", id, traceId);
            return NotFound(new { error = $"Event '{id}' not found" });
        }
        return Ok(ToResponse(ev));
    }

    [HttpGet("events")]
    public async Task<IActionResult> ListEvents([FromQuery] string account)
    {
        var traceId = HttpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
        logger.LogInformation("Listing events for account {AccountId} traceId={TraceId}", account, traceId);

        var events = await db.Events
            .Where(e => e.AccountId == account)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync();

        return Ok(events.Select(ToResponse));
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            return Ok(new { status = "healthy", service = "event-gateway", database = "connected", timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "unhealthy", service = "event-gateway", database = "disconnected" });
        }
    }

    private static object ToResponse(EventRecord e) => new
    {
        eventId = e.EventId,
        accountId = e.AccountId,
        type = e.Type,
        amount = e.Amount,
        currency = e.Currency,
        eventTimestamp = e.EventTimestamp,
        receivedAt = e.ReceivedAt
    };
}
