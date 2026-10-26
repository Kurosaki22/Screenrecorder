# Screenrecorder

Schlanker Bildschirmrekorder für Windows 11 mit GPU-Encoding (NVIDIA NVENC inkl. RTX 50, Intel Quick Sync, AMD AMF),
frei definierbarem Aufnahmebereich und Systemsound + Mikrofon.

## Funktionen

- **Bereich**: verschiebbarer Rahmen mit Presets (YouTube 720p/1080p, 16:9, 4:3), eigene Größen, Bereich aufziehen,
  Hauptbildschirm, zweiter Bildschirm, ganzer Desktop
- **Bereich auswählen** schlägt beim Überfahren das Fenster bzw. den Fensterteil unter der Maus vor (z. B. die
  Webseitenfläche im Browser); Klick übernimmt, Ziehen zeichnet frei
- **Während der Aufnahme**: Laufzeit und aktuelle Dateigröße im Fenster und in der Titelleiste. Eine kleine
  Steuerleiste (Zeit, Größe, Pause/Fortsetzen, Stopp) sitzt unten links am Rahmen, oben links wenn unten kein Platz ist;
  sie wird nie mitaufgenommen. Der Rahmen ist rot bei Aufnahme und orange bei Pause.
- **Codecs**: H.264 / H.265 / AV1 über NVENC, Quick Sync, AMF, Windows Media Foundation, SVT-AV1 (CPU).
  Beim Start wird jeder Encoder per Testkodierung geprüft; angezeigt wird nur, was auf dem Rechner funktioniert.
- **Ton**: Systemsound (WASAPI Loopback) und Mikrofon, gemischt, als Opus
- **Pause**: Aufnahme pausieren und fortsetzen; am Ende entsteht eine einzige Datei
- **Hotkeys** (global, in den Einstellungen änderbar): **F2** Aufnahme starten/stoppen, **F3** Pause/Fortsetzen,
  **F4** Bereich auswählen; der aktuelle Hotkey steht unter dem jeweiligen Button
- **Einstellungen**: Qualitätsregler (kleinere Datei ↔ bessere Qualität), Mauszeiger an/aus, Hotkeys,
  Ausgabepfad und Dateiname mit Platzhaltern (`<YYYY> <MM> <DD> <HH> <NN> <SS> <Z> <Nr>`), Sprache (Deutsch/Englisch),
  Farbschema (Windows/Hell/Dunkel), Info mit Lizenzen
- Standard-Ausgabe: `%USERPROFILE%\Videos\Screenrecorder\Aufnahme_JJJJ_MM_TT_hh_mm_ss_zzz.mp4`

## Installation

`Screenrecorder-vX.Y.Z.exe` aus dem Release herunterladen und starten, mehr braucht es nicht. Die eingebettete
LGPL-`ffmpeg.exe` wird beim ersten Start nach `%LOCALAPPDATA%\Screenrecorder\ffmpeg\<Version>\` entpackt.
Eine eigene `ffmpeg.exe` neben der EXE hat Vorrang.

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
git tag v0.2.0
git push origin v0.2.0
```

Die GitHub Action lädt eine LGPL-`ffmpeg.exe` (bricht bei einem GPL-Build ab), bettet sie samt Lizenztext ein und
veröffentlicht eine self-contained Single-File-EXE plus `THIRD-PARTY-NOTICES.md` als GitHub Release.
Lokale Builds betten nichts ein und nutzen `ffmpeg` aus dem `PATH`.

Das App-Icon wird mit `dotnet run tools/make-icon.cs -- src/Screenrecorder/Assets/app.ico preview.png` erzeugt.

## Lizenz

Proprietär, alle Rechte vorbehalten (siehe `LICENSE`). Fremdkomponenten: `THIRD-PARTY-NOTICES.md`.
