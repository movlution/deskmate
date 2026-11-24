# DeskmateLoader.ps1
# Assembly Reflection, Architecture Validation, and Offscreen Control Helpers
# Zero external dependencies.

# Ensure System assemblies are loaded
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# 1. Architecture Validation
function Assert-DeskmateArchitecture {
    if ([IntPtr]::Size -ne 8) {
        throw "Deskmate E2E Test Runner requires 64-bit PowerShell ([IntPtr]::Size == 8). You are currently running in a 32-bit (x86) process. Please launch 64-bit pwsh.exe (or Windows PowerShell 5.1 for NetFx builds)."
    }
}

# Run architecture assertion immediately upon dot-sourcing
Assert-DeskmateArchitecture

# 2. Assembly Loading & Caching
if ($null -eq $global:DeskmateAssembly) {
    $global:DeskmateAssembly = $null
    $global:DeskmateAssemblyPath = ""
    $global:DeskmateCliPath = ""
}

if ($PSVersionTable.PSEdition -eq 'Core' -and -not ('DeskmateAlcResolver' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

public static class DeskmateAlcResolver
{
    private static readonly ConcurrentDictionary<string, AssemblyDependencyResolver> Resolvers =
        new ConcurrentDictionary<string, AssemblyDependencyResolver>(StringComparer.OrdinalIgnoreCase);
    private static int Hooked;

    public static void Register(string assemblyPath)
    {
        string fullPath = Path.GetFullPath(assemblyPath);
        Resolvers.TryAdd(fullPath, new AssemblyDependencyResolver(fullPath));
        if (System.Threading.Interlocked.CompareExchange(ref Hooked, 1, 0) == 0)
        {
            AssemblyLoadContext.Default.Resolving += OnResolving;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
        }
    }

    private static Assembly OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (var resolver in Resolvers.Values)
        {
            string path = resolver.ResolveAssemblyToPath(name);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return context.LoadFromAssemblyPath(path);
            }
        }
        return null;
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string unmanagedDllName)
    {
        foreach (var resolver in Resolvers.Values)
        {
            string path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                IntPtr handle;
                if (NativeLibrary.TryLoad(path, out handle))
                {
                    return handle;
                }
            }
        }
        return IntPtr.Zero;
    }
}
'@
}

function Get-DeskmateBinaryInfo {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Test binary not found: $Path"
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $ext = [IO.Path]::GetExtension($resolved)
    $hasMetadata = $false
    try {
        [void][System.Reflection.AssemblyName]::GetAssemblyName($resolved)
        $hasMetadata = $true
    }
    catch [System.BadImageFormatException] {
        $hasMetadata = $false
    }

    if (-not $hasMetadata) {
        $sibDll = [IO.Path]::ChangeExtension($resolved, ".dll")
        if ((Test-Path -LiteralPath $sibDll -PathType Leaf)) {
            $dllManaged = $false
            try {
                [void][System.Reflection.AssemblyName]::GetAssemblyName($sibDll)
                $dllManaged = $true
            }
            catch [System.BadImageFormatException] {
                $dllManaged = $false
            }
            if ($dllManaged) {
                return [PSCustomObject]@{
                    Kind         = 'NetCoreAppHost'
                    Path         = $resolved
                    AssemblyPath = (Resolve-Path -LiteralPath $sibDll).Path
                    CliPath      = $resolved
                }
            }
        }
        return [PSCustomObject]@{
            Kind         = 'SingleFileAppHost'
            Path         = $resolved
            AssemblyPath = $null
            CliPath      = $resolved
        }
    }

    $rtConfig = [IO.Path]::ChangeExtension($resolved, ".runtimeconfig.json")
    if ($ext -ieq ".dll" -or (Test-Path -LiteralPath $rtConfig -PathType Leaf)) {
        $sibExe = [IO.Path]::ChangeExtension($resolved, ".exe")
        $cliExe = if (Test-Path -LiteralPath $sibExe -PathType Leaf) { (Resolve-Path -LiteralPath $sibExe).Path } else { $null }
        return [PSCustomObject]@{
            Kind         = 'NetCoreDll'
            Path         = $resolved
            AssemblyPath = $resolved
            CliPath      = $cliExe
        }
    }

    return [PSCustomObject]@{
        Kind         = 'NetFxExe'
        Path         = $resolved
        AssemblyPath = $resolved
        CliPath      = $resolved
    }
}

