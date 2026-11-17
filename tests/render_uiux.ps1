param([string]$BinaryPath = 'Deskmate.UIUX.exe')
$env:DESKMATE_TEST_BINARY = (Resolve-Path $BinaryPath).Path
. (Join-Path $PSScriptRoot 'common\DeskmateLoader.ps1')
$root = Split-Path $PSScriptRoot
$sheet = New-Object Drawing.Bitmap(760, 340)
$g = [Drawing.Graphics]::FromImage($sheet)
try {
    foreach ($light in @($false, $true)) {
        Reset-DeskmateTheme -Light $light
        $top = if ($light) { 170 } else { 0 }
        $bg = Get-DeskmateStaticProperty 'KitInk' 'Window'
        $brush = New-Object Drawing.SolidBrush($bg)
        $g.FillRectangle($brush, 0, $top, 760, 170)
        $brush.Dispose()
        $icon = [Drawing.Image]::FromFile((Join-Path $root 'assets\brand\deskmate-128.png'))
        $g.DrawImage($icon, 20, ($top + 20), 128, 128)
        $icon.Dispose()
        foreach ($enabled in @($true, $false)) {
            $s = New-DeskmateControl 'KitSegmented'
            try {
                $s.Items = [string[]]@('Small', 'Medium', 'Large')
                $s.Selected = 1
                $s.Enabled = $enabled
                $bmp = Render-DeskmateControlToBitmap $s 540 42
                $y = $top + $(if ($enabled) { 32 } else { 96 })
                $g.DrawImageUnscaled($bmp, 180, $y)
                $bmp.Dispose()
            } finally { $s.Dispose() }
        }
    }
    $sheet.Save((Join-Path $root 'assets\brand\controls-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $g.Dispose(); $sheet.Dispose() }
