// Browser side of OpenTalkIt.Browser.BrowserInterop.

const AudioContextType = globalThis.AudioContext ?? globalThis.webkitAudioContext;
let context = null;
let buffer = null;
let source = null;

function audio() {
    context ??= new AudioContextType();
    return context;
}

// Browsers start audio only after a user gesture. Talk is one, but speech
// starts after synthesis, so unlock the context on the first gesture instead.
for (const type of ['pointerdown', 'keydown', 'touchend'])
    addEventListener(type, () => { if (AudioContextType) audio().resume(); }, { capture: true, passive: true });

export function audioUnavailableReason() {
    return AudioContextType ? null : 'This browser has no Web Audio support. WAV export is still available.';
}

export function loadPcm(view, sampleRate) {
    const bytes = view.slice(); // the view is only valid during this call
    const ctx = audio();
    let rate = sampleRate, samples = Float32Array.from(bytes, b => (b - 128) / 128);
    try {
        buffer = ctx.createBuffer(1, samples.length, rate);
    } catch {
        // Older Safari rejects low rates: resample linearly to the device rate.
        const ratio = sampleRate / ctx.sampleRate, out = new Float32Array(Math.ceil(samples.length / ratio));
        for (let i = 0; i < out.length; i++) {
            const x = i * ratio, j = Math.floor(x), f = x - j;
            out[i] = samples[j] * (1 - f) + (samples[Math.min(j + 1, samples.length - 1)] ?? 0) * f;
        }
        rate = ctx.sampleRate; samples = out;
        buffer = ctx.createBuffer(1, samples.length, rate);
    }
    buffer.getChannelData(0).set(samples);
}

export function play() {
    stop();
    const ctx = audio();
    const node = ctx.createBufferSource();
    node.buffer = buffer;
    node.connect(ctx.destination);
    source = node;
    return new Promise(resolve => {
        node.onended = () => { if (source === node) source = null; resolve(); };
        node.start();
    });
}

export function stop() {
    const node = source;
    source = null;
    try { node?.stop(); } catch { /* already finished */ }
}

export function pause() { context?.suspend(); }
export function resume() { context?.resume(); }

export function download(fileName, view, mimeType) {
    const url = URL.createObjectURL(new Blob([view.slice()], { type: mimeType }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
