using System.Collections.Concurrent;

namespace VoiceChat.Documents;

/// <summary>
/// In-memory holding area for uploaded documents. Documents live only as
/// long as the process does and are removed after a short expiry so a
/// forgotten upload doesn't linger. This is a stand-in for the future
/// Azure-backed library - the interface (Add / Get / Remove) is what a
/// blob-backed implementation would also expose.
/// </summary>
public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<string, UploadedDocument> _docs = new();
    private static readonly TimeSpan Expiry = TimeSpan.FromHours(2);

    public UploadedDocument Add(string fileName, DocumentKind kind, string text)
    {
        Sweep();
        var tokens = DocumentTextExtractor.EstimateTokens(text);
        var doc = new UploadedDocument(
            Id: Guid.NewGuid().ToString("N"),
            FileName: fileName,
            Kind: kind,
            Text: text,
            EstimatedTokens: tokens,
            EstimatedPages: DocumentTextExtractor.EstimatePages(tokens),
            UploadedAt: DateTimeOffset.UtcNow);
        _docs[doc.Id] = doc;
        return doc;
    }

    public UploadedDocument? Get(string? id) =>
        id is not null && _docs.TryGetValue(id, out var d) ? d : null;

    public bool Remove(string id) => _docs.TryRemove(id, out _);

    private void Sweep()
    {
        var cutoff = DateTimeOffset.UtcNow - Expiry;
        foreach (var kv in _docs)
        {
            if (kv.Value.UploadedAt < cutoff) _docs.TryRemove(kv.Key, out _);
        }
    }
}
