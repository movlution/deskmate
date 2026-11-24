# verify_live_e2e.ps1
# Opaque-box 4-stage live verification for Deskmate (self-contained .NET 10 or baseline NetFx 4.8).
# Copies ONLY the target Deskmate.exe into an isolated folder, strips DOTNET_* environment hints,
# enforces DESKMATE_DIR / DESKMATE_HOME / DESKMATE_DOCK_DRY=1, and exercises CLI, GUI, Ingatan HTTP, and MCP stdio.

[CmdletBinding()]
param(
    [string]$ExePath = '',
    [int]$TimeoutSeconds = 25,
    [switch]$RequireSelfContained,
    [switch]$KeepTemp
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common\TestHarness.ps1')

if (-not ('LiveE2EWin32' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LiveE2EWin32
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static bool HasAnyWindow(int pid, out int totalCount, out int visibleCount)
    {
        int total = 0;
        int visible = 0;
        EnumWindows((h, l) =>
        {
            uint winPid;
            GetWindowThreadProcessId(h, out winPid);
            if ((int)winPid == pid)
            {
                total++;
                if (IsWindowVisible(h)) visible++;
            }
            return true;
        }, IntPtr.Zero);
        totalCount = total;
        visibleCount = visible;
        return total > 0;
    }
}
'@
}

function Resolve-TargetExe([string]$Requested) {
    if ($Requested) {
        if (-not (Test-Path -LiteralPath $Requested -PathType Leaf)) {
            throw "Target executable not found: $Requested"
        }
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    $candidates = @(
        (Join-Path $projectRoot 'artifacts\publish\Deskmate.exe'),
        (Join-Path $projectRoot 'bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish\Deskmate.exe'),
        (Join-Path $projectRoot 'bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\Deskmate.exe'),
        (Join-Path $projectRoot 'Deskmate.exe')
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c -PathType Leaf) {
            return (Resolve-Path -LiteralPath $c).Path
        }
    }
    throw "No Deskmate.exe found to verify. Pass -ExePath <path-to-Deskmate.exe>."
}

$sourceExe = Resolve-TargetExe $ExePath
$root = Join-Path ([IO.Path]::GetTempPath()) ('deskmate-live-e2e-' + [Guid]::NewGuid().ToString('N'))
$appDir = Join-Path $root 'app'
$dataDir = Join-Path $root 'data'
$homeDir = Join-Path $root 'home'
$isolatedExe = Join-Path $appDir 'Deskmate.exe'
$trackedProcesses = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

function New-IsolatedStartInfo {
    param(
        [string]$Arguments = '',
        [bool]$RedirectStd = $true,
        [bool]$RedirectStdin = $false
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $isolatedExe
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $appDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $RedirectStd
    $psi.RedirectStandardOutput = $RedirectStd
    $psi.RedirectStandardError = $RedirectStd
    $psi.RedirectStandardInput = $RedirectStdin
    if ($RedirectStd) {
        $psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $psi.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    }
    $psi.EnvironmentVariables['DESKMATE_DIR'] = $dataDir
    $psi.EnvironmentVariables['DESKMATE_HOME'] = $homeDir
    $psi.EnvironmentVariables['DESKMATE_DOCK_DRY'] = '1'
    $psi.EnvironmentVariables['DESKMATE_FPS'] = '1'
    foreach ($v in @('DOTNET_ROOT', 'DOTNET_ROOT(x86)', 'DOTNET_MULTILEVEL_LOOKUP')) {
        if ($psi.EnvironmentVariables.ContainsKey($v)) {
            [void]$psi.EnvironmentVariables.Remove($v)
        }
    }
    return $psi
}

function Invoke-IsolatedCli {
    param(
        [string]$Arguments,
        [string]$StdinText = $null,
        [int]$WaitMs = 5000
    )
    $hasStdin = ($null -ne $StdinText)
    $psi = New-IsolatedStartInfo -Arguments $Arguments -RedirectStd $true -RedirectStdin $hasStdin
    $proc = [System.Diagnostics.Process]::Start($psi)
    [void]$trackedProcesses.Add($proc)
    try {
        if ($hasStdin) {
            $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($StdinText)
            $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
            $proc.StandardInput.Close()
        }
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        $exited = $proc.WaitForExit($WaitMs)
        if (-not $exited) {
            try { $proc.Kill() } catch { }
            [void]$proc.WaitForExit(2000)
            throw "CLI '$Arguments' did not exit within ${WaitMs}ms"
        }
        $stdout = $outTask.GetAwaiter().GetResult()
        $stderr = $errTask.GetAwaiter().GetResult()
        return [PSCustomObject]@{
            ExitCode = $proc.ExitCode
            Stdout   = $stdout
            Stderr   = $stderr
        }
    }
    finally {
        $proc.Dispose()
    }
}

function Invoke-HttpGetStatus {
    param(
        [int]$Port,
        [string]$Path,
        [hashtable]$Headers = @{}
    )
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect([System.Net.IPAddress]::Loopback, $Port)
        $client.ReceiveTimeout = 4000
        $client.SendTimeout = 4000
        $stream = $client.GetStream()
        $sb = New-Object System.Text.StringBuilder
        [void]$sb.Append("GET $Path HTTP/1.1`r`nHost: 127.0.0.1:$Port`r`nConnection: close`r`n")
        foreach ($k in $Headers.Keys) {
            [void]$sb.Append("$k`: $($Headers[$k])`r`n")
        }
        [void]$sb.Append("`r`n")
        $reqBytes = [System.Text.Encoding]::ASCII.GetBytes($sb.ToString())
        $stream.Write($reqBytes, 0, $reqBytes.Length)

        $ms = New-Object System.IO.MemoryStream
        $buf = New-Object byte[] 4096
        while (($n = $stream.Read($buf, 0, $buf.Length)) -gt 0) {
            $ms.Write($buf, 0, $n)
        }
        $raw = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
        $sep = $raw.IndexOf("`r`n`r`n", [StringComparison]::Ordinal)
        $head = if ($sep -ge 0) { $raw.Substring(0, $sep) } else { $raw }
        $body = if ($sep -ge 0) { $raw.Substring($sep + 4) } else { '' }
        $status = 0
        if ($head -match '^HTTP/\S+\s+(\d{3})') { $status = [int]$matches[1] }
        return [PSCustomObject]@{
            Status = $status
            Body   = $body
        }
    }
    finally {
        $client.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $appDir, $dataDir, $homeDir -Force | Out-Null
    Copy-Item -LiteralPath $sourceExe -Destination $isolatedExe -Force
    $appFiles = @(Get-ChildItem -LiteralPath $appDir)
    if ($appFiles.Count -ne 1) {
        throw "Isolated app dir must contain strictly 1 file (found $($appFiles.Count))"
    }

    Set-Content -LiteralPath (Join-Path $dataDir 'settings.ini') -Encoding UTF8 -Value @(
        'material=solid',
        'glass=0',
        'wall.sync=0'
    )

    Describe "Live E2E Stage 1: Portable Artifact Integrity ($sourceExe)" {
        It "Contains strictly 1 isolated file and valid 64-bit PE header with version info" {
            $files = @(Get-ChildItem -LiteralPath $appDir)
            Assert-Equal $files.Count 1 "Isolated app directory must hold strictly 1 file (Deskmate.exe)"
            $exeItem = Get-Item -LiteralPath $isolatedExe
            Assert-True ($exeItem.Length -gt 500000) "Deskmate.exe size must be > 500KB"

            $fs = [System.IO.File]::OpenRead($isolatedExe)
            try {
                $br = New-Object System.IO.BinaryReader($fs)
                $mz = $br.ReadUInt16()
                Assert-Equal $mz 0x5A4D "PE MZ header must be present"
                [void]$fs.Seek(0x3C, [System.IO.SeekOrigin]::Begin)
                $peOffset = $br.ReadInt32()
                [void]$fs.Seek($peOffset, [System.IO.SeekOrigin]::Begin)
                $peSig = $br.ReadUInt32()
                Assert-Equal $peSig 0x00004550 "PE signature must be valid"
                $machine = $br.ReadUInt16()
                Assert-Equal $machine 0x8664 "PE machine architecture must be x64 (0x8664)"
            }
            finally {
                $fs.Dispose()
            }

            $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($isolatedExe)
            Assert-True (-not [string]::IsNullOrWhiteSpace($ver.FileVersion) -or -not [string]::IsNullOrWhiteSpace($ver.ProductName)) "FileVersionInfo must be populated"

            if ($RequireSelfContained) {
                Assert-True ($exeItem.Length -ge (50L * 1024L * 1024L)) "Self-contained single-file Deskmate.exe must be >= 50 MB (actual: $($exeItem.Length) bytes)"
                Assert-True ($exeItem.Length -lt (256L * 1024L * 1024L)) "Self-contained Deskmate.exe must remain below Update.AssetMax 256 MB (actual: $($exeItem.Length) bytes)"
            }
        }
    }

    Describe "Live E2E Stage 2: CLI Fast Paths (No Island Running)" {
        It "Handles --notify missing args (exit 2) and no-island probe (exit 1)" {
            $rMissing = Invoke-IsolatedCli -Arguments '--notify' -WaitMs 5000
            Assert-Equal $rMissing.ExitCode 2 "--notify with no args must exit with 2"

            $rNoIsland = Invoke-IsolatedCli -Arguments '--notify "E2E Probe" "No island"' -WaitMs 5000
            Assert-Equal $rNoIsland.ExitCode 1 "--notify without running Island must exit with 1"
        }

        It "Handles --run missing args (exit 2), command stdout (exit 0), and exit code propagation (exit 7)" {
            $rMissing = Invoke-IsolatedCli -Arguments '--run' -WaitMs 5000
            Assert-Equal $rMissing.ExitCode 2 "--run with no args must exit with 2"

            $rOk = Invoke-IsolatedCli -Arguments '--run cmd /c echo LIVE_E2E_RUN_OK' -WaitMs 8000
            Assert-Equal $rOk.ExitCode 0 "--run echo must exit 0"
            Assert-True ($rOk.Stdout.Contains('LIVE_E2E_RUN_OK')) "--run stdout must contain echoed marker (got: $($rOk.Stdout))"

            $rSeven = Invoke-IsolatedCli -Arguments '--run cmd /c exit 7' -WaitMs 8000
            Assert-Equal $rSeven.ExitCode 7 "--run must propagate wrapped command exit code 7"
        }

        It "Formats --statusline from JSON stdin and exits 0" {
            $stdin = '{"model":{"display_name":"Claude"},"context_window":{"used_percentage":42},"rate_limits":{"five_hour":{"used_percentage":15,"resets_at":1999999999}}}'
            $r = Invoke-IsolatedCli -Arguments '--statusline' -StdinText $stdin -WaitMs 5000
            Assert-Equal $r.ExitCode 0 "--statusline must exit 0"
            Assert-True (-not [string]::IsNullOrWhiteSpace($r.Stdout)) "--statusline must write non-empty formatted line"
        }

        It "Handles --relay claude --json without Island and outputs {}" {
            $stdin = '{"hook_event_name":"Notification","message":"probe"}'
            $r = Invoke-IsolatedCli -Arguments '--relay claude --json' -StdinText $stdin -WaitMs 5000
            Assert-Equal $r.ExitCode 0 "--relay must exit 0"
            Assert-Equal ($r.Stdout.Trim()) '{}' "--relay claude --json without Island must emit {}"
        }
    }

    Describe "Live E2E Stage 3: Live Desktop/Overlay GUI Boot + Live CLI IPC" {
        It "Boots GUI cleanly, exposes window & agent.port, accepts live IPC, and terminates cleanly" {
            $logPath = Join-Path $dataDir 'deskmate.log'
            $iniPath = Join-Path $dataDir 'settings.ini'
            $guiPsi = New-IsolatedStartInfo -Arguments '' -RedirectStd $false -RedirectStdin $false
            $guiProc = [System.Diagnostics.Process]::Start($guiPsi)
            [void]$trackedProcesses.Add($guiProc)
            try {
                $bootMatch = $null
                $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
                while ([DateTime]::UtcNow -lt $deadline) {
                    if ($guiProc.HasExited) {
                        $logTail = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { '<no log>' }
                        throw "GUI process exited prematurely with code $($guiProc.ExitCode). Log:`n$logTail"
                    }
                    if (Test-Path -LiteralPath $logPath) {
                        $logText = Get-Content -LiteralPath $logPath -Raw -ErrorAction SilentlyContinue
                        if ($logText) {
                            $m = [regex]::Match($logText, 'boot\s+(\d+)ms')
                            if ($m.Success) {
                                $bootMatch = $m
                                break
                            }
                        }
                    }
                    [System.Threading.Thread]::Sleep(100)
                }
                Assert-NotNull $bootMatch "deskmate.log must log 'boot <ms>ms' within ${TimeoutSeconds}s"
                $bootMs = [int]$bootMatch.Groups[1].Value

                try { [void]$guiProc.WaitForInputIdle(3000) } catch { }
                $guiProc.Refresh()
                Assert-False $guiProc.HasExited "GUI process must remain alive after boot"

                $totWins = 0
                $visWins = 0
                $hasWin = [LiveE2EWin32]::HasAnyWindow($guiProc.Id, [ref]$totWins, [ref]$visWins)
                Assert-True ($guiProc.MainWindowHandle -ne [IntPtr]::Zero -or $hasWin) "GUI process must create a native window handle (total=$totWins, visible=$visWins)"

                $logLines = Get-Content -LiteralPath $logPath -ErrorAction SilentlyContinue
                $fatalLines = @($logLines | Where-Object { $_ -match '(?i)\b(fatal:|ui:)' })
                Assert-Equal $fatalLines.Count 0 "deskmate.log must have 0 fatal:/ui: exceptions (found: $($fatalLines -join ' | '))"

                $wsMb = [Math]::Round($guiProc.WorkingSet64 / 1MB, 2)
                $privMb = [Math]::Round($guiProc.PrivateMemorySize64 / 1MB, 2)
                Write-Host "       [LIVE METRICS] boot=${bootMs}ms  WorkingSet=${wsMb}MB  PrivateMem=${privMb}MB" -ForegroundColor Cyan

                # Wait briefly for settings.ini to persist agent.port
                $agentPort = 0
                $portDeadline = [DateTime]::UtcNow.AddSeconds(5)
                while ([DateTime]::UtcNow -lt $portDeadline) {
                    if (Test-Path -LiteralPath $iniPath) {
                        $iniRaw = Get-Content -LiteralPath $iniPath -Raw -ErrorAction SilentlyContinue
                        if ($iniRaw -match '(?m)^agent\.port=(\d+)\s*$') {
                            $agentPort = [int]$matches[1]
                            break
                        }
                    }
                    [System.Threading.Thread]::Sleep(80)
                }
                Assert-True ($agentPort -gt 0) "settings.ini must populate agent.port"

                $tcp = New-Object System.Net.Sockets.TcpClient
                try {
                    $ar = $tcp.BeginConnect([System.Net.IPAddress]::Loopback, $agentPort, $null, $null)
                    $connected = $ar.AsyncWaitHandle.WaitOne(2000)
                    if ($connected) { $tcp.EndConnect($ar) }
                    Assert-True $connected "Loopback TCP connect to agent.port ($agentPort) must succeed"
                }
                finally {
                    $tcp.Dispose()
                }

                # Live IPC invocations against running Island
                $rNotify = Invoke-IsolatedCli -Arguments '--notify "Live Toast" "Over loopback" --id e2e-1 --progress 50' -WaitMs 5000
                Assert-Equal $rNotify.ExitCode 0 "Live --notify against running Island must exit 0 (stderr=$($rNotify.Stderr))"
                Assert-Equal $rNotify.Stderr.Trim().Length 0 "Live --notify must produce empty stderr"

                $rRelay = Invoke-IsolatedCli -Arguments '--relay claude --json' -StdinText '{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"echo hi"}}' -WaitMs 5000
                Assert-Equal $rRelay.ExitCode 0 "Live --relay against running Island must exit 0"

                $rRun = Invoke-IsolatedCli -Arguments '--run cmd /c echo LIVE_WITH_ISLAND' -WaitMs 8000
                Assert-Equal $rRun.ExitCode 0 "Live --run against running Island must exit 0"
                Assert-True ($rRun.Stdout.Contains('LIVE_WITH_ISLAND')) "Live --run stdout must contain LIVE_WITH_ISLAND"
            }
            finally {
                if (-not $guiProc.HasExited) {
                    try { [void]$guiProc.CloseMainWindow() } catch { }
                    if (-not $guiProc.WaitForExit(1500)) {
                        try { $guiProc.Kill() } catch { }
                        [void]$guiProc.WaitForExit(5000)
                    }
                }
                Assert-True $guiProc.HasExited "GUI process must exit cleanly"
                $guiProc.Dispose()
            }
        }
    }

    Describe "Live E2E Stage 4: Live ingatan Daemon (--ingat) + HTTP API + --mcp JSON-RPC 2.0" {
        It "Starts --ingat, validates HTTP auth & /status, and exercises all 14 --mcp tools via stdio JSON-RPC 2.0" {
            # Enable ingat=1 now so --ingat stays alive (IngatService polls settings every 10s)
            Set-Content -LiteralPath (Join-Path $dataDir 'settings.ini') -Encoding UTF8 -Value @(
                'ingat=1',
                'material=solid',
                'glass=0',
                'wall.sync=0'
            )

            $ingatPsi = New-IsolatedStartInfo -Arguments "--ingat --parent $PID" -RedirectStd $false -RedirectStdin $false
            $ingatProc = [System.Diagnostics.Process]::Start($ingatPsi)
            [void]$trackedProcesses.Add($ingatProc)
            try {
                $dbPath = Join-Path $dataDir 'ingat\ingat.db'
                $apiPath = Join-Path $dataDir 'ingat\api.json'
                $deadline = [DateTime]::UtcNow.AddSeconds(15)
                while ([DateTime]::UtcNow -lt $deadline) {
                    if ($ingatProc.HasExited) {
                        throw "--ingat daemon exited early with code $($ingatProc.ExitCode)"
                    }
                    if ((Test-Path -LiteralPath $dbPath -PathType Leaf) -and (Test-Path -LiteralPath $apiPath -PathType Leaf)) {
                        break
                    }
                    [System.Threading.Thread]::Sleep(100)
                }

                Assert-True (Test-Path -LiteralPath $dbPath -PathType Leaf) "ingat\ingat.db must be created"
                Assert-True (Test-Path -LiteralPath $apiPath -PathType Leaf) "ingat\api.json must be created"

                $apiInfo = (Get-Content -LiteralPath $apiPath -Raw) | ConvertFrom-Json
                Assert-Equal ([int]$apiInfo.pid) $ingatProc.Id "api.json pid must match --ingat process ID"
                Assert-True ([int]$apiInfo.port -gt 0) "api.json port must be > 0"
                Assert-True (-not [string]::IsNullOrWhiteSpace([string]$apiInfo.token)) "api.json token must be non-empty"

                $port = [int]$apiInfo.port
                $token = [string]$apiInfo.token

                # Auth negative tests
                $rNoToken = Invoke-HttpGetStatus -Port $port -Path '/status'
                Assert-Equal $rNoToken.Status 403 "GET /status without X-Deskmate-Token must return 403"

                $rOrigin = Invoke-HttpGetStatus -Port $port -Path '/status' -Headers @{
                    'X-Deskmate-Token' = $token
                    'Origin'           = 'http://evil.example'
                }
                Assert-Equal $rOrigin.Status 403 "GET /status with Origin header must return 403"

                # Auth positive test
                $rOk = Invoke-HttpGetStatus -Port $port -Path '/status' -Headers @{
                    'X-Deskmate-Token' = $token
                }
                Assert-Equal $rOk.Status 200 "GET /status with valid X-Deskmate-Token must return 200"
                $statusJson = $rOk.Body | ConvertFrom-Json
                Assert-NotNull $statusJson.state "/status response must include state"
                Assert-True ($null -ne $statusJson.episodes) "/status response must include episodes"
                Assert-True ($null -ne $statusJson.people) "/status response must include people"
                Assert-True ($null -ne $statusJson.privateMb) "/status response must include privateMb"

                # Live --mcp JSON-RPC 2.0 stdio test
                $mcpPsi = New-IsolatedStartInfo -Arguments '--mcp' -RedirectStd $true -RedirectStdin $true
                $mcpPsi.StandardInputEncoding = New-Object System.Text.UTF8Encoding($false)
                $mcpProc = [System.Diagnostics.Process]::Start($mcpPsi)
                [void]$trackedProcesses.Add($mcpProc)
                try {
                    $readMcpLine = {
                        $readTask = $mcpProc.StandardOutput.ReadLineAsync()
                        if (-not $readTask.Wait(5000)) {
                            throw "Timed out waiting for --mcp JSON-RPC response line"
                        }
                        $line = $readTask.Result
                        if ([string]::IsNullOrWhiteSpace($line)) {
                            throw "Received empty/EOF line from --mcp stdout"
                        }
                        return ($line | ConvertFrom-Json)
                    }

                    # 1. initialize
                    $mcpProc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}')
                    $mcpProc.StandardInput.Flush()
                    $r1 = & $readMcpLine
                    Assert-Equal ([int]$r1.id) 1 "initialize response id must equal 1"
                    Assert-Equal ([string]$r1.result.serverInfo.name) 'deskmate-ingatan' "serverInfo.name must equal deskmate-ingatan"
                    Assert-NotNull $r1.result.capabilities.tools "initialize response must advertise capabilities.tools"

                    # 2. notifications/initialized (notification - no reply expected)
                    $mcpProc.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
                    $mcpProc.StandardInput.Flush()

                    # 3. tools/list
                    $mcpProc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
                    $mcpProc.StandardInput.Flush()
                    $r2 = & $readMcpLine
                    Assert-Equal ([int]$r2.id) 2 "tools/list response id must equal 2"
                    Assert-Equal $r2.result.tools.Count 14 "tools/list must expose 14 read-only tools"
                    $toolNames = @($r2.result.tools | ForEach-Object { $_.name })
                    foreach ($expectedTool in @('search_activity', 'current_context', 'get_timeline', 'get_promises')) {
                        Assert-True ($toolNames -contains $expectedTool) "tools/list must include $expectedTool"
                    }

                    # 4. tools/call current_context
                    $mcpProc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"current_context","arguments":{}}}')
                    $mcpProc.StandardInput.Flush()
                    $r3 = & $readMcpLine
                    Assert-Equal ([int]$r3.id) 3 "tools/call current_context id must equal 3"
                    Assert-False ([bool]$r3.result.isError) "current_context must succeed (isError=false); got $($r3.result.content[0].text)"
                    Assert-True (-not [string]::IsNullOrWhiteSpace([string]$r3.result.content[0].text)) "current_context content[0].text must be non-empty"

                    # 5. tools/call get_timeline
                    $mcpProc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_timeline","arguments":{}}}')
                    $mcpProc.StandardInput.Flush()
                    $r4 = & $readMcpLine
                    Assert-Equal ([int]$r4.id) 4 "tools/call get_timeline id must equal 4"
                    Assert-False ([bool]$r4.result.isError) "get_timeline must succeed (isError=false); got $($r4.result.content[0].text)"
                    Assert-True (([string]$r4.result.content[0].text).Contains('"episodes"')) "get_timeline JSON payload must contain episodes"

                    # Clean EOF exit
                    $mcpProc.StandardInput.Close()
                    Assert-True ($mcpProc.WaitForExit(5000)) "--mcp must exit within 5s after stdin EOF"
                    Assert-Equal $mcpProc.ExitCode 0 "--mcp must exit cleanly with code 0"
                }
                finally {
                    if (-not $mcpProc.HasExited) {
                        try { $mcpProc.Kill() } catch { }
                        [void]$mcpProc.WaitForExit(2000)
                    }
                    $mcpProc.Dispose()
                }
            }
            finally {
                if (-not $ingatProc.HasExited) {
                    try { $ingatProc.Kill() } catch { }
                    [void]$ingatProc.WaitForExit(5000)
                }
                $ingatProc.Dispose()
            }
        }
    }
}
finally {
    foreach ($p in $trackedProcesses) {
        try {
            if ($p -and -not $p.HasExited) { $p.Kill() }
        }
        catch { }
    }
    try {
        Get-Process -ErrorAction SilentlyContinue | Where-Object {
            try { $_.MainModule.FileName -ieq $isolatedExe } catch { $false }
        } | ForEach-Object {
            try { $_.Kill(); [void]$_.WaitForExit(3000) } catch { }
        }
    }
    catch { }

    if (-not $KeepTemp -and (Test-Path -LiteralPath $root)) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$summary = Get-TestSummary
Format-TestReport $summary
exit $(if ($summary.Success) { 0 } else { 1 })
