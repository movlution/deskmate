# test_f11_menu_look.ps1
# Feature 11: MenuLook Submenu & Disabled Contrast
# Verifies MenuLook submenu arrow and disabled item contrast >= WCAG AA 4.5:1 in light mode.

$commonDir = Join-Path $PSScriptRoot "..\common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Feature 11: MenuLook Submenu & Disabled Contrast" {
    $menuLookType = Get-DeskmateType "MenuLook"
    $flags = [System.Reflection.BindingFlags]'NonPublic,Static,Public'

    BeforeEach {
        Reset-DeskmateTheme -Light $true
    }

    It "Verifies MenuLook type exists and inherits from ToolStripProfessionalRenderer" {
        Assert-NotNull $menuLookType "MenuLook type must exist"
        Assert-True ([System.Windows.Forms.ToolStripProfessionalRenderer].IsAssignableFrom($menuLookType)) "MenuLook must inherit from ToolStripProfessionalRenderer"
    }

    It "Verifies MenuLook Text contrast against MenuLook Bg in Light Mode achieves >= 4.5:1" {
        $propBg = $menuLookType.GetProperty("Bg", $flags)
        $propText = $menuLookType.GetProperty("Text", $flags)

        $bg = $propBg.GetValue($null, $null)
        $text = $propText.GetValue($null, $null)

        $ratio = Assert-Contrast $text $bg 4.5 "MenuLook Text vs Bg"
        Assert-True ($ratio -ge 4.5) "Normal menu text contrast must achieve >= 4.5:1"
    }

    It "Verifies MenuLook Sub contrast against MenuLook Bg in Light Mode achieves >= 4.5:1 (WCAG AA)" {
        $propBg = $menuLookType.GetProperty("Bg", $flags)
        $propSub = $menuLookType.GetProperty("Sub", $flags)

        $bg = $propBg.GetValue($null, $null)
        $sub = $propSub.GetValue($null, $null)

        $ratio = Assert-Contrast $sub $bg 4.5 "MenuLook Sub vs Bg (Submenu arrows & disabled items)"
        Assert-True ($ratio -ge 4.5) "Sub color contrast must achieve >= 4.5:1 (actual: ${ratio}:1)"
    }

    It "Verifies MenuLook disabled item and arrow colors derive from Sub property" {
        $propSub = $menuLookType.GetProperty("Sub", $flags)
        Assert-NotNull $propSub "Sub property must exist on MenuLook"

        $subColor = $propSub.GetValue($null, $null)
        Assert-NotNull $subColor "Sub color must not be null"
        Assert-Equal $subColor.A 255 "Sub color must be fully opaque"
    }

    It "Verifies MenuLook.Apply configures ToolStripDropDownMenu and child submenus" {
        $applyMethod = $menuLookType.GetMethod("Apply", [System.Reflection.BindingFlags]'Public,Static')
        Assert-NotNull $applyMethod "MenuLook.Apply method must exist"

        $menu = New-Object System.Windows.Forms.ToolStripDropDownMenu
        $item1 = New-Object System.Windows.Forms.ToolStripMenuItem("File")
        $subItem = New-Object System.Windows.Forms.ToolStripMenuItem("Recent")
        [void]$item1.DropDownItems.Add($subItem)
        [void]$menu.Items.Add($item1)

        $applyMethod.Invoke($null, [object[]]@([System.Windows.Forms.ToolStripDropDownMenu]$menu))

        Assert-NotNull $menu.Renderer "Menu renderer must be set"
        Assert-True ($menu.Renderer.GetType() -eq $menuLookType) "Menu renderer must be MenuLook instance"

        $menu.Dispose()
    }

    It "Keeps Island context menus above the panel during fullscreen checks" {
        $instance = [System.Reflection.BindingFlags]'Public,NonPublic,Instance'
        $islandType = Get-DeskmateType "Island"
        $nativeType = Get-DeskmateType "Native"
        $getWindow = $nativeType.GetMethod("GetWindow")
        $checkFullscreen = $islandType.GetMethod("CheckFullscreen", $instance)
        $showMenu = $islandType.GetMethod("ShowIslandMenu", $instance)
        $isle = $null
        $main = [System.Windows.Forms.ContextMenuStrip]::new()
        $app = $null
        try {
            $isle = [Activator]::CreateInstance($islandType, $true)
            [void]$nativeType.GetMethod("ShowWindow").Invoke($null, @($isle.Handle, 4))
            $islandType.GetField("windowShown", $instance).SetValue($isle, $true)
            [void]$main.Items.Add("Deskmate")
            $islandType.GetField("menu", $instance).SetValue($isle, $main)
            $app = $islandType.GetMethod("BuildNotifAppMenu", $instance).Invoke(
                $isle, @("test.app", "Test app", $null, $true))

            foreach ($popup in @($main, $app)) {
                $showMenu.Invoke($isle, @($popup))
                $checkFullscreen.Invoke($isle, $null)
                Assert-True $popup.Visible "Context menu stays open during the check"
                $above = $getWindow.Invoke($null, @($isle.Handle, [uint32]3))
                while ($above -ne [IntPtr]::Zero -and $above -ne $popup.Handle) {
                    $above = $getWindow.Invoke($null, @($above, [uint32]3))
                }
                Assert-Equal $above $popup.Handle "Menu must be above the layered Island window"
            }
            Assert-True (!$main.Visible) "Opening the app menu closes the general menu"
        }
        finally {
            if ($isle) { $isle.Dispose() }
            if ($app) { $app.Dispose() }
            $main.Dispose()
        }
    }
}
