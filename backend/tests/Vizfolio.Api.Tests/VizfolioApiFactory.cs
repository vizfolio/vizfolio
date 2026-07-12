using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vizfolio.Api.Tests.Extracts.Fakes;
using Vizfolio.Api.Tests.PortfolioImports.Fakes;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Infrastructure.Persistence;

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
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connectionString));

            services.RemoveAll<ISecuritiesExtractSource>();
            services.RemoveAll<IFundsExtractSource>();
            services.AddSingleton<ISecuritiesExtractSource>(SecuritiesSource);
            services.AddSingleton<IFundsExtractSource>(FundsSource);

            // Provide a generic, metadata-less CSV parser for tests. Production ships only
            // provider-specific parsers, but the endpoint tests need a format that carries no
            // account metadata (which QFX always does) to exercise account-scoped imports.
            services.AddScoped<IPortfolioFileParser, CsvLedgerTestParser>();
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
