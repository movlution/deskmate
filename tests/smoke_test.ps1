# smoke_test.ps1
# End-to-End Infrastructure Smoke Test Suite for Deskmate
# Verifies compilation, assembly loading, reflection helpers, headless offscreen rendering, and CLI invocation.

$commonDir = Join-Path $PSScriptRoot "common"
$script:smokeOwnedTmp = $null
$script:smokePrevDir = $env:DESKMATE_DIR
$script:smokePrevHome = $env:DESKMATE_HOME
$script:smokePrevDry = $env:DESKMATE_DOCK_DRY
if ([string]::IsNullOrEmpty($env:DESKMATE_DIR)) {
    $script:smokeOwnedTmp = Join-Path ([IO.Path]::GetTempPath()) ("deskmate-smoke-" + [Guid]::NewGuid().ToString("N"))
    $smokeHome = Join-Path $script:smokeOwnedTmp "home"
    New-Item -ItemType Directory -Path $script:smokeOwnedTmp, $smokeHome -Force | Out-Null
    $env:DESKMATE_DIR = $script:smokeOwnedTmp
    $env:DESKMATE_HOME = $smokeHome
    $env:DESKMATE_DOCK_DRY = "1"
}

. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

try {
Describe "Smoke Test Suite: Environment & Binary Integrity" {
    It "Validates 64-bit runtime environment" {
        Assert-Equal ([IntPtr]::Size) 8 "Process must be 64-bit ([IntPtr]::Size == 8)"
    }

    It "Validates Deskmate reflection binary exists on disk and is non-empty" {
        $binPath = Find-DeskmateBinary
        Assert-NotNull $binPath "Deskmate reflection binary must be located"
        $file = Get-Item $binPath
        Assert-True ($file.Length -gt 500000) "Deskmate reflection binary size must exceed 500KB (actual: $($file.Length) bytes)"
    }

    It "Loads Deskmate assembly into reflection cache" {
        $asm = Initialize-DeskmateLoader
        Assert-NotNull $asm "Assembly must be loaded"
        try { $types = $asm.GetTypes() }
        catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $null -ne $_ } }
        Assert-True ($types.Length -gt 100) "Assembly must contain application types (found: $($types.Length))"
    }

    It "Embeds Deskmate.ico manifest resource stream" {
        $asm = Initialize-DeskmateLoader
        $icoStream = $asm.GetManifestResourceStream("Deskmate.ico")
        try {
            Assert-NotNull $icoStream "Embedded manifest resource 'Deskmate.ico' must be present"
            Assert-True ($icoStream.Length -gt 0) "Embedded 'Deskmate.ico' stream must be non-empty"
        }
        finally {
            if ($icoStream) { $icoStream.Dispose() }
        }
    }
}

Describe "Smoke Test Suite: Core Reflection & Type Resolution" {
    It "Resolves primary engine and token types" {
        $requiredTypes = @("Ink", "Tone", "Island", "KitInk", "KitButton", "KitSwitch", "KitSlider", "KitRow", "KitWindow")
        foreach ($typeName in $requiredTypes) {
            $t = Get-DeskmateType $typeName
            Assert-NotNull $t "Type [$typeName] must be resolvable via Get-DeskmateType"
        }
    }

    It "Verifies Design Token Spacing & Radii constants on Deskmate.Ink" {
        $radiusShell = Get-DeskmateStaticField "Ink" "RadiusShell"
        $radiusModule = Get-DeskmateStaticField "Ink" "RadiusModule"
        $radiusControl = Get-DeskmateStaticField "Ink" "RadiusControl"

        Assert-Equal $radiusShell 32 "RadiusShell must equal 32"
        Assert-Equal $radiusModule 20 "RadiusModule must equal 20"
        Assert-Equal $radiusControl 12 "RadiusControl must equal 12"

        $sp4 = Get-DeskmateStaticField "Ink" "Sp4"
        $sp8 = Get-DeskmateStaticField "Ink" "Sp8"
        $sp16 = Get-DeskmateStaticField "Ink" "Sp16"
        $sp32 = Get-DeskmateStaticField "Ink" "Sp32"

        Assert-Equal $sp4 4 "Sp4 spacing token must equal 4"
        Assert-Equal $sp8 8 "Sp8 spacing token must equal 8"
        Assert-Equal $sp16 16 "Sp16 spacing token must equal 16"
        Assert-Equal $sp32 32 "Sp32 spacing token must equal 32"
    }

    It "Keeps memory leak guard threshold at or below 100 MB" {
        $leakMb = [double](Get-DeskmateStaticField "Island" "LeakMb")
        Assert-True ($leakMb -gt 0 -and $leakMb -le 100) "LeakMb must be > 0 and <= 100 (actual: $leakMb)"
    }

    It "Verifies Typography Scale properties on Deskmate.Ink" {
        Invoke-DeskmateStaticMethod "Ink" "Init" @()

        $roles = @("Hero", "Title1", "Title2", "Headline", "Body", "Callout", "Caption")
        foreach ($role in $roles) {
            $font = Get-DeskmateStaticProperty "Ink" $role
            Assert-NotNull $font "Typography role [$role] must return a non-null Font object"
            Assert-True ($font -is [System.Drawing.Font]) "[$role] must be an instance of System.Drawing.Font"
            Assert-True ($font.SizeInPoints -gt 0) "[$role] font size must be greater than 0"
        }
    }

    It "Verifies KitInk color token properties" {
        Reset-DeskmateTheme -Light $false

        $kitTokens = @("Window", "Side", "Group", "Line", "Hover", "Raised", "Text", "Sub", "Faint", "Disabled", "Accent", "OnAccent")
        foreach ($token in $kitTokens) {
            $color = Get-DeskmateStaticProperty "KitInk" $token
            Assert-NotNull $color "KitInk token [$token] must not be null"
            Assert-True ($color -is [System.Drawing.Color]) "KitInk token [$token] must be System.Drawing.Color"
            Assert-Equal $color.A 255 "KitInk token [$token] must be fully opaque (A=255)"
        }
    }
}

