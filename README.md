# AI NativeFactory Voice Chat

An ASP.NET Core 8 Razor Pages app for a live, spoken, streaming
conversation with an AI colleague who takes on a software-delivery
persona. Deployable to Render via Docker, auto-deployed from GitHub.

## How it works

Your browser opens a direct WebRTC connection to OpenAI's Realtime API.
Microphone audio streams out; the model's spoken reply streams back; a
live transcript of both sides is shown and can be copied or downloaded.

The C# backend's only job is to mint a short-lived ("ephemeral") token so
your real API key never leaves the server or appears in browser code.

## Configuration philosophy

**Exactly one thing is configured outside the code: the OpenAI API key.**

Everything else - model, voice, transcription model, system instructions
(including the British accent) - lives in `OpenAiSettings.cs`. To change
any of it, edit that file, commit, and push; Render redeploys.

| What                     | Where                                   |
|--------------------------|-----------------------------------------|
| OpenAI API key           | `OPENAI_API_KEY` environment variable   |
| Model, voice, transcription model | `OpenAiSettings.cs` |
| Personas (role, guidelines, guardrails, document analysis) | `Personas/PersonaCatalog.cs` |
| Upload limits (page cap, file size) | `UploadLimits` in `Program.cs` |

`appsettings.json` contains only logging defaults and holds no secrets,
so it's safe to commit.

## Personas

Four built-in personas, selectable from the dropdown before starting a
conversation: **Solution Architect** (default), **Lead Developer**,
**User-Centred Design Lead**, and **Business Analyst**.

Each persona is defined in `Personas/PersonaCatalog.cs` with three
separate sections - `Role`, `Guidelines`, and `Guardrails` - which are
assembled into the system prompt along with a shared `Common` block that
holds the voice/accent and general-conduct instructions. Keeping the
sections separate makes them easy to tune independently, and is the
shape we'll keep when personas later become editable or stored externally.

The persona is locked while a conversation is running; end the call to
switch. The active persona is recorded in the transcript header and in the
downloaded filename.

## Attaching a document

Before starting a conversation you can attach one file for the persona to
discuss: PDF, Word (.docx), PowerPoint (.pptx), plain text / Markdown, or
a source-code or configuration file (C#, TypeScript, Python, SQL, YAML,
Terraform, etc.). The server extracts the text and holds it in memory for
the session; nothing is stored anywhere. Documents over roughly 200
pages (or 15 MB) are rejected with a message rather than truncated.

The full document text is included in the persona's context, so the
conversation is about the actual document - "what does section 4 say?"
works. Each persona has a `DocumentAnalysis` section in
`Personas/PersonaCatalog.cs` describing what it looks for and which kinds
of file are outside its lane. If the file isn't the persona's area (e.g. a
Business Analyst given source code) it says so, names the right persona,
offers to hand over (end, switch persona, start again with the same
document), and offers what it can from its own perspective.

The server accepts a list of document ids on `/api/session` even though
the UI attaches one at a time - this is groundwork for a future document
library where several can be selected.

## Run locally

```
export OPENAI_API_KEY=sk-your-key      # PowerShell: $env:OPENAI_API_KEY="sk-your-key"
dotnet run
```

Then open http://localhost:8080, click **Start**, allow the microphone,
and talk. Chrome/Edge recommended.

## Personas

Four built-in personas, selectable from the dropdown before starting a
conversation: **Solution Architect** (default), **Lead Developer**,
**User-Centred Design Lead**, and **Business Analyst**.

Each persona is defined in `Personas/PersonaCatalog.cs` with three
separate sections - `Role`, `Guidelines`, and `Guardrails` - which are
assembled into the system prompt along with a shared `Common` block that
holds the voice/accent and general-conduct instructions. Keeping the
sections separate makes them easy to tune independently, and is the
shape we'll keep when personas later become editable or stored externally.

The persona is locked while a conversation is running; end the call to
switch. The active persona is recorded in the transcript header and in the
downloaded filename.

## Attaching a document

Before starting a conversation you can attach one file for the persona to
discuss: PDF, Word (.docx), PowerPoint (.pptx), plain text / Markdown, or
a source-code or configuration file (C#, TypeScript, Python, SQL, YAML,
Terraform, etc.). The server extracts the text and holds it in memory for
the session; nothing is stored anywhere. Documents over roughly 200
pages (or 15 MB) are rejected with a message rather than truncated.

The full document text is included in the persona's context, so the
conversation is about the actual document - "what does section 4 say?"
works. Each persona has a `DocumentAnalysis` section in
`Personas/PersonaCatalog.cs` describing what it looks for and which kinds
of file are outside its lane. If the file isn't the persona's area (e.g. a
Business Analyst given source code) it says so, names the right persona,
offers to hand over (end, switch persona, start again with the same
document), and offers what it can from its own perspective.

The server accepts a list of document ids on `/api/session` even though
the UI attaches one at a time - this is groundwork for a future document
library where several can be selected.

## Run locally with Docker

```
docker build -t voicechat .
docker run -p 8080:8080 -e OPENAI_API_KEY=sk-your-key voicechat
```

## Deploy to Render (via GitHub)

1. Push this project to a GitHub repository.
2. In Render: **New +** -> **Blueprint** -> connect the repo. Render reads
   `render.yaml` and creates the web service. (Or **New +** -> **Web
   Service** -> Docker, if you'd rather configure it by hand.)
3. In the service's **Environment** tab, add `OPENAI_API_KEY` with your key.
   That's the only variable you need.
4. Deploy. Subsequent pushes to the default branch redeploy automatically.

The app reads Render's `PORT` env var and listens on it, and deliberately
does *not* redirect to HTTPS internally - Render terminates TLS at its
edge and forwards plain HTTP to the container.

## Notes

- **No auth on `/api/session`.** Anyone who can reach the URL can mint
  sessions against your API key, and Realtime is billed per minute of
  audio. Fine for a personal tool; add an access gate before sharing it.
- **Accent.** OpenAI has no British voice. The accent comes from the
  `Common` instructions in `Personas/PersonaCatalog.cs`. It's good but not perfectly
  consistent; strengthen the wording if it drifts. `marin`, `sage` and
  `coral` tend to hold it best.
- **Disable user-side transcription** by setting `TranscriptionModel` to
  `""` in `OpenAiSettings.cs`. The AI's own transcript still appears.
- **Cost.** Check current OpenAI Realtime pricing before extended use.
