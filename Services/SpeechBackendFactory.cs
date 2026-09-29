using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TiSpeech;

namespace OpenTalkIt.Services;

/// <summary>
/// The backends the app ended up with, plus what happened while bringing the
/// speech one up. <see cref="SpeechOpened"/> is the single flag the UI gates
/// Talk/Export on; it is true only when a backend genuinely reported that it
/// will produce the original engine's audio.
/// </summary>
/// <param name="Speech">The selected speech backend (never null; it may simply be one that cannot speak).</param>
/// <param name="Phonemes">Letter-to-sound provider — the native reconstruction, on every platform.</param>
/// <param name="SpeechOpened">True only when <see cref="ITiSpeechBackend.Open"/> actually succeeded.</param>
/// <param name="SpeechError">Everything the candidate backends reported while failing, or null on success.</param>
public sealed record SpeechBackendSelection(
    ITiSpeechBackend Speech,
    ITiPhonemeProvider Phonemes,
    bool SpeechOpened,
    string? SpeechError);

/// <summary>
/// Composition root for the speech side of the app.
///
/// Uses the portable engine and bundled English/Spanish data on every platform.
/// The original Windows host remains a compatibility fallback when installed.
/// </summary>
public static class SpeechBackendFactory
{
    /// <summary>
    /// Bring up the best available speech backend and pair it with the phoneme
    /// provider. Never throws and never returns a backend that claims speech it
    /// cannot deliver.
    /// </summary>
    public static SpeechBackendSelection Create(TiLanguageFlags languages)
    {
        var native = new NativeTiSpeechBackend(CreatePlayer());

        // Prefer the portable engine; it needs neither the original DLLs nor an x86 host.
        // The x86 host fallback exists only on Windows; elsewhere it only adds a
        // second, irrelevant failure message.
        ITiSpeechBackend[] candidates = OperatingSystem.IsAndroid() ? [native] : [native, new TiSpeechClient()];

        var failures = new List<string>();
        foreach (var candidate in candidates)
        {
            string? raised = null;
            void OnError(object? _, string message) => raised = message;

            // Subscribe before Open(): both backends raise Error synchronously
            // from inside Open() when they fail, so subscribing afterwards would
            // miss it.
            candidate.Error += OnError;
            bool opened;
            try
            {
                opened = candidate.Open(languages);
            }
            catch (Exception ex)
            {
                // A backend throwing out of Open() is a bug, not a normal state,
                // but it must not take the whole app down: degrade to "this
                // backend is unavailable" like any other failure.
                opened = false;
                raised = $"{candidate.Name} failed to start: {ex.Message}";
            }
            finally
            {
                candidate.Error -= OnError;
            }

            if (opened)
            {
                DisposeUnused(candidates, keep: candidate, native: native);
                return new SpeechBackendSelection(candidate, native, true, null);
            }

            var reason = raised ?? candidate.UnavailableReason;
            if (!string.IsNullOrWhiteSpace(reason))
                failures.Add(reason);
        }

        // Nothing can speak. Keep the candidate that can do the most (so the
        // rest of the UI still sees accurate capabilities) and report every
        // reason we collected rather than picking one and hoping it is the one
        // this user needed — on Windows the useful message is about the missing
        // host, on macOS it is about missing synthesis data or audio player.
        var best = candidates
            .OrderByDescending(c => BitOperations.PopCount((uint)c.Capabilities))
            .First();

        DisposeUnused(candidates, keep: best, native: native);

        var error = failures.Count > 0
            ? string.Join(Environment.NewLine, failures.Distinct())
            : null;

        return new SpeechBackendSelection(best, native, false, error);
    }

    private static IPcmPlayer CreatePlayer()
    {
#if ANDROID
        return new Android.AndroidPcmPlayer();
#else
        return OperatingSystem.IsWindows() ? new WindowsPcmPlayer() : new SystemPcmPlayer();
#endif
    }

    /// <summary>
    /// Dispose the candidates we are not keeping. <paramref name="native"/> is
    /// never disposed here even when it lost, because it stays on as the
    /// phoneme provider.
    /// </summary>
    private static void DisposeUnused(
        IEnumerable<ITiSpeechBackend> candidates, ITiSpeechBackend keep, NativeTiSpeechBackend native)
    {
        foreach (var candidate in candidates)
        {
            if (ReferenceEquals(candidate, keep) || ReferenceEquals(candidate, native))
                continue;
            try { candidate.Dispose(); } catch { /* nothing useful to do while tearing down */ }
        }
    }
}
