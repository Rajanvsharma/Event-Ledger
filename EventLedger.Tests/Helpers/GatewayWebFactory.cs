using EventGateway.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventLedger.Tests.Helpers;

public class GatewayWebFactory : WebApplicationFactory<EventGatewayProgram>
{
    public HttpMessageHandler AccountHandler { get; init; } = new HttpClientHandler();
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var efDesc = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<EventDbContext>));
            if (efDesc != null) services.Remove(efDesc);
            services.AddDbContext<EventDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));

            services.AddHttpClient<EventGateway.Services.AccountServiceClient>()
                .ConfigurePrimaryHttpMessageHandler(() => AccountHandler);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(DbPath))
            try { File.Delete(DbPath); } catch { }
    }
}
