# test_desktop_pet.ps1
# Tier 1 Feature Tests: desktop pet (PetBrain behaviour, PetSheet art and timing, dock lane, Aseprite import)
# Windowless: the brain is stepped with simulated time, atlases are read as pixels, and
# Aseprite exports are written to a temporary folder that is removed afterwards.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 1 Feature Suite: Desktop Pet" {

    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $brainType = Get-DeskmateType "PetBrain"
    $sheetType = Get-DeskmateType "PetSheet"
    $winType = Get-DeskmateType "PetWindow"
    $modeType = Get-DeskmateType "PetMode"

    function New-Brain([int]$seed, [double]$left, [double]$right) {
        $b = [Activator]::CreateInstance($brainType, $flags, $null, @($seed), $null)
        $brainType.GetMethod("Bounds").Invoke($b, @($left, $right, $true)) | Out-Null
        return $b
    }

    function Get-LoadError([string]$dir) {
        try { $sheetType.GetMethod("Load").Invoke($null, @($dir)) | Out-Null; return $null }
        catch {
            $e = $_.Exception
            while ($e -ne $null -and -not ($e -is [System.IO.InvalidDataException])) { $e = $e.InnerException }
            if ($e -eq $null) { return "unexpected: " + $_.Exception.ToString() }
            return $e.Message
        }
    }

    It "1. Built-in cat and dog: 10 opaque cells per facing, left row mirrors right, paws on row 14" {
        foreach ($kind in "cat", "dog") {
        $sheet = $sheetType.GetMethod("BuiltIn").Invoke($null, @([string]$kind, "orange"))
        Assert-Equal $sheet.Count 10 "$kind must have 10 frames"
        Assert-Equal $sheet.Base 14 "$kind paws (outline row) must be the ground row"
        $f = 2; $cell = 16 * $f; $w = $cell * 10
        $px = [int[]]$sheetType.GetMethod("Atlas").Invoke($sheet, @($f))
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
            Assert-True ($opaque -gt 100) "$kind cell $c must draw a visible sprite (opaque px: $opaque)"
            Assert-True $mirrorOk "$kind cell $c left-facing row must mirror the right-facing row"
        }
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

    It "6. Sheet timing: walk at 8 fps follows pace, idle waits for its next frame, never zero" {
        $sheet = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "orange"))
        $walk = [Enum]::Parse($modeType, "Walk"); $idle = [Enum]::Parse($modeType, "Idle")
        Assert-Equal $sheet.Frame($walk, 0.0, 1.0) 2 "Walk starts on cell 2"
        Assert-Equal $sheet.Frame($walk, 0.13, 1.0) 3 "0.13 s into a walk shows cell 3"
        Assert-Equal $sheet.Frame($walk, 0.13, 2.0) 4 "Twice the speed plays the cycle twice as fast"
        Assert-Equal $sheet.Frame($walk, 0.5, 1.0) 2 "The walk cycle loops after 0.5 s"
        $wait = $sheet.NextChange($idle, 0.1, 1.0)
        Assert-True ([Math]::Abs($wait - (1 / 1.5 - 0.1)) -lt 1e-9) "Idle waits until its next frame (got $wait)"
        Assert-True ($sheet.NextChange($idle, 1 / 1.5, 1.0) -gt 0) "The wait is never zero"
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

    It "11. Size: whole art px per screen px from the chosen height and DPI" {
        $cat = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "orange"))
        $factor = $winType.GetMethod("Factor")
        Assert-Equal $factor.Invoke($null, @($cat, 48, [single]1)) 3 "Medium at 100% is 3x"
        Assert-Equal $factor.Invoke($null, @($cat, 32, [single]1)) 2 "Small at 100% is 2x"
        Assert-Equal $factor.Invoke($null, @($cat, 64, [single]2)) 8 "Large at 200% is 8x"
        Assert-Equal $factor.Invoke($null, @($cat, 48, [single]0.2)) 1 "Never below 1x"
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("deskmate-pet-test-" + [Guid]::NewGuid().ToString("N"))

    function Write-Pet([string]$name, [string]$json, [switch]$NoImage) {
        $dir = Join-Path $tmp $name
        New-Item -ItemType Directory -Force $dir | Out-Null
        if (-not $NoImage) {
            # Four 8x8 frames in a row; rows 6-7 clear, so row 5 is the ground. Pixel (1,0) is half transparent.
            $bmp = New-Object System.Drawing.Bitmap 32, 8, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            for ($fr = 0; $fr -lt 4; $fr++) {
                for ($y = 0; $y -lt 6; $y++) { for ($x = 0; $x -lt 8; $x++) { $bmp.SetPixel($fr * 8 + $x, $y, [System.Drawing.Color]::FromArgb(255, 40 * $fr + 20, 90, 160)) } }
            }
            $bmp.SetPixel(1, 0, [System.Drawing.Color]::FromArgb(128, 200, 100, 50))
            $bmp.Save((Join-Path $dir "sheet.png"), [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
        }
        [IO.File]::WriteAllText((Join-Path $dir "pet.json"), $json)
        return $dir
    }

    function Frame-Json([int]$i, [int]$x, [int]$w) {
        return '{"filename":"p ' + $i + '.aseprite","frame":{"x":' + $x + ',"y":0,"w":' + $w + ',"h":8},"sourceSize":{"w":8,"h":8},"spriteSourceSize":{"x":0,"y":0,"w":' + $w + ',"h":8},"duration":100}'
    }

    It "12. Aseprite array export: tags pick poses, pingpong plays back, durations and alpha carry over" {
        try {
            $frames = (0..3 | ForEach-Object { Frame-Json $_ ($_ * 8) 8 }) -join ","
            $json = '{"frames":[' + $frames + '],"meta":{"app":"https://www.aseprite.org/","frameTags":[' +
                '{"name":"idle","from":0,"to":0,"direction":"forward"},{"name":"Walk","from":1,"to":3,"direction":"pingpong"}]}}'
            $dir = Write-Pet "array" $json
            $sheet = $sheetType.GetMethod("Load").Invoke($null, @([string]$dir))
            Assert-Equal $sheet.W 8 "Frame width from sourceSize"
            Assert-Equal $sheet.H 8 "Frame height from sourceSize"
            Assert-Equal $sheet.Count 4 "All four frames load"
            Assert-Equal $sheet.Base 5 "Ground row is the lowest opaque row"
            $walk = [Enum]::Parse($modeType, "Walk")
            Assert-Equal (($sheet.Sequence($walk)) -join ",") "1,2,3,2" "Pingpong 1-3 plays 1,2,3,2"
            Assert-Equal (($sheet.Sequence([Enum]::Parse($modeType, "Idle"))) -join ",") "0" "idle tag"
            Assert-Equal (($sheet.Sequence([Enum]::Parse($modeType, "Sleep"))) -join ",") "0" "Missing sit and sleep borrow idle"
            Assert-Equal $sheet.Frame($walk, 0.25, 1.0) 3 "100 ms per frame: 0.25 s shows the third walk frame"
            Assert-True ([Math]::Abs($sheet.NextChange($walk, 0.25, 1.0) - 0.05) -lt 1e-9) "Next change at the frame boundary"
            $half = $sheet.Art[0][1]
            $expected = [int]((128 -shl 24) -bor ([int](200 * 128 / 255) -shl 16) -bor ([int][Math]::Floor(100 * 128 / 255) -shl 8) -bor [int][Math]::Floor(50 * 128 / 255))
            Assert-Equal $half $expected "Half-transparent pixels are premultiplied"
        } finally { if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp } }
    }

    It "13. Aseprite hash export without tags walks through every frame; trimmed frames keep their place" {
        try {
            $f0 = Frame-Json 0 0 8
            # Trimmed: 4 px wide from x=8, placed 2 px into the 8 px source
            $f1 = '{"frame":{"x":8,"y":0,"w":4,"h":8},"sourceSize":{"w":8,"h":8},"spriteSourceSize":{"x":2,"y":0,"w":4,"h":8},"duration":250}'
            $json = '{"frames":{"a":' + $f0 + ',"b":' + $f1 + '},"meta":{"frameTags":[]}}'
            $dir = Write-Pet "hash" $json
            $sheet = $sheetType.GetMethod("Load").Invoke($null, @([string]$dir))
            $walk = [Enum]::Parse($modeType, "Walk")
            Assert-Equal (($sheet.Sequence($walk)) -join ",") "0,1" "Without tags every frame is the walk"
            Assert-Equal $sheet.Art[1][0] 0 "Left of a trimmed frame stays clear"
            Assert-True ($sheet.Art[1][2] -ne 0) "A trimmed frame is placed at its source offset"
            Assert-Equal $sheet.Frame($walk, 0.2, 1.0) 1 "Frame durations come from the export"
        } finally { if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp } }
    }

    It "14. Broken exports are refused with a reason instead of crashing" {
        try {
            $frames = (0..3 | ForEach-Object { Frame-Json $_ ($_ * 8) 8 }) -join ","
            $noWalk = Write-Pet "nowalk" ('{"frames":[' + $frames + '],"meta":{"frameTags":[{"name":"idle","from":0,"to":1}]}}')
            Assert-Equal (Get-LoadError $noWalk) "butuh tag walk" "Tags without walk"
            $outside = Write-Pet "outside" ('{"frames":[' + (Frame-Json 0 30 8) + ']}')
            Assert-Equal (Get-LoadError $outside) "frame di luar gambar" "Frame past the image edge"
            $badTag = Write-Pet "badtag" ('{"frames":[' + $frames + '],"meta":{"frameTags":[{"name":"walk","from":2,"to":9}]}}')
            Assert-Equal (Get-LoadError $badTag) "frame di luar gambar" "Tag past the last frame"
            $noPng = Write-Pet "nopng" ('{"frames":[' + $frames + ']}') -NoImage
            Assert-Equal (Get-LoadError $noPng) "butuh sheet.png dan pet.json" "Missing image"
            $badJson = Write-Pet "badjson" '{"frames": [ oops'
            Assert-Equal (Get-LoadError $badJson) "pet.json tidak terbaca" "Malformed JSON"
            $notPng = Write-Pet "notpng" ('{"frames":[' + $frames + ']}')
            [IO.File]::WriteAllText((Join-Path $notPng "sheet.png"), "not an image")
            Assert-Equal (Get-LoadError $notPng) "sheet.png tidak terbaca" "Corrupt image"
        } finally { if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp } }
    }

    It "15. Every built-in colour paints a different fur, and unknown colours fall back to orange" {
        $seen = @{}
        foreach ($color in $sheetType.GetField("Colors").GetValue($null)) {
            $sheet = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", [string]$color))
            $fur = $sheet.Art[0][8 * 16 + 6]
            Assert-True (-not $seen.ContainsKey($fur)) "Colour $color must differ from the others"
            $seen[$fur] = $true
        }
        $odd = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "plaid"))
        $orange = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "orange"))
        Assert-Equal $odd.Art[0][8 * 16 + 6] $orange.Art[0][8 * 16 + 6] "Unknown colour falls back to orange"
    }

    $strokeType = Get-DeskmateType "PetStroke"
    $emoteArtType = Get-DeskmateType "PetEmoteArt"

    function Get-Mode($b) { return [string]$brainType.GetField("Mode").GetValue($b) }
    function Step-Brain($b, [double]$dt, [int]$n) { for ($i = 0; $i -lt $n; $i++) { $brainType.GetMethod("Step").Invoke($b, @([double]$dt)) | Out-Null } }

    It "16. A reaction holds its pose with a hop, then returns to Idle; nothing reacts while away" {
        $b = New-Brain 21 0 1000
        $happy = [Enum]::Parse($modeType, "Happy")
        Assert-True ($brainType.GetMethod("React").Invoke($b, @($happy, [double]1.5, [double]1))) "React must be accepted"
        Assert-Equal (Get-Mode $b) "Happy" "The reaction pose shows"
        Assert-True $brainType.GetProperty("Moving").GetValue($b) "A happy reaction hops"
        Step-Brain $b (1.0 / 60) 96
        Assert-Equal (Get-Mode $b) "Idle" "After the reaction the pet stands again"
        $brainType.GetMethod("SetAway").Invoke($b, @($true, [double]0)) | Out-Null
        Assert-False ($brainType.GetMethod("React").Invoke($b, @($happy, [double]1.5, [double]1))) "Nobody to react to while away"
    }

    It "17. Away: the pet sleeps in place until the user returns, then stretches and stands" {
        $b = New-Brain 23 0 1000
        $x0 = $brainType.GetField("X").GetValue($b)
        $brainType.GetMethod("SetAway").Invoke($b, @($true, [double]0)) | Out-Null
        Assert-Equal (Get-Mode $b) "Sleep" "Away puts the pet to sleep"
        Step-Brain $b 1.0 600
        Assert-Equal (Get-Mode $b) "Sleep" "Ten minutes away and still asleep"
        Assert-Equal $brainType.GetField("X").GetValue($b) $x0 "A sleeping pet does not move"
        $brainType.GetMethod("SetAway").Invoke($b, @($false, [double]1.2)) | Out-Null
        Assert-Equal (Get-Mode $b) "Stretch" "Coming back starts with a stretch"
        Step-Brain $b 0.1 13
        Assert-Equal (Get-Mode $b) "Idle" "After the stretch the pet stands"
    }

    It "18. A focus session keeps the pet off its feet for ten minutes" {
        $b = New-Brain 7 0 1000
        $brainType.GetField("Calm").SetValue($b, $true)
        $x0 = $brainType.GetField("X").GetValue($b)
        for ($i = 0; $i -lt 6000; $i++) {
            $brainType.GetMethod("Step").Invoke($b, @([double]0.1)) | Out-Null
            Assert-True ((Get-Mode $b) -ne "Walk") "No walking during focus (step $i)"
        }
        Assert-Equal $brainType.GetField("X").GetValue($b) $x0 "The pet stays where it was"
    }

    It "19. Music bobs a standing pet in quarter-second steps, never with reduced motion" {
        $b = New-Brain 9 0 1000
        $brainType.GetField("Groove").SetValue($b, $true)
        Step-Brain $b 0.1 1
        Assert-Equal $brainType.GetProperty("Lift").GetValue($b) ([double]0) "First quarter second on the ground"
        Step-Brain $b 0.2 1
        Assert-Equal $brainType.GetProperty("Lift").GetValue($b) ([double]2) "Second quarter second lifted 2 px"
        $next = $brainType.GetProperty("NextBob").GetValue($b)
        Assert-True ($next -gt 0 -and $next -le 0.25) "Next bob within a quarter second (got $next)"
        $brainType.GetField("NoWalk").SetValue($b, $true)
        Assert-Equal $brainType.GetProperty("Lift").GetValue($b) ([double]0) "Reduced motion: no bob"
        Assert-Equal $brainType.GetProperty("NextBob").GetValue($b) ([double]::MaxValue) "Reduced motion: no bob frames"
    }

    It "20. During a reaction, hover does not turn the pet" {
        $b = New-Brain 11 0 1000
        $brainType.GetMethod("React").Invoke($b, @([Enum]::Parse($modeType, "Surprised"), [double]1, [double]0.5)) | Out-Null
        $dir = $brainType.GetField("Dir").GetValue($b)
        Assert-False ($brainType.GetMethod("Notice").Invoke($b, @([int](-$dir * 50)))) "Hover is ignored while reacting"
        Assert-Equal $brainType.GetField("Dir").GetValue($b) $dir "The pet keeps facing the same way"
    }

    It "21. Petting: three quick back-and-forth turns count, slow or tiny moves do not" {
        $feed = $strokeType.GetMethod("Feed")
        $s = [Activator]::CreateInstance($strokeType, $true)
        $got = @(); $t = 0.0
        foreach ($x in 100, 110, 100, 110, 100) { $got += $feed.Invoke($s, @([int]$x, [double]$t, [int]4)); $t += 0.1 }
        Assert-Equal ($got -join ",") "False,False,False,False,True" "The third quick turn is petting"
        $slow = [Activator]::CreateInstance($strokeType, $true); $t = 0.0; $any = $false
        foreach ($x in 100, 110, 100, 110, 100, 110, 100) { if ($feed.Invoke($slow, @([int]$x, [double]$t, [int]4))) { $any = $true }; $t += 1.0 }
        Assert-False $any "Turns a second apart are not petting"
        $tiny = [Activator]::CreateInstance($strokeType, $true); $t = 0.0; $any = $false
        foreach ($x in 100, 102, 100, 102, 100, 102, 100) { if ($feed.Invoke($tiny, @([int]$x, [double]$t, [int]4))) { $any = $true }; $t += 0.1 }
        Assert-False $any "Jitter under the step size is not petting"
    }

    It "22. Reaction tags are optional: built-ins borrow poses, Aseprite tags are used when present" {
        $cat = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "orange"))
        $seq = { param($s, [string]$m) ($s.Sequence([Enum]::Parse($modeType, $m))) -join "," }
        Assert-Equal (& $seq $cat "Happy") "0,1" "Built-in joy borrows standing"
        Assert-Equal (& $seq $cat "Surprised") "0,1" "Built-in surprise borrows standing"
        Assert-Equal (& $seq $cat "Stretch") "6,7" "Built-in stretch borrows sitting"
        Assert-True ($cat.Top -ge 1 -and $cat.Top -lt $cat.Base) "Top row lies above the ground row (top $($cat.Top), base $($cat.Base))"
        Assert-True ([Math]::Abs($cat.Length([Enum]::Parse($modeType, "Walk")) - 0.5) -lt 1e-9) "Walk cycle lasts 0.5 s"
        try {
            $frames = (0..3 | ForEach-Object { Frame-Json $_ ($_ * 8) 8 }) -join ","
            $dir = Write-Pet "reactions" ('{"frames":[' + $frames + '],"meta":{"frameTags":[{"name":"walk","from":0,"to":1},{"name":"Happy","from":2,"to":2},{"name":"stretch","from":3,"to":3}]}}')
            $sheet = $sheetType.GetMethod("Load").Invoke($null, @([string]$dir))
            Assert-Equal (& $seq $sheet "Happy") "2" "happy tag"
            Assert-Equal (& $seq $sheet "Stretch") "3" "stretch tag"
            Assert-Equal (& $seq $sheet "Surprised") "0" "Missing surprised borrows standing"
            Assert-True ([Math]::Abs($sheet.Length([Enum]::Parse($modeType, "Happy")) - 0.1) -lt 1e-9) "Tag length comes from frame durations"
        } finally { if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp } }
    }

    It "23. Emote glyphs: four outlined cells, each clearly drawn" {
        $count = [int]$emoteArtType.GetProperty("Count").GetValue($null)
        Assert-Equal $count 4 "Heart, bang, note, sparkle"
        $f = 2; $cell = 9 * $f; $w = $cell * $count
        $px = [int[]]$emoteArtType.GetMethod("Atlas").Invoke($null, @([int]$f))
        Assert-Equal $px.Length ($w * $cell) "Atlas is one row of cells"
        for ($k = 0; $k -lt $count; $k++) {
            $opaque = 0
            for ($y = 0; $y -lt $cell; $y++) { for ($x = 0; $x -lt $cell; $x++) { if ($px[$y * $w + $k * $cell + $x] -ne 0) { $opaque++ } } }
            Assert-True ($opaque -gt 60) "Glyph $k must be visible (opaque px: $opaque)"
        }
    }
}
