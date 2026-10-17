# Lichtbild Studio

Mac-first Avalonia/.NET 8 prototype for a fast JPEG photo workflow.

## Features

- JPEG and JPG folder browsing
- Large image preview
- Search/filter by filename
- Bildabhängige automatische Vorschläge
- Independent switches for exposure, white balance, highlights, shadows, contrast and color
- Preview before applying the automatic proposal
- Manual exposure, contrast, saturation, warmth, highlights, shadows and black point controls
- Light/Dark mode
- JPEG export with quality 100 and 300 DPI metadata
- Original files are never overwritten

## GitHub macOS build

The repository contains:

`.github/workflows/build-macos.yml`

Open **Actions**, select **Build Lichtbild Studio - macOS**, then choose **Run workflow**.

Two artifacts are produced:

- `LichtbildStudio-osx-arm64.dmg` for Apple Silicon
- `LichtbildStudio-osx-x64.dmg` for Intel Macs

The GitHub runner installs the .NET SDK itself. A local .NET SDK is not required on the Mac used to install/test the resulting DMG.

## Project structure

```text
LichtbildStudio/
├── .github/
│   └── workflows/
│       └── build-macos.yml
├── App.axaml
├── App.axaml.cs
├── MainWindow.axaml
├── MainWindow.axaml.cs
├── Program.cs
├── LichtbildStudio.Avalonia.csproj
└── README.md
```

## Important

This repository is a source/build package. It is not claimed to have been compiled on this environment. GitHub Actions is the intended macOS build environment.
