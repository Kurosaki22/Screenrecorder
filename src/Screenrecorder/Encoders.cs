using System.Diagnostics;

namespace Screenrecorder;

/// <summary>
/// A video encoder ffmpeg may offer. TakesD3D11: accepts ddagrab's GPU frames without a download.
/// Quality 1..100 maps linearly from Worst to Best on QualityFlag (a CQ/CRF level, or a bitrate when Unit is "M").
/// </summary>
public sealed record VideoEncoder(string Name, string Label, string Vendor, bool TakesD3D11,
    string[] BaseArgs, string QualityFlag, int Worst, int Best, string Unit = "")
{
    public string[] QualityArgs(int quality)
    {
        var t = (Math.Clamp(quality, 1, 100) - 1) / 99.0;
        var value = (int)Math.Round(Worst + (Best - Worst) * t);
        return [.. BaseArgs, QualityFlag, $"{value}{Unit}"];
    }
}

public static class Encoders
{
    static readonly string[] Nvenc = ["-preset", "p5", "-rc", "vbr", "-b:v", "0"];

    // Only LGPL/BSD-compatible encoders (no libx264/libx265) so the app stays sellable.
    // Ranges are chosen so the default quality (80) matches the previous fixed settings.
    public static readonly IReadOnlyList<VideoEncoder> Known =
    [
        new("h264_nvenc", "H.264 (NVENC)", "NVIDIA", true, Nvenc, "-cq", 42, 18),
        new("hevc_nvenc", "H.265 / HEVC (NVENC)", "NVIDIA", true, Nvenc, "-cq", 44, 20),
        new("av1_nvenc", "AV1 (NVENC)", "NVIDIA", true, Nvenc, "-cq", 50, 24),
        // shortcut: QSV/AMF go through hwdownload (untested on Intel/AMD hardware); zero-copy could be added there.
        new("h264_qsv", "H.264 (Quick Sync)", "Intel", false, [], "-global_quality", 45, 18),
        new("hevc_qsv", "H.265 / HEVC (Quick Sync)", "Intel", false, [], "-global_quality", 45, 18),
        new("av1_qsv", "AV1 (Quick Sync)", "Intel", false, [], "-global_quality", 50, 22),
        new("h264_amf", "H.264 (AMF)", "AMD", false, [], "-b:v", 2, 24, "M"),
        new("hevc_amf", "H.265 / HEVC (AMF)", "AMD", false, [], "-b:v", 1, 16, "M"),
        new("av1_amf", "AV1 (AMF)", "AMD", false, [], "-b:v", 1, 16, "M"),
        new("h264_mf", "H.264 (Windows Media Foundation)", "Windows", false, [], "-b:v", 2, 24, "M"),
        new("hevc_mf", "H.265 / HEVC (Windows Media Foundation)", "Windows", false, [], "-b:v", 1, 16, "M"),
        new("libsvtav1", "AV1 (SVT-AV1, CPU)", "Software", false, ["-preset", "10"], "-crf", 55, 22),
    ];

    /// <summary>Known encoders whose name appears in `ffmpeg -encoders` output, in Known order.</summary>
    public static List<VideoEncoder> ParseAvailable(string encodersOutput)
    {
        var names = encodersOutput.Split('\n')
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 2 && p[0].StartsWith('V'))
            .Select(p => p[1])
            .ToHashSet();
        return Known.Where(e => names.Contains(e.Name)).ToList();
    }

    /// <summary>Compiled-in isn't enough (e.g. QSV on an NVIDIA box): test-encode one tiny clip per encoder.</summary>
    public static async Task<List<VideoEncoder>> DetectAsync(string ffmpeg)
    {
        var (_, output) = await RunAsync(ffmpeg, "-hide_banner -encoders");
        var candidates = ParseAvailable(output);
        var ok = await Task.WhenAll(candidates.Select(async e =>
            (await RunAsync(ffmpeg, $"-hide_banner -loglevel error -f lavfi -i color=s=256x256:d=0.2 -c:v {e.Name} -f null -")).exitCode == 0));
        return candidates.Where((_, i) => ok[i]).ToList();
    }

    static async Task<(int exitCode, string output)> RunAsync(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await stdout + await stderr);
    }
}
