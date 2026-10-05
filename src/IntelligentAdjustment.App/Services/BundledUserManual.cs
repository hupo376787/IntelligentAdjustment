using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace IntelligentAdjustment.App.Services;

internal static class BundledUserManual
{
    private const string FileName = "Intelligent_Adjustment_User_Manual.pdf";
    private const string ResourceMarker = ".Help.ManualBase64.manual.part";

    public static string GetOrCreatePath()
    {
        byte[] compressed = ReadEmbeddedCompressedPdf();
        byte[] pdf;

        using (var source = new MemoryStream(compressed, writable: false))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            gzip.CopyTo(output);
            pdf = output.ToArray();
        }

        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelligentAdjustment",
            "Help");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, FileName);

        bool needsWrite = true;
        if (File.Exists(path))
        {
            try
            {
                byte[] existing = File.ReadAllBytes(path);
                needsWrite = !existing.AsSpan().SequenceEqual(pdf);
            }
            catch
            {
                needsWrite = true;
            }
        }

        if (needsWrite)
        {
            File.WriteAllBytes(path, pdf);
        }

        return path;
    }

    private static byte[] ReadEmbeddedCompressedPdf()
    {
        Assembly assembly = typeof(BundledUserManual).Assembly;
        string[] resources = assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
        {
            throw new InvalidDataException("未找到内置用户使用手册资源。");
        }

        var base64 = new StringBuilder(resources.Length * 8000);
        foreach (string resource in resources)
        {
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException($"无法读取内置用户使用手册资源：{resource}");
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false);
            base64.Append(reader.ReadToEnd().Trim());
        }

        return Convert.FromBase64String(base64.ToString());
    }
}
