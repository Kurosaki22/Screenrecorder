param([string]$Out = "$PSScriptRoot\..\docs\screenshots")
# Takes the README screenshots (English, dark + light) by driving the real app through UI Automation.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
$exe = Resolve-Path "$PSScriptRoot\..\src\Screenrecorder\bin\Debug\net10.0-windows\Screenrecorder.exe"
$cfg = "$env:APPDATA\Screenrecorder\settings.json"
$backup = Get-Content $cfg -Raw
New-Item -ItemType Directory -Force $Out | Out-Null
$root = [Windows.Automation.AutomationElement]::RootElement

function Find($parent, $type, $scope = 'Children') {
    $c = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty), ([Windows.Automation.ControlType]::$type)
    $parent.FindAll([Windows.Automation.TreeScope]::$scope, $c)
}
function Snap($rect, $file) {
    $rect = [Drawing.Rectangle]::new([int]$rect.X + 8, [int]$rect.Y, [int]$rect.Width - 16, [int]$rect.Height - 8)
    $b = New-Object Drawing.Bitmap ([int]$rect.Width), ([int]$rect.Height)
    $g = [Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $b.Size)
    $b.Save("$Out\$file", [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()
}
function Window($title) {
    for ($i = 0; $i -lt 40; $i++) {
        $w = @($root.FindAll('Children', [Windows.Automation.Condition]::TrueCondition)) + @(if ($script:main) { $script:main.FindAll('Children', [Windows.Automation.Condition]::TrueCondition) }) | Where-Object { $_.Current.Name -eq $title -and $_.Current.ProcessId -eq $script:proc.Id } | Select-Object -First 1
        if ($w) { return $w }; Start-Sleep -Milliseconds 250
    }
    throw "window $title not found; have: " + (($root.FindAll('Children', [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ProcessId -eq $script:proc.Id } | ForEach-Object { $_.Current.Name }) -join '|')
}
function Press($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

try {
    foreach ($theme in 'dark', 'light') {
        Get-Process Screenrecorder -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1
        $s = $backup | ConvertFrom-Json
        $s.Language = 'en'; $s.Theme = if ($theme -eq 'dark') { 2 } else { 1 }; $s.Encoder = 'h264_mf'; $s.Region.X = 60; $s.Region.Y = 60
        $s.OutputFolder = 'C:\Users\you\Videos\Screenrecorder'; $s.FileNamePattern = 'Recording_<YYYY>_<MM>_<DD>_<HH>_<NN>_<SS>_<Z>'
        $s | ConvertTo-Json -Depth 5 | Set-Content $cfg -Encoding utf8
        $bd = New-Object Windows.Forms.Form
        $bd.FormBorderStyle = 'None'; $bd.StartPosition = 'Manual'; $bd.Bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bd.BackColor = if ($theme -eq 'dark') { [Drawing.Color]::FromArgb(70, 70, 78) } else { [Drawing.Color]::FromArgb(200, 200, 208) }
        $bd.ShowInTaskbar = $false; $bd.Show(); [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 500
        $script:proc = Start-Process $exe -PassThru; Start-Sleep 5
        $script:main = $null; $main = Window 'Screenrecorder'; $script:main = $main
        $suffix = if ($theme -eq 'dark') { '' } else { '-light' }
        $main.SetFocus(); Start-Sleep 1
        $btn = @(Find $main Button 'Descendants')
        Snap $main.Current.BoundingRectangle "main-window$suffix.png"
        # Settings (button 7), every page
        Press $btn[7]; Start-Sleep 1
        $set = Window 'Settings'
        $items = @(Find $set ListItem 'Descendants')
        $names = 'recording', 'hotkeys', 'output', 'appearance', 'about'
        for ($i = 0; $i -lt $items.Count -and $i -lt 5; $i++) {
            $items[$i].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 600
            Snap $set.Current.BoundingRectangle "settings-$($names[$i])$suffix.png"
        }
        $set.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
        Start-Sleep 1
        # Sound -> mixer popup (button 5), shot of the whole area around the window
        # The closed dialog hands focus to another app: put the backdrop above it, then the main window above the backdrop.
        $bd.TopMost = $true; [Windows.Forms.Application]::DoEvents(); $bd.TopMost = $false; [Windows.Forms.Application]::DoEvents()
        $main.SetFocus(); Start-Sleep 1
        Press $btn[5]; Start-Sleep 1
        $mr = $main.Current.BoundingRectangle
        $u = [Drawing.Rectangle]::new([int]$mr.X - 8, [int]$mr.Y, [int]$mr.Width + 260, [int]$mr.Height + 175)
        Snap $u "mixer$suffix.png"
        $bd.Close()
    }
} finally {
    Get-Process Screenrecorder -ErrorAction SilentlyContinue | Stop-Process -Force
    Set-Content $cfg $backup -Encoding utf8 -NoNewline
}
