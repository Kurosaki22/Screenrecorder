# Third-party notices

## FFmpeg (LGPL-3.0-or-later)

Screenrecorder ships an unmodified `ffmpeg.exe` and runs it as a separate process; it is not linked into the
application. The binary is an **LGPL** build from https://github.com/BtbN/FFmpeg-Builds (`win64-lgpl` variant).
The release pipeline rejects builds configured with `--enable-gpl`.

- FFmpeg license: https://www.gnu.org/licenses/lgpl-3.0.html (the build uses `--enable-version3`; full text in `FFMPEG-LICENSE.txt`)
- Exact FFmpeg version and build configuration: `FFMPEG-VERSION.txt`
- FFmpeg source code: https://github.com/FFmpeg/FFmpeg (check out the commit named in `FFMPEG-VERSION.txt`)
- Build scripts used to produce the binary: https://github.com/BtbN/FFmpeg-Builds

You may replace `ffmpeg.exe` with your own build of FFmpeg.

FFmpeg itself bundles further libraries in this build (for example Opus, SVT-AV1, NVIDIA codec headers, AMD AMF,
Intel oneVPL), each under a permissive or LGPL-compatible license as listed by the BtbN build.

## NAudio (MIT)

Copyright 2020 Mark Heath. https://github.com/naudio/NAudio

## Vortice.Windows (MIT)

Copyright (c) Amer Koleci and contributors. https://github.com/amerkoleci/Vortice.Windows

## SharpGen.Runtime (MIT)

Copyright (c) 2010-2014 SharpDX - Alexandre Mutel, 2017-2021 SharpGenTools contributors.

## .NET / WPF (MIT)

Copyright (c) .NET Foundation and Contributors. https://github.com/dotnet

### MIT License text

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
