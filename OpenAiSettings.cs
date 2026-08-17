namespace VoiceChat;

/// <summary>
/// OpenAI Realtime configuration lives here, in code, so the only thing
/// that ever needs to be set in the hosting environment is the API key
/// (OPENAI_API_KEY). Change a value, commit, redeploy.
///
/// Persona-specific instructions (role, guidelines, guardrails) live in
/// Personas/PersonaCatalog.cs.
/// </summary>
public static class OpenAiSettings
{
    /// <summary>Realtime speech-to-speech model.</summary>
    public const string RealtimeModel = "gpt-realtime-2.1";

    /// <summary>
    /// Voice for the AI's spoken output - shared by all personas. Options at
    /// time of writing include marin, cedar, alloy, ash, ballad, coral, echo,
    /// sage, shimmer, verse.
    /// </summary>
    public const string Voice = "marin";

    /// <summary>
    /// Model used to transcribe the *user's* speech for the transcript panel.
    /// Set to "" (empty string) to disable user-side transcription entirely
    /// (the AI's own transcript will still appear).
    /// </summary>
    public const string TranscriptionModel = "gpt-live-transcribe";

    /// <summary>
    /// Name of the environment variable holding the OpenAI API key.
    /// This is the ONLY thing configured outside the code.
    /// </summary>
    public const string ApiKeyEnvVar = "OPENAI_API_KEY";
}
