using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Endpoints.Settings;

/// <summary>
/// A price provider as Settings shows it. The key itself is never returned: <see cref="KeySource"/> says whether one
/// is set and where (<c>Configuration</c> — env var or user-secrets, which wins and can't be changed here — or
/// <c>Settings</c>).
/// </summary>
public sealed record PriceProviderResponse(
    string Provider,
    string DisplayName,
    int Priority,
    bool RequiresApiKey,
    bool Available,
    bool ProvidesRawCloses,
    string KeySource);

/// <summary>GET /api/settings/price-providers.</summary>
public sealed class ListPriceProvidersEndpoint : EndpointWithoutRequest<IReadOnlyList<PriceProviderResponse>>
{
    private readonly IPriceHistorySourceSelector _sources;
    private readonly IProviderKeyStore _keys;

    public ListPriceProvidersEndpoint(IPriceHistorySourceSelector sources, IProviderKeyStore keys)
    {
        _sources = sources;
        _keys = keys;
    }

    public override void Configure()
    {
        Get("/settings/price-providers");
        AllowAnonymous();
        Description(b => b.WithTags("Settings").Produces<IReadOnlyList<PriceProviderResponse>>(StatusCodes.Status200OK));
        Summary(s =>
        {
            s.Summary = "List price providers in the order they're tried, and whether each is ready.";
            s.Description =
                "Providers are tried highest priority first, falling back to the next when one fails or has no data. Keys are never " +
                "returned; `keySource` says where a key is set. Providers with `providesRawCloses: false` (Stooq) only have adjusted " +
                "closes, which can't value holdings.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await _keys.EnsureLoadedAsync(ct);
        await Send.OkAsync(_sources.All.Select(s => PriceProviders.Describe(s, _keys)).ToList(), ct);
    }
}

public sealed class SetPriceProviderKeyRequest
{
    public string Provider { get; set; } = string.Empty;

    /// <summary>The key to save; blank or null removes the saved key.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>PUT /api/settings/price-providers/{provider}.</summary>
public sealed class SetPriceProviderKeyEndpoint : Endpoint<SetPriceProviderKeyRequest, PriceProviderResponse>
{
    private readonly IPriceHistorySourceSelector _sources;
    private readonly IProviderKeyStore _keys;
    private readonly IPriceRefreshQueue _queue;

    public SetPriceProviderKeyEndpoint(IPriceHistorySourceSelector sources, IProviderKeyStore keys, IPriceRefreshQueue queue)
    {
        _sources = sources;
        _keys = keys;
        _queue = queue;
    }

    public override void Configure()
    {
        Put("/settings/price-providers/{provider}");
        AllowAnonymous();
        Description(b => b
            .WithTags("Settings")
            .Produces<PriceProviderResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict));
        Summary(s =>
        {
            s.Summary = "Save (or clear) a price provider's API key.";
            s.Description =
                "Stores the key server-side (write-only: it's never returned) and queues a price refresh when one is saved. A blank " +
                "`apiKey` removes the saved key. 404 for an unknown provider, 400 for one that takes no key, 409 when the key is set " +
                "in server configuration (env var or user-secrets), which always wins.";
        });
    }

    public override async Task HandleAsync(SetPriceProviderKeyRequest req, CancellationToken ct)
    {
        var source = Enum.TryParse<PriceSource>(req.Provider, ignoreCase: true, out var provider)
            ? _sources.All.FirstOrDefault(s => s.Source == provider)
            : null;
        if (source is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!source.RequiresApiKey)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        await _keys.EnsureLoadedAsync(ct);
        if (_keys.GetKeySource(provider) == ProviderKeySource.Configuration)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status409Conflict, ct);
            return;
        }

        await _keys.SetApiKeyAsync(provider, req.ApiKey, ct);
        if (!string.IsNullOrWhiteSpace(req.ApiKey))
            _queue.Enqueue(PriceRefreshRequest.Everything(PriceRefreshTrigger.Manual));

        await Send.OkAsync(PriceProviders.Describe(source, _keys), ct);
    }
}

internal static class PriceProviders
{
    public static PriceProviderResponse Describe(IPriceHistorySource source, IProviderKeyStore keys) => new(
        source.Source.ToString(),
        source.DisplayName,
        source.Priority,
        source.RequiresApiKey,
        source.IsAvailable,
        source.ProvidesRawCloses,
        source.RequiresApiKey ? keys.GetKeySource(source.Source).ToString() : nameof(ProviderKeySource.None));
}
