# run_e2e.ps1
# Master End-to-End Test Runner CLI for Deskmate
# Usage:
#   .\tests\run_e2e.ps1 [-Tier <1-4>] [-Feature <1-35>] [-Rebuild] [-BinaryPath <path>] [-TestFile <path>] [-VerboseOutput]

[CmdletBinding()]
param(
    [ValidateRange(1, 4)]
    [int]$Tier,

    [ValidateRange(1, 35)]
    [int]$Feature,

    [switch]$Rebuild,

    [string]$BinaryPath,

    [string]$TestFile,

    [switch]$VerboseOutput
)

# 1. Architecture Check
if ([IntPtr]::Size -ne 8) {
    Write-Host "[ERROR] 64-bit PowerShell is required. Current process architecture is 32-bit (x86)." -ForegroundColor Red
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

# 2. Rebuild step if requested
if ($Rebuild) {
    Write-Host "[BUILD] Executing build.cmd to compile fresh Deskmate.exe..." -ForegroundColor Cyan
    $buildCmd = Join-Path $projectRoot "build.cmd"
    if (-not (Test-Path $buildCmd)) {
        Write-Host "[ERROR] build.cmd not found at $buildCmd" -ForegroundColor Red
        exit 1
    }

    $pinfo = New-Object System.Diagnostics.ProcessStartInfo
    $pinfo.FileName = "cmd.exe"
    $pinfo.Arguments = "/c `"$buildCmd`""
    $pinfo.WorkingDirectory = $projectRoot
    $pinfo.UseShellExecute = $false
    $pinfo.RedirectStandardOutput = $true
    $pinfo.RedirectStandardError = $true

    $p = [System.Diagnostics.Process]::Start($pinfo)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit()

    if ($p.ExitCode -ne 0) {
        Write-Host "[ERROR] Compilation failed with exit code $($p.ExitCode):" -ForegroundColor Red
        Write-Host $stdout
        Write-Host $stderr -ForegroundColor DarkRed
        exit 1
    }

    Write-Host "[BUILD] Compilation successful." -ForegroundColor Green
}

$previousBinary = $env:DESKMATE_TEST_BINARY
$previousDeskmateDir = $env:DESKMATE_DIR
$previousDry = $env:DESKMATE_DOCK_DRY
if ($BinaryPath) {
    if (-not (Test-Path -LiteralPath $BinaryPath -PathType Leaf)) {
        Write-Host "[ERROR] Test binary not found: $BinaryPath" -ForegroundColor Red
        exit 1
    }
    $BinaryPath = (Resolve-Path -LiteralPath $BinaryPath).Path
    if ([IO.Path]::GetExtension($BinaryPath) -ine ".exe") {
        Write-Host "[ERROR] Test binary must be an .exe file: $BinaryPath" -ForegroundColor Red
        exit 1
    }
    $env:DESKMATE_TEST_BINARY = $BinaryPath
}
$testData = Join-Path ([IO.Path]::GetTempPath()) ("Deskmate\tests\" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testData -Force | Out-Null
$env:DESKMATE_DIR = $testData
$env:DESKMATE_DOCK_DRY = "1"
try {
# 3. Load Common Infrastructure
$harnessPath = Join-Path $scriptDir "common\TestHarness.ps1"
$loaderPath = Join-Path $scriptDir "common\DeskmateLoader.ps1"

if (-not (Test-Path $harnessPath) -or -not (Test-Path $loaderPath)) {
    Write-Host "[ERROR] Test harness infrastructure missing in $scriptDir\common" -ForegroundColor Red
    exit 1
}

. $harnessPath
. $loaderPath

if ($VerboseOutput) {
    Set-TestVerbose $true
}

# 4. Discover Test Files to Execute
$testFilesToRun = New-Object System.Collections.ArrayList

if ($TestFile) {
    $fullTestPath = if (Test-Path $TestFile) { (Resolve-Path $TestFile).Path } else { Join-Path $projectRoot $TestFile }
    if (Test-Path $fullTestPath) {
        [void]$testFilesToRun.Add([PSCustomObject]@{ Tier = 0; Path = $fullTestPath; Name = (Split-Path -Leaf $fullTestPath) })
    } else {
        Write-Host "[ERROR] Specified test file not found: $TestFile" -ForegroundColor Red
        exit 1
    }
}
else {
    $tierDirs = @{
        1 = (Join-Path $scriptDir "tier1_features")
        2 = (Join-Path $scriptDir "tier2_boundaries")
        3 = (Join-Path $scriptDir "tier3_interactions")
        4 = (Join-Path $scriptDir "tier4_scenarios")
    }

    $tiersToSearch = if ($Tier) { @($Tier) } else { @(1, 2, 3, 4) }

    foreach ($t in $tiersToSearch) {
        $dir = $tierDirs[$t]
        if (Test-Path $dir) {
            $pattern = if ($Feature) {
                # Match files like test_f02_*.ps1 or test_b02_*.ps1
                $fPad = "{0:D2}" -f $Feature
                "*$fPad*.ps1"
            } else {
                "*.ps1"
            }

            $files = Get-ChildItem -Path $dir -Filter $pattern | Sort-Object Name
            foreach ($f in $files) {
                [void]$testFilesToRun.Add([PSCustomObject]@{ Tier = $t; Path = $f.FullName; Name = $f.Name })
            }
        }
    }

    # If no tier files are present yet, default to running smoke test
    if ($testFilesToRun.Count -eq 0) {
        $smoke = Join-Path $scriptDir "smoke_test.ps1"
        if (Test-Path $smoke) {
            Write-Host "[INFO] No tier test files found matching criteria. Running smoke test suite..." -ForegroundColor Yellow
            [void]$testFilesToRun.Add([PSCustomObject]@{ Tier = 0; Path = $smoke; Name = "smoke_test.ps1" })
        }
    }
}

if ($testFilesToRun.Count -eq 0) {
    Write-Host "[WARN] No test files discovered to run." -ForegroundColor Yellow
    exit 0
}

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "             DESKMATE E2E TEST RUNNER EXECUTION             " -ForegroundColor White
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Discovered Files: $($testFilesToRun.Count)"
Write-Host " Working Dir     : $projectRoot"
Write-Host " Binary Target   : $global:DeskmateAssemblyPath"
Write-Host " Binary SHA-256  : $((Get-FileHash -LiteralPath $global:DeskmateAssemblyPath -Algorithm SHA256).Hash)"
Write-Host " Isolated Data   : $testData"
Write-Host " Suite Isolation : fresh PowerShell process per file"
Write-Host "========================================================`n" -ForegroundColor Cyan

# 5. Execute Test Files and Track Metrics per Tier
$tierStats = @{}
foreach ($t in 0..4) {
    $tierStats[$t] = @{ Total = 0; Passed = 0; Failed = 0; DurationMs = 0.0 }
}

$overallStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($tf in $testFilesToRun) {
    Write-Host ">> Executing: $($tf.Name)" -ForegroundColor Magenta
    if ($global:TestContext) {
        $global:TestContext.BeforeHooks.Clear()
        $global:TestContext.AfterHooks.Clear()
    }
    
    $fileSw = [System.Diagnostics.Stopwatch]::StartNew()

    $suiteData = Join-Path $testData ([IO.Path]::GetFileNameWithoutExtension($tf.Name))
    New-Item -ItemType Directory -Path $suiteData -Force | Out-Null
    $suiteInfo = New-Object System.Diagnostics.ProcessStartInfo
    $suiteInfo.FileName = (Get-Process -Id $PID).Path
    $suiteInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$scriptDir\common\Invoke-Suite.ps1`" -TestPath `"$($tf.Path)`" -HarnessPath `"$harnessPath`" -LoaderPath `"$loaderPath`""
    if ($BinaryPath) { $suiteInfo.Arguments += " -BinaryPath `"$BinaryPath`"" }
    if ($VerboseOutput) { $suiteInfo.Arguments += " -VerboseOutput" }
    $suiteInfo.WorkingDirectory = $projectRoot
    $suiteInfo.UseShellExecute = $false
    $suiteInfo.CreateNoWindow = $true
    $suiteInfo.RedirectStandardOutput = $true
    $suiteInfo.RedirectStandardError = $true
    $suiteInfo.EnvironmentVariables["DESKMATE_DIR"] = $suiteData
    $suiteInfo.EnvironmentVariables["DESKMATE_DOCK_DRY"] = "1"

    $suiteProcess = [System.Diagnostics.Process]::Start($suiteInfo)
    $suiteOutput = $suiteProcess.StandardOutput.ReadToEnd()
    $suiteError = $suiteProcess.StandardError.ReadToEnd()
    $suiteProcess.WaitForExit()
    $fileSw.Stop()

    if ($suiteOutput) { Write-Host $suiteOutput.TrimEnd() }
    if ($suiteError) { Write-Host $suiteError.TrimEnd() -ForegroundColor DarkRed }
    $suiteSummary = [regex]::Match($suiteOutput, "DESKMATE_TEST_SUMMARY TOTAL=(\d+) PASSED=(\d+) FAILED=(\d+)")
    if ($suiteSummary.Success) {
        $deltaTotal = [int]$suiteSummary.Groups[1].Value
        $deltaPass = [int]$suiteSummary.Groups[2].Value
        $deltaFail = [int]$suiteSummary.Groups[3].Value
    }
    else {
        Write-Host "   [FATAL ERROR IN SCRIPT] No test summary was emitted; the suite did not complete." -ForegroundColor Red
        $deltaTotal = 1
        $deltaPass = 0
        $deltaFail = 1
    }

    $tKey = $tf.Tier
    $tierStats[$tKey].Total += $deltaTotal
    $tierStats[$tKey].Passed += $deltaPass
    $tierStats[$tKey].Failed += $deltaFail
    $tierStats[$tKey].DurationMs += $fileSw.Elapsed.TotalMilliseconds
}

$overallStopwatch.Stop()

# 6. Render Formatted Multi-Tier Results Table
Write-Host "`n==========================================================================================" -ForegroundColor Gray
Write-Host "                              MULTI-TIER TEST EXECUTION REPORT                            " -ForegroundColor White
Write-Host "==========================================================================================" -ForegroundColor Gray

$tableRows = New-Object System.Collections.ArrayList

foreach ($t in 1..4) {
    if ($tierStats[$t].Total -gt 0 -or ($Tier -eq $t)) {
        $st = $tierStats[$t]
        $status = if ($st.Total -eq 0) { "EMPTY" } elseif ($st.Failed -eq 0) { "PASS" } else { "FAIL" }
        [void]$tableRows.Add([PSCustomObject]@{
            Tier     = "Tier $t"
            Total    = $st.Total
            Passed   = $st.Passed
            Failed   = $st.Failed
            Duration = "$([Math]::Round($st.DurationMs, 1)) ms"
            Status   = $status
        })
    }
}

if ($tierStats[0].Total -gt 0) {
    $st0 = $tierStats[0]
    $status0 = if ($st0.Failed -eq 0) { "PASS" } else { "FAIL" }
    [void]$tableRows.Add([PSCustomObject]@{
        Tier     = $(if ($TestFile) { "Custom" } else { "Smoke" })
        Total    = $st0.Total
        Passed   = $st0.Passed
        Failed   = $st0.Failed
        Duration = "$([Math]::Round($st0.DurationMs, 1)) ms"
        Status   = $status0
    })
}

# Grand Total Row: child-process counters live in tierStats, not the parent harness.
$grandTotal = 0
$grandPassed = 0
$grandFailed = 0
foreach ($st in $tierStats.Values) {
    $grandTotal += $st.Total
    $grandPassed += $st.Passed
    $grandFailed += $st.Failed
}
$grandStatus = if ($grandTotal -gt 0 -and $grandFailed -eq 0) { "PASS" } else { "FAIL" }
$grandDuration = "$([Math]::Round($overallStopwatch.Elapsed.TotalMilliseconds, 1)) ms"

[void]$tableRows.Add([PSCustomObject]@{
    Tier     = "GRAND TOTAL"
    Total    = $grandTotal
    Passed   = $grandPassed
    Failed   = $grandFailed
    Duration = $grandDuration
    Status   = $grandStatus
})

# Display table
$tableRows | Format-Table -Property Tier, Total, Passed, Failed, Duration, Status -AutoSize | Out-String | Write-Host

Write-Host "==========================================================================================" -ForegroundColor Gray
if ($grandStatus -eq "PASS") {
    Write-Host " OVERALL STATUS: ALL TESTS PASSED SUCCESSFULLY" -ForegroundColor Green
} else {
    Write-Host " OVERALL STATUS: TEST FAILURES DETECTED ($grandFailed failed)" -ForegroundColor Red
}
}
finally {
    $env:DESKMATE_TEST_BINARY = $previousBinary
    $env:DESKMATE_DIR = $previousDeskmateDir
    $env:DESKMATE_DOCK_DRY = $previousDry
    if (Test-Path -LiteralPath $testData) {
        Remove-Item -LiteralPath $testData -Recurse -Force -ErrorAction SilentlyContinue
    }
}

exit $(if ($grandStatus -eq "PASS") { 0 } else { 1 })
