// ---------------------------------------------------------------------
// Voice chat via OpenAI Realtime API (WebRTC, browser <-> OpenAI direct)
//
// Flow:
//   1. Ask our own server for a short-lived ("ephemeral") token
//      (POST /api/session - the real API key never leaves the server).
//   2. Open a WebRTC peer connection, attach the microphone, and open
//      a data channel for JSON events (transcripts etc).
//   3. Send our SDP offer to OpenAI along with the ephemeral token,
//      get back an SDP answer, and the audio starts flowing both ways.
//
// A Web Audio AnalyserNode is attached to both the mic stream and the
// incoming AI stream so we can draw a live level visualizer showing
// who is currently speaking.
// ---------------------------------------------------------------------

const startBtn = document.getElementById("startBtn");
const stopBtn = document.getElementById("stopBtn");
const statusEl = document.getElementById("status");
const transcriptEl = document.getElementById("transcript");
const remoteAudio = document.getElementById("remoteAudio");
const canvas = document.getElementById("visualizer");
const speakerLabel = document.getElementById("speakerLabel");
const copyBtn = document.getElementById("copyBtn");
const downloadBtn = document.getElementById("downloadBtn");
const clearBtn = document.getElementById("clearBtn");
const ctx2d = canvas.getContext("2d");

// Model name is served from /api/config so it lives in one place
// (OpenAiSettings.cs) rather than being duplicated here.
let MODEL = "gpt-realtime-2.1";
fetch("/api/config")
    .then((r) => r.json())
    .then((c) => { if (c?.model) MODEL = c.model; })
    .catch(() => { /* fall back to default above */ });

let peerConnection = null;
let dataChannel = null;
let localStream = null;
let currentAiLine = null;

// Web Audio bits for the visualizer
let audioContext = null;
let micAnalyser = null;
let aiAnalyser = null;
let micData = null;
let aiData = null;
let animationFrame = null;
let placeholderCleared = false;

// In-memory copy of the transcript, used for Copy / Download.
// Each entry: { role: "you" | "ai", text: string, time: Date }
const transcriptEntries = [];

// ---------------------------------------------------------------------
// UI helpers
// ---------------------------------------------------------------------

function setStatus(text) {
    statusEl.textContent = text;
}

function setSpeaker(mode) {
    // mode: "idle" | "listening" | "you" | "ai"
    speakerLabel.className = "speaker-label " + mode;
    speakerLabel.textContent =
        mode === "you" ? "You" :
        mode === "ai" ? "AI" :
        mode === "listening" ? "Listening" :
        "Idle";
}

function clearPlaceholder() {
    if (placeholderCleared) return;
    transcriptEl.innerHTML = "";
    placeholderCleared = true;
}

function updateActionButtons() {
    const has = transcriptEntries.length > 0;
    copyBtn.disabled = !has;
    downloadBtn.disabled = !has;
    clearBtn.disabled = !has;
}

function addLine(role, text) {
    clearPlaceholder();
    const entry = { role, text, time: new Date() };
    transcriptEntries.push(entry);
    updateActionButtons();

    const div = document.createElement("div");
    div.className = "line " + (role === "you" ? "you" : "ai");

    const speaker = document.createElement("span");
    speaker.className = "speaker";
    speaker.textContent = role === "you" ? "You" : "AI";

    const body = document.createElement("span");
    body.className = "body";
    body.textContent = text;

    div.appendChild(speaker);
    div.appendChild(body);
    transcriptEl.appendChild(div);
    transcriptEl.scrollTop = transcriptEl.scrollHeight;

    // Return both so the AI streaming path can keep the entry in sync.
    return { body, entry };
}

let currentAiEntry = null;

function appendToAiLine(delta) {
    if (!currentAiLine) {
        const created = addLine("ai", "");
        currentAiLine = created.body;
        currentAiEntry = created.entry;
    }
    currentAiLine.textContent += delta;
    if (currentAiEntry) currentAiEntry.text += delta;
    transcriptEl.scrollTop = transcriptEl.scrollHeight;
}

// ---------------------------------------------------------------------
// Transcript export
// ---------------------------------------------------------------------

function pad(n) { return String(n).padStart(2, "0"); }

