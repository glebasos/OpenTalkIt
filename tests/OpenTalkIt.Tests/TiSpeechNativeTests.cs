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
/// The invariants that hold unconditionally (no synthesis, ever; no audio
/// buffer on failure; availability and reason are mutually exclusive) are
/// asserted with no branch at all, because those are the project's honesty
/// guarantees and must never depend on the machine.
/// </summary>
public class TiSpeechNativeTests
{
    // ── Unconditional honesty invariants ─────────────────────────────────────

    [Fact]
    public void Capabilities_NeverReportSynthesis()
    {
        // capi.c deliberately never sets TISPEECH_CAP_SYNTHESIS: the
        // phoneme-to-frame stage is not reconstructed. If this ever fails,
        // either the native side started claiming something it cannot do, or
        // synthesis genuinely landed — in which case this test is the place to
        // find out and update the UI gating deliberately.
        Assert.False(TiSpeechNative.Capabilities.HasFlag(TiEngineCapabilities.Synthesis));
    }

    [Fact]
    public void Synthesize_NeverSucceedsAndNeverReturnsABuffer()
    {
        var result = TiSpeechNative.Synthesize(TiLanguage.English, "DHAX KWIHK BROWN FAAKS");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Samples); // not an empty buffer a careless caller could "play"
        Assert.Equal(0, result.SampleRate);
        Assert.Contains(result.Status, new[] { TiStatus.NotImplemented, TiStatus.LibraryUnavailable });
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
        Assert.Equal("DHAX KWIHK BROWN FAAKS JAH5MPS OWVER DHAX LEYZIY DAAG", result.Phonemes);
    }

    [Fact]
    public void TextToPhonemes_IsCaseInsensitiveAndToleratesExtraWhitespace()
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        var lower = TiSpeechNative.TextToPhonemes(TiLanguage.English, "hello world");
        var messy = TiSpeechNative.TextToPhonemes(TiLanguage.English, "  HELLO\tworld  ");

        Assert.True(lower.IsSuccess, lower.Message);
        Assert.Equal(lower.Phonemes, messy.Phonemes);
    }

    [Fact]
    public void TextToPhonemes_HandlesLongInputByGrowingTheBuffer()
    {
        if (!TiSpeechNative.SupportsLanguage(TiLanguage.English)) return;

        // Far longer than the wrapper's initial buffer guess, so this exercises
        // the TISPEECH_E_BUFFERFULL retry loop rather than the happy path.
        var text = string.Join(' ', System.Linq.Enumerable.Repeat("synthesizer", 400));

        var result = TiSpeechNative.TextToPhonemes(TiLanguage.English, text);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(400, result.Phonemes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
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
