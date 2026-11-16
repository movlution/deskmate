# DeskmateLoader.ps1
# Assembly Reflection, Architecture Validation, and Offscreen Control Helpers
# Zero external dependencies.

# Ensure System assemblies are loaded
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# 1. Architecture Validation
function Assert-DeskmateArchitecture {
    if ([IntPtr]::Size -ne 8) {
        throw "Deskmate E2E Test Runner requires 64-bit PowerShell ([IntPtr]::Size == 8). You are currently running in a 32-bit (x86) process. Please launch 64-bit PowerShell (e.g. C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe)."
    }
}

# Run architecture assertion immediately upon dot-sourcing
Assert-DeskmateArchitecture

# 2. Assembly Loading & Caching
if ($null -eq $global:DeskmateAssembly) {
    $global:DeskmateAssembly = $null
    $global:DeskmateAssemblyPath = ""
}

function Find-DeskmateBinary {
    param([string]$ExplicitPath = "")

    $requested = if ($ExplicitPath) { $ExplicitPath } else { $env:DESKMATE_TEST_BINARY }
    if ($requested) {
        if (-not (Test-Path -LiteralPath $requested -PathType Leaf)) {
            throw "Test binary not found: $requested"
        }
        $resolved = (Resolve-Path -LiteralPath $requested).Path
        if ([IO.Path]::GetExtension($resolved) -ine ".exe") {
            throw "Test binary must be an .exe file: $resolved"
        }
        return $resolved
    }

    $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $candidateLocations = @(
        (Join-Path $projectRoot "Deskmate.exe"),
        (Join-Path (Get-Location) "Deskmate.exe")
    )

    foreach ($loc in $candidateLocations) {
        if (Test-Path -LiteralPath $loc -PathType Leaf) {
            return (Resolve-Path -LiteralPath $loc).Path
        }
    }

    return $null
}

function Initialize-DeskmateLoader {
    param(
        [string]$Path = "",
        [switch]$ForceReload
    )

    $binPath = Find-DeskmateBinary $Path
    if (-not $binPath) {
        throw "Deskmate.exe not found. Please run 'build.cmd' at the project root to compile Deskmate.exe before running tests."
    }

    if ($global:DeskmateAssembly -and (-not $ForceReload)) {
        if ([IO.Path]::GetFullPath($global:DeskmateAssemblyPath) -ne [IO.Path]::GetFullPath($binPath)) {
            throw "A different Deskmate assembly is already loaded. Run each binary in a fresh PowerShell process."
        }
        return $global:DeskmateAssembly
    }

    # Load assembly bytes or direct assembly via LoadFrom
    $global:DeskmateAssemblyPath = $binPath
    $global:DeskmateAssembly = [System.Reflection.Assembly]::LoadFrom($binPath)
    return $global:DeskmateAssembly
}

# Auto-initialize loader if Deskmate.exe exists
try {
    [void](Initialize-DeskmateLoader)
} catch {
    # If binary doesn't exist yet, defer until explicit initialization or rebuild
}

# 3. Reflection Helpers

function Get-DeskmateType {
    param([Parameter(Mandatory = $true)][string]$TypeName)

    if (-not $global:DeskmateAssembly) {
        [void](Initialize-DeskmateLoader)
    }

    # Check exact name
    $t = $global:DeskmateAssembly.GetType($TypeName)
    if ($t) { return $t }

    # Check prefixed with Deskmate.
    $t = $global:DeskmateAssembly.GetType("Deskmate.$TypeName")
    if ($t) { return $t }

    # Check case-insensitive / partial match among all loaded types
    $all = $global:DeskmateAssembly.GetTypes()
    foreach ($candidate in $all) {
        if ($candidate.Name -ieq $TypeName -or $candidate.FullName -ieq $TypeName -or $candidate.FullName -ieq "Deskmate.$TypeName") {
            return $candidate
        }
    }

    # Also search nested types (e.g. Island+GlassSheet)
    foreach ($candidate in $all) {
        if ($candidate.FullName -like "*+$TypeName") {
            return $candidate
        }
    }

    return $null
}

function Invoke-DeskmateStaticMethod {
    param(
        [Parameter(Mandatory = $true)][string]$TypeName,
        [Parameter(Mandatory = $true)][string]$MethodName,
        [object[]]$Parameters = @()
    )

    $t = Get-DeskmateType $TypeName
    if (-not $t) {
        throw "Invoke-DeskmateStaticMethod: Type [$TypeName] not found in Deskmate assembly."
    }

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
    
    # Try direct method resolution
    $m = $t.GetMethod($MethodName, $flags)
    if (-not $m) {
        # Try matching among all methods with same name and parameter count
        $methods = $t.GetMethods($flags) | Where-Object { $_.Name -eq $MethodName }
        if ($methods) {
            foreach ($candidate in $methods) {
                if ($candidate.GetParameters().Length -eq $Parameters.Length) {
                    $m = $candidate
                    break
                }
            }
        }
    }

    if (-not $m) {
        throw "Invoke-DeskmateStaticMethod: Method [$MethodName] with $($Parameters.Length) parameters not found on type [$($t.FullName)]."
    }

    return $m.Invoke($null, $Parameters)
}

