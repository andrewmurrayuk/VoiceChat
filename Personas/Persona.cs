namespace VoiceChat.Personas;

/// <summary>
/// A selectable AI persona. The three prompt sections are kept separate so
/// they can be edited, stored, or extended independently later (for
/// example, adding a DocumentAnalysis section when document upload arrives).
/// </summary>
public sealed record Persona(
    string Id,
    string Name,
    string Description,
    string Role,
    string Guidelines,
    string Guardrails)
{
    /// <summary>
    /// Assemble the full system prompt for this persona. Common (voice/accent)
    /// instructions come first so they apply regardless of persona.
    /// </summary>
    public string BuildInstructions() =>
        string.Join("\n\n",
            PersonaCatalog.Common.Trim(),
            $"# Your role\n{Role.Trim()}",
            $"# How you work\n{Guidelines.Trim()}",
            $"# Guardrails - things you must not do\n{Guardrails.Trim()}");
}
