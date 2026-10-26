# Screenrecorder

Schlanker Bildschirmrekorder für Windows 11 mit GPU-Encoding (NVIDIA NVENC inkl. RTX 50, Intel Quick Sync, AMD AMF),
frei definierbarem Aufnahmebereich und Systemsound + Mikrofon.

## Funktionen

- **Bereich**: verschiebbarer Rahmen mit Presets (YouTube 720p/1080p, 16:9, 4:3), eigene Größen, Bereich aufziehen,
  Hauptbildschirm, zweiter Bildschirm, ganzer Desktop
- **Bereich auswählen** schlägt beim Überfahren das Fenster bzw. den Fensterteil unter der Maus vor (z. B. die
  Webseitenfläche im Browser); Klick übernimmt, Ziehen zeichnet frei
- **Während der Aufnahme**: Laufzeit und aktuelle Dateigröße im Fenster und in der Titelleiste
- **Codecs**: H.264 / H.265 / AV1 über NVENC, Quick Sync, AMF, Windows Media Foundation, SVT-AV1 (CPU).
  Beim Start wird jeder Encoder per Testkodierung geprüft; angezeigt wird nur, was auf dem Rechner funktioniert.
- **Ton**: Systemsound (WASAPI Loopback) und Mikrofon, gemischt, als Opus
- **F2** startet/stoppt die Aufnahme global
- Deutsch / Englisch (Menü → Sprache)
- Ausgabe: `%USERPROFILE%\Videos\Screenrecorder\JJJJ-MM-TT_hh-mm-ss.mp4`

## Installation

Release-ZIP entpacken und `Screenrecorder.exe` starten. `ffmpeg.exe` liegt bei. Ohne sie wird `ffmpeg.exe` im
`PATH` gesucht.

## Hinweise

- Ton wird als **Opus** gespeichert (lizenzfrei). VLC, Browser, YouTube und moderne Schnittprogramme spielen das ab;
  ältere Player oder Editoren evtl. nicht.
- Aufnahmen innerhalb eines Monitors laufen komplett auf der GPU (Desktop Duplication). Ein Bereich über mehrere
  Monitore oder „Ganzer Bildschirm“ bei mehreren Monitoren nutzt GDI-Capture (mehr CPU-Last).
- Nur Monitore an der primären Grafikkarte werden per GPU erfasst.

## Entwicklung

Voraussetzungen: .NET 10 SDK, `ffmpeg`/`ffprobe` im `PATH` für die Integrationstests.

```powershell
dotnet build
dotnet test --filter "Category!=Integration"   # schnelle Unit-Tests (wie in CI)
dotnet test                                    # inkl. echter Aufnahme + A/V-Sync-Messung
dotnet run --project src/Screenrecorder
```

Die A/V-Sync-Messung blendet kurz ein weißes Fenster oben links ein und spielt einen leisen Ton ab.

## Release

```powershell
git tag v0.1.0
git push origin v0.1.0
```

Die GitHub Action baut eine self-contained Single-File-EXE, legt eine LGPL-`ffmpeg.exe` dazu (bricht bei einem
GPL-Build ab) und veröffentlicht das ZIP als GitHub Release.

## Lizenz

Proprietär, alle Rechte vorbehalten (siehe `LICENSE`). Fremdkomponenten: `THIRD-PARTY-NOTICES.md`.
