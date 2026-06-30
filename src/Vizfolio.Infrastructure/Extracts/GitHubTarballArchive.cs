using System.Formats.Tar;
using System.IO.Compression;

namespace Vizfolio.Infrastructure.Extracts;

internal sealed class GitHubTarballArchive : IDisposable
{
    private readonly string _root;

    private GitHubTarballArchive(string root) => _root = root;

    public static async Task<GitHubTarballArchive> DownloadAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"vizfolio-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var network = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var gzip = new GZipStream(network, CompressionMode.Decompress);
            await using var tar = new TarReader(gzip, leaveOpen: false);

            string? prefix = null;
            while (await tar.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                if (entry.EntryType is not TarEntryType.Directory
                    and not TarEntryType.RegularFile
                    and not TarEntryType.V7RegularFile)
                {
                    continue;
                }

                var name = entry.Name;
                if (string.IsNullOrEmpty(name)) continue;

                prefix ??= name.Split('/', 2)[0];
                var relative = StripPrefix(name, prefix);
                if (relative.Length == 0) continue;

                if (entry.EntryType is TarEntryType.Directory)
                {
                    Directory.CreateDirectory(Path.Combine(tempRoot, relative));
                    continue;
                }

                var destPath = Path.Combine(tempRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await using var destination = File.Create(destPath);
                if (entry.DataStream is not null)
                    await entry.DataStream.CopyToAsync(destination, cancellationToken);
            }

            return new GitHubTarballArchive(tempRoot);
        }
        catch
        {
            TryDelete(tempRoot);
            throw;
        }
    }

    public Stream? OpenEntry(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public bool ContainsEntry(string relativePath) =>
        File.Exists(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public void Dispose() => TryDelete(_root);

    private static string StripPrefix(string name, string prefix)
    {
        if (name.StartsWith(prefix + "/", StringComparison.Ordinal))
            return name[(prefix.Length + 1)..].TrimEnd('/');
        return name == prefix ? string.Empty : name.TrimEnd('/');
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
