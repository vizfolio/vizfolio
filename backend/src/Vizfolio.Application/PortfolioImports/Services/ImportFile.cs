using System.IO.Compression;
using System.Security.Cryptography;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

/// <summary>An uploaded file's bytes and SHA-256, and how it's stored (gzip) on its <see cref="ImportBatch"/>.</summary>
internal sealed record ImportFile(string FileName, byte[] Content, string Sha256)
{
    public static async Task<ImportFile> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();
        return new ImportFile(fileName, content, Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    /// <summary>The file stored on a batch. Only call for a batch with <see cref="ImportBatch.Content"/>.</summary>
    public static ImportFile FromBatch(ImportBatch batch)
    {
        using var input = new MemoryStream(batch.Content ?? throw new InvalidOperationException("The import has no stored file."));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return new ImportFile(batch.FileName, output.ToArray(), batch.FileSha256);
    }

    public ImportBatch ToBatch(Guid portfolioId, Guid? accountId, string parserSourceSystem)
        => new(portfolioId, accountId, FileName, Sha256, parserSourceSystem, Compress(), ContentTypeFor(FileName));

    private byte[] Compress()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(Content);
        return output.ToArray();
    }

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".qfx" or ".ofx" => "application/x-ofx",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".csv" => "text/csv",
        _ => "application/octet-stream",
    };
}
