# test_f39_pet_live_ws_and_tools.ps1
# Tier 1 Feature Tests (F9, F15): Live PetHost + PetLinkClient WebSocket Roundtrip & Shelf/Downloads/Drives Contracts

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
$script:f39OwnedTmp = $null
$script:f39PrevDir = $env:DESKMATE_DIR
$script:f39PrevHome = $env:DESKMATE_HOME
$script:f39PrevDry = $env:DESKMATE_DOCK_DRY
if ([string]::IsNullOrEmpty($env:DESKMATE_DIR)) {
    $script:f39OwnedTmp = Join-Path ([IO.Path]::GetTempPath()) ("deskmate-f39-" + [Guid]::NewGuid().ToString("N"))
    $f39Home = Join-Path $script:f39OwnedTmp "home"
    New-Item -ItemType Directory -Path $script:f39OwnedTmp, $f39Home -Force | Out-Null
    $env:DESKMATE_DIR = $script:f39OwnedTmp
    $env:DESKMATE_HOME = $f39Home
    $env:DESKMATE_DOCK_DRY = "1"
}

. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

try {
    Describe "Tier 1 Feature F39: PetHost/PetLinkClient Live WS & Shelf/Downloads/Drives Tools" {
        $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'

        It "Connects PetLinkClient to PetHost over loopback WebSocket, exchanges chat & state, and shuts down cleanly" {
            $hostType = Get-DeskmateType "PetHost"
            $clientType = Get-DeskmateType "PetLinkClient"
            $profileType = Get-DeskmateType "PetPeerProfile"
            $inviteType = Get-DeskmateType "PetInvite"

            $hostProf = [Activator]::CreateInstance($profileType)
            $hostProf.Name = "HostPet"
            $hostProf.Kind = "cat"
            $guestProf = [Activator]::CreateInstance($profileType)
            $guestProf.Id = "guest-f39"
            $guestProf.Name = "GuestPet"
            $guestProf.Kind = "fox"

            $petHost = [Activator]::CreateInstance($hostType)
            $petClient = [Activator]::CreateInstance($clientType)
            try {
                $petHost.Start($hostProf, $false, 0)
                Assert-True $petHost.Running "PetHost must be running"
                Assert-True ($petHost.Port -gt 0) "PetHost must bind a valid loopback port"
                Assert-Equal $petHost.Pin.Length 6 "PetHost PIN must be 6 characters"

                $inviteStr = "ws://127.0.0.1:$($petHost.Port)/ws#$($petHost.Pin)"
                $parseArgs = [object[]]@($inviteStr, $null, $null)
                $tryParse = $inviteType.GetMethod("TryParse", $flags)
                $parsedOk = [bool]$tryParse.Invoke($null, $parseArgs)
                Assert-True $parsedOk "PetInvite.TryParse must accept loopback invite string"
                $invite = $parseArgs[1]
                Assert-NotNull $invite "Parsed PetInvite must not be null"

                $petClient.Connect($invite.WsUri, $invite.Pin, $guestProf)

                $guestsField = $hostType.GetField("guests", $flags)
                $syncField = $hostType.GetField("sync", $flags)
                $guestsDict = $guestsField.GetValue($petHost)
                $syncObj = $syncField.GetValue($petHost)

                $guestCount = 0
                $deadline = [DateTime]::UtcNow.AddSeconds(5)
                while ([DateTime]::UtcNow -lt $deadline) {
                    [System.Threading.Monitor]::Enter($syncObj)
                    try { $guestCount = $guestsDict.Count }
                    finally { [System.Threading.Monitor]::Exit($syncObj) }
                    if ($petClient.Connected -and $guestCount -ge 1) { break }
                    [System.Threading.Thread]::Sleep(40)
                }

                Assert-True $petClient.Connected "PetLinkClient must connect over WebSocket"
                Assert-Equal $guestCount 1 "PetHost must register 1 connected guest after hello handshake"

                $chatJson = Invoke-DeskmateStaticMethod "PetPacket" "EncodeChat" @($guestProf.Id, "Halo dari F39!", 0)
                $stateJson = Invoke-DeskmateStaticMethod "PetPacket" "EncodeState" @($guestProf.Id, [double]0.5, 1, 0, $false)
                $petClient.Send($chatJson)
                $petClient.Send($stateJson)
                $petHost.BroadcastFromHost($stateJson)
                [System.Threading.Thread]::Sleep(150)
                Assert-True $petClient.Connected "PetLinkClient must remain connected after exchanging chat and state frames"
            }
            finally {
                $petClient.Dispose()
                $petHost.Dispose()
            }
        }

        It "Enforces ShelfSafe file validation (blocks UNC paths, .lnk/.url, and missing paths)" {
            $islandType = Get-DeskmateType "Island"
            $shelfSafe = $islandType.GetMethod("ShelfSafe", $flags)
            Assert-NotNull $shelfSafe "Island.ShelfSafe must exist"

            $goodFile = [string](Join-Path $env:DESKMATE_DIR "shelf-valid.txt")
            $lnkFile = [string](Join-Path $env:DESKMATE_DIR "shelf-shortcut.lnk")
            $missingFile = [string](Join-Path $env:DESKMATE_DIR "does-not-exist.txt")
            Set-Content -LiteralPath $goodFile -Value "ok" -Encoding UTF8
            Set-Content -LiteralPath $lnkFile -Value "stub" -Encoding UTF8

            Assert-True ([bool]$shelfSafe.Invoke($null, [object[]]@(([string]$goodFile)))) "Existing regular file must pass ShelfSafe"
            Assert-False ([bool]$shelfSafe.Invoke($null, [object[]]@(([string]$lnkFile)))) "Shortcut .lnk file must be rejected by ShelfSafe"
            Assert-False ([bool]$shelfSafe.Invoke($null, [object[]]@("\\server\share\leak.txt"))) "UNC path must be rejected by ShelfSafe"
            Assert-False ([bool]$shelfSafe.Invoke($null, [object[]]@(([string]$missingFile)))) "Missing file must be rejected by ShelfSafe"
        }

        It "Strips browser partial download extensions in Island.FinalName" {
            $islandType = Get-DeskmateType "Island"
            $finalName = $islandType.GetMethod("FinalName", $flags)
            Assert-NotNull $finalName "Island.FinalName must exist"

            Assert-Equal ($finalName.Invoke($null, @("C:\Downloads\setup.exe.crdownload"))) "C:\Downloads\setup.exe" "Must strip .crdownload"
            Assert-Equal ($finalName.Invoke($null, @("C:\Downloads\archive.zip.part"))) "C:\Downloads\archive.zip" "Must strip .part"
            Assert-Null ($finalName.Invoke($null, @("C:\Downloads\notes.txt"))) "Non-partial extension must return null"
        }

        It "Instantiates DriveCard and handles empty WM_DEVICECHANGE safely" {
            $islandType = Get-DeskmateType "Island"
            $driveCardType = $islandType.GetNestedType("DriveCard", $flags)
            Assert-NotNull $driveCardType "Island+DriveCard must resolve"

            $card = [Activator]::CreateInstance($driveCardType)
            $card.Letter = [char]'E'
            $card.Label = "Backup"
            Assert-Equal ([string]$card.Letter) "E" "DriveCard.Letter must hold drive letter"

            $isle = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
            $onDevChange = $islandType.GetMethod("OnDeviceChange", $flags)
            $onDevChange.Invoke($isle, @([IntPtr]0x8000, [IntPtr]::Zero))
            Assert-True $true "OnDeviceChange with lParam=Zero must return safely"
        }
    }
}
finally {
    if ($script:f39OwnedTmp) {
        $env:DESKMATE_DIR = $script:f39PrevDir
        $env:DESKMATE_HOME = $script:f39PrevHome
        $env:DESKMATE_DOCK_DRY = $script:f39PrevDry
        Remove-Item -LiteralPath $script:f39OwnedTmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
