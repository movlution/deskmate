# test_f21_discord_notif.ps1
# Tier 1 tests for Feature 21: Discord toast click routing.
# Discord's Electron toasts have no launch attribute; the stored tag is the
# Electron notification id that its COM activator expects in type=click&tag=.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 1 Feature Suite: Feature 21 - Discord notification activation" {

    $flags = [System.Reflection.BindingFlags]'NonPublic,Static'
    $notifOpenType = Get-DeskmateType "NotifOpen"
    $isDiscord = $notifOpenType.GetMethod("IsDiscord", $flags)
    $discordArgs = $notifOpenType.GetMethod("DiscordArgs", $flags)
    $fromClsidServer = $notifOpenType.GetMethod("FromClsidServer", $flags)

    It "1. Recognizes Discord stable, PTB, and Canary app ids only" {
        Assert-True ($isDiscord.Invoke($null, @("com.squirrel.Discord.Discord"))) "Discord stable must be recognized"
        Assert-True ($isDiscord.Invoke($null, @("com.squirrel.DiscordPTB.DiscordPTB"))) "Discord PTB must be recognized"
        Assert-True ($isDiscord.Invoke($null, @("com.squirrel.DiscordCanary.DiscordCanary"))) "Discord Canary must be recognized"
        Assert-False ($isDiscord.Invoke($null, @("com.squirrel.Slack.Slack"))) "Slack must not be treated as Discord"
        Assert-False ($isDiscord.Invoke($null, @(""))) "An empty app id must not be treated as Discord"
        Assert-False ($isDiscord.Invoke($null, @($null))) "A null app id must not be treated as Discord"
    }

    It "2. Builds Electron's exact click activation arguments from the toast tag" {
        $tag = "a60cc0be-1e18-4230-a031-4b5fa8d8e30b"
        $built = $discordArgs.Invoke($null, @($tag))
        Assert-Equal "type=click&tag=$tag" $built "Discord clicks must use type=click plus the Electron notification tag"
    }

    It "3. Rejects empty, oversized, or field-injecting tags" {
        Assert-Null ($discordArgs.Invoke($null, @(""))) "Empty tag must not produce activation args"
        Assert-Null ($discordArgs.Invoke($null, @($null))) "Null tag must not produce activation args"
        Assert-Null ($discordArgs.Invoke($null, @("tag&type=action"))) "Ampersand must not inject another field"
        Assert-Null ($discordArgs.Invoke($null, @("tag=value"))) "Equals must not inject another field"
        Assert-Null ($discordArgs.Invoke($null, @("tag with spaces"))) "Whitespace must not reach the activator"
        Assert-Null ($discordArgs.Invoke($null, @(("a" * 129)))) "Oversized tags must be rejected"
    }

    It "4. Does not guess an activator for an unrelated app id" {
        $none = $fromClsidServer.Invoke($null, @("com.example.DeskmateNoSuchToastApp"))
        Assert-Null $none "An unregistered app must not resolve to a CLSID"
    }
}
