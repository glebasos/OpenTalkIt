using System.Collections.Generic;
using TiSpeech;

namespace OpenTalkIt.Models;

/// <summary>
/// Default presets matching the original Talk It! application settings per personality.
/// Pitch and Speed are percentages (100 / 150 = engine defaults).
/// </summary>
public static class PersonalityPresets
{
    public static readonly IReadOnlyDictionary<TiPersonality, PersonalityPreset> Defaults =
        new Dictionary<TiPersonality, PersonalityPreset>
        {
            [TiPersonality.Male]         = new(Pitch: 100, Speed: 150, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.Female]       = new(Pitch: 200, Speed: 150, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.LargeMale]    = new(Pitch: 190, Speed: 250, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.Child]        = new(Pitch: 350, Speed: 130, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.GiantMale]    = new(Pitch: 75, Speed: 140, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.MellowFemale] = new(Pitch: 190, Speed: 140, TiF0Style.Natural,  TiVoicingMode.Breathy),
            [TiPersonality.MellowMale]   = new(Pitch: 310, Speed: 90, TiF0Style.Sung,     TiVoicingMode.Normal),
            [TiPersonality.CrispMale]    = new(Pitch: 200, Speed: 140, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.TheFly]       = new(Pitch: 480, Speed: 150, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.Robotoid]     = new(Pitch: 90, Speed: 150, TiF0Style.Monotone, TiVoicingMode.Normal),
            [TiPersonality.Martian]      = new(Pitch: 80, Speed: 150, TiF0Style.Monotone,  TiVoicingMode.Normal),
            [TiPersonality.Colossus]     = new(Pitch: 66, Speed: 138, TiF0Style.Monotone, TiVoicingMode.Normal),
            [TiPersonality.FastFred]     = new(Pitch: 135, Speed: 300, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.OldWoman]     = new(Pitch: 270, Speed: 115, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.Munchkin]     = new(Pitch: 90, Speed: 150, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.Troll]        = new(Pitch: 110, Speed: 200, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.Nerd]         = new(Pitch: 140, Speed: 155, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.Milktoast]    = new(Pitch: 120, Speed: 165, TiF0Style.Natural,  TiVoicingMode.Normal),
            [TiPersonality.Tipsy]        = new(Pitch: 145, Speed: 115, TiF0Style.Sung,  TiVoicingMode.Normal),
            [TiPersonality.Choirboy]     = new(Pitch: 310, Speed: 90, TiF0Style.Sung,     TiVoicingMode.Normal),
        };

    public static PersonalityPreset For(TiPersonality p)
        => Defaults.TryGetValue(p, out var preset) ? preset : Defaults[TiPersonality.Male];
}
