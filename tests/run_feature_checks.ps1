# run_feature_checks.ps1
# Compiles and runs passive feature checks that need an isolated STA process.
# Usage: .\tests\run_feature_checks.ps1 [-Baseline]

[CmdletBinding()]
param(
    [switch]$Baseline
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $projectRoot "Deskmate.csproj"
$featureProj = Join-Path $PSScriptRoot "FeatureChecks.csproj"
$buildCmd = Join-Path $projectRoot "build.cmd"
$powershell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
if (-not (Test-Path -LiteralPath $powershell)) { $powershell = "powershell.exe" }

$checks = @(
    @{ Name = "Task note images"; Source = Join-Path $PSScriptRoot "task_note_images_check.cs"; Type = "TaskNoteImagesCheck" },
    @{ Name = "Notification emoji"; Source = Join-Path $PSScriptRoot "notification_emoji_check.cs"; Type = "NotificationEmojiCheck" },
    @{ Name = "Pet link multiplayer"; Source = Join-Path $PSScriptRoot "pet_link_check.cs"; Type = "PetLinkCheck" },
    @{ Name = "Ingatan memory"; Source = Join-Path $PSScriptRoot "ingat_check.cs"; Type = "IngatCheck" }
)

$temporary = New-Object System.Collections.Generic.List[string]
$previousDir = $env:DESKMATE_DIR
$previousHome = $env:DESKMATE_HOME
$previousDry = $env:DESKMATE_DOCK_DRY
$failure = $null

function Invoke-Captured([string]$file, [string]$arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $file
    $psi.Arguments = $arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($psi)
    try {
        $errTask = $process.StandardError.ReadToEndAsync()
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $errTask.GetAwaiter().GetResult()
        $process.WaitForExit()
        return @{ Output = ($stdout + $stderr).Trim(); ExitCode = $process.ExitCode }
    }
    finally {
        $process.Dispose()
    }
}

try {
    $useBaseline = $Baseline -or (-not (Test-Path -LiteralPath $csproj -PathType Leaf))

    if ($useBaseline) {
        if (-not (Test-Path -LiteralPath $buildCmd)) { throw "build.cmd not found: $buildCmd" }

        foreach ($check in $checks) {
            if (-not (Test-Path -LiteralPath $check.Source)) { throw "Check source not found: $($check.Source)" }
            $stamp = [Guid]::NewGuid().ToString("N")
            $exe = Join-Path $env:TEMP ("Deskmate-feature-$stamp.exe")
            $data = Join-Path $env:TEMP ("Sentinel\feature-$stamp")
            $homeDir = Join-Path $data "home"
            $runner = Join-Path $env:TEMP ("Deskmate-feature-$stamp.ps1")
            $temporary.Add($exe)
            $temporary.Add($data)
            $temporary.Add($runner)
            New-Item -ItemType Directory -Path $data, $homeDir -Force | Out-Null

            Write-Host "[BUILD] $($check.Name)" -ForegroundColor Cyan
            $buildArgs = '/c ""{0}" "{1}" "{2}""' -f $buildCmd, $exe, $check.Source
            $build = Invoke-Captured "cmd.exe" $buildArgs
            if ($build.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $exe)) {
                throw "Build failed for $($check.Name)`n$($build.Output)"
            }
            if ($build.Output -match 'warning CS|error CS') {
                throw "Compiler diagnostics for $($check.Name)`n$($build.Output)"
            }

            @'
param([string]$AssemblyPath, [string]$TypeName)
$ErrorActionPreference = "Stop"
try {
    $assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $type = $assembly.GetType("Deskmate." + $TypeName, $true)
    $method = $type.GetMethod("Run", [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
    if ($null -eq $method) { throw "Run method not found on $TypeName" }
    $result = $method.Invoke($null, @())
    if ($null -ne $result) { Write-Output ([string]$result) }
    exit 0
} catch {
    Write-Error $_
    exit 1
}
'@ | Set-Content -LiteralPath $runner -Encoding UTF8

            Write-Host "[CHECK] $($check.Name)" -ForegroundColor Cyan
            $env:DESKMATE_DIR = $data
            $env:DESKMATE_HOME = $homeDir
            $env:DESKMATE_DOCK_DRY = "1"
            $runArgs = '-NoProfile -STA -ExecutionPolicy Bypass -File "{0}" "{1}" "{2}"' -f $runner, $exe, $check.Type
            $run = Invoke-Captured $powershell $runArgs
            if ($run.ExitCode -ne 0) { throw "$($check.Name) failed`n$($run.Output)" }
            Write-Host "[PASS] $($run.Output)" -ForegroundColor Green
        }
    }
    else {
        if (-not (Test-Path -LiteralPath $featureProj -PathType Leaf)) { throw "FeatureChecks.csproj not found: $featureProj" }
        foreach ($check in $checks) {
            if (-not (Test-Path -LiteralPath $check.Source)) { throw "Check source not found: $($check.Source)" }
        }

        $buildStamp = [Guid]::NewGuid().ToString("N")
        $buildTemp = Join-Path $env:TEMP ("Sentinel\feature-build-$buildStamp")
        $objDir = ($buildTemp + "\obj").Replace("\", "/") + "/"
        $binDir = ($buildTemp + "\bin").Replace("\", "/") + "/"
        $temporary.Add($buildTemp)
        New-Item -ItemType Directory -Path (Join-Path $buildTemp "obj"), (Join-Path $buildTemp "bin") -Force | Out-Null

        Write-Host "[BUILD] FeatureChecks.csproj (.NET 10)" -ForegroundColor Cyan
        $dotnetArgs = ('build "{0}" -c Release --nologo -p:BaseIntermediateOutputPath="{1}" -p:MSBuildProjectExtensionsPath="{1}" -p:OutputPath="{2}"' -f $featureProj, $objDir, $binDir)
        $build = Invoke-Captured "dotnet" $dotnetArgs
        if ($build.ExitCode -ne 0) {
            throw "Build failed for FeatureChecks.csproj`n$($build.Output)"
        }
        if ($build.Output -match 'error CS') {
            throw "Compiler errors for FeatureChecks.csproj`n$($build.Output)"
        }
        $testWarnings = [regex]::Matches($build.Output, '(?m)^.*\\tests\\[^(\r\n]+\(\d+,\d+\):\s*warning\s+[^\r\n]+')
        if ($testWarnings.Count -gt 0) {
            throw "Compiler warnings in tests/ for FeatureChecks.csproj`n$(($testWarnings | ForEach-Object { $_.Value }) -join "`n")"
        }

        $featureExe = Join-Path $buildTemp "bin\FeatureChecks.exe"
        if (-not (Test-Path -LiteralPath $featureExe -PathType Leaf)) {
            $found = Get-ChildItem -Path (Join-Path $buildTemp "bin") -Filter "FeatureChecks.exe" -Recurse | Select-Object -First 1
            if ($found) { $featureExe = $found.FullName }
        }
        if (-not (Test-Path -LiteralPath $featureExe -PathType Leaf)) {
            throw "FeatureChecks.exe not found under $buildTemp\bin`n$($build.Output)"
        }

        foreach ($check in $checks) {
            $stamp = [Guid]::NewGuid().ToString("N")
            $data = Join-Path $env:TEMP ("Sentinel\feature-$stamp")
            $homeDir = Join-Path $data "home"
            $temporary.Add($data)
            New-Item -ItemType Directory -Path $data, $homeDir -Force | Out-Null

            Write-Host "[CHECK] $($check.Name)" -ForegroundColor Cyan
            $env:DESKMATE_DIR = $data
            $env:DESKMATE_HOME = $homeDir
            $env:DESKMATE_DOCK_DRY = "1"
            $run = Invoke-Captured $featureExe $check.Type
            if ($run.ExitCode -ne 0) { throw "$($check.Name) failed`n$($run.Output)" }
            Write-Host "[PASS] $($run.Output)" -ForegroundColor Green
        }
    }
} catch {
    $failure = $_
} finally {
    $env:DESKMATE_DIR = $previousDir
    $env:DESKMATE_HOME = $previousHome
    $env:DESKMATE_DOCK_DRY = $previousDry
    foreach ($path in $temporary) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
    foreach ($leftover in @((Join-Path $PSScriptRoot "bin"), (Join-Path $PSScriptRoot "obj"))) {
        if (Test-Path -LiteralPath $leftover) {
            Remove-Item -LiteralPath $leftover -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($null -ne $failure) {
    Write-Host "[FAIL] $($failure.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] All feature checks passed." -ForegroundColor Green
