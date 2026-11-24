# test_f36_dotnet10_core_contracts.ps1
# Tier 1 Feature Tests (F2, F17): .NET 10 Core JSON, NotifAppRules, ProcessPath, Update.AssetMax Contracts

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
$script:f36OwnedTmp = $null
$script:f36PrevDir = $env:DESKMATE_DIR
$script:f36PrevHome = $env:DESKMATE_HOME
$script:f36PrevDry = $env:DESKMATE_DOCK_DRY
if ([string]::IsNullOrEmpty($env:DESKMATE_DIR)) {
    $script:f36OwnedTmp = Join-Path ([IO.Path]::GetTempPath()) ("deskmate-f36-" + [Guid]::NewGuid().ToString("N"))
    $f36Home = Join-Path $script:f36OwnedTmp "home"
    New-Item -ItemType Directory -Path $script:f36OwnedTmp, $f36Home -Force | Out-Null
    $env:DESKMATE_DIR = $script:f36OwnedTmp
    $env:DESKMATE_HOME = $f36Home
    $env:DESKMATE_DOCK_DRY = "1"
}

. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

try {
    Describe "Tier 1 Feature F36: Core JSON, NotifAppRules, ProcessPath & Update Contracts" {
        $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'

        It "Round-trips nested objects, arrays, numbers, booleans, unicode, and null via Json.Parse / Json.Encode" {
            $payload = New-Object 'System.Collections.Generic.Dictionary[string, object]'
            $payload.Add("name", [string]"Deskmate • Uji")
            $payload.Add("count", [int]42)
            $payload.Add("big", [long]9876543210)
            $payload.Add("ratio", [double]3.25)
            $payload.Add("ok", $true)
            $payload.Add("empty", $null)
            $nested = New-Object 'System.Collections.Generic.Dictionary[string, object]'
            $nested.Add("k", [string]"v")
            $payload.Add("nested", $nested.psobject.BaseObject)
            $items = New-Object object[] 3
            $items[0] = [string]"a"
            $items[1] = [int]2
            $items[2] = $false
            $payload.Add("items", $items)

            $encoded = Invoke-DeskmateStaticMethod "Json" "Encode" @($payload.psobject.BaseObject)
            Assert-NotNull $encoded "Json.Encode must return JSON text"
            Assert-True ($encoded.Contains("Deskmate")) "Encoded JSON must preserve string content"

            $parsed = Invoke-DeskmateStaticMethod "Json" "Parse" @($encoded)
            Assert-NotNull $parsed "Json.Parse must decode encoded JSON"
            Assert-Equal $parsed["name"] "Deskmate • Uji" "Unicode string round-trip must match"
            Assert-Equal ([int]$parsed["count"]) 42 "Int round-trip must match"
            Assert-Equal ([long]$parsed["big"]) 9876543210 "Long round-trip must match"
            Assert-InRange ([double]$parsed["ratio"]) 3.249 3.251 "Floating-point round-trip must match"
            Assert-True ([bool]$parsed["ok"]) "Boolean true must round-trip"
            Assert-Null $parsed["empty"] "Null field must round-trip as null"
            Assert-Equal $parsed["nested"]["k"] "v" "Nested dictionary value must round-trip"
            Assert-Equal $parsed["items"].Length 3 "Array length must round-trip"
            Assert-Equal $parsed["items"][0] "a" "Array item 0 must match"
            Assert-Equal ([int]$parsed["items"][1]) 2 "Array item 1 must match"
            Assert-False ([bool]$parsed["items"][2]) "Array item 2 must match"

            Assert-Null (Invoke-DeskmateStaticMethod "Json" "Parse" @("   ")) "Whitespace JSON must return null"

            $threw = $false
            try {
                [void](Invoke-DeskmateStaticMethod "Json" "Parse" @("{bad-json"))
            }
            catch {
                $threw = $true
            }
            Assert-True $threw "Malformed JSON must throw"
        }

        It "Deserializes NotifAppRules case-insensitively from settings" {
            $islandType = Get-DeskmateType "Island"
            $settingsType = Get-DeskmateType "Settings"
            $isle = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)
            $settings = [Activator]::CreateInstance($settingsType)
            $settings.Set("notif.appRules", '{"com.example.chat":{"name":"ChatApp","mode":"inbox","untilUtc":0}}')
            $islandType.GetField("settings", $flags).SetValue($isle, $settings)

            $rulesProp = $islandType.GetProperty("NotifAppRules", $flags)
            Assert-NotNull $rulesProp "Island.NotifAppRules property must exist"
            $rules = $rulesProp.GetValue($isle, $null)
            Assert-NotNull $rules "NotifAppRules dictionary must resolve"
            Assert-True ($rules.ContainsKey("COM.EXAMPLE.CHAT")) "Rule key lookup must be case-insensitive"
            $rule = $rules["com.example.chat"]
            Assert-Equal $rule.Name "ChatApp" "NotifAppRule.Name must deserialize case-insensitively"
            Assert-Equal $rule.Mode "inbox" "NotifAppRule.Mode must deserialize case-insensitively"
            Assert-Equal ([long]$rule.UntilUtc) 0 "NotifAppRule.UntilUtc must deserialize"
        }

        It "Resolves IngatMcp.ConfigJson() with non-empty command and --mcp arg" {
            $cfgJson = Invoke-DeskmateStaticMethod "IngatMcp" "ConfigJson" @()
            Assert-NotNull $cfgJson "IngatMcp.ConfigJson() must return JSON"
            $parsed = Invoke-DeskmateStaticMethod "Json" "Parse" @($cfgJson)
            $deskmate = $parsed["mcpServers"]["deskmate"]
            Assert-NotNull $deskmate "mcpServers.deskmate must exist"
            Assert-Equal $deskmate["args"][0] "--mcp" "First MCP arg must be --mcp"
            $cmd = [string]$deskmate["command"]
            Assert-True (-not [string]::IsNullOrWhiteSpace($cmd)) "MCP command path must be non-empty"
            if ([Environment]::Version.Major -ge 8) {
                Assert-Equal $cmd ([Environment]::ProcessPath) ".NET 10 ConfigJson command must resolve via Environment.ProcessPath"
            }
        }

        It "Verifies Update.AssetMax and Feeds.SelfPath contracts" {
            $assetMax = [long](Get-DeskmateStaticField "Island" "AssetMax")
            if ([Environment]::Version.Major -ge 8) {
                Assert-True ($assetMax -ge (200L * 1024L * 1024L)) ".NET 10 Update.AssetMax must accommodate self-contained exe (>= 200 MB), got $assetMax"
                $islandType = Get-DeskmateType "Island"
                $selfPathField = $islandType.GetField("SelfPath", $flags)
                if ($selfPathField) {
                    $selfPath = [string]$selfPathField.GetValue($null)
                    Assert-Equal $selfPath ([Environment]::ProcessPath) "Feeds.SelfPath must resolve via Environment.ProcessPath"
                }
            }
            else {
                Assert-True ($assetMax -gt 0) "Baseline Update.AssetMax must be positive, got $assetMax"
            }
        }
    }
}
finally {
    if ($script:f36OwnedTmp) {
        $env:DESKMATE_DIR = $script:f36PrevDir
        $env:DESKMATE_HOME = $script:f36PrevHome
        $env:DESKMATE_DOCK_DRY = $script:f36PrevDry
        Remove-Item -LiteralPath $script:f36OwnedTmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
