using AccountService.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventLedger.Tests.Helpers;

public class AccountWebFactory : WebApplicationFactory<AccountServiceProgram>
{
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var efDesc = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AccountDbContext>));
            if (efDesc != null) services.Remove(efDesc);
            services.AddDbContext<AccountDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(DbPath))
            try { File.Delete(DbPath); } catch { }
    }
}
