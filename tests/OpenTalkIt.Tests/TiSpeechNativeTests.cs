using System;
using TiSpeech;
using Xunit;

namespace OpenTalkIt.Tests;

/// <summary>
/// Tests for the managed bindings over the reconstructed native engine
/// (<see cref="TiSpeechNative"/>, P/Invoke against capi.h).
///
/// These run in BOTH worlds and must pass in both:
///   * no native library present — the normal state for a contributor who has
///     not built it, since it needs a proprietary TIENG32.DLL. Every member is
///     expected to answer calmly rather than throw.
///   * library present — then the letter-to-sound assertions actually execute
///     against the real rule tables.
///
/// Synthesis assertions follow the compiled capability: a complete build must
/// return PCM, while missing data or a missing library must report failure.
/// </summary>
public class TiSpeechNativeTests
{
    // ── Unconditional honesty invariants ─────────────────────────────────────

    [Fact]
    public void Synthesize_MatchesTheAdvertisedCapability()
    {
        var result = TiSpeechNative.Synthesize(TiLanguage.English, " /HEH5LOW WER5LD");
        if (TiSpeechNative.Capabilities.HasFlag(TiEngineCapabilities.Synthesis))
        {
            Assert.True(result.IsSuccess, result.Status.Describe());
            Assert.NotNull(result.Samples);
            Assert.True(result.Samples.Length >= 8192);
            Assert.Equal(11025, result.SampleRate);
        }
        else
        {
            Assert.False(result.IsSuccess);
            Assert.Null(result.Samples);
            Assert.Equal(0, result.SampleRate);
            Assert.Contains(result.Status, new[] { TiStatus.NotImplemented, TiStatus.LibraryUnavailable });
        }
    }

    [Fact]
    public void Synthesize_InvalidPhonemesNeverReturnAudio()
    {
        var result = TiSpeechNative.Synthesize(TiLanguage.English, " XYZ");
        Assert.False(result.IsSuccess);
        Assert.Null(result.Samples);
        Assert.Equal(0, result.SampleRate);
    }

