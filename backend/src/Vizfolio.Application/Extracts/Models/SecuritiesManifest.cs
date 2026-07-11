namespace Vizfolio.Application.Extracts.Models;

public sealed record SecuritiesManifest(IReadOnlyDictionary<string, string> ByTicker);
