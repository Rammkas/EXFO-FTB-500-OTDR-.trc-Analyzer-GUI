# ExfoTrcAnalyzerGUI

Windows desktop (WPF) analyzer for **EXFO FTB-500 reflectogram files (`.trc`)**:
a GUI front-end for the QC/acceptance-test engine of
[ExfoTrcAnalyzer](https://github.com/) (console version) — the `.trc` parser and
analysis engine are embedded in the `Engine/` folder, so this repository builds
standalone.

Built for a typical fiber acceptance bench:

```
OTDR port -> fixed launcher connector -> buffer spool (~1 km)
          -> QC connector -> fiber under test
```

Each measurement is taken at two wavelengths (1310 nm and 1550 nm). The tool
splits the trace into sections and events, excludes the buffer spool, computes
length / loss / specific attenuation of the fiber under test, checks configurable
thresholds and shows PASS / ATTN / FAIL verdicts with reasons.

## Features

- **Localization**: Ukrainian (default), English and Russian; switchable in
  Settings, persisted between runs. All labels, table columns, messages,
  CSV headers, reports and the built-in help are translated.
- **Three result tabs**: Fibers, Sections, Events — with per-column sorting
  (verdict-aware), search filter across all tabs, and row coloring by verdict
  (green PASS, red FAIL, amber borderline "yellow zone").
- **ATTN verdict**: PASS whose attenuation falls within the configurable
  yellow zone (±N% of the threshold) is flagged ATTN with an explanatory note.
- **Cable module grouping**: opening a folder named like
  `02579 OBgPo-96A8(8x12)-2,7` (96 fibers, 8 modules of 12) groups the Fibers
  tab into collapsible "Module N — color" headers. Module colors follow the
  TIA-598 standard order (blue, orange, green, brown, slate, white, red, black,
  yellow, violet, indigo, rose) and can be remapped in Settings.
- **Route map tab**: raw dB/meter trace with sections and event markers;
  wheel zoom around the cursor, pan, frozen dB window (Auto Y off), Shift+wheel
  dB zoom, shaded region below 0 dB, A/B measurement cursors with delta loss
  readout, compare mode that keeps cursors across files.
- **CSV export** of the three tables (same format as the console version,
  UTF-8 BOM, semicolon separator, comma decimal point).
- **Print / PDF report**: section picker (Map / Fibers / Sections / Events);
  if no printers are installed the report is saved as PDF via headless
  Edge/Chrome (with a built-in vector PDF writer as fallback).
- **Three themes**: gray, dark, light (cycle with the Theme button).
- **Settings window**: analysis thresholds, yellow-zone width, QC connector
  check, show-loss-columns, compare mode, interface language, module color
  order (separate dialog, persisted).

## Requirements

- **Windows 10/11 x64** (officially supported: Windows 10 1607+ and Windows 11;
  Windows 7/8.1 are not supported — .NET 10 does not run on them).
- .NET 10 SDK for building only (`global.json` pins SDK `10.0.404` with
  `rollForward: latestFeature`); the published exe is self-contained, no .NET
  installation needed on the target machine. No NuGet dependencies.

## Build & run

```
dotnet run -c Release
```

## Release build (single self-contained exe)

```
dotnet publish -c Release -p:PublishProfile=win-x64-selfcontained -o publish\win-x64
```

Produces one `exfo_trc_analyzer_gui.exe` (~130 MB, no .NET installation needed
on the target machine).

## Persisted user data

`%LOCALAPPDATA%\ExfoTrcAnalyzerGUI\`:

- `language.txt` — interface language;
- `module_colors.txt` — module color order (12 numbers).

## Notes

- `.trc` files are proprietary EXFO exports; the reader used here is
  reverse-engineered (see the console repository for the format documentation).
- Do not commit real reflectograms: `.gitignore` excludes `*.trc` on purpose.
- The `Engine/` sources are a snapshot of the console project's analysis engine;
  user-facing strings there are routed through the GUI's `Loc` class.

## License

MIT — see [LICENSE](LICENSE).
