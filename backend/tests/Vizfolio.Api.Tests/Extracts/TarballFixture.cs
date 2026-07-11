using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Vizfolio.Api.Tests.Extracts;

internal static class TarballFixture
{
    public static byte[] Build(string rootPrefix, IReadOnlyDictionary<string, byte[]> files, bool includePaxGlobalHeader = false)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            if (includePaxGlobalHeader)
            {
                tar.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(
                    new Dictionary<string, string> { ["comment"] = "fixture" }));
            }

            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, rootPrefix + "/"));

            foreach (var (path, bytes) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, $"{rootPrefix}/{path}")
                {
                    DataStream = new MemoryStream(bytes)
                };
                tar.WriteEntry(entry);
            }
        }
        return output.ToArray();
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    public static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new StreamWriter(gz, Encoding.UTF8))
        {
            writer.Write(text);
        }
        return output.ToArray();
    }
}
