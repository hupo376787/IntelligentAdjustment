using System.Reflection;
using System.Text;

namespace IntelligentAdjustment.Application.Reporting;

internal static class LegacyReportTemplate
{
    private const int MainPieceLength = 5000;
    private const int SubPieceLength = 1000;

    private static readonly string[] Piece00Resources =
    [
        "Report.docx.b64.piece00_0",
        "Report.docx.b64.piece00_1",
        "Report.docx.b64.piece00_2",
        "Report.docx.b64.piece00_3",
        "Report.docx.b64.piece00_4"
    ];

    public static Stream Open()
    {
        var base64 = new StringBuilder(40516);

        for (int i = 0; i < Piece00Resources.Length; i++)
        {
            string part = Read(Piece00Resources[i]);
            // GitHub's text-content transport normalizes very long repeated A runs.
            // Only the first two 1k template pieces contain such runs. Restoring their
            // known source lengths recreates the exact user-supplied DOCX bytes.
            if (i < 2)
            {
                part = RestoreLongestARun(part, SubPieceLength);
            }

            base64.Append(part);
        }

        base64.Append(Read("Report.docx.b64.piece01"));

        string piece02 = Read("Report.docx.b64.piece02");
        base64.Append(piece02.AsSpan(0, 4000));
        base64.Append(Read("Report.docx.b64.piece02_tail"));

        base64.Append(RestoreLongestARun(Read("Report.docx.b64.piece03"), MainPieceLength));
        base64.Append(Read("Report.docx.b64.piece04"));
        base64.Append(Read("Report.docx.b64.piece05"));
        base64.Append(Read("Report.docx.b64.piece06"));

        string piece07 = Read("Report.docx.b64.piece07");
        base64.Append(piece07.AsSpan(0, 4000));
        base64.Append(Read("Report.docx.b64.piece07_tail"));

        base64.Append(Read("Report.docx.b64.piece08"));

        byte[] bytes = Convert.FromBase64String(base64.ToString());
        return new MemoryStream(bytes, writable: false);
    }

    private static string Read(string suffix)
    {
        Assembly assembly = typeof(LegacyReportTemplate).Assembly;
        string? resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidDataException($"未找到内置报告模板资源：{suffix}");
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"无法读取内置报告模板资源：{resourceName}");
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd().Trim();
    }

    private static string RestoreLongestARun(string value, int expectedLength)
    {
        int missing = expectedLength - value.Length;
        if (missing <= 0)
        {
            return value;
        }

        int bestStart = -1;
        int bestLength = 0;

        for (int i = 0; i < value.Length;)
        {
            if (value[i] != 'A')
            {
                i++;
                continue;
            }

            int end = i + 1;
            while (end < value.Length && value[end] == 'A')
            {
                end++;
            }

            int length = end - i;
            if (length > bestLength)
            {
                bestStart = i;
                bestLength = length;
            }

            i = end;
        }

        if (bestStart < 0)
        {
            throw new InvalidDataException("内置报告模板资源损坏：无法恢复 Base64 数据。");
        }

        int insertAt = bestStart + bestLength;
        return string.Concat(
            value.AsSpan(0, insertAt),
            new string('A', missing),
            value.AsSpan(insertAt));
    }
}
