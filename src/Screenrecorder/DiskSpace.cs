using System.IO;

namespace Screenrecorder;

public enum DiskState { Ok, Low, Critical }

/// <summary>Free space on the output drive, so a recording never dies on a full disk.</summary>
public static class DiskSpace
{
    public const long LowBytes = 2L << 30, CriticalBytes = 500L << 20; // 2 GB, 500 MB

    public static DiskState Status(long freeBytes) =>
        freeBytes < CriticalBytes ? DiskState.Critical : freeBytes < LowBytes ? DiskState.Low : DiskState.Ok;

    /// <summary>Free bytes for the folder's drive; long.MaxValue when unknown (e.g. network shares).</summary>
    public static long FreeBytes(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    public static string Format(long bytes) => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
}
