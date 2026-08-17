namespace VoiceChat.Documents;

/// <summary>
/// A document the user has attached for the persona to analyse.
/// Held in memory for the browser session only; nothing is persisted.
/// The shape is deliberately storage-agnostic so the same record can
/// later describe a document fetched from an Azure library.
/// </summary>
public sealed record UploadedDocument(
    string Id,
    string FileName,
    DocumentKind Kind,
    string Text,
    int EstimatedTokens,
    int EstimatedPages,
    DateTimeOffset UploadedAt);

/// <summary>
/// Coarse classification of what a document is, used as a hint to the
/// persona so it can tell whether the file is in its lane.
/// </summary>
public enum DocumentKind
{
    Unknown,
    SourceCode,
    Configuration,
    Document,       // prose: PDF/DOCX/TXT/MD
    Presentation,   // PPTX
    Data,           // CSV / JSON data
}
