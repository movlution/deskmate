# run_feature_checks.ps1
# Compiles and runs passive feature checks that need an isolated STA process.
# Usage: .\tests\run_feature_checks.ps1

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildCmd = Join-Path $projectRoot "build.cmd"
$powershell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
if (-not (Test-Path -LiteralPath $powershell)) { $powershell = "powershell.exe" }

$checks = @(
    @{ Name = "Task note images"; Source = Join-Path $PSScriptRoot "task_note_images_check.cs"; Type = "TaskNoteImagesCheck" },
    @{ Name = "Notification emoji"; Source = Join-Path $PSScriptRoot "notification_emoji_check.cs"; Type = "NotificationEmojiCheck" },
    @{ Name = "Pet link multiplayer"; Source = Join-Path $PSScriptRoot "pet_link_check.cs"; Type = "PetLinkCheck" },
    @{ Name = "Ingatan memory"; Source = Join-Path $PSScriptRoot "ingat_check.cs"; Type = "IngatCheck" },
    @{ Name = "Load guard"; Source = Join-Path $PSScriptRoot "guard_check.cs"; Type = "GuardCheck" },
    @{ Name = "Dock window card motion"; Source = Join-Path $PSScriptRoot "dock_card_motion_check.cs"; Type = "DockCardMotionCheck" }
)

$temporary = New-Object System.Collections.Generic.List[string]
$previousDir = $env:DESKMATE_DIR
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
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    return @{ Output = ($stdout + $stderr).Trim(); ExitCode = $process.ExitCode }
}

try {
    if (-not (Test-Path -LiteralPath $buildCmd)) { throw "build.cmd not found: $buildCmd" }

    foreach ($check in $checks) {
        if (-not (Test-Path -LiteralPath $check.Source)) { throw "Check source not found: $($check.Source)" }
        $stamp = [Guid]::NewGuid().ToString("N")
        $exe = Join-Path $env:TEMP ("Deskmate-feature-$stamp.exe")
        $data = Join-Path $env:TEMP ("Sentinel\feature-$stamp")
        $runner = Join-Path $env:TEMP ("Deskmate-feature-$stamp.ps1")
        $temporary.Add($exe)
        $temporary.Add($data)
        $temporary.Add($runner)
        New-Item -ItemType Directory -Path $data -Force | Out-Null

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
        $runArgs = '-NoProfile -STA -ExecutionPolicy Bypass -File "{0}" "{1}" "{2}"' -f $runner, $exe, $check.Type
        $run = Invoke-Captured $powershell $runArgs
        if ($run.ExitCode -ne 0) { throw "$($check.Name) failed`n$($run.Output)" }
        Write-Host "[PASS] $($run.Output)" -ForegroundColor Green
    }
} catch {
    $failure = $_
} finally {
    $env:DESKMATE_DIR = $previousDir
    foreach ($path in $temporary) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($null -ne $failure) {
    Write-Host "[FAIL] $($failure.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] All feature checks passed." -ForegroundColor Green
