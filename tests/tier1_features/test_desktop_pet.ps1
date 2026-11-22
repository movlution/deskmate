# test_desktop_pet.ps1
# Tier 1 Feature Tests: desktop pet stages 1-2 (PetBrain behaviour, PetArt atlas, dock lane)
# Windowless: the brain is stepped with simulated time and the atlas is read as pixels.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 1 Feature Suite: Desktop Pet" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $brainType = Get-DeskmateType "PetBrain"
    $artType = Get-DeskmateType "PetArt"
    $winType = Get-DeskmateType "PetWindow"
    $modeType = Get-DeskmateType "PetMode"

    function New-Brain([int]$seed, [double]$left, [double]$right) {
        $b = [Activator]::CreateInstance($brainType, $flags, $null, @($seed), $null)
        $brainType.GetMethod("Bounds").Invoke($b, @($left, $right, $true)) | Out-Null
        return $b
    }

    It "1. Atlas has 10 opaque cells per facing, and the left row mirrors the right row" {
        $f = 3; $cell = 16 * $f; $w = $cell * 10
        $px = [int[]]$artType.GetMethod("Atlas").Invoke($null, @($f))
        Assert-Equal $px.Length ($w * $cell * 2) "Atlas must be 10 cells wide and 2 rows tall"
        for ($c = 0; $c -lt 10; $c++) {
            $opaque = 0; $mirrorOk = $true
            for ($y = 0; $y -lt $cell; $y++) {
                for ($x = 0; $x -lt $cell; $x++) {
                    $a = $px[$y * $w + $c * $cell + $x]
                    $b = $px[($y + $cell) * $w + $c * $cell + ($cell - 1 - $x)]
                    if ($a -ne 0) { $opaque++ }
                    if ($a -ne $b) { $mirrorOk = $false }
                }
            }
            Assert-True ($opaque -gt 200) "Cell $c must draw a visible sprite (opaque px: $opaque)"
            Assert-True $mirrorOk "Cell $c left-facing row must mirror the right-facing row"
        }
    }

    It "2. Ten simulated minutes stay in bounds, never exceed walking speed, and visit every mode" {
        $b = New-Brain 7 0 1000
        $speed = $brainType.GetField("Speed").GetValue($b)
        $seen = @{}
        $dt = 1.0 / 60
        $prev = $brainType.GetField("X").GetValue($b)
        $maxStep = 0.0
        for ($i = 0; $i -lt 36000; $i++) {
            $brainType.GetMethod("Step").Invoke($b, @($dt)) | Out-Null
            $x = $brainType.GetField("X").GetValue($b)
            Assert-True ($x -ge 0 -and $x -le 1000) "X must stay inside [0, 1000] (step $i, x=$x)"
            $step = [Math]::Abs($x - $prev)
            if ($step -gt $maxStep) { $maxStep = $step }
            $prev = $x
            $seen[[string]$brainType.GetField("Mode").GetValue($b)] = $true
        }
        Assert-True ($maxStep -le $speed * $dt + 1e-9) "One frame must not move more than speed*dt (max $maxStep)"
        foreach ($m in "Idle", "Walk", "Sit", "Sleep") { Assert-True $seen.ContainsKey($m) "Mode $m must occur in 10 minutes" }
    }

    It "3. A stalled frame during a walk does not teleport the pet" {
        $b = New-Brain 3 0 2000
        $guard = 0
        while (-not $brainType.GetProperty("Moving").GetValue($b) -and $guard -lt 20000) {
            $brainType.GetMethod("Step").Invoke($b, @(0.05)) | Out-Null; $guard++
        }
        Assert-True $brainType.GetProperty("Moving").GetValue($b) "Brain must start walking eventually"
        $x0 = $brainType.GetField("X").GetValue($b)
        $brainType.GetMethod("Step").Invoke($b, @(5.0)) | Out-Null
        $moved = [Math]::Abs($brainType.GetField("X").GetValue($b) - $x0)
        Assert-True ($moved -le 36 * 0.1 + 1e-9) "A 5 s stall must move at most 0.1 s worth (moved $moved px)"
    }

    It "4. Reduced motion keeps the pet in place" {
        $b = New-Brain 11 0 1000
        $brainType.GetField("NoWalk").SetValue($b, $true)
        $x0 = $brainType.GetField("X").GetValue($b)
        for ($i = 0; $i -lt 6000; $i++) { $brainType.GetMethod("Step").Invoke($b, @(0.1)) | Out-Null }
        Assert-Equal $brainType.GetField("X").GetValue($b) $x0 "X must not change with NoWalk"
    }

    It "5. Shrinking the bounds clamps the pet inside the new range" {
        $b = New-Brain 5 0 1000
        $brainType.GetField("X").SetValue($b, [double]900)
        $brainType.GetMethod("Bounds").Invoke($b, @([double]0, [double]400, $false)) | Out-Null
        Assert-Equal $brainType.GetField("X").GetValue($b) ([double]400) "X must clamp to the new right edge"
    }

    It "6. Standing still asks to sleep until the next picture change, never for zero time" {
        $b = New-Brain 9 0 1000
        $wait = $brainType.GetMethod("NextChangeIn").Invoke($b, $null)
        Assert-True ($wait -gt 0 -and $wait -le 1.0) "Idle wait must be in (0, 1] s (got $wait)"
    }

    It "7. A click wakes a sleeping pet and one hop rises, peaks at HopHeight, and lands" {
        $b = New-Brain 13 0 1000
        $brainType.GetMethod("Enter", $flags).Invoke($b, @([Enum]::Parse($modeType, "Sleep"))) | Out-Null
        $brainType.GetMethod("Hop").Invoke($b, $null) | Out-Null
        Assert-Equal ([string]$brainType.GetField("Mode").GetValue($b)) "Idle" "A click must wake the pet"
        Assert-True $brainType.GetProperty("Moving").GetValue($b) "A hop must run on vsync frames"
        $hopTime = $brainType.GetField("HopTime").GetValue($null)
        $height = $brainType.GetField("HopHeight").GetValue($null)
        $peak = 0.0; $dt = 1.0 / 60
        for ($t = 0; $t -lt $hopTime + 0.1; $t += $dt) {
            $brainType.GetMethod("Step").Invoke($b, @($dt)) | Out-Null
            $lift = $brainType.GetProperty("Lift").GetValue($b)
            Assert-True ($lift -ge 0 -and $lift -le $height + 1e-9) "Lift must stay in [0, HopHeight] (got $lift)"
            if ($lift -gt $peak) { $peak = $lift }
        }
        Assert-True ($peak -gt $height * 0.95) "Hop must reach close to HopHeight (peak $peak)"
        Assert-Equal $brainType.GetProperty("Lift").GetValue($b) ([double]0) "The pet must land"
        Assert-True (-not $brainType.GetProperty("Moving").GetValue($b)) "After landing an idle pet stops vsync frames"
    }

    It "8. Reduced motion: a click still wakes the pet but never lifts it" {
        $b = New-Brain 17 0 1000
        $brainType.GetField("NoWalk").SetValue($b, $true)
        $brainType.GetMethod("Enter", $flags).Invoke($b, @([Enum]::Parse($modeType, "Sleep"))) | Out-Null
        $brainType.GetMethod("Hop").Invoke($b, $null) | Out-Null
        Assert-Equal ([string]$brainType.GetField("Mode").GetValue($b)) "Idle" "Click must wake the pet"
        $brainType.GetMethod("Step").Invoke($b, @(0.2)) | Out-Null
        Assert-Equal $brainType.GetProperty("Lift").GetValue($b) ([double]0) "No hop with reduced motion"
    }

    It "9. Hover stops a walk and faces the pointer; hover never wakes a sleeper" {
        $b = New-Brain 3 0 2000
        $guard = 0
        while (-not $brainType.GetProperty("Moving").GetValue($b) -and $guard -lt 20000) {
            $brainType.GetMethod("Step").Invoke($b, @(0.05)) | Out-Null; $guard++
        }
        $dir = $brainType.GetField("Dir").GetValue($b)
        $changed = $brainType.GetMethod("Notice").Invoke($b, @([int](-$dir * 30)))
        Assert-True $changed "Hover on a walking pet must change the picture"
        Assert-Equal ([string]$brainType.GetField("Mode").GetValue($b)) "Idle" "Hover must stop the walk"
        Assert-Equal $brainType.GetField("Dir").GetValue($b) (-$dir) "The pet must turn to the pointer"
        $brainType.GetMethod("Enter", $flags).Invoke($b, @([Enum]::Parse($modeType, "Sleep"))) | Out-Null
        Assert-True (-not $brainType.GetMethod("Notice").Invoke($b, @([int]50))) "Hover must not wake a sleeper"
        Assert-Equal ([string]$brainType.GetField("Mode").GetValue($b)) "Sleep" "Sleeper keeps sleeping"
    }

    It "10. Lane: work area bottom without a dock, the dock's top edge with one, centred when too narrow" {
        $lane = $winType.GetMethod("Lane")
        # ::new, not New-Object: a PSObject wrapper does not bind to a Rectangle parameter in Invoke
        $work = [System.Drawing.Rectangle]::new(0, 0, 1920, 1080)
        $none = $lane.Invoke($null, @($work, [System.Drawing.Rectangle]::Empty, 48))
        Assert-Equal $none.Y 1080 "Without a dock the ground is the work area bottom"
        Assert-Equal $none.X 0 "Without a dock the lane starts at the work area left"
        Assert-Equal $none.Right (1920 - 48) "The sprite must fit inside the work area"
        $floor = [System.Drawing.Rectangle]::new(620, 1012, 680, 1)
        $on = $lane.Invoke($null, @($work, $floor, 48))
        Assert-Equal $on.Y 1012 "On a dock the ground is its top edge"
        Assert-Equal $on.X 620 "Lane starts at the flat top's left"
        Assert-Equal $on.Right (620 + 680 - 48) "The sprite must stay on the flat top"
        $narrow = $lane.Invoke($null, @($work, [System.Drawing.Rectangle]::new(900, 1012, 20, 1), 48))
        Assert-Equal $narrow.Width 0 "A floor narrower than the sprite leaves no room to walk"
        Assert-Equal ($narrow.X + 24) 910 "The sprite centres on a narrow floor"
    }
}
