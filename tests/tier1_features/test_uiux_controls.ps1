. (Join-Path $PSScriptRoot '..\common\TestHarness.ps1')
. (Join-Path $PSScriptRoot '..\common\DeskmateLoader.ps1')
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
function New-Seg {
    $s = New-DeskmateControl 'KitSegmented'
    $s.Items = [string[]]@('Small', 'Medium', 'Large')
    $s.Size = New-Object Drawing.Size(300, 36)
    return $s
}
Describe 'Shared segmented control interaction' {
    It 'Right-click preserves the current choice' {
        $s = New-Seg
        try {
            $e = New-Object Windows.Forms.MouseEventArgs([Windows.Forms.MouseButtons]::Right, 1, 250, 18, 0)
            $s.GetType().GetMethod('OnMouseUp', $flags).Invoke($s, @($e.PSObject.BaseObject))
            Assert-Equal $s.Selected 0 'Context click must not change settings'
        } finally { $s.Dispose() }
    }
    It 'Left-click selects the target segment' {
        $s = New-Seg
        try {
            $e = New-Object Windows.Forms.MouseEventArgs([Windows.Forms.MouseButtons]::Left, 1, 250, 18, 0)
            $s.GetType().GetMethod('OnMouseUp', $flags).Invoke($s, @($e.PSObject.BaseObject))
            Assert-Equal $s.Selected 2 'Left click selects Large'
        } finally { $s.Dispose() }
    }
    It 'Disabled accessibility actions cannot change a setting' {
        $s = New-Seg
        try {
            $s.Enabled = $false
            $child = $s.AccessibilityObject.GetChild(2)
            $child.DoDefaultAction()
            Assert-Equal $s.Selected 0 'Disabled selection remains unchanged'
            Assert-True (($child.State -band [Windows.Forms.AccessibleStates]::Unavailable) -ne 0) 'Reader reports unavailable'
        } finally { $s.Dispose() }
    }
    It 'Accessibility enumeration handles out-of-range indices' {
        $s = New-Seg
        try {
            Assert-Null ($s.AccessibilityObject.GetChild(-1)) 'Negative child is absent'
            Assert-Null ($s.AccessibilityObject.GetChild(3)) 'Past-end child is absent'
            $s.Items = [string[]]@()
            Assert-Null ($s.AccessibilityObject.GetSelected()) 'Empty control has no selected child'
        } finally { $s.Dispose() }
    }
    It 'Home and End select endpoints and consume navigation keys' {
        $s = New-Seg
        try {
            foreach ($pair in @(@([Windows.Forms.Keys]::End, 2), @([Windows.Forms.Keys]::Home, 0))) {
                $e = New-Object Windows.Forms.KeyEventArgs($pair[0])
                $s.GetType().GetMethod('OnKeyDown', $flags).Invoke($s, @($e.PSObject.BaseObject))
                Assert-Equal $s.Selected $pair[1] 'Endpoint selected'
                Assert-True $e.SuppressKeyPress 'No navigation beep'
            }
        } finally { $s.Dispose() }
    }
    It 'Tour motion reaches its destination from both directions' {
        $t = Get-DeskmateType 'TourCard'
        $method = $t.GetMethod('GlideDelta', [Reflection.BindingFlags]'Static,NonPublic')
        foreach ($start in @(-200, -3, -2, 0, 2, 3, 200)) {
            $distance = $start
            for ($i = 0; $i -lt 100 -and $distance -ne 0; $i++) {
                $distance -= [int]$method.Invoke($null, @([int]$distance))
            }
            Assert-Equal $distance 0 'Animation converges without an endless timer'
        }
    }
    It 'Embedded app icon loads at Windows shell sizes' {
        foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128)) {
            $icon = Invoke-DeskmateStaticMethod 'KitWindow' 'LoadAppIcon' @([Drawing.Size]::new($size, $size))
            try { Assert-Equal $icon.Width $size 'Requested icon resolution available' }
            finally { $icon.Dispose() }
        }
    }
}
