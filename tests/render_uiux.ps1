param([string]$BinaryPath = '')
# Contact sheet of every shared Kit control, dark and light, enabled / disabled / focused.
# Output: assets\brand\controls-preview.png
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    $hostExe = (Get-Process -Id $PID).Path
    $relaunchArgs = @('-NoProfile', '-NonInteractive', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($BinaryPath) { $relaunchArgs += @('-BinaryPath', $BinaryPath) }
    & $hostExe @relaunchArgs
    exit $LASTEXITCODE
}

$envNames = 'DESKMATE_DIR', 'DESKMATE_HOME', 'DESKMATE_DOCK_DRY', 'DESKMATE_SCALE', 'DESKMATE_TEST_BINARY'
$savedEnv = @{}
foreach ($n in $envNames) { $savedEnv[$n] = [Environment]::GetEnvironmentVariable($n) }

$profileDir = Join-Path ([IO.Path]::GetTempPath()) ('Deskmate\uiux-render\' + [Guid]::NewGuid().ToString('N'))
$focusHost = $null
$sheet = $null
$g = $null
$font = $null

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $profileDir 'data'), (Join-Path $profileDir 'home') | Out-Null
    $env:DESKMATE_DIR = Join-Path $profileDir 'data'
    $env:DESKMATE_HOME = Join-Path $profileDir 'home'
    $env:DESKMATE_DOCK_DRY = '1'
    $env:DESKMATE_SCALE = '1'
    if ($BinaryPath) { $env:DESKMATE_TEST_BINARY = (Resolve-Path -LiteralPath $BinaryPath).Path }
    . (Join-Path $PSScriptRoot 'common\DeskmateLoader.ps1')
    [void](Initialize-DeskmateLoader -Path $BinaryPath)
    $root = Split-Path -Parent $PSScriptRoot
    $flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'

    # Focus rings draw only for a focused control in a window showing keyboard cues
    $focusHost = New-Object Windows.Forms.Form
    $focusHost.ShowInTaskbar = $false
    $focusHost.StartPosition = 'Manual'
    $focusHost.Location = New-Object Drawing.Point(-4000, -4000)
    $focusHost.Size = New-Object Drawing.Size(600, 200)
    $focusHost.Show()

    function Render-Focused([Windows.Forms.Control]$c, [int]$w, [int]$h) {
        $c.Size = New-Object Drawing.Size($w, $h)
        $focusHost.Controls.Add($c)
        [void]$c.Focus()
        # WM_UPDATEUISTATE / UIS_CLEAR / UISF_HIDEFOCUS: show focus cues as after keyboard use
        $msg = [Windows.Forms.Message]::Create($focusHost.Handle, 0x0128, [IntPtr]((1 -shl 16) -bor 2), [IntPtr]::Zero)
        $focusHost.GetType().GetMethod('WndProc', $flags).Invoke($focusHost, @($msg)) | Out-Null
        Set-DeskmateStaticField 'KitInk' 'ByMouse' $false
        $bmp = New-Object Drawing.Bitmap($w, $h)
        $c.DrawToBitmap($bmp, (New-Object Drawing.Rectangle(0, 0, $w, $h)))
        $focusHost.Controls.Remove($c)
        return $bmp
    }

    function New-Kit([string]$kind) {
        switch ($kind) {
            'Primary' { $c = New-DeskmateControl 'KitButton'; $c.Primary = $true; $c.Text = 'Simpan'; return @($c, 140, 36) }
            'Secondary' { $c = New-DeskmateControl 'KitButton'; $c.Text = 'Batal'; return @($c, 140, 36) }
            'Switch on' { $c = New-DeskmateControl 'KitSwitch'; $c.Checked = $true; return @($c, 46, 26) }
            'Switch off' { $c = New-DeskmateControl 'KitSwitch'; return @($c, 46, 26) }
            'Segmented' { $c = New-DeskmateControl 'KitSegmented'; $c.Items = [string[]]@('Kecil', 'Sedang', 'Besar'); $c.Selected = 1; return @($c, 270, 36) }
            'Check on' { $c = New-DeskmateControl 'KitCheck'; $c.Text = 'Ingatkan saya'; $c.Checked = $true; return @($c, 200, 36) }
            'Check off' { $c = New-DeskmateControl 'KitCheck'; $c.Text = 'Ingatkan saya'; return @($c, 200, 36) }
            'Slider' { $c = New-DeskmateControl 'KitSlider'; $c.Value = 62; $c.ShowValue = $true; $c.Format = [Func[int, string]] { param($v) "$v%" }; return @($c, 270, 36) }
            'Palette' {
                $c = New-DeskmateControl 'KitPalette'
                $c.Presets = Get-DeskmateStaticField 'Ink' 'DarkAccents'
                $c.PresetsLight = Get-DeskmateStaticField 'Ink' 'LightAccents'
                $c.SelectedHex = [string](Get-DeskmateStaticField 'Ink' 'DefaultAccent')
                return @($c, 252, 36)
            }
            'Row in group' {
                $grp = New-DeskmateControl 'KitGroup'
                $row = New-DeskmateControl 'KitRow'
                $row.Title = 'Warna aksen'; $row.Detail = 'Ungu'; $row.Glyph = [char]0xE790
                $row.Dock = 'Fill'
                $grp.Controls.Add($row)
                return @($grp, 270, 52)
            }
        }
    }

    $cols = @('Enabled', 'Disabled', 'Focused')
    $kinds = @('Primary', 'Secondary', 'Switch on', 'Switch off', 'Segmented', 'Check on', 'Check off', 'Slider', 'Palette', 'Row in group')
    $colW = 300; $labelW = 130; $rowH = 56
    $panelH = 44 + $kinds.Count * $rowH + 12
    $sheetW = $labelW + $cols.Count * $colW + 16
    $sheet = New-Object Drawing.Bitmap($sheetW, ($panelH * 2))
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $font = New-Object Drawing.Font('Segoe UI', 12, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)

    foreach ($light in @($false, $true)) {
        Reset-DeskmateTheme -Light $light
        Invoke-DeskmateStaticMethod 'Ink' 'SetAccent' @([string](Get-DeskmateStaticField 'Ink' 'DefaultAccent'))
        $top = if ($light) { $panelH } else { 0 }
        $bg = Get-DeskmateStaticProperty 'KitInk' 'Window'
        $focusHost.BackColor = $bg
        $brush = New-Object Drawing.SolidBrush($bg); $g.FillRectangle($brush, 0, $top, $sheetW, $panelH); $brush.Dispose()
        $textBrush = New-Object Drawing.SolidBrush((Get-DeskmateStaticProperty 'KitInk' 'Sub'))
        $g.DrawString($(if ($light) { 'Terang' } else { 'Gelap' }), $font, $textBrush, 12, ($top + 14))
        for ($ci = 0; $ci -lt $cols.Count; $ci++) { $g.DrawString($cols[$ci], $font, $textBrush, ($labelW + $ci * $colW), ($top + 14)) }
        for ($ki = 0; $ki -lt $kinds.Count; $ki++) {
            $y = $top + 44 + $ki * $rowH
            $g.DrawString($kinds[$ki], $font, $textBrush, 12, ($y + 10))
            for ($ci = 0; $ci -lt $cols.Count; $ci++) {
                $made = New-Kit $kinds[$ki]
                $c = $made[0]; $w = $made[1]; $h = $made[2]
                if ($c -is [Windows.Forms.ButtonBase]) { $c.Font = Invoke-DeskmateStaticMethod 'Ink' 'Text' @([single]13) }
                try {
                    if ($cols[$ci] -eq 'Disabled') { $c.Enabled = $false }
                    $bmp = if ($cols[$ci] -eq 'Focused') { Render-Focused $c $w $h } else { Render-DeskmateControlToBitmap $c $w $h }
                    $g.DrawImageUnscaled($bmp, ($labelW + $ci * $colW), $y)
                    $bmp.Dispose()
                }
                finally { $c.Dispose() }
            }
        }
        $textBrush.Dispose()
    }
    $out = Join-Path $root 'assets\brand\controls-preview.png'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
    $sheet.Save($out, [Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Saved $out"
}
finally {
    if ($font) { $font.Dispose() }
    if ($g) { $g.Dispose() }
    if ($sheet) { $sheet.Dispose() }
    if ($focusHost) { $focusHost.Dispose() }
    foreach ($n in $envNames) { [Environment]::SetEnvironmentVariable($n, $savedEnv[$n]) }
    Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue
}
