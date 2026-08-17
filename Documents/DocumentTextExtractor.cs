using System.Text;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;

namespace VoiceChat.Documents;

/// <summary>
/// Turns an uploaded file into plain text plus a kind classification.
/// Reusable regardless of where the bytes come from (browser upload now,
/// blob storage later).
/// </summary>
public static class DocumentTextExtractor
{
    /// <summary>Rough tokens-per-page used for the page estimate shown to users.</summary>
    public const int TokensPerPage = 500;

    /// <summary>Roughly 4 characters per token for English / code.</summary>
    private const double CharsPerToken = 4.0;

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".fs", ".java", ".kt", ".scala", ".go", ".rs", ".c", ".h", ".cpp", ".hpp",
        ".js", ".jsx", ".ts", ".tsx", ".py", ".rb", ".php", ".swift", ".m", ".sql", ".sh", ".ps1",
        ".psm1", ".bat", ".cmd", ".html", ".htm", ".css", ".scss", ".razor", ".cshtml", ".vue", ".dart",
        ".r", ".pl", ".lua", ".groovy", ".gradle"
    };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".yaml", ".yml", ".xml", ".toml", ".ini", ".env", ".config", ".csproj", ".sln",
        ".props", ".targets", ".tf", ".tfvars", ".bicep", ".dockerfile", ".editorconfig"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".rst", ".log", ".adoc"
    };

    private static readonly HashSet<string> DataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".tsv"
    };

    /// <summary>All extensions this extractor accepts.</summary>
    public static IEnumerable<string> SupportedExtensions =>
        new[] { ".pdf", ".docx", ".pptx" }
            .Concat(CodeExtensions).Concat(ConfigExtensions).Concat(TextExtensions).Concat(DataExtensions);

    public static bool IsSupported(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
        {
            // Files like "Dockerfile" or "Makefile" with no extension
            var name = Path.GetFileName(fileName);
            return name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Makefile", StringComparison.OrdinalIgnoreCase);
        }
        return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public static (string Text, DocumentKind Kind) Extract(string fileName, Stream content)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var name = Path.GetFileName(fileName);

        if (ext == ".pdf") return (ExtractPdf(content), DocumentKind.Document);
        if (ext == ".docx") return (ExtractDocx(content), DocumentKind.Document);
        if (ext == ".pptx") return (ExtractPptx(content), DocumentKind.Presentation);

        // Everything else is treated as text.
        var text = ReadAllText(content);

        if (CodeExtensions.Contains(ext)) return (text, DocumentKind.SourceCode);
        if (ConfigExtensions.Contains(ext)) return (text, DocumentKind.Configuration);
        if (DataExtensions.Contains(ext)) return (text, DocumentKind.Data);
        if (TextExtensions.Contains(ext)) return (text, DocumentKind.Document);
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Makefile", StringComparison.OrdinalIgnoreCase))
            return (text, DocumentKind.Configuration);

        return (text, DocumentKind.Unknown);
    }

    public static int EstimateTokens(string text) =>
        (int)Math.Ceiling(text.Length / CharsPerToken);

    public static int EstimatePages(int tokens) =>
        Math.Max(1, (int)Math.Ceiling(tokens / (double)TokensPerPage));

    // -----------------------------------------------------------------

    private static string ReadAllText(Stream s)
    {
        using var reader = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ExtractPdf(Stream s)
    {
        using var ms = CopyToMemory(s);
        using var pdf = PdfDocument.Open(ms);
        var sb = new StringBuilder();
        var pageNo = 0;
        foreach (var page in pdf.GetPages())
        {
            pageNo++;
            sb.AppendLine($"--- Page {pageNo} ---");
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string ExtractDocx(Stream s)
    {
        using var ms = CopyToMemory(s);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            var line = para.InnerText;
            if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private static string ExtractPptx(Stream s)
    {
        using var ms = CopyToMemory(s);
        using var pres = PresentationDocument.Open(ms, false);
        var presPart = pres.PresentationPart;
        if (presPart?.Presentation?.SlideIdList is null) return string.Empty;

        var sb = new StringBuilder();
        var slideNo = 0;
        foreach (var slideId in presPart.Presentation.SlideIdList.Elements<P.SlideId>())
        {
            slideNo++;
            if (slideId.RelationshipId is null) continue;
            if (presPart.GetPartById(slideId.RelationshipId!) is not SlidePart slidePart) continue;

            sb.AppendLine($"--- Slide {slideNo} ---");
            foreach (var text in slidePart.Slide.Descendants<A.Text>())
            {
                if (!string.IsNullOrWhiteSpace(text.Text)) sb.AppendLine(text.Text);
            }
            // Speaker notes, if any
            var notes = slidePart.NotesSlidePart?.NotesSlide;
            if (notes is not null)
            {
                var noteText = string.Join(" ", notes.Descendants<A.Text>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(noteText))
                {
                    sb.AppendLine("[Notes] " + noteText);
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static MemoryStream CopyToMemory(Stream s)
    {
        var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }
}
