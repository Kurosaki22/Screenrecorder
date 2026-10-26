# Measures how evenly motion is captured: shows a vsync-driven moving bar (bar.html) in an isolated Edge app
# window, records it with the capture paths the app can use and prints, per recording, how many frames show the
# bar where it should be at that frame's timestamp (analyze.py).
# Needs: ffmpeg/ffprobe on PATH, Python 3, Microsoft Edge, NVENC. Keep the test window in front while it runs.
# Usage: powershell -File tools/motion-test/run.ps1 [-Rounds 2]
param([int]$Rounds = 2)
$dir = $PSScriptRoot
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Mon {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct INFO { public int Size; public RECT Monitor, Work; public uint Flags; }
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(PT p, uint flags);
  [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref INFO i);
}
'@
$A = [System.Windows.Automation.AutomationElement]

# Separate profile = separate browser process tree, so the user's own Edge windows are never touched.
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$profile = Join-Path $dir 'edge-profile'
$page = 'file:///' + (Join-Path $dir 'bar.html').Replace('\', '/')
Start-Process $edge -ArgumentList @("--user-data-dir=`"$profile`"", '--no-first-run', "--app=$page", '--window-position=150,150', '--window-size=900,260') | Out-Null
Start-Sleep 5
$win = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, 'motion-test')))
$r = $win.Current.BoundingRectangle
# inner page area only (skip title bar and borders), physical pixels
$x = [int]$r.X + 20; $y = [int]$r.Y + 80; $w = ([int]$r.Width - 40) -band -2; $h = ([int]$r.Height - 120) -band -2

$pt = New-Object Mon+PT; $pt.X = $x; $pt.Y = $y
$hmon = [Mon]::MonitorFromPoint($pt, 2)
$info = New-Object Mon+INFO; $info.Size = [Runtime.InteropServices.Marshal]::SizeOf($info); [Mon]::GetMonitorInfo($hmon, [ref]$info) | Out-Null
$mx = $x - $info.Monitor.L; $my = $y - $info.Monitor.T
$cropR = ($info.Monitor.R - $info.Monitor.L) - $mx - $w; $cropB = ($info.Monitor.B - $info.Monitor.T) - $my - $h
"test area: $x,$y ${w}x$h"

$enc = @('-c:v', 'hevc_nvenc', '-preset', 'p5', '-rc', 'vbr', '-cq', '20', '-b:v', '0')
$gfx = "gfxcapture=hmonitor=$($hmon.ToInt64()):max_framerate=240:crop_left=${mx}:crop_top=${my}:crop_right=${cropR}:crop_bottom=${cropB}"
$cases = [ordered]@{
    'ddagrab-60'    = @('-f', 'lavfi', '-i', "ddagrab=output_idx=0:framerate=60:video_size=${w}x${h}:offset_x=${mx}:offset_y=${my}")
    'gfxcapture-60' = @('-f', 'lavfi', '-i', $gfx, '-vf', 'fps=60')
    'gfxcapture-120' = @('-f', 'lavfi', '-i', $gfx, '-vf', 'fps=120')
    'source-240'    = @('-f', 'lavfi', '-i', $gfx, '-vf', 'fps=240') # how even the test page itself is
}
& ffmpeg -hide_banner -loglevel error -y -f lavfi -i "ddagrab=output_idx=0:framerate=60:video_size=${w}x${h}:offset_x=${mx}:offset_y=${my}" -t 3 @enc (Join-Path $dir 'warmup.mp4')
foreach ($round in 1..$Rounds) {
    foreach ($name in $cases.Keys) {
        try { $win.SetFocus() } catch { } # Edge throttles requestAnimationFrame in background windows
        Start-Sleep -Milliseconds 300
        $out = Join-Path $dir "$name-$round.mp4"
        & ffmpeg -hide_banner -loglevel error -y @($cases[$name]) -t 5 @enc $out
        if (Test-Path $out) { python -I (Join-Path $dir 'analyze.py') $out $w ([int]($h / 2)) } else { "$name failed" }
    }
}
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*$profile*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