function formatTime(d) {
    return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

function formatFileStamp(d) {
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}`;
}

function transcriptAsText() {
    const header = `Voice Chat transcript - ${new Date().toLocaleString()}\n\n`;
    const lines = transcriptEntries
        .filter((e) => e.text.trim().length > 0)
        .map((e) => `[${formatTime(e.time)}] ${e.role === "you" ? "You" : "AI"}: ${e.text.trim()}`);
    return header + lines.join("\n") + "\n";
}

async function copyTranscript() {
    try {
        await navigator.clipboard.writeText(transcriptAsText());
        const original = copyBtn.textContent;
        copyBtn.textContent = "Copied";
        setTimeout(() => (copyBtn.textContent = original), 1200);
    } catch (err) {
        console.error("Clipboard write failed:", err);
        setStatus("Copy failed - see console");
    }
}

function downloadTranscript() {
    const blob = new Blob([transcriptAsText()], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `voice-chat-${formatFileStamp(new Date())}.txt`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

function clearTranscript() {
    transcriptEntries.length = 0;
    currentAiLine = null;
    currentAiEntry = null;
    transcriptEl.innerHTML = '<div class="transcript-placeholder">Your conversation will appear here.</div>';
    placeholderCleared = false;
    updateActionButtons();
}

// ---------------------------------------------------------------------
// Visualizer
// ---------------------------------------------------------------------

function ensureAudioContext() {
    if (!audioContext) {
        audioContext = new (window.AudioContext || window.webkitAudioContext)();
    }
    if (audioContext.state === "suspended") {
        audioContext.resume();
    }
    return audioContext;
}

function attachAnalyser(stream) {
    const ac = ensureAudioContext();
    const source = ac.createMediaStreamSource(stream);
    const analyser = ac.createAnalyser();
    analyser.fftSize = 256;
    analyser.smoothingTimeConstant = 0.75;
    source.connect(analyser);
    // Deliberately NOT connecting analyser -> destination for the mic,
    // otherwise you'd hear yourself echoed back.
    return analyser;
}

function averageLevel(analyser, dataArray) {
    if (!analyser) return 0;
    analyser.getByteFrequencyData(dataArray);
    let sum = 0;
    for (let i = 0; i < dataArray.length; i++) sum += dataArray[i];
    return sum / dataArray.length / 255; // 0..1
}

function drawVisualizer() {
    animationFrame = requestAnimationFrame(drawVisualizer);

    const micLevel = averageLevel(micAnalyser, micData);
    const aiLevel = averageLevel(aiAnalyser, aiData);

    // Decide who is "speaking" for colouring + label. Small threshold
    // stops background noise flickering the label.
    const THRESHOLD = 0.04;
    let active = null;
    let activeData = null;

    if (aiLevel > THRESHOLD && aiLevel >= micLevel) {
        active = "ai";
        activeData = aiData;
    } else if (micLevel > THRESHOLD) {
        active = "you";
        activeData = micData;
    }

    if (peerConnection) {
        setSpeaker(active ?? "listening");
    }

    const colour =
        active === "ai" ? "#b45309" :
        active === "you" ? "#1f5eff" :
        "#cfd4dc";

    drawBars(activeData, colour);
}

// Draws a small, discreet set of rounded bars centred vertically.
function drawBars(bins, colour) {
    const width = canvas.width;
    const height = canvas.height;
    ctx2d.clearRect(0, 0, width, height);

    const barCount = 20;
    const gap = 3;
    const barWidth = (width - gap * (barCount - 1)) / barCount;
    const centreY = height / 2;
    const minH = 4;
    const maxH = height - 8;

    ctx2d.fillStyle = colour;
    for (let i = 0; i < barCount; i++) {
        let level = 0;
        if (bins) {
            const idx = Math.floor((i / barCount) * bins.length * 0.6);
            level = bins[idx] / 255;
        }
        const barHeight = Math.max(minH, level * maxH);
        const x = i * (barWidth + gap);
        const y = centreY - barHeight / 2;
        roundRect(ctx2d, x, y, barWidth, barHeight, barWidth / 2);
    }
}

function roundRect(c, x, y, w, h, r) {
    r = Math.min(r, h / 2, w / 2);
    c.beginPath();
    c.moveTo(x + r, y);
    c.lineTo(x + w - r, y);
    c.quadraticCurveTo(x + w, y, x + w, y + r);
    c.lineTo(x + w, y + h - r);
    c.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    c.lineTo(x + r, y + h);
    c.quadraticCurveTo(x, y + h, x, y + h - r);
    c.lineTo(x, y + r);
    c.quadraticCurveTo(x, y, x + r, y);
    c.closePath();
    c.fill();
}

function startVisualizer() {
    if (!animationFrame) drawVisualizer();
}

function stopVisualizer() {
    if (animationFrame) {
        cancelAnimationFrame(animationFrame);
        animationFrame = null;
    }
    drawBars(null, "#cfd4dc");
}

// ---------------------------------------------------------------------
// OpenAI Realtime plumbing
// ---------------------------------------------------------------------

async function getEphemeralToken() {
    const res = await fetch("/api/session", { method: "POST" });
    if (!res.ok) {
        const errText = await res.text();
        throw new Error("Failed to get session token: " + errText);
    }
    const data = await res.json();

    // The token field name has shifted between API versions; handle
    // both shapes defensively.
    const token = data?.client_secret?.value ?? data?.value ?? null;

    if (!token) {
        throw new Error("Could not find ephemeral token in server response: " + JSON.stringify(data));
    }
    return token;
}

function handleServerEvent(evt) {
    let msg;
    try {
        msg = JSON.parse(evt.data);
    } catch {
        return;
    }

    switch (msg.type) {
        // AI's spoken response, transcribed, arriving in chunks.
        // GA event name (the beta name was response.audio_transcript.delta -
        // handled too in case of an older model).
        case "response.output_audio_transcript.delta":
        case "response.audio_transcript.delta":
            appendToAiLine(msg.delta ?? "");
            break;

        case "response.output_audio_transcript.done":
        case "response.audio_transcript.done":
            currentAiLine = null;
            currentAiEntry = null;
            break;

        // Your own speech, transcribed (requires input transcription enabled
        // via session.update below).
        case "conversation.item.input_audio_transcription.completed":
            addLine("you", msg.transcript ?? "");
            break;

        case "error":
            console.error("Realtime API error:", msg);
            setStatus("API error - see console");
            break;

        default:
            // Uncomment to inspect all events during development:
            // console.log(msg.type, msg);
            break;
    }
}

async function startConversation() {
    startBtn.disabled = true;
    setStatus("Requesting microphone access...");

    try {
        localStream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (err) {
        setStatus("Microphone access denied.");
        startBtn.disabled = false;
        return;
    }

    // Wire up mic analyser + start the visualizer straight away so you
    // can see the mic responding even before the AI connection is up.
    micAnalyser = attachAnalyser(localStream);
    micData = new Uint8Array(micAnalyser.frequencyBinCount);
    startVisualizer();
    setSpeaker("listening");

    setStatus("Getting session token...");
    let ephemeralToken;
    try {
        ephemeralToken = await getEphemeralToken();
    } catch (err) {
        console.error(err);
        setStatus("Error: " + err.message);
        cleanup();
        return;
    }

    setStatus("Connecting...");

    peerConnection = new RTCPeerConnection();

    // Play the AI's audio as it arrives, and attach an analyser to it
    // for the visualizer.
    peerConnection.ontrack = (event) => {
        const stream = event.streams[0];
        remoteAudio.srcObject = stream;
        aiAnalyser = attachAnalyser(stream);
        aiData = new Uint8Array(aiAnalyser.frequencyBinCount);
    };

    // Send our microphone audio.
    localStream.getTracks().forEach((track) => {
        peerConnection.addTrack(track, localStream);
    });

    // Data channel carries transcript/control events as JSON.
    dataChannel = peerConnection.createDataChannel("oai-events");
    dataChannel.addEventListener("message", handleServerEvent);
    // Note: transcription of your speech is configured server-side in
    // Program.cs when the session token is minted, so no session.update
    // is needed here. Sending one after connect is risky - a bad value
    // silently breaks the live session.

    const offer = await peerConnection.createOffer();
    await peerConnection.setLocalDescription(offer);

    const sdpResponse = await fetch(`https://api.openai.com/v1/realtime/calls?model=${MODEL}`, {
        method: "POST",
        body: offer.sdp,
        headers: {
            Authorization: `Bearer ${ephemeralToken}`,
            "Content-Type": "application/sdp",
        },
    });

    if (!sdpResponse.ok) {
        const errText = await sdpResponse.text();
        setStatus("Connection failed.");
        console.error("SDP exchange failed:", errText);
        cleanup();
        return;
    }

    const answerSdp = await sdpResponse.text();
    await peerConnection.setRemoteDescription({ type: "answer", sdp: answerSdp });

    setStatus("Connected - start talking");
    stopBtn.disabled = false;
}

function cleanup() {
    if (dataChannel) {
        dataChannel.close();
        dataChannel = null;
    }
    if (peerConnection) {
        peerConnection.close();
        peerConnection = null;
    }
    if (localStream) {
        localStream.getTracks().forEach((t) => t.stop());
        localStream = null;
    }
    remoteAudio.srcObject = null;
    currentAiLine = null;
    currentAiEntry = null;
    micAnalyser = null;
    aiAnalyser = null;
    micData = null;
    aiData = null;

    stopVisualizer();
    setSpeaker("idle");
    startBtn.disabled = false;
    stopBtn.disabled = true;
}

function stopConversation() {
    cleanup();
    setStatus("Ready");
}

startBtn.addEventListener("click", startConversation);
stopBtn.addEventListener("click", stopConversation);
copyBtn.addEventListener("click", copyTranscript);
downloadBtn.addEventListener("click", downloadTranscript);
clearBtn.addEventListener("click", clearTranscript);

// Draw the resting state on first load
stopVisualizer();
