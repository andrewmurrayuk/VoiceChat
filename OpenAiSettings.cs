namespace VoiceChat;

/// <summary>
/// All OpenAI Realtime configuration lives here, in code, so the only
/// thing that ever needs to be set in the hosting environment is the
/// API key (OPENAI_API_KEY). Change a value, commit, redeploy.
/// </summary>
public static class OpenAiSettings
{
    /// <summary>Realtime speech-to-speech model.</summary>
    public const string RealtimeModel = "gpt-realtime-2.1";

    /// <summary>
    /// Voice for the AI's spoken output. Options at time of writing include
    /// marin, cedar, alloy, ash, ballad, coral, echo, sage, shimmer, verse.
    /// </summary>
    public const string Voice = "marin";

    /// <summary>
    /// Model used to transcribe the *user's* speech for the transcript panel.
    /// Set to "" (empty string) to disable user-side transcription entirely
    /// (the AI's own transcript will still appear).
    /// </summary>
    public const string TranscriptionModel = "gpt-live-transcribe";

    /// <summary>
    /// System prompt. Controls accent, tone and persona. OpenAI has no
    /// dedicated British voice - the accent is achieved by instruction, so
    /// keep this explicit and repetitive; it improves consistency.
    /// </summary>
    public const string Instructions =
        "You are a friendly, helpful conversational assistant. " +
        "Always speak with a soft, gentle British English accent (Received Pronunciation, southern English) - never an American accent. " +
        "Use British spelling, vocabulary and phrasing throughout (for example 'colour', 'flat', 'lift', 'queue', 'quite right'). " +
        "Keep your responses conversational, warm and reasonably concise, as if chatting with someone in person. " +
        "Maintain the British accent consistently across every single response.";

    /// <summary>
    /// Name of the environment variable holding the OpenAI API key.
    /// This is the ONLY thing configured outside the code.
    /// </summary>
    public const string ApiKeyEnvVar = "OPENAI_API_KEY";
}
