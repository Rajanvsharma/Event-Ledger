using AccountService.Data;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});

builder.Services.AddControllers();

var dbPath = builder.Configuration.GetValue<string>("DatabasePath") ?? "account-service.db";
builder.Services.AddDbContext<AccountDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("account-service"))
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
    db.Database.EnsureCreated();
}

app.MapControllers();
app.Run();

public partial class AccountServiceProgram { }
