using System.Text;
using VoiceChat.Documents;

namespace VoiceChat.Personas;

/// <summary>
/// A selectable AI persona. The prompt sections are kept separate so they
/// can be edited, stored, or extended independently later.
/// </summary>
public sealed record Persona(
    string Id,
    string Name,
    string Description,
    string Role,
    string Guidelines,
    string Guardrails,
    string DocumentAnalysis)
{
    /// <summary>
    /// Assemble the full system prompt for this persona, optionally with
    /// attached documents. Accepts a list even though the UI currently
    /// attaches one at a time - the future library will pass several.
    /// </summary>
    public string BuildInstructions(IReadOnlyList<UploadedDocument>? documents = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine(PersonaCatalog.Common.Trim());
        sb.AppendLine();
        sb.AppendLine("# Your role");
        sb.AppendLine(Role.Trim());
        sb.AppendLine();
        sb.AppendLine("# How you work");
        sb.AppendLine(Guidelines.Trim());
        sb.AppendLine();
        sb.AppendLine("# Guardrails - things you must not do");
        sb.AppendLine(Guardrails.Trim());
        sb.AppendLine();
        sb.AppendLine("# When a document is attached");
        sb.AppendLine(PersonaCatalog.CommonDocumentGuidance.Trim());
        sb.AppendLine();
        sb.AppendLine(DocumentAnalysis.Trim());

        if (documents is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("# Attached documents");
            sb.AppendLine(documents.Count == 1
                ? "The user has attached one document. Its full text follows. Refer to it directly and specifically when discussing it."
                : $"The user has attached {documents.Count} documents. Their full text follows. Refer to them directly and specifically, by name, when discussing them.");

            foreach (var doc in documents)
            {
                sb.AppendLine();
                sb.AppendLine($"## Document: {doc.FileName}");
                sb.AppendLine($"Type: {DescribeKind(doc.Kind)}. Approximately {doc.EstimatedPages} page(s).");
                sb.AppendLine("<document>");
                sb.AppendLine(doc.Text.Trim());
                sb.AppendLine("</document>");
            }
        }

        return sb.ToString();
    }

    private static string DescribeKind(DocumentKind kind) => kind switch
    {
        DocumentKind.SourceCode => "source code",
        DocumentKind.Configuration => "configuration / infrastructure file",
        DocumentKind.Document => "written document",
        DocumentKind.Presentation => "presentation (slides)",
        DocumentKind.Data => "data file",
        _ => "file of unknown type",
    };
}
