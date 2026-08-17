# Voice Chat (OpenAI Realtime API)

A minimal ASP.NET Core 8 Razor Pages app for a live, spoken, streaming
conversation with an OpenAI model in your browser. Deployable to Render
via Docker, auto-deployed from GitHub.

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
| Model, voice, instructions, transcription model | `OpenAiSettings.cs` |

`appsettings.json` contains only logging defaults and holds no secrets,
so it's safe to commit.

## Run locally

```
export OPENAI_API_KEY=sk-your-key      # PowerShell: $env:OPENAI_API_KEY="sk-your-key"
dotnet run
```

Then open http://localhost:8080, click **Start**, allow the microphone,
and talk. Chrome/Edge recommended.

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
  instructions in `OpenAiSettings.cs`. It's good but not perfectly
  consistent; strengthen the wording if it drifts. `marin`, `sage` and
  `coral` tend to hold it best.
- **Disable user-side transcription** by setting `TranscriptionModel` to
  `""` in `OpenAiSettings.cs`. The AI's own transcript still appears.
- **Cost.** Check current OpenAI Realtime pricing before extended use.
