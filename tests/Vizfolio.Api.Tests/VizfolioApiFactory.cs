using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vizfolio.Api.Tests.Extracts.Fakes;
using Vizfolio.Application.Extracts.Abstractions;

namespace Vizfolio.Api.Tests;

public sealed class VizfolioApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vizfolio-test-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;

    public VizfolioApiFactory()
    {
        _connectionString = $"Data Source={_dbPath}";
    }

    public FakeSecuritiesExtractSource SecuritiesSource { get; } = new();

    public FakeFundsExtractSource FundsSource { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:AutoMigrate"] = "true",
                ["ConnectionStrings:Default"] = _connectionString,
                ["Extracts:Schedule:Enabled"] = "false",
                ["Extracts:Schedule:RunOnStartup"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISecuritiesExtractSource>();
            services.RemoveAll<IFundsExtractSource>();
            services.AddSingleton<ISecuritiesExtractSource>(SecuritiesSource);
            services.AddSingleton<IFundsExtractSource>(FundsSource);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); }
            catch { }
        }
    }
}
