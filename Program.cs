using System.Net.Http.Headers;
using System.Text.Json;
using VoiceChat;
using VoiceChat.Personas;
using VoiceChat.Documents;

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS hosts) tell the app which port to listen on via
// the PORT env var. Fall back to 8080 for local Docker runs.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DocumentStore>();

// Allow uploads up to the configured limit.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = UploadLimits.MaxFileBytes;
});
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = UploadLimits.MaxFileBytes);

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
app.MapPost("/api/session", async (IHttpClientFactory httpClientFactory, DocumentStore store, SessionRequest request) =>
{
    var persona = PersonaCatalog.Get(request.PersonaId);

    // Resolve any attached documents. Accepts a list so the future library
    // can pass several; the UI currently sends at most one.
    var documents = (request.DocumentIds ?? Array.Empty<string>())
        .Select(store.Get)
        .Where(d => d is not null)
        .Cast<UploadedDocument>()
        .ToList();

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
            instructions = persona.BuildInstructions(documents),
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

// List of selectable personas for the dropdown. Only the display fields
// are sent - the prompt text stays server-side.
app.MapGet("/api/personas", () => Results.Json(new
{
    defaultId = PersonaCatalog.DefaultPersonaId,
    personas = PersonaCatalog.All.Select(p => new { p.Id, p.Name, p.Description })
}));

// -----------------------------------------------------------------------
// POST /api/documents  (multipart/form-data, field name "file")
//
// Extracts text from the uploaded file and holds it in memory for this
// browser session. Returns metadata + an id to pass to /api/session.
// Nothing is persisted; this is a stand-in for the future Azure library.
// -----------------------------------------------------------------------
app.MapPost("/api/documents", async (HttpRequest http, DocumentStore store) =>
{
    if (!http.HasFormContentType)
        return Results.BadRequest(new { error = "Expected multipart/form-data with a 'file' field." });

    var form = await http.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No file was uploaded." });

    if (file.Length > UploadLimits.MaxFileBytes)
        return Results.BadRequest(new { error = $"File is too large. The limit is {UploadLimits.MaxFileBytes / (1024 * 1024)} MB." });

    if (!DocumentTextExtractor.IsSupported(file.FileName))
        return Results.BadRequest(new { error = $"'{Path.GetExtension(file.FileName)}' files aren't supported. Upload a PDF, Word (.docx), PowerPoint (.pptx), text/markdown, or a source-code or configuration file." });

    string text;
    DocumentKind kind;
    try
    {
        await using var stream = file.OpenReadStream();
        (text, kind) = DocumentTextExtractor.Extract(file.FileName, stream);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Couldn't read that file: {ex.Message}" });
    }

    if (string.IsNullOrWhiteSpace(text))
        return Results.BadRequest(new { error = "No readable text was found in that file. If it's a scanned PDF, it would need OCR first." });

    var tokens = DocumentTextExtractor.EstimateTokens(text);
    var pages = DocumentTextExtractor.EstimatePages(tokens);
    if (pages > UploadLimits.MaxPages)
        return Results.BadRequest(new { error = $"That document is roughly {pages} pages, which is over the {UploadLimits.MaxPages}-page limit for a voice conversation. Please split it or upload the section you want to discuss." });

    var doc = store.Add(file.FileName, kind, text);

    return Results.Json(new
    {
        id = doc.Id,
        fileName = doc.FileName,
        kind = doc.Kind.ToString(),
        estimatedPages = doc.EstimatedPages,
        estimatedTokens = doc.EstimatedTokens
    });
});

app.MapDelete("/api/documents/{id}", (string id, DocumentStore store) =>
    store.Remove(id) ? Results.NoContent() : Results.NotFound());

app.MapRazorPages();

app.Run();

/// <summary>Body of POST /api/session.</summary>
public sealed record SessionRequest(string? PersonaId, string[]? DocumentIds);

/// <summary>Upload limits. "200 pages" is expressed via a page estimate of ~500 tokens/page.</summary>
public static class UploadLimits
{
    public const long MaxFileBytes = 15 * 1024 * 1024; // 15 MB raw file
    public const int MaxPages = 200;                    // ~100k tokens of extracted text
}
