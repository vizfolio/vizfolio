using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vizfolio.Application.Abstractions;
using Vizfolio.Infrastructure.Extracts;
using Vizfolio.Infrastructure.Persistence;

namespace Vizfolio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = ResolveProvider(configuration);
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(options => ConfigureProvider(options, provider, connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddExtracts(configuration);

        return services;
    }

    private static DatabaseProvider ResolveProvider(IConfiguration configuration)
    {
        var raw = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(raw))
            return DatabaseProvider.Sqlite;

        if (!Enum.TryParse<DatabaseProvider>(raw, ignoreCase: true, out var provider))
            throw new InvalidOperationException($"Unsupported Database:Provider '{raw}'. Valid values: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}.");

        return provider;
    }

    private static void ConfigureProvider(DbContextOptionsBuilder options, DatabaseProvider provider, string connectionString)
    {
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                options.UseSqlite(connectionString);
                break;
            case DatabaseProvider.Postgres:
                options.UseNpgsql(connectionString);
                break;
            case DatabaseProvider.SqlServer:
                options.UseSqlServer(connectionString);
                break;
            default:
                throw new InvalidOperationException($"Database provider '{provider}' is not handled.");
        }
    }
}
