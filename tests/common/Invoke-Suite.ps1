param(
    [Parameter(Mandatory = $true)][string]$TestPath,
    [Parameter(Mandatory = $true)][string]$HarnessPath,
    [Parameter(Mandatory = $true)][string]$LoaderPath,
    [string]$BinaryPath,
    [switch]$VerboseOutput
)

# One fresh process per suite keeps assembly state and Settings.Dir isolated.
if ($BinaryPath) { $env:DESKMATE_TEST_BINARY = $BinaryPath }
. $HarnessPath
. $LoaderPath
Set-TestVerbose ([bool]$VerboseOutput)

try {
    & $TestPath
}
catch {
    $global:TestContext.Total++
    $global:TestContext.Failed++
    Write-Output $_.Exception.ToString()
}
finally {
    $summary = Get-TestSummary
    Write-Output "DESKMATE_TEST_SUMMARY TOTAL=$($summary.Total) PASSED=$($summary.Passed) FAILED=$($summary.Failed)"
    exit [Math]::Min(1, $summary.Failed)
}