function Get-DeskmateStaticField {
    param(
        [Parameter(Mandatory = $true)][string]$TypeName,
        [Parameter(Mandatory = $true)][string]$FieldName
    )

    $t = Get-DeskmateType $TypeName
    if (-not $t) {
        throw "Get-DeskmateStaticField: Type [$TypeName] not found in Deskmate assembly."
    }

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
    $f = $t.GetField($FieldName, $flags)
    if (-not $f) {
        throw "Get-DeskmateStaticField: Field [$FieldName] not found on type [$($t.FullName)]."
    }

    return $f.GetValue($null)
}

function Set-DeskmateStaticField {
    param(
        [Parameter(Mandatory = $true)][string]$TypeName,
        [Parameter(Mandatory = $true)][string]$FieldName,
        $Value
    )

    $t = Get-DeskmateType $TypeName
    if (-not $t) {
        throw "Set-DeskmateStaticField: Type [$TypeName] not found in Deskmate assembly."
    }

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
    $f = $t.GetField($FieldName, $flags)
    if (-not $f) {
        throw "Set-DeskmateStaticField: Field [$FieldName] not found on type [$($t.FullName)]."
    }

    $f.SetValue($null, $Value)
}

function Get-DeskmateStaticProperty {
    param(
        [Parameter(Mandatory = $true)][string]$TypeName,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $t = Get-DeskmateType $TypeName
    if (-not $t) {
        throw "Get-DeskmateStaticProperty: Type [$TypeName] not found in Deskmate assembly."
    }

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
    $p = $t.GetProperty($PropertyName, $flags)
    if (-not $p) {
        throw "Get-DeskmateStaticProperty: Property [$PropertyName] not found on type [$($t.FullName)]."
    }

    return $p.GetValue($null, $null)
}

# 4. WCAG Reference Calculator via Deskmate.Island.Contrast
function Get-DeskmateContrast {
    param(
        [Parameter(Mandatory = $true)]$ColorA,
        [Parameter(Mandatory = $true)]$ColorB
    )

    $toColor = {
        param($c)
        if ($c -is [System.Drawing.Color]) { return $c }
        if ($c -is [string]) {
            if ($c.StartsWith("#")) { return [System.Drawing.ColorTranslator]::FromHtml($c) }
            return [System.Drawing.Color]::FromName($c)
        }
        return $c
    }

    $ca = & $toColor $ColorA
    $cb = & $toColor $ColorB

    $res = Invoke-DeskmateStaticMethod "Island" "Contrast" @($ca, $cb)
    return [Math]::Round([double]$res, 2)
}

# 5. Deterministic State Reset
function Reset-DeskmateTheme {
    param([bool]$Light = $false)

    try {
        $inkType = Get-DeskmateType "Ink"
        if ($inkType) {
            Invoke-DeskmateStaticMethod "Ink" "Init" @()
            Invoke-DeskmateStaticMethod "Ink" "SetPack" @($null)
            Invoke-DeskmateStaticMethod "Ink" "SetAccent" @([string]$null)
            Set-DeskmateStaticField "Ink" "VeilTint" $null
            Invoke-DeskmateStaticMethod "Ink" "Apply" @($Light)
        }
    }
    catch {
        # Ignore if types are not loaded or methods have varied signatures across milestones
    }
}

# 6. Headless Offscreen Rendering Helper
function Render-DeskmateControlToBitmap {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Forms.Control]$Control,
        [int]$Width = 120,
        [int]$Height = 36
    )

    $Control.Size = New-Object System.Drawing.Size($Width, $Height)
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $rect = New-Object System.Drawing.Rectangle 0, 0, $Width, $Height
    $Control.DrawToBitmap($bmp, $rect)
    return $bmp
}

function New-DeskmateControl {
    param(
        [Parameter(Mandatory = $true)][string]$TypeName,
        [hashtable]$Properties = @{}
    )

    $t = Get-DeskmateType $TypeName
    if (-not $t) {
        throw "New-DeskmateControl: Control type [$TypeName] not found in Deskmate assembly."
    }

    $instance = [Activator]::CreateInstance($t)
    foreach ($entry in $Properties.GetEnumerator()) {
        $prop = $t.GetProperty($entry.Key, [System.Reflection.BindingFlags]'Public,NonPublic,Instance')
        if ($prop -and $prop.CanWrite) {
            $prop.SetValue($instance, $entry.Value, $null)
        }
    }

    return $instance
}
