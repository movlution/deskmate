param(
    [string]$BinaryPath = 'Deskmate.ds.exe',
    [string]$OutDir = '',
    [string[]]$Sections = @('general', 'look', 'island', 'tabs', 'dock', 'messages', 'work', 'remind', 'ai', 'system', 'keys', 'about')
)
# Renders the Settings window offscreen, dark and light, one PNG per section: <OutDir>\prefs-<id>-<theme>.png
# Runs against a throwaway profile (DESKMATE_DIR / DESKMATE_HOME); the user's settings are never touched.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'out' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$profileDir = Join-Path ([IO.Path]::GetTempPath()) ('Deskmate\prefs-render\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $profileDir 'data'), (Join-Path $profileDir 'home') | Out-Null
Set-Content -LiteralPath (Join-Path $profileDir 'data\settings.ini') -Encoding UTF8 -Value @('material=solid', 'glass=0', 'wall.sync=0')
$env:DESKMATE_DIR = Join-Path $profileDir 'data'
$env:DESKMATE_HOME = Join-Path $profileDir 'home'
$env:DESKMATE_DOCK_DRY = '1'
$env:DESKMATE_SCALE = '1'
$env:DESKMATE_TEST_BINARY = (Resolve-Path $BinaryPath).Path
. (Join-Path $PSScriptRoot 'common\DeskmateLoader.ps1')

$I = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$createControl = [Windows.Forms.Control].GetMethod('CreateControl', [Reflection.BindingFlags]'NonPublic,Instance', $null, [Type[]]@([bool]), $null)
$onLoad = [Windows.Forms.Form].GetMethod('OnLoad', [Reflection.BindingFlags]'NonPublic,Instance')
Set-DeskmateStaticField 'Anim' 'Reduced' $true

$island = [Activator]::CreateInstance((Get-DeskmateType 'Island'), $I, $null, @(), $null)
$prefsType = Get-DeskmateType 'PrefsWindow'
if ($Sections.Count -eq 1 -and $Sections[0] -like '*,*') { $Sections = $Sections[0] -split ',' }
try {
    foreach ($theme in @('dark', 'light')) {
        Reset-DeskmateTheme -Light ($theme -eq 'light')
        Invoke-DeskmateStaticMethod 'Ink' 'SetAccent' @([string](Get-DeskmateStaticField 'Ink' 'DefaultAccent'))
        foreach ($id in $Sections) {
            $form = [Activator]::CreateInstance($prefsType, $I, $null, @($island), $null)
            try {
                $form.StartPosition = 'Manual'
                $form.ShowInTaskbar = $false
                $form.Location = New-Object Drawing.Point(-32000, -32000)
                [void]$createControl.Invoke($form, @($true))
                [void]$onLoad.Invoke($form, @([EventArgs]::Empty))
                $form.Opacity = 1
                $form.ClientSize = New-Object Drawing.Size(1040, 720)
                $prefsType.GetMethod('Go', $I).Invoke($form, @($id)) | Out-Null
                $form.PerformLayout()
                # Pages build their rows after the first CreateControl: give the new children handles too
                [void]$createControl.Invoke($form, @($true))
                $full = New-Object Drawing.Bitmap($form.Width, $form.Height)
                $form.DrawToBitmap($full, (New-Object Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
                $o = $form.PointToScreen([Drawing.Point]::Empty)
                $client = $full.Clone((New-Object Drawing.Rectangle(($o.X - $form.Left), ($o.Y - $form.Top), 1040, 720)), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
                $file = Join-Path $OutDir "prefs-$id-$theme.png"
                $client.Save($file, [Drawing.Imaging.ImageFormat]::Png)
                $client.Dispose(); $full.Dispose()
                Write-Host "OK $file"
            }
            catch { $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }; Write-Host "FAIL $id $theme $($e.GetType().Name): $($e.Message)" }
            finally { if (-not $form.IsDisposed) { $form.Dispose() } }
        }
    }
}
finally {
    $island.Dispose()
    Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue
}
