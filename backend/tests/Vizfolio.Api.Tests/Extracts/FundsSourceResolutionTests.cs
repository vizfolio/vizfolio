using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shouldly;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Infrastructure;
using Vizfolio.Infrastructure.Extracts.Sources;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class FundsSourceResolutionTests
{
    [Theory]
    [InlineData("Tarball", typeof(GitHubFundsExtractSource))]
    [InlineData("PerFile", typeof(PerFileFundsExtractSource))]
    public void Resolves_funds_source_implementation_based_on_configured_mode(string mode, Type expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = "DataSource=:memory:",
                ["Extracts:Schedule:Enabled"] = "false",
                ["Extracts:Sources:Funds:Mode"] = mode,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        // Strip the DbContext registration's scope tracking; we only resolve the source.
        services.RemoveAll<IAppDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<IFundsExtractSource>();

        source.ShouldBeOfType(expected);
    }
}
