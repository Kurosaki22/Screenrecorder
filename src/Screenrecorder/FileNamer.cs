using System.IO;
using Screenrecorder.Resources;

namespace Screenrecorder;

/// <summary>Builds output file names from patterns like "Recording_&lt;YYYY&gt;_&lt;MM&gt;_&lt;DD&gt;_&lt;HH&gt;_&lt;NN&gt;_&lt;SS&gt;_&lt;Z&gt;".</summary>
public static class FileNamer
{
    /// <summary>"Recording_…" or "Aufnahme_…" depending on the UI language.</summary>
    public static string DefaultPattern => Strings.FileNamePrefix + "_<YYYY>_<MM>_<DD>_<HH>_<NN>_<SS>_<Z>";

    /// <summary>Placeholders in the order the settings menu offers them.</summary>
    public static readonly string[] Tokens = ["<YYYY>", "<MM>", "<DD>", "<HH>", "<NN>", "<SS>", "<Z>", "<Nr>"];

    public static string Format(string pattern, DateTime time, int number)
    {
        var name = pattern
            .Replace("<YYYY>", time.ToString("yyyy"))
            .Replace("<MM>", time.ToString("MM"))
            .Replace("<DD>", time.ToString("dd"))
            .Replace("<HH>", time.ToString("HH"))
            .Replace("<NN>", time.ToString("mm"))
            .Replace("<SS>", time.ToString("ss"))
            .Replace("<Z>", time.ToString("fff"))
            .Replace("<Nr>", number.ToString());
        name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        return name.Length == 0 ? Strings.FileNamePrefix : name;
    }

    /// <summary>folder\name.mp4, or name_2.mp4, name_3.mp4 … if that file already exists.</summary>
    public static string Unique(string folder, string name, string extension = ".mp4")
    {
        var path = Path.Combine(folder, name + extension);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name}_{i}{extension}");
        return path;
    }
}
