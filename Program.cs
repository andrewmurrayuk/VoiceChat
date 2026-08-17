using System.Net.Http.Headers;
using System.Text.Json;
using VoiceChat;

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS hosts) tell the app which port to listen on via
// the PORT env var. Fall back to 8080 for local Docker runs.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// No UseHttpsRedirection here on purpose: Render terminates TLS at its
// edge and forwards plain HTTP to the container. Redirecting inside the
// container would create a loop.

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// -----------------------------------------------------------------------
// POST /api/session
//
// Mints a short-lived ("ephemeral") OpenAI Realtime token using the real
// API key from the OPENAI_API_KEY environment variable. The browser only
// ever sees the short-lived token, never the real key.
//
// All other configuration (model, voice, instructions, transcription)
// is baked into OpenAiSettings.cs.
// -----------------------------------------------------------------------
app.MapPost("/api/session", async (IHttpClientFactory httpClientFactory) =>
{
    var apiKey = Environment.GetEnvironmentVariable(OpenAiSettings.ApiKeyEnvVar);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Problem(
            $"OpenAI API key is not configured. Set the {OpenAiSettings.ApiKeyEnvVar} environment variable.",
            statusCode: 500);
    }

    // Build the audio block; only include input transcription if a model
    // is configured, so it can be switched off cleanly by leaving it empty.
    object audioConfig = string.IsNullOrWhiteSpace(OpenAiSettings.TranscriptionModel)
        ? new { output = new { voice = OpenAiSettings.Voice } }
        : new
        {
            input = new { transcription = new { model = OpenAiSettings.TranscriptionModel } },
            output = new { voice = OpenAiSettings.Voice }
        };

    var sessionConfig = new
    {
        session = new
        {
            type = "realtime",
            model = OpenAiSettings.RealtimeModel,
            instructions = OpenAiSettings.Instructions,
            audio = audioConfig
        }
    };

    var client = httpClientFactory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    using var response = await client.PostAsync(
        "https://api.openai.com/v1/realtime/client_secrets",
        new StringContent(JsonSerializer.Serialize(sessionConfig), System.Text.Encoding.UTF8, "application/json"));

    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        return Results.Problem($"OpenAI error ({(int)response.StatusCode}): {body}", statusCode: (int)response.StatusCode);
    }

    return Results.Content(body, "application/json");
});

// Expose the model name to the browser so realtime.js doesn't have to
// hard-code it separately (it's needed as a query param on the SDP call).
app.MapGet("/api/config", () => Results.Json(new { model = OpenAiSettings.RealtimeModel }));

app.MapRazorPages();

app.Run();
