# Black MIDI Visualizer — Own Edition

A Windows **Black MIDI** player/visualizer written in **C# + HLSL**, in the spirit of [Kiva](https://github.com/arduano/Kiva) — not a fork. Black background, Yamaha-style piano with a red strike bar, and a compact HUD of your own.

## Why it stays smooth

- **Smart culling** — only notes that currently intersect the falling window are collected. Notes that already passed the keyboard, or that are still waiting above the screen, are never submitted.
- **GPU batches, not per-note packets** — visible notes are packed into one `float32` structured buffer (`start, end, key, color`) and uploaded once per frame. The HLSL vertex shader expands each record into a quad. One `Draw` for the whole screen.
- **Hard cap** — the GPU buffer is preallocated (default 400k visible notes, max 500k). Excess notes are skipped instead of growing allocations until the process dies.
- **Audio on its own thread** — WinMM `midiOutShortMsg` runs at above-normal priority with a flood cap (events/ms) so dense Black MIDI cannot stall the note clock.

## Controls

| Action | UI | Keyboard |
| --- | --- | --- |
| Choose MIDI file | folder | `Ctrl+O` |
| Pause | pause bars | — |
| Play | triangle | `Space` |
| Settings | gear | `Ctrl+S` |
| Playback speed | Speed slider | — |
| Note size | Size slider | `[` `]` |
| Seek | red bar under toolbar | `←` `→` (±5s) |
| Stop | — | `Esc` |
| Fullscreen | — | `F11` |

Settings: MIDI device, 88 / 128 keys, note size, max visible notes, flood cap, HUD, VSync.

## Requirements

- Windows 10/11 **x64**
- DirectX 11 GPU
- A MIDI synth for sound (Windows GS, [OmniMIDI](https://github.com/KeppySoftware/OmniMIDI), VirtualMIDISynth, a hardware port, …)

## Run the release EXE

Grab `publish/win-x64/BlackMidiVisualizer.exe` (self-contained, no extra .NET install).

## Build from source

```bat
dotnet publish src\BlackMidiVisualizer\BlackMidiVisualizer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win-x64
```

Needs the .NET 8+ SDK with the `net8.0-windows` targeting pack.

## Layout of the code

```
src/BlackMidiVisualizer/
  Midi/        SMF parser, compact note store, WinMM player
  Render/      D3D11 device, visible-note collector, HUD bitmap
  Shaders/     Notes.hlsl (structured buffer → quads), Quads.hlsl (piano / icons / HUD)
  Ui/          Settings dialog
```

Notes are grouped **per MIDI key** and sorted by start time. Each frame a monotonic cursor (or a binary search after a seek) walks only the overlap `[now, now + window]`.

## License

MIT. Inspired by Kiva, Synthesia, and Piano From Above — implementation is original.
