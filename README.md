# OpenTalkIt

An open-source reimplementation of the **Microsoft Talk It!** frontend, built with Avalonia UI and .NET 10.

Talk It! was a Windows speech synthesis application from the 1990s powered by the SoftVoice engine (`TIBASE32.DLL`). OpenTalkIt recreates its UI and behaviour as a modern cross-architecture desktop app, wrapping the original 32-bit DLL through the companion **TiSpeech** library.

![OpenTalkIt UI](ui.png)

## Features

- 20 voice personalities with per-personality presets (pitch, speed, pitch quality, vocal effort) matching the original application
- Pitch quality control: Natural / Monotone / Sung
- Vocal effort control: Normal / Breathy / Whispered
- Language switching: English / Spanish (German supported by the engine)
- Pitch and speed adjustable per personality
- Async speech with Stop support
- WAV export via WASAPI loopback capture

## Requirements

| Requirement | Detail |
|---|---|
| OS | Windows only |
| Architecture | **x86** — `TIBASE32.DLL` is a 32-bit native DLL; the app is compiled as x86 |
| .NET | .NET 10 |
| Native DLLs | `TIBASE32.DLL`, `TIENG32.DLL`, optionally `TISPAN32.DLL` |

The native DLLs are **not** included. You need an original Talk It! installation.

## Getting Started

1. Clone the repo
2. Copy `TIBASE32.DLL`, `TIENG32.DLL` (and optionally `TISPAN32.DLL`) into `OpenTalkIt/DLLs/`
3. Build and run:

```
dotnet run --project OpenTalkIt/OpenTalkIt.csproj
```

## Project Structure

The solution spans three sibling repositories:

```
TiSpeech/              Core P/Invoke bindings and engine types
TiSpeech.Client/       Named-pipe client that talks to TiSpeech.Host
TiSpeech.Host/         32-bit out-of-process host that loads TIBASE32.DLL
OpenTalkIt/            Avalonia UI application (this repo)
  Models/
    PersonalityButtonModel.cs   Per-personality button state
    PersonalityPreset.cs        Preset record (pitch, speed, pitch quality, vocal effort)
    PersonalityPresets.cs       Static lookup table of OG Talk It! presets per personality
    Records.cs                  TalkParameters — snapshot of all settings passed to the engine
  ViewModels/
    MainWindowViewModel.cs      Composition root; wires preset application on personality change
    PersonalityControlViewModel Personality grid + pitch/speed controls
    ParameterControlViewModel   Pitch quality, vocal effort, language
    TalkControlViewModel        Text input, Talk/Stop/Export commands
  Views/Controls/
    PersonalityControl.axaml    4×5 personality grid with pitch and speed inputs
    ParameterControl.axaml      Radio button groups for pitch quality, vocal effort, language
    TalkControl.axaml           Text box and Talk It! / Stop / Export buttons
  Services/
    WavRecorder.cs              WASAPI loopback capture for WAV export
    Settings.cs                 JSON settings persistence (%APPDATA%/OpenTalkIt/)
```

## TiSpeech Library

TiSpeech is a standalone .NET 10 library that wraps `TIBASE32.DLL` via P/Invoke. Because `TIBASE32.DLL` is 32-bit, the engine runs in a dedicated **TiSpeech.Host** process (x86 self-contained executable). The main app communicates with it through **TiSpeech.Client** over a named pipe. This lets the 64-bit Avalonia host and the 32-bit engine coexist without forcing the whole app to run as x86.

TiSpeech.Host handles engine lifecycle, speech synthesis, voice parameter setting, and async completion notification through a hidden Win32 message-only window (no Windows Forms dependency).

See [`TiSpeech/TiSpeech.md`](../../TiSpeech/TiSpeech.md) for the full API reference.

### Key enums

| Enum | Controls |
|---|---|
| `TiPersonality` | Voice character (0–19) |
| `TiVoicingMode` | Vocal effort: Normal / Breathy / Whispered |
| `TiF0Style` | Pitch contour: Natural / Monotone / Sung / Whispered-style |
| `TiSpeakingMode` | Token interpretation: Natural / Word / Spell / Number |
| `TiLanguage` | Runtime language switch |

## WAV Export

Since SoftVoice exposes no file-export API, WAV export works by capturing the system audio output (WASAPI loopback) while speech is playing. The captured audio is written to a WAV file at a location chosen via a save-file dialog. The last-used export folder is persisted in `%APPDATA%/OpenTalkIt/settings.json`.

## Reverse Engineering Notes

The preset values and engine parameter mappings were confirmed through a combination of Ghidra static analysis of `TIBASE32.DLL` and dynamic analysis (API Monitor + x32dbg breakpoints) of the original Talk It! application.

Notable findings:
- **Vocal effort** (Normal / Breathy / Whispered) is controlled by `SVSetVoicingMode` (values 0–2), **not** `SVSetGlottalSource` as the internal string table might suggest
- **Pitch quality** (Natural / Monotone / Sung) maps to `SVSetF0Style` values 0 / 2 / 4
- `SVSetGlottalSource` is present in the DLL but is not called by the original UI's Vocal Effort buttons

## License

This project contains no original Talk It! code or assets. The native DLLs (`TIBASE32.DLL` etc.) remain the property of their respective owners and are not redistributed here.
