using TiSpeech;

namespace OpenTalkIt.Models;

public record PersonalityPreset(
    int           Pitch,
    int           Speed,
    TiF0Style     PitchQuality,
    TiVoicingMode VocalEffort
);
