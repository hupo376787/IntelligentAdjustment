using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace IntelligentAdjustment.App.Services;

internal static class BundledUserManual
{
    private const string FileName = "Intelligent_Adjustment_User_Manual.pdf";
    private const string ResourceMarker = "manual.part";

    public static string GetOrCreatePath()
    {
        string deployedPath = Path.Combine(
            AppContext.BaseDirectory,
            "Help",
            FileName);
        if (File.Exists(deployedPath))
        {
            return deployedPath;
        }

        byte[] compressed = ReadCompressedPdf();
        byte[] pdf;

        using (var source = new MemoryStream(compressed, writable: false))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            gzip.CopyTo(output);
            pdf = output.ToArray();
        }

        if (pdf.Length < 5 ||
            pdf[0] != (byte)'%' ||
            pdf[1] != (byte)'P' ||
            pdf[2] != (byte)'D' ||
            pdf[3] != (byte)'F' ||
            pdf[4] != (byte)'-')
        {
            throw new InvalidDataException("内置用户使用手册不是有效的 PDF 文件。");
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

    private static byte[] ReadCompressedPdf()
    {
        Assembly assembly = typeof(BundledUserManual).Assembly;
        string[] resources = assembly
            .GetManifestResourceNames()
            .Where(name =>
                name.Contains(ResourceMarker, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resources.Length > 0)
        {
            var embeddedBase64 = new StringBuilder(resources.Length * 8000);
            foreach (string resource in resources)
            {
                using Stream stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidDataException($"无法读取内置用户使用手册资源：{resource}");
                using var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false);
                embeddedBase64.Append(reader.ReadToEnd().Trim());
            }

            return Convert.FromBase64String(embeddedBase64.ToString());
        }

        // Some WPF/MSBuild combinations can change manifest resource names.
        // The same tiny base64 chunks are therefore also copied to the output
        // directory and provide a deterministic fallback.
        string physicalDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Help",
            "ManualBase64");
        string[] physicalParts = Directory.Exists(physicalDirectory)
            ? Directory.GetFiles(physicalDirectory, "manual.part*.txt")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        if (physicalParts.Length > 0)
        {
            var fileBase64 = new StringBuilder(physicalParts.Length * 8000);
            foreach (string part in physicalParts)
            {
                fileBase64.Append(File.ReadAllText(part, Encoding.ASCII).Trim());
            }

            return Convert.FromBase64String(fileBase64.ToString());
        }

        string available = string.Join(
            ", ",
            assembly.GetManifestResourceNames()
                .Where(name => name.Contains("Manual", StringComparison.OrdinalIgnoreCase))
                .Take(8));
        throw new InvalidDataException(
            string.IsNullOrWhiteSpace(available)
                ? "未找到内置用户使用手册资源。"
                : $"未找到用户手册分片资源。已发现：{available}");
    }
}
