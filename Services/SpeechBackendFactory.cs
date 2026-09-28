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
/// OpenTalkIt can talk to two backends:
///   * <c>TiSpeechClient</c> — the out-of-process 32-bit SoftVoice host. Real
///     speech, Windows only, because it runs a PE binary against the original
///     TIBASE32.DLL.
///   * <see cref="NativeTiSpeechBackend"/> — the from-scratch portable
///     reconstruction. Converts text to phonemes on macOS/Linux/Windows and,
///     when built with TIBASE32 + TIENG32 data, synthesises English audio
///     sample-exact with the original and plays it via the system player.
///
/// This class does not ask what operating system it is on. It asks each
/// candidate to open and takes the first one that says yes, which is the same
/// question the UI ultimately cares about. Each backend keeps its own platform
/// knowledge (TiSpeechClient refuses to launch a PE binary on Unix without
/// spawning anything), so nothing here has to guess.
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
        var native = new NativeTiSpeechBackend();

        // Order matters only for which failure message reads first. The pipe
        // client is the one that can actually speak, so it goes first.
        ITiSpeechBackend[] candidates = [new TiSpeechClient(), native];

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
