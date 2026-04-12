using TiSpeech;

namespace OpenTalkIt.Models;

public record TalkParameters(
    int Pitch,
    int Speed,
    PersonalityButtonModel? Personality,
    TiLanguage Language,
    TiF0Style PitchQuality,
    TiVoicingMode VocalEffort
);
