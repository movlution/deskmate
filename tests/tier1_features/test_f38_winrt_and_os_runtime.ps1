# test_f38_winrt_and_os_runtime.ps1
# Tier 1 Feature Tests (F3, F4, F8, F12): CompGlass DirectComposition, WinRT OCR/PDF/ApplicationDataManager, icu.dll, System.Management WMI

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
$script:f38OwnedTmp = $null
$script:f38PrevDir = $env:DESKMATE_DIR
$script:f38PrevHome = $env:DESKMATE_HOME
$script:f38PrevDry = $env:DESKMATE_DOCK_DRY
if ([string]::IsNullOrEmpty($env:DESKMATE_DIR)) {
    $script:f38OwnedTmp = Join-Path ([IO.Path]::GetTempPath()) ("deskmate-f38-" + [Guid]::NewGuid().ToString("N"))
    $f38Home = Join-Path $script:f38OwnedTmp "home"
    New-Item -ItemType Directory -Path $script:f38OwnedTmp, $f38Home -Force | Out-Null
    $env:DESKMATE_DIR = $script:f38OwnedTmp
    $env:DESKMATE_HOME = $f38Home
    $env:DESKMATE_DOCK_DRY = "1"
}

. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

try {
    Describe "Tier 1 Feature F38: WinRT, CompGlass, icu.dll & OS Runtime Parity" {
        $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'

        It "Initializes CompGlass and exercises CompBackdrop Sync/Hide/Dispose on an offscreen Form without crashing" {
            $compGlassType = Get-DeskmateType "CompGlass"
            $backdropType = Get-DeskmateType "CompBackdrop"
            $glassKindType = Get-DeskmateType "GlassKind"
            $pillSpecType = $compGlassType.GetNestedType("PillSpec", $flags)
            Assert-NotNull $compGlassType "CompGlass type must resolve"
            Assert-NotNull $backdropType "CompGlass+CompBackdrop type must resolve"

            $form = New-Object System.Windows.Forms.Form
            $form.StartPosition = 'Manual'
            $form.ShowInTaskbar = $false
            $form.FormBorderStyle = 'None'
            $form.Location = New-Object System.Drawing.Point(-32000, -32000)
            $form.Size = New-Object System.Drawing.Size(160, 48)
            $form.Show()
            [System.Windows.Forms.Application]::DoEvents()
            try {
                $avail = [bool](Get-DeskmateStaticProperty "CompGlass" "Available")
                if ($avail) {
                    $backdrop = [Activator]::CreateInstance($backdropType)
                    try {
                        $frost = [Enum]::Parse($glassKindType, "Frost")
                        $spec = Invoke-DeskmateStaticMethod "CompGlass" "Spec" @($frost, $false, [single]1.0, [single]0.9, $false)
                        $pill = [Activator]::CreateInstance($pillSpecType, @([single]0, [single]0, [single]160, [single]48, [single]20))
                        $pills = [Array]::CreateInstance($pillSpecType, 1)
                        $pills.SetValue($pill, 0)
                        $bounds = New-Object System.Drawing.Rectangle(-32000, -32000, 160, 48)

                        $syncMethod = $backdropType.GetMethod("Sync", $flags)
                        $syncMethod.Invoke($backdrop, @($form.Handle, $bounds, $true, $spec, $pills, [int]1, $null, $null, [single]1.0, "", $false))
                        $backdrop.HideBackdrop()
                    }
                    finally {
                        $backdrop.Dispose()
                    }
                }
                Assert-True $true "CompGlass availability check ($avail) and backdrop lifecycle completed without exception"
            }
            finally {
                $form.Dispose()
            }
        }

        It "Probes Windows Ocr WinRT runtime cleanly and reads a crisp synthetic PNG when a recognizer is installed" {
            $avail = [bool](Get-DeskmateStaticProperty "Ocr" "Available")
            if ($avail) {
                $pngPath = Join-Path $env:DESKMATE_DIR "ocr-probe.png"
                $bmp = New-Object System.Drawing.Bitmap(260, 80)
                $g = [System.Drawing.Graphics]::FromImage($bmp)
                $font = New-Object System.Drawing.Font("Segoe UI", 24, [System.Drawing.FontStyle]::Bold)
                try {
                    $g.Clear([System.Drawing.Color]::White)
                    $g.DrawString("DESKMATE 2026", $font, [System.Drawing.Brushes]::Black, 10, 18)
                    $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
                }
                finally {
                    $font.Dispose(); $g.Dispose(); $bmp.Dispose()
                }

                $ocrType = Get-DeskmateType "Ocr"
                $readStringMethod = $ocrType.GetMethod("Read", $flags, $null, [type[]]@([string]), $null)
                $text = $readStringMethod.Invoke($null, [object[]]@(([string]$pngPath)))
                Assert-NotNull $text "Ocr.Read must return non-null string when Ocr.Available is true"
            }
            else {
                Assert-True $true "Ocr.Available returned false cleanly (no recognizer installed)"
            }
        }

        It "Creates a PDF via Converter.ImagesToPdf and reads/renders page 1 via WinRT PdfDocument" {
            $convType = Get-DeskmateType "Converter"
            $jobType = Get-DeskmateType "ConvJob"
            $imgPath = Join-Path $env:DESKMATE_DIR "pdf-src.png"
            $pdfPath = Join-Path $env:DESKMATE_DIR "pdf-out.pdf"

            $bmp = New-Object System.Drawing.Bitmap(64, 64)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.Clear([System.Drawing.Color]::CornflowerBlue)
                $bmp.Save($imgPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $g.Dispose(); $bmp.Dispose()
            }

            $job = [Activator]::CreateInstance($jobType)
            $files = New-Object 'System.Collections.Generic.List[string]'
            $files.Add($imgPath)

            $imagesToPdf = $convType.GetMethod("ImagesToPdf", $flags)
            $imagesToPdf.Invoke($null, [object[]]@($job.psobject.BaseObject, $files.psobject.BaseObject, ([string]$pdfPath), $false))
            Assert-True (Test-Path -LiteralPath $pdfPath -PathType Leaf) "ImagesToPdf must generate PDF output"

            $pdfPages = $convType.GetMethod("PdfPages", $flags)
            $count = [uint32]$pdfPages.Invoke($null, [object[]]@(([string]$pdfPath), $true))
            Assert-Equal $count 1 "WinRT PdfDocument must read and render 1 page"
        }

        It "Resolves IANA timezones to Windows timezone IDs via icu.dll in Ics.WindowsId" {
            $jakarta = Invoke-DeskmateStaticMethod "Ics" "WindowsId" @("Asia/Jakarta")
            $ny = Invoke-DeskmateStaticMethod "Ics" "WindowsId" @("America/New_York")
            Assert-Equal $jakarta "SE Asia Standard Time" "icu.dll must map Asia/Jakarta to SE Asia Standard Time"
            Assert-Equal $ny "Eastern Standard Time" "icu.dll must map America/New_York to Eastern Standard Time"
            Assert-Null (Invoke-DeskmateStaticMethod "Ics" "WindowsId" @("Invalid/Zone Name")) "Invalid timezone name must return null"
        }

        It "Exercises StartKey Win-token parsing and ApplicationDataManager WinRT interop safely" {
            $skType = Get-DeskmateType "StartKey"
            Assert-NotNull $skType "StartKey type must resolve"
            Assert-True ([bool]($skType.GetMethod("HasWinToken", $flags).Invoke($null, @("Meta+Space")))) "HasWinToken must match Meta+Space"
            Assert-True ([bool]($skType.GetMethod("HasWinToken", $flags).Invoke($null, @("LeftWin+Space")))) "HasWinToken must match LeftWin+Space"
            Assert-False ([bool]($skType.GetMethod("HasWinToken", $flags).Invoke($null, @("Ctrl+Space")))) "HasWinToken must reject Ctrl+Space"

            $rayMethod = $skType.GetMethod("RaycastUsesWinKey", $flags)
            Assert-NotNull $rayMethod "StartKey.RaycastUsesWinKey must exist"
            $res = $rayMethod.Invoke($null, @([string]$env:LOCALAPPDATA))
            Assert-True ($res -is [bool]) "RaycastUsesWinKey() must execute ApplicationDataManager WinRT/dir probe and return a bool"
        }

        It "Loads System.Management and probes WmiMonitorBrightness without runtime assembly errors" {
            $devicesAsm = (Get-DeskmateType "BrightnessService").Assembly
            Assert-NotNull $devicesAsm "BrightnessService type must resolve"
            try {
                $searcher = New-Object System.Management.ManagementObjectSearcher("root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness")
                try {
                    foreach ($obj in $searcher.Get()) {
                        $null = $obj["CurrentBrightness"]
                        break
                    }
                }
                finally {
                    $searcher.Dispose()
                }
            }
            catch [System.Management.ManagementException] {
                # Expected on VMs / headless monitors that do not expose WmiMonitorBrightness
            }
            catch [System.Runtime.InteropServices.COMException] {
                # Expected when WMI provider is unavailable on headless runner
            }
            Assert-True $true "System.Management WMI probe executed without assembly or platform load failure"
        }
    }
}
finally {
    if ($script:f38OwnedTmp) {
        $env:DESKMATE_DIR = $script:f38PrevDir
        $env:DESKMATE_HOME = $script:f38PrevHome
        $env:DESKMATE_DOCK_DRY = $script:f38PrevDry
        Remove-Item -LiteralPath $script:f38OwnedTmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