function Find-DeskmateBinary {
    param([string]$ExplicitPath = "")

    $isCore = ($PSVersionTable.PSEdition -eq 'Core')
    $requested = if ($ExplicitPath) { $ExplicitPath } else { $env:DESKMATE_TEST_BINARY }
    if ($requested) {
        if (-not (Test-Path -LiteralPath $requested -PathType Leaf)) {
            throw "Test binary not found: $requested"
        }
        $resolved = (Resolve-Path -LiteralPath $requested).Path
        $ext = [IO.Path]::GetExtension($resolved)
        if ($ext -ine ".exe" -and $ext -ine ".dll") {
            throw "Test binary must be an .exe or .dll file: $resolved"
        }

        $info = Get-DeskmateBinaryInfo -Path $resolved
        switch ($info.Kind) {
            'SingleFileAppHost' {
                throw "Single-file executable '$resolved' has no IL manifest and cannot be loaded for reflection. Pass an unpacked Deskmate.dll (e.g. from dotnet build) for reflection tests, or use Find-DeskmateCliBinary for CLI execution."
            }
            'NetCoreAppHost' {
                if (-not $isCore) {
                    throw "Target '$resolved' is a .NET Core/.NET 10 binary and requires pwsh (PowerShell 7+)."
                }
                if (-not $global:DeskmateCliPath) { $global:DeskmateCliPath = $info.CliPath }
                return $info.AssemblyPath
            }
            'NetCoreDll' {
                if (-not $isCore) {
                    throw "Target '$resolved' is a .NET Core/.NET 10 assembly and requires pwsh (PowerShell 7+)."
                }
                if ($info.CliPath -and -not $global:DeskmateCliPath) { $global:DeskmateCliPath = $info.CliPath }
                return $info.AssemblyPath
            }
            'NetFxExe' {
                if ($isCore) {
                    throw "Target '$resolved' is a .NET Framework 4.8 assembly and cannot be safely reflected under pwsh (Core). Run under Windows PowerShell 5.1 (powershell.exe) or pass a .NET 10 Deskmate.dll."
                }
                if (-not $global:DeskmateCliPath) { $global:DeskmateCliPath = $info.CliPath }
                return $info.AssemblyPath
            }
        }
    }

    $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    if ($isCore) {
        $candidateDlls = @(
            (Join-Path $projectRoot "bin\x64\Release\net10.0-windows10.0.19041.0\Deskmate.dll"),
            (Join-Path $projectRoot "bin\Release\net10.0-windows10.0.19041.0\Deskmate.dll"),
            (Join-Path $projectRoot "bin\x64\Debug\net10.0-windows10.0.19041.0\Deskmate.dll"),
            (Join-Path $projectRoot "bin\Debug\net10.0-windows10.0.19041.0\Deskmate.dll"),
            (Join-Path $projectRoot "artifacts\test\Deskmate.dll"),
            (Join-Path (Get-Location) "Deskmate.dll")
        )
        foreach ($loc in $candidateDlls) {
            if (Test-Path -LiteralPath $loc -PathType Leaf) {
                $info = Get-DeskmateBinaryInfo -Path $loc
                if ($info.Kind -eq 'NetCoreDll') {
                    if ($info.CliPath -and -not $global:DeskmateCliPath) { $global:DeskmateCliPath = $info.CliPath }
                    return $info.AssemblyPath
                }
            }
        }

        $rootExe = Join-Path $projectRoot "Deskmate.exe"
        if (Test-Path -LiteralPath $rootExe -PathType Leaf) {
            $exeInfo = Get-DeskmateBinaryInfo -Path $rootExe
            if ($exeInfo.Kind -eq 'NetCoreAppHost') {
                if (-not $global:DeskmateCliPath) { $global:DeskmateCliPath = $exeInfo.CliPath }
                return $exeInfo.AssemblyPath
            }
            if ($exeInfo.Kind -eq 'SingleFileAppHost') {
                throw "Found single-file '$rootExe' which cannot be loaded for reflection, and no unpacked Deskmate.dll was found in bin/ or artifacts/test/. Build unpacked Deskmate.dll or pass -BinaryPath <path-to-Deskmate.dll>."
            }
            if ($exeInfo.Kind -eq 'NetFxExe') {
                throw "Found .NET Framework 4.8 '$rootExe' while running under pwsh (Core). Build .NET 10 Deskmate.dll or run under Windows PowerShell 5.1."
            }
        }
        return $null
    }

    $candidateLocations = @(
        (Join-Path $projectRoot "Deskmate.exe"),
        (Join-Path (Get-Location) "Deskmate.exe")
    )

    foreach ($loc in $candidateLocations) {
        if (Test-Path -LiteralPath $loc -PathType Leaf) {
            $info = Get-DeskmateBinaryInfo -Path $loc
            if ($info.Kind -ne 'NetFxExe') {
                throw "Target '$($info.Path)' is a .NET Core/.NET 10 binary and requires pwsh (PowerShell 7+)."
            }
            if (-not $global:DeskmateCliPath) { $global:DeskmateCliPath = $info.CliPath }
            return $info.AssemblyPath
        }
    }

    return $null
}

