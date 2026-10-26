using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Screenrecorder;

/// <summary>One running recording: an ffmpeg process plus optional audio pipe writers.</summary>
public sealed class Recorder
{
    readonly Process ffmpeg;
    readonly List<AudioPipeWriter> writers;
    readonly CancellationTokenSource cts = new();
    readonly Queue<string> errorTail = new();
    bool stopping;
    long bytesWritten;

    public string OutputPath { get; }

    /// <summary>Output size so far as reported by ffmpeg (FileInfo.Length lags while ffmpeg holds the file open).</summary>
    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    /// <summary>Raised (on a worker thread) when ffmpeg exits without being asked to; carries its last error lines.</summary>
    public event Action<string>? Failed;

    Recorder(Process ffmpeg, List<AudioPipeWriter> writers, string outputPath)
    {
        this.ffmpeg = ffmpeg;
        this.writers = writers;
        OutputPath = outputPath;
    }

    public static Recorder Start(string ffmpegPath, CaptureArea area, VideoEncoder encoder, int fps,
        bool systemAudio, bool microphone, string outputPath)
    {
        var writers = new List<AudioPipeWriter>();
        try
        {
            // shortcut: NAudio 3 marks these IWaveIn classes obsolete in favour of WasapiRecorder; migrate if they get removed.
#pragma warning disable CS0618
            if (systemAudio) writers.Add(new AudioPipeWriter(new WasapiLoopbackCapture()));
            if (microphone) writers.Add(new AudioPipeWriter(new WasapiCapture()));
#pragma warning restore CS0618
        }
        catch
        {
            writers.ForEach(w => w.Dispose());
            throw;
        }

        var psi = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in FfmpegArgs.Build(area, encoder, fps, writers.Select(w => w.Input).ToList(), outputPath))
            psi.ArgumentList.Add(arg);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var rec = new Recorder(p, writers, outputPath);
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (rec.errorTail)
            {
                rec.errorTail.Enqueue(e.Data);
                if (rec.errorTail.Count > 20) rec.errorTail.Dequeue();
            }
        };
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && ParseTotalSize(e.Data) is long size) Interlocked.Exchange(ref rec.bytesWritten, size);
        };
        p.Exited += (_, _) =>
        {
            if (rec.stopping) return;
            rec.cts.Cancel();
            rec.writers.ForEach(w => w.Dispose());
            p.WaitForExit(); // flush stderr events
            rec.Failed?.Invoke(rec.ErrorText());
        };

        try
        {
            p.Start();
        }
        catch
        {
            writers.ForEach(w => w.Dispose());
            throw;
        }
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        foreach (var w in writers)
            _ = w.StartWhenConnectedAsync(rec.cts.Token).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
        return rec;
    }

    /// <summary>Asks ffmpeg to finish ('q') so the MP4 index is written; kills it only if it hangs.</summary>
    public async Task StopAsync()
    {
        stopping = true;
        if (!ffmpeg.HasExited)
        {
            try
            {
                await ffmpeg.StandardInput.WriteAsync('q');
                await ffmpeg.StandardInput.FlushAsync();
            }
            catch (IOException) { }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await ffmpeg.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { ffmpeg.Kill(); }
        }
        cts.Cancel();
        writers.ForEach(w => w.Dispose());
        ffmpeg.Dispose();
    }

    /// <summary>Reads "total_size=12345" from ffmpeg's -progress output; other lines and "N/A" give null.</summary>
    public static long? ParseTotalSize(string line) =>
        line.StartsWith("total_size=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(11), out var size) ? size : null;

    string ErrorText()
    {
        lock (errorTail) return string.Join(Environment.NewLine, errorTail);
    }

    /// <summary>ffmpeg.exe next to the app wins over one on PATH.</summary>
    public static string? FindFfmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim('"'), "ffmpeg.exe"))
            .FirstOrDefault(File.Exists);
    }
}
