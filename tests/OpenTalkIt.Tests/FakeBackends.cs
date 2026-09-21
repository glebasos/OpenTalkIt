using System;
using TiSpeech;

namespace OpenTalkIt.Tests;

/// <summary>
/// Deterministic <see cref="ITiPhonemeProvider"/> so the view-model tests can
/// exercise every availability path — library present, library missing,
/// library present but built without language data — on any machine,
/// regardless of whether the real native library happens to be there.
/// </summary>
internal sealed class FakePhonemeProvider : ITiPhonemeProvider
{
    public string Name { get; init; } = "fake provider";
    public bool IsAvailable { get; init; } = true;
    public TiLanguageFlags SupportedLanguages { get; init; } = TiLanguageFlags.English;
    public string? UnavailableReason { get; init; }
    public string? UnavailableDetail { get; init; }
    public string? BuildInfo { get; init; } = "fake provider; letter-to-sound: English; synthesis: not implemented";

    /// <summary>What <see cref="TextToPhonemes"/> returns for a supported language.</summary>
    public Func<TiLanguage, string, TiPhonemeResult>? Conversion { get; init; }

    public int CallCount { get; private set; }

    public TiPhonemeResult TextToPhonemes(TiLanguage language, string text)
    {
        CallCount++;

        if (!SupportedLanguages.HasFlag((TiLanguageFlags)(uint)language))
            return TiPhonemeResult.Failure(TiStatus.NoLanguage, $"No {language} data.");

        return Conversion?.Invoke(language, text)
               ?? TiPhonemeResult.Success(text.ToUpperInvariant());
    }

    /// <summary>The library was never built — the common contributor state.</summary>
    public static FakePhonemeProvider MissingLibrary() => new()
    {
        IsAvailable = false,
        SupportedLanguages = 0,
        BuildInfo = null,
        UnavailableReason =
            "The TiSpeech native library (libtispeech.dylib) was not found... " +
            "it comes from the tispeech_shared CMake target in TiSpeech/native and needs an original TIENG32.DLL.",
    };

    /// <summary>The library loaded, but CMake had no TIENG32.DLL to extract rules from.</summary>
    public static FakePhonemeProvider MissingLanguageData() => new()
    {
        IsAvailable = false,
        SupportedLanguages = 0,
        BuildInfo = "tispeech native reconstruction; letter-to-sound: no language data compiled in; synthesis: not implemented",
        UnavailableReason =
            "The TiSpeech native library is loaded but was built without language data, so it cannot " +
            "convert text to phonemes.",
    };
}

/// <summary>
/// Deterministic <see cref="ITiSpeechBackend"/> for testing capability gating
/// in both directions without launching TiSpeech.Host or loading anything
/// native. Its <see cref="Capabilities"/> and <see cref="UnavailableReason"/>
/// are whatever the test says they are.
/// </summary>
internal sealed class FakeSpeechBackend : ITiSpeechBackend
{
    public string Name { get; init; } = "fake backend";
    public TiEngineCapabilities Capabilities { get; init; } = TiEngineCapabilities.None;
    public string? UnavailableReason { get; init; }
    public bool OpenResult { get; init; }

    public bool IsOpen { get; private set; }
    public bool IsSpeaking { get; private set; }

    public string? LastSpokenText { get; private set; }

#pragma warning disable CS0067 // raised only by Speak(), which tests reach rarely
    public event EventHandler? SpeakStarted;
#pragma warning restore CS0067
    public event EventHandler? SpeakCompleted;
    public event EventHandler<string>? Error;

    public bool Open(TiLanguageFlags languages = TiLanguageFlags.English)
    {
        if (!OpenResult)
        {
            if (UnavailableReason is not null) Error?.Invoke(this, UnavailableReason);
            return false;
        }
        IsOpen = true;
        return true;
    }

    public void Close() => IsOpen = false;

    public void Speak(string text, bool interrupt = true)
    {
        LastSpokenText = text;
        SpeakCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Stop() => IsSpeaking = false;
    public void Pause() { }
    public void Resume() { }

    public void SetPersonality(TiPersonality personality) { }
    public void SetLanguage(TiLanguage language) { }
    public void SetPitch(int value) { }
    public void SetRate(int value) { }
    public void SetVoicingMode(TiVoicingMode mode) { }
    public void SetF0Style(TiF0Style style) { }
    public void SetSpeakingMode(TiSpeakingMode mode) { }
    public void SetF0Range(int value) { }
    public void SetF0Perturb(int value) { }
    public void SetVowelFactor(int value) { }
    public void SetGlottalSource(TiGlottalSource source) { }

    public void Dispose() { }
}
