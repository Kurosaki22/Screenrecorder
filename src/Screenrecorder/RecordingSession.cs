using System.Diagnostics;
using System.IO;

namespace Screenrecorder;

/// <summary>
/// One user-visible recording. ffmpeg cannot pause, so each pause ends a segment (name.partN.mp4) and resuming
/// starts the next; stopping joins the segments losslessly into the final file.
/// </summary>
public sealed class RecordingSession
{
    readonly string ffmpeg;
    readonly RecordOptions options;
    readonly List<string> parts = [];
    readonly Stopwatch clock = new();
    Recorder? current;
    long finishedBytes;

    public string OutputPath { get; }
    public bool IsPaused => current is null;
    public bool IsWaitingForFirstFrame => current is { HasStarted: false };
    public TimeSpan Elapsed => clock.Elapsed;
    public long BytesWritten => finishedBytes + (current?.BytesWritten ?? 0);

    /// <summary>Raised (on a worker thread) when ffmpeg dies mid-segment; carries its last error lines.</summary>
    public event Action<string>? Failed;

    RecordingSession(string ffmpeg, RecordOptions options, string outputPath)
    {
        this.ffmpeg = ffmpeg;
        this.options = options;
        OutputPath = outputPath;
    }

    public static RecordingSession Start(string ffmpeg, RecordOptions options, string outputPath)
    {
        var session = new RecordingSession(ffmpeg, options, outputPath);
        session.StartSegment();
        return session;
    }

    void StartSegment()
    {
        var part = Path.ChangeExtension(OutputPath, $".part{parts.Count + 1}.mp4");
        var rec = Recorder.Start(ffmpeg, options, part);
        rec.Failed += message => Failed?.Invoke(message);
        parts.Add(part);
        current = rec;
        clock.Start();
    }

    public async Task PauseAsync()
    {
        if (current is null) return;
        clock.Stop();
        var rec = current;
        current = null;
        await rec.StopAsync();
        finishedBytes += File.Exists(parts[^1]) ? new FileInfo(parts[^1]).Length : 0;
    }

    public void Resume()
    {
        if (current is null) StartSegment();
    }

    /// <summary>Finishes the file. Returns null on success, otherwise an error message (segments are kept).</summary>
    public async Task<string?> StopAsync()
    {
        await PauseAsync();
        var existing = parts.Where(File.Exists).ToList();
        if (existing.Count == 0) return Resources.Strings.NoData;
        if (existing.Count == 1)
        {
            File.Move(existing[0], OutputPath, overwrite: true);
            return null;
        }

        var list = OutputPath + ".concat.txt";
        await File.WriteAllTextAsync(list, ConcatList(existing));
        var psi = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", list, "-c", "copy", OutputPath })
            psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        var errors = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        File.Delete(list);
        if (p.ExitCode != 0)
            return $"{errors.Trim()}{Environment.NewLine}{string.Join(Environment.NewLine, existing)}";

        existing.ForEach(File.Delete);
        return null;
    }

    /// <summary>ffmpeg concat-demuxer list; single quotes inside paths are escaped as '\''.</summary>
    public static string ConcatList(IEnumerable<string> paths) =>
        string.Concat(paths.Select(p => $"file '{p.Replace("'", @"'\''")}'\n"));
}