    [Fact]
    public void Availability_AndUnavailableReason_AreMutuallyExclusive()
    {
        if (TiSpeechNative.IsAvailable)
        {
            Assert.Null(TiSpeechNative.UnavailableReason);
            Assert.False(string.IsNullOrWhiteSpace(TiSpeechNative.BuildInfo));
            Assert.False(string.IsNullOrWhiteSpace(TiSpeechNative.LibraryPath));
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(TiSpeechNative.UnavailableReason));
            Assert.Null(TiSpeechNative.BuildInfo);
            Assert.Equal(TiEngineCapabilities.None, TiSpeechNative.Capabilities);
            Assert.Equal((TiLanguageFlags)0, TiSpeechNative.Languages);
        }
    }

    [Fact]
    public void UnavailableReason_ExplainsWhatToDoAndWhereItLooked()
    {
        if (TiSpeechNative.IsAvailable) return; // covered by the branch above

        var reason = TiSpeechNative.UnavailableReason!;

        // A missing native library is an expected steady state, so the message
        // has to be actionable rather than alarming: name the artifact, say
        // where it comes from, list what was searched, and mention the override.
        Assert.Contains("TIENG32.DLL", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Searched:", TiSpeechNative.UnavailableDetail!, StringComparison.Ordinal);
        Assert.Contains(TiSpeechNative.LibraryPathVariable, reason, StringComparison.Ordinal);
        Assert.Contains(TiSpeechNative.LibraryDirectoryVariable, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityAndLanguageBits_AgreeWithEachOther()
    {
        // capi.c sets TISPEECH_CAP_TEXT_TO_PHONEMES if and only if at least one
        // language's rule data was compiled in. Verifying the pair here means
        // the UI can rely on either without checking both.
        Assert.Equal(
            TiSpeechNative.Capabilities.HasFlag(TiEngineCapabilities.TextToPhonemes),
            TiSpeechNative.Languages != 0);
    }

    // ── Missing-library path ─────────────────────────────────────────────────

    [Fact]
    public void TextToPhonemes_WithoutLibrary_ReportsLibraryUnavailableRatherThanThrowing()
    {
        if (TiSpeechNative.IsAvailable) return; // exercised by the conversion tests below

        var result = TiSpeechNative.TextToPhonemes(TiLanguage.English, "hello");

        Assert.False(result.IsSuccess);
        Assert.Equal(TiStatus.LibraryUnavailable, result.Status);
        Assert.Equal(string.Empty, result.Phonemes);
        Assert.Equal(TiSpeechNative.UnavailableReason, result.Message);
    }

    // ── Missing-language-data path ───────────────────────────────────────────

    [Fact]
    public void TextToPhonemes_ForALanguageThisBuildLacks_ReportsNoLanguage()
    {
        if (!TiSpeechNative.IsAvailable) return;

        // German rule data is never compiled in by the current CMake setup
        // (only TISPEECH_ENG_DLL exists), so this exercises the real
        // TISPEECH_E_NOLANGUAGE path in capi.c whenever the library is present.
        if (TiSpeechNative.SupportsLanguage(TiLanguage.German)) return;

        var result = TiSpeechNative.TextToPhonemes(TiLanguage.German, "hallo");

        Assert.False(result.IsSuccess);
        Assert.Equal(TiStatus.NoLanguage, result.Status);
        Assert.Equal(string.Empty, result.Phonemes);
        Assert.Contains("German", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportsLanguage_MatchesTheLanguagesBitmask()
    {
        Assert.Equal(TiSpeechNative.Languages.HasFlag(TiLanguageFlags.English),
                     TiSpeechNative.SupportsLanguage(TiLanguage.English));
        Assert.Equal(TiSpeechNative.Languages.HasFlag(TiLanguageFlags.Spanish),
                     TiSpeechNative.SupportsLanguage(TiLanguage.Spanish));
        Assert.Equal(TiSpeechNative.Languages.HasFlag(TiLanguageFlags.German),
                     TiSpeechNative.SupportsLanguage(TiLanguage.German));
    }

    [Theory]
    [InlineData((TiLanguageFlags)0, "none")]
    [InlineData(TiLanguageFlags.English, "English")]
    [InlineData(TiLanguageFlags.English | TiLanguageFlags.Spanish, "English, Spanish")]
    public void DescribeLanguages_NamesWhatIsActuallyThere(TiLanguageFlags flags, string expected)
    {
        Assert.Equal(expected, TiSpeechNative.DescribeLanguages(flags));
    }

    // ── Working path (only when the library is actually built) ───────────────

    [Fact]
    public void TextToPhonemes_ConvertsAKnownSentence()
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        var result = TiSpeechNative.TextToPhonemes(
            TiLanguage.English, "the quick brown fox jumps over the lazy dog");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(" DHAX KWIH5K BRAW4N FAA5KS JAH5MPS OW5VER DHAX LEY5ZIY DAA5G", result.Phonemes);
    }

    [Fact]
    public void TextToPhonemes_PreservesOriginalTabAndStressBehavior()
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        var lower = TiSpeechNative.TextToPhonemes(TiLanguage.English, "hello world");
        var messy = TiSpeechNative.TextToPhonemes(TiLanguage.English, "  HELLO\tworld  ");

        Assert.True(lower.IsSuccess, lower.Message);
        Assert.True(messy.IsSuccess, messy.Message);
        Assert.Equal(" /HEH5LOW WER5LD", lower.Phonemes);
        Assert.Equal(" /HEHLOW WER5LD", messy.Phonemes);
    }

    [Fact]
    public void TextToPhonemes_RejectsTheOriginalSilentTruncationLimit()
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        // The original front end silently produces nothing above 514 bytes.
        var text = string.Join(' ', System.Linq.Enumerable.Repeat("synthesizer", 400));

        var result = TiSpeechNative.TextToPhonemes(TiLanguage.English, text);

        Assert.Equal(TiStatus.BadParam, result.Status);
        Assert.Empty(result.Phonemes);
        Assert.Contains("514", result.Message);
    }

    // ── Input validation (works with or without the library) ─────────────────

    [Fact]
    public void TextToPhonemes_RefusesTextOutsideTheEightBitTables()
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        // capi.h documents the input as UTF-8 restricted to Latin-1 because the
        // rule tables are 8-bit. Passing anything else would produce
        // confident-looking but wrong phonemes, so the wrapper refuses instead.
        var result = TiSpeechNative.TextToPhonemes(TiLanguage.English, "привет");

        Assert.False(result.IsSuccess);
        Assert.Equal(TiStatus.UnsupportedCharacters, result.Status);
        Assert.Equal(string.Empty, result.Phonemes);
    }

    [Theory]
    [InlineData("café", "CAFÉ")]
    [InlineData("niño", "NIÑO")]
    [InlineData("über", "ÜBER")]
    public void TextToPhonemes_DecodesLatin1BeforeCaseConversion(string lower, string upper)
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        var result = TiSpeechNative.TextToPhonemes(TiLanguage.English, lower);
        var expected = TiSpeechNative.TextToPhonemes(TiLanguage.English, upper);

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(expected.IsSuccess, expected.Message);
        Assert.NotEmpty(result.Phonemes);
        Assert.Equal(expected.Phonemes, result.Phonemes);
    }

    [Fact]
    public void TextToPhonemes_RejectsEmbeddedNulRatherThanTruncating()
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        var result = TiSpeechNative.TextToPhonemes(TiLanguage.English, "hello\0world");

        Assert.Equal(TiStatus.BadParam, result.Status);
        Assert.Empty(result.Phonemes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(-1)]
    public void SupportsLanguage_RejectsCombinedOrUnknownSelectors(int language)
    {
        Assert.False(TiSpeechNative.SupportsLanguage((TiLanguage)language));
    }

    [Fact]
    public void TextToPhonemes_EmptyTextStillRequiresSupportedLanguage()
    {
        if (!TiSpeechNative.IsAvailable) return;

        var result = TiSpeechNative.TextToPhonemes(TiLanguage.German, "");

        Assert.Equal(TiStatus.NoLanguage, result.Status);
    }

    [Fact]
    public void TextToPhonemes_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TiSpeechNative.TextToPhonemes(TiLanguage.English, null!));
    }

    [Fact]
    public void TextToPhonemes_EmptyText_IsAnEmptyResultNotAnError()
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        var result = TiSpeechNative.TextToPhonemes(TiLanguage.English, "   ");

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Phonemes);
    }

    // ── Status descriptions ──────────────────────────────────────────────────

    [Fact]
    public void Describe_NamesTheNativeConstantForNotImplemented()
    {
        // The UI shows this string verbatim; keeping the native constant in it
        // means a bug report quotes something greppable in capi.h.
        Assert.Contains("TISPEECH_E_NOTIMPL", TiStatus.NotImplemented.Describe(), StringComparison.Ordinal);
    }
}