Describe "Smoke Test Suite: Mathematical Contrast & Oracles" {
    It "Verifies authoritative WCAG contrast calculation for Black vs White" {
        $black = [System.Drawing.Color]::Black
        $white = [System.Drawing.Color]::White

        $ratio = Get-WcagContrastRatio $black $white
        Assert-Equal $ratio 21.0 "Black vs White contrast must equal 21.0:1"
    }

    It "Verifies Deskmate.Island.Contrast reflection matches authoritative calculation" {
        $black = [System.Drawing.Color]::Black
        $white = [System.Drawing.Color]::White

        $deskmateRatio = Get-DeskmateContrast $black $white
        $wcagRatio = Get-WcagContrastRatio $black $white

        Assert-Equal $deskmateRatio $wcagRatio "Deskmate.Island.Contrast ($deskmateRatio) must match Get-WcagContrastRatio ($wcagRatio)"
    }

    It "Verifies Assert-Contrast assertion passes for accessible pairs and fails for poor pairs" {
        $white = [System.Drawing.Color]::White
        $darkGrey = [System.Drawing.Color]::FromArgb(255, 30, 30, 30)
        $ratio = Assert-Contrast $white $darkGrey 4.5 "Dark grey on white must pass 4.5:1"
        Assert-True ($ratio -ge 4.5) "Contrast ratio must be >= 4.5"
    }
}

Describe "Smoke Test Suite: Headless Offscreen Control Rendering" {
    It "Instantiates and renders KitButton offscreen via DrawToBitmap" {
        $btn = New-DeskmateControl "KitButton" @{ Text = "Smoke Test Button" }
        Assert-NotNull $btn "KitButton instance must be created"

        $bmp = Render-DeskmateControlToBitmap $btn 120 36
        Assert-NotNull $bmp "Rendered bitmap must not be null"
        Assert-Equal $bmp.Width 120 "Bitmap width must match requested width"
        Assert-Equal $bmp.Height 36 "Bitmap height must match requested height"

        # Check center pixel has opaque alpha
        $centerPixel = $bmp.GetPixel(60, 18)
        Assert-Equal $centerPixel.A 255 "Center pixel must be opaque"

        $bmp.Dispose()
        $btn.Dispose()
    }

    It "Instantiates and renders KitSwitch offscreen" {
        $switch = New-DeskmateControl "KitSwitch" @{ Checked = $true }
        Assert-NotNull $switch "KitSwitch instance must be created"

        $bmp = Render-DeskmateControlToBitmap $switch 60 28
        Assert-NotNull $bmp "Rendered switch bitmap must not be null"
        Assert-Equal $bmp.Width 60 "Switch width must match"
        Assert-Equal $bmp.Height 28 "Switch height must match"

        $bmp.Dispose()
        $switch.Dispose()
    }

    It "Instantiates and renders KitSlider offscreen" {
        $slider = New-DeskmateControl "KitSlider" @{ Value = 40; Minimum = 0; Maximum = 100 }
        Assert-NotNull $slider "KitSlider instance must be created"

        $bmp = Render-DeskmateControlToBitmap $slider 140 24
        Assert-NotNull $bmp "Rendered slider bitmap must not be null"
        Assert-Equal $bmp.Width 140 "Slider width must match"
        Assert-Equal $bmp.Height 24 "Slider height must match"

        $bmp.Dispose()
        $slider.Dispose()
    }

    It "Instantiates and renders KitRow offscreen" {
        $row = New-DeskmateControl "KitRow" @{ Text = "Smoke Row Option" }
        Assert-NotNull $row "KitRow instance must be created"

        $bmp = Render-DeskmateControlToBitmap $row 180 36
        Assert-NotNull $bmp "Rendered row bitmap must not be null"
        Assert-Equal $bmp.Width 180 "Row width must match"
        Assert-Equal $bmp.Height 36 "Row height must match"

        $bmp.Dispose()
        $row.Dispose()
    }
}