function Find-DeskmateCliBinary {
    param([string]$ExplicitPath = "")

    $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($ExplicitPath) { $candidates.Add($ExplicitPath) }
    if ($env:DESKMATE_CLI_BINARY) { $candidates.Add($env:DESKMATE_CLI_BINARY) }
    if ($global:DeskmateCliPath) { $candidates.Add($global:DeskmateCliPath) }
    if ($global:DeskmateAssemblyPath) {
        $sibExe = [IO.Path]::ChangeExtension($global:DeskmateAssemblyPath, ".exe")
        $candidates.Add($sibExe)
    }
    $candidates.Add((Join-Path $projectRoot "Deskmate.exe"))
    $candidates.Add((Join-Path $projectRoot "artifacts\publish\Deskmate.exe"))
    $candidates.Add((Join-Path $projectRoot "bin\x64\Release\net10.0-windows10.0.19041.0\Deskmate.exe"))
    $candidates.Add((Join-Path $projectRoot "bin\Release\net10.0-windows10.0.19041.0\Deskmate.exe"))
    $candidates.Add((Join-Path (Get-Location) "Deskmate.exe"))

    foreach ($cand in $candidates) {
        if ($cand -and (Test-Path -LiteralPath $cand -PathType Leaf)) {
            $resolved = (Resolve-Path -LiteralPath $cand).Path
            $ext = [IO.Path]::GetExtension($resolved)
            if ($ext -ieq ".exe") {
                return @{ FileName = $resolved; PrefixArgs = @() }
            }
            if ($ext -ieq ".dll") {
                $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
                $dotnetPath = if ($dotnetCmd) { $dotnetCmd.Source } else { "dotnet" }
                return @{ FileName = $dotnetPath; PrefixArgs = @($resolved) }
            }
        }
    }

    $dllPath = $global:DeskmateAssemblyPath
    if (-not $dllPath) {
        try { $dllPath = Find-DeskmateBinary } catch { $dllPath = $null }
    }
    if ($dllPath -and (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
        if ([IO.Path]::GetExtension($dllPath) -ieq ".exe") {
            return @{ FileName = $dllPath; PrefixArgs = @() }
        }
        $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
        $dotnetPath = if ($dotnetCmd) { $dotnetCmd.Source } else { "dotnet" }
        return @{ FileName = $dotnetPath; PrefixArgs = @($dllPath) }
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
        throw "Deskmate reflection binary not found. Please build Deskmate (dotnet build Deskmate.csproj -c Release, or build.cmd for NetFx baseline) before running tests."
    }

    $info = Get-DeskmateBinaryInfo -Path $binPath
    $isCore = ($PSVersionTable.PSEdition -eq 'Core')
    if ($info.Kind -eq 'SingleFileAppHost') {
        throw "Single-file executable '$binPath' has no IL manifest and cannot be loaded for reflection."
    }
    if ($info.Kind -eq 'NetCoreDll' -and -not $isCore) {
        throw "Target '$binPath' is a .NET Core/.NET 10 assembly and requires pwsh (PowerShell 7+)."
    }
    if ($info.Kind -eq 'NetFxExe' -and $isCore) {
        throw "Target '$binPath' is a .NET Framework 4.8 assembly and cannot be safely reflected under pwsh (Core). Run under Windows PowerShell 5.1."
    }

    if ($global:DeskmateAssembly -and (-not $ForceReload)) {
        if ([IO.Path]::GetFullPath($global:DeskmateAssemblyPath) -ne [IO.Path]::GetFullPath($binPath)) {
            throw "A different Deskmate assembly is already loaded. Run each binary in a fresh PowerShell process."
        }
        return $global:DeskmateAssembly
    }

    if ($isCore) {
        [DeskmateAlcResolver]::Register($binPath)
    }

    # Load assembly via LoadFrom after registering AssemblyDependencyResolver
    $global:DeskmateAssemblyPath = $binPath
    $global:DeskmateAssembly = [System.Reflection.Assembly]::LoadFrom($binPath)
    return $global:DeskmateAssembly
}

# Auto-initialize loader if a compatible Deskmate reflection binary exists.
# Never swallow compatibility, single-file, or load failures.
$autoBinPath = $null
$missingBinary = $false
try {
    $autoBinPath = Find-DeskmateBinary
}
catch {
    if ($_.Exception.Message -like "Test binary not found:*") {
        $missingBinary = $true
    }
    else {
        throw
    }
}
if ($autoBinPath -and -not $missingBinary) {
    [void](Initialize-DeskmateLoader -Path $autoBinPath)
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
    try {
        $all = $global:DeskmateAssembly.GetTypes()
    }
    catch [System.Reflection.ReflectionTypeLoadException] {
        $all = $_.Exception.Types | Where-Object { $null -ne $_ }
    }
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
