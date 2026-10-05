using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace IntelligentAdjustment.App.Services;

/// <summary>
/// Optional Microsoft Word post-processing used only to paginate the generated
/// DOCX and refresh its cached table of contents. Report generation itself remains
/// NPOI-based and still succeeds when Word is unavailable.
/// </summary>
internal static class WordReportPostProcessor
{
    public static bool TryRefreshTableOfContents(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!OperatingSystem.IsWindows())
        {
            RemoveAutomaticFieldUpdateFlag(filePath);
            return false;
        }

        Type? wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType is null)
        {
            RemoveAutomaticFieldUpdateFlag(filePath);
            return false;
        }

        object? applicationObject = null;
        object? documentsObject = null;
        object? documentObject = null;
        object? tablesOfContentsObject = null;

        try
        {
            applicationObject = Activator.CreateInstance(wordType);
            if (applicationObject is null)
            {
                RemoveAutomaticFieldUpdateFlag(filePath);
                return false;
            }

            dynamic application = applicationObject;
            application.Visible = false;
            application.DisplayAlerts = 0;
            // msoAutomationSecurityForceDisable: never run document macros.
            application.AutomationSecurity = 3;

            documentsObject = application.Documents;
            dynamic documents = documentsObject;
            documentObject = documents.Open(
                FileName: Path.GetFullPath(filePath),
                ConfirmConversions: false,
                ReadOnly: false,
                AddToRecentFiles: false,
                UpdateLinks: 0,
                Visible: false,
                OpenAndRepair: false,
                NoEncodingDialog: true);

            dynamic document = documentObject;
            document.Repaginate();

            tablesOfContentsObject = document.TablesOfContents;
            dynamic tablesOfContents = tablesOfContentsObject;
            int count = tablesOfContents.Count;

            for (int index = 1; index <= count; index++)
            {
                object? tableOfContentsObject = null;
                try
                {
                    tableOfContentsObject = tablesOfContents.Item(index);
                    dynamic tableOfContents = tableOfContentsObject;
                    tableOfContents.Update();

                    // The exported report is a final deliverable. Keep the freshly
                    // calculated TOC display text/hyperlinks, but lock its fields so
                    // Word will not show "update table of contents" or field-update
                    // security prompts when the user opens the file.
                    object? rangeObject = null;
                    object? fieldsObject = null;
                    try
                    {
                        rangeObject = tableOfContents.Range;
                        dynamic range = rangeObject;
                        fieldsObject = range.Fields;
                        dynamic fields = fieldsObject;
                        fields.Locked = true;
                    }
                    finally
                    {
                        ReleaseComObject(fieldsObject);
                        ReleaseComObject(rangeObject);
                    }
                }
                finally
                {
                    ReleaseComObject(tableOfContentsObject);
                }
            }

            document.Save();
            document.Close(0);
            ReleaseComObject(documentObject);
            documentObject = null;

            application.Quit(0);
            ReleaseComObject(tablesOfContentsObject);
            tablesOfContentsObject = null;
            ReleaseComObject(documentsObject);
            documentsObject = null;
            ReleaseComObject(applicationObject);
            applicationObject = null;

            // The old template can carry w:updateFields. Remove it after the cached
            // TOC has been saved so Word will not prompt again when the user opens it.
            RemoveAutomaticFieldUpdateFlag(filePath);
            return true;
        }
        catch
        {
            TryCloseDocument(documentObject);
            TryQuitWord(applicationObject);
            RemoveAutomaticFieldUpdateFlag(filePath);
            return false;
        }
        finally
        {
            ReleaseComObject(tablesOfContentsObject);
            ReleaseComObject(documentObject);
            ReleaseComObject(documentsObject);
            ReleaseComObject(applicationObject);
        }
    }

    private static void RemoveAutomaticFieldUpdateFlag(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            using var archive = new ZipArchive(
                stream,
                ZipArchiveMode.Update,
                leaveOpen: false);

            ZipArchiveEntry? settingsEntry = archive.GetEntry("word/settings.xml");
            if (settingsEntry is null)
            {
                return;
            }

            string xml;
            using (Stream entryStream = settingsEntry.Open())
            using (var reader = new StreamReader(
                       entryStream,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true,
                       leaveOpen: false))
            {
                xml = reader.ReadToEnd();
            }

            XDocument settings = XDocument.Parse(
                xml,
                LoadOptions.PreserveWhitespace);

            XElement[] flags = settings
                .Descendants()
                .Where(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "updateFields",
                        StringComparison.Ordinal))
                .ToArray();

            if (flags.Length == 0)
            {
                return;
            }

            foreach (XElement flag in flags)
            {
                flag.Remove();
            }

            settingsEntry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry(
                "word/settings.xml",
                CompressionLevel.Optimal);
            using Stream replacementStream = replacement.Open();
            using var writer = new StreamWriter(
                replacementStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            settings.Save(writer, SaveOptions.DisableFormatting);
        }
        catch
        {
            // Best effort. A report must remain usable even if this cleanup fails.
        }
    }

    private static void TryCloseDocument(object? documentObject)
    {
        if (documentObject is null)
        {
            return;
        }

        try
        {
            dynamic document = documentObject;
            document.Close(0);
        }
        catch
        {
            // Best effort. Word post-processing must never break report export.
        }
    }

    private static void TryQuitWord(object? applicationObject)
    {
        if (applicationObject is null)
        {
            return;
        }

        try
        {
            dynamic application = applicationObject;
            application.Quit(0);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}