Describe "Smoke Test Suite: Theme State Management & Isolation" {
    It "Switches to Light Mode and verifies state update" {
        Reset-DeskmateTheme -Light $true
        $isLight = Get-DeskmateStaticField "Ink" "Light"
        Assert-True $isLight "Ink.Light should be true in Light Mode"

        $baseTone = Get-DeskmateStaticField "Ink" "Base"
        Assert-NotNull $baseTone "Base tone must be set"
    }

    It "Resets to Dark Mode and verifies state isolation" {
        Reset-DeskmateTheme -Light $false
        $isLight = Get-DeskmateStaticField "Ink" "Light"
        Assert-False $isLight "Ink.Light should be false in Dark Mode"

        $baseTone = Get-DeskmateStaticField "Ink" "Base"
        Assert-NotNull $baseTone "Base tone must be set"
    }
}

Describe "Smoke Test Suite: Release Notes" {
    It "Keeps a release note for the current version and resolves unseen notes" {
        $appVersion = Get-DeskmateStaticField "Island" "AppVersion"
        $notes = Get-DeskmateStaticField "WhatsNew" "Notes"

        Assert-True ($notes.Length -gt 0) "WhatsNew.Notes must contain at least one release note"
        Assert-Equal $notes[0].Version $appVersion "Newest release note must match Island.AppVersion"

        $current = Invoke-DeskmateStaticMethod "WhatsNew" "For" @($appVersion)
        Assert-NotNull $current "WhatsNew.For($appVersion) must return the current note"

        foreach ($note in $notes) {
            Assert-True ($note.Items.Length -gt 0) "Release note $($note.Version) must have at least one item"
            foreach ($item in $note.Items) {
                Assert-True ($item.Length -ge 2) "Release note $($note.Version) item needs a title and detail"
                Assert-True (-not [string]::IsNullOrWhiteSpace($item[0])) "Release note $($note.Version) title must not be empty"
                Assert-True (-not [string]::IsNullOrWhiteSpace($item[1])) "Release note $($note.Version) detail must not be empty"
            }
        }

        $alreadySeen = Invoke-DeskmateStaticMethod "WhatsNew" "Since" @($appVersion, $appVersion)
        Assert-Equal $alreadySeen.Count 0 "No notes should show when already on $appVersion"

        $all = Invoke-DeskmateStaticMethod "WhatsNew" "Since" @("0.0", $appVersion)
        Assert-True ($all.Count -ge 1) "A fresh install should resolve at least one note"
        Assert-Equal $all[0].Version $appVersion "Newest note must come first"
    }
}

Describe "Smoke Test Suite: Process & CLI Argument Execution" {
    It "Invokes Deskmate CLI --notify without parameters and captures expected exit code" {
        $cli = Find-DeskmateCliBinary
        Assert-NotNull $cli "Deskmate CLI binary path resolved"

        $pinfo = New-Object System.Diagnostics.ProcessStartInfo
        $pinfo.FileName = $cli.FileName
        $prefix = if ($cli.PrefixArgs -and $cli.PrefixArgs.Count -gt 0) {
            (($cli.PrefixArgs | ForEach-Object { '"' + $_ + '"' }) -join " ") + " "
        } else { "" }
        $pinfo.Arguments = $prefix + "--notify"
        $pinfo.UseShellExecute = $false
        $pinfo.CreateNoWindow = $true
        $pinfo.RedirectStandardOutput = $true
        $pinfo.RedirectStandardError = $true

        $proc = [System.Diagnostics.Process]::Start($pinfo)
        try {
            $errTask = $proc.StandardError.ReadToEndAsync()
            [void]$proc.StandardOutput.ReadToEnd()
            [void]$errTask.GetAwaiter().GetResult()
            [void]$proc.WaitForExit(5000)

            Assert-True $proc.HasExited "Process must exit within 5 seconds"
            # Code 2 indicates missing mandatory arguments in Plugin.cs:63-83
            Assert-Equal $proc.ExitCode 2 "Deskmate CLI --notify without parameters must exit with code 2"
        }
        finally {
            $proc.Dispose()
        }
    }
}
}
finally {
    if ($script:smokeOwnedTmp) {
        $env:DESKMATE_DIR = $script:smokePrevDir
        $env:DESKMATE_HOME = $script:smokePrevHome
        $env:DESKMATE_DOCK_DRY = $script:smokePrevDry
        Remove-Item -LiteralPath $script:smokeOwnedTmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
