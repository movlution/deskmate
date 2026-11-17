# test_s08_dialog_stack.ps1
# Tier 4 scenario S08: stacked dialogs and live theme broadcast.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")
Add-Type -AssemblyName WindowsBase, PresentationCore, PresentationFramework, WindowsFormsIntegration

Describe "Tier 4 Scenario: S08 Modal Dialog Stack" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'

    BeforeEach {
        Reset-DeskmateTheme -Light $false
        $langType = Get-DeskmateType "Lang"
        if ($langType) { $langType.GetField("English", $flags).SetValue($null, $false) }
    }

    It "broadcasts a theme switch into stacked KitAsk and editor windows without recreating controls" {
        $kitWindowType = Get-DeskmateType "KitWindow"
        $kitAskType = Get-DeskmateType "KitAsk"
        $taskEditorType = Get-DeskmateType "TaskEditor"
        $todoType = Get-DeskmateType "TodoItem"
        $prefsType = Get-DeskmateType "PrefsWindow"
        Assert-True ($kitWindowType.IsAssignableFrom($prefsType)) "PrefsWindow must participate in the same KitWindow broadcast registry"

        $ctor = $kitAskType.GetConstructor($flags, $null, [Type[]]@([string], [bool], [string], [string], [string], [string]), $null)
        Assert-NotNull $ctor "KitAsk must expose the localized six-argument constructor"
        $first = [Activator]::CreateInstance($kitWindowType)
        $second = [Activator]::CreateInstance($kitWindowType)
        $ask = $ctor.Invoke(@("Buang perubahan?", $true, "Buang", "Batal", $null, "Tutup"))
        $item = [Activator]::CreateInstance($todoType, $true)
        $editor = [Activator]::CreateInstance($taskEditorType, $flags, $null, @($item), $null)
        $button = [Activator]::CreateInstance((Get-DeskmateType "KitButton"))
        try {
            $button.Text = "Tetap"
            $second.Controls.Add($button)
            $askYes = $kitAskType.GetField("yes", $flags).GetValue($ask)
            $askNo = $kitAskType.GetField("no", $flags).GetValue($ask)
            Assert-Equal "Buang" $askYes.Text "KitAsk confirm label must come from Lang.T"
            Assert-Equal "Batal" $askNo.Text "KitAsk dismiss label must come from Lang.T"
            $askText = $ask.Text

            Reset-DeskmateTheme -Light $true
            $expectedWindow = Get-DeskmateStaticProperty "KitInk" "Window"
            $expectedText = Get-DeskmateStaticProperty "KitInk" "Text"
            Invoke-DeskmateStaticMethod "KitWindow" "BroadcastThemeChange" @()

            Assert-True (-not $first.IsDisposed -and -not $second.IsDisposed -and -not $ask.IsDisposed -and -not $editor.IsDisposed) "Broadcast must not recreate or dispose a stacked dialog"
            Assert-True ([object]::ReferenceEquals($second.Controls[0], $button)) "Broadcast must not replace child controls"
            foreach ($win in @($first, $second, $ask, $editor)) {
                Assert-Equal $expectedWindow.ToArgb() $win.BackColor.ToArgb() "Every stacked dialog must adopt the new window token"
                Assert-Equal $expectedText.ToArgb() $win.ForeColor.ToArgb() "Every stacked dialog must adopt the new text token"
            }
            Assert-Equal "Tetap" $button.Text "Broadcast must not rewrite local labels"
            Assert-Equal $askText $ask.Text "Broadcast must not rewrite the dialog title"
            Assert-Equal "Buang" $askYes.Text "Broadcast must preserve localized action text"
        }
        finally {
            $first.Dispose()
            $second.Dispose()
            $ask.Dispose()
            $editor.Dispose()
        }
    }
}
