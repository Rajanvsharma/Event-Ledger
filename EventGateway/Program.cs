using EventGateway.Data;
using EventGateway.Middleware;
using EventGateway.Services;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});

builder.Services.AddControllers();

var dbPath = builder.Configuration.GetValue<string>("DatabasePath") ?? "event-gateway.db";
builder.Services.AddDbContext<EventDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

var accountServiceUrl = builder.Configuration.GetValue<string>("AccountService:BaseUrl") ?? "http://localhost:5001/";

// Circuit breaker: opens after 3 consecutive failures, stays open for 15 seconds
var circuitBreakerPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 3,
        durationOfBreak: TimeSpan.FromSeconds(15),
        onBreak: (outcome, breakDelay) =>
        {
            Console.WriteLine($"[circuit-breaker] OPEN for {breakDelay.TotalSeconds}s — {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
        },
        onReset: () => Console.WriteLine("[circuit-breaker] CLOSED"),
        onHalfOpen: () => Console.WriteLine("[circuit-breaker] HALF-OPEN")
    );

// Retry: 2 retries with exponential backoff, only when circuit is closed
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

builder.Services.AddHttpClient<AccountServiceClient>(client =>
    {
        client.BaseAddress = new Uri(accountServiceUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddPolicyHandler(retryPolicy)
    .AddPolicyHandler(circuitBreakerPolicy);

builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("event-gateway"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();
    db.Database.EnsureCreated();
}

app.UseMiddleware<TracingMiddleware>();
app.MapControllers();
app.Run();

public partial class EventGatewayProgram { }
