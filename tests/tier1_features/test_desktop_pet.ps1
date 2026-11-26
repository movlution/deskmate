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

    It "1. Built-in cat, dog, and chao: 20 opaque cells per facing, left row mirrors right, paws on row 19" {
        foreach ($kind in "cat", "dog", "chao") {
        $sheet = $sheetType.GetMethod("BuiltIn").Invoke($null, @([string]$kind, "orange"))
        Assert-Equal $sheet.Count 20 "$kind must have 20 frames"
        Assert-Equal $sheet.W 24 "$kind has native 24 px detail"
        Assert-Equal $sheet.H 24 "$kind has the same canvas as Rubah"
        foreach ($frame in $sheet.Art) {
            Assert-True (($frame[(19 * 24)..(20 * 24 - 1)] | Where-Object { $_ -ne 0 }).Count -gt 0) "Every frame keeps the same foothold"
            Assert-True (($frame[(20 * 24)..($frame.Length - 1)] | Where-Object { $_ -ne 0 }).Count -eq 0) "Nothing is drawn below the foothold"
        }
        Assert-Equal $sheet.Base 19 "$kind paws (outline row) must be the ground row"
        $f = 2; $cell = $sheet.W * $f; $w = $cell * 20
        $px = [int[]]$sheetType.GetMethod("Atlas").Invoke($sheet, @($f))
        Assert-Equal $px.Length ($w * $cell * 2) "Atlas must be 20 cells wide and 2 rows tall"
        for ($c = 0; $c -lt 20; $c++) {
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

    It "Wellness bubbles clamp to the pet monitor and decline when they cannot fit" {
        $bubbleType = Get-DeskmateType "PetWellnessBubble"
        $bounds = $bubbleType.GetMethod("BoundsAbove")
        $area = [Drawing.Rectangle]::new(-1600, -100, 1600, 900)
        $size = [Drawing.Size]::new(268, 107)
        $left = $bounds.Invoke($null, @($area, [Drawing.Point]::new(-1590, 750), $size, 6))
        $right = $bounds.Invoke($null, @($area, [Drawing.Point]::new(-10, 750), $size, 6))
        Assert-Equal $left.Left (-1594) "The body stays on the left monitor"
        Assert-Equal $right.Right (-6) "The body stays left of the right edge"
        Assert-Equal $left.Bottom 744 "The bubble is above the pet"
        $blocked = $bounds.Invoke($null, @($area, [Drawing.Point]::new(-10, -60), $size, 6))
        Assert-True $blocked.IsEmpty "No room above the pet means no bubble"
    }

    It "Wellness bubble uses the correct prompt, actions and eye-rest duration" {
        $bubbleType = Get-DeskmateType "PetWellnessBubble"
        $kindType = Get-DeskmateType "RemindKind"
        $bounds = [Drawing.Rectangle]::new(0, 0, 1920, 1040)
        foreach ($name in "Drink", "Eyes", "Stretch") {
            $kind = [Enum]::Parse($kindType, $name)
            $b = [Activator]::CreateInstance($bubbleType, [object[]]@($kind, "Wellness detail", [single]1, [int]1920, [Func[bool]]{ $true }, $null, $null))
            try {
                Assert-True ($b.MoveAbove($bounds, [Drawing.Point]::new(900, 960))) "Bubble fits above the dock"
                Assert-False $b.Visible "Construction and layout must not show a real window"
                Assert-True ($b.Text.Length -gt 0) "$name has a spoken prompt"
                $buttons = @($b.Controls | Where-Object { $_ -is [Windows.Forms.Button] })
                $expected = if ($name -eq "Eyes") { 1 } else { 2 }
                Assert-Equal $buttons.Count $expected "$name action count"
                Assert-False ($b.Region.IsVisible(0, 0)) "Rounded corners do not intercept clicks"
                Assert-True ($b.Region.IsVisible([int]($b.Width / 2), $b.Height - 3)) "The pointer tail is part of the shape"
            } finally { $b.Dispose() }
        }
        $left = $bubbleType.GetMethod("SecondsLeft")
        Assert-Equal ($left.Invoke($null, @(0.0))) 20 "The eye break starts at 20 seconds"
        Assert-Equal ($left.Invoke($null, @(19.2))) 1 "Fractional time rounds up"
        Assert-Equal ($left.Invoke($null, @(20.1))) 0 "Expired reminders never show negative time"
    }

    It "11. Size: whole art px per screen px from the chosen height and DPI" {
        $cat = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "orange"))
        $factor = $winType.GetMethod("Factor")
        Assert-Equal $factor.Invoke($null, @($cat, 48, [single]1)) 2 "Medium matches the 48 px Rubah at 100%"
        Assert-Equal $factor.Invoke($null, @($cat, 32, [single]1)) 1 "Small uses whole pixels at 100%"
        Assert-Equal $factor.Invoke($null, @($cat, 64, [single]2)) 5 "Large uses whole pixels at 200%"
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
            $fur = $sheet.Art[0][13 * 24 + 8]
            Assert-True (-not $seen.ContainsKey($fur)) "Colour $color must differ from the others"
            $seen[$fur] = $true
        }
        $odd = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "plaid"))
        $orange = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "orange"))
        Assert-Equal $odd.Art[0][13 * 24 + 8] $orange.Art[0][13 * 24 + 8] "Unknown colour falls back to orange"
        # Open eyes stay visible on a black coat; elsewhere they match the outline
        foreach ($kind in "cat", "dog") {
            $eyeAt = if ($kind -eq "cat") { 7 * 24 + 18 } else { 7 * 24 + 17 }
            $black = $sheetType.GetMethod("BuiltIn").Invoke($null, @([string]$kind, "black"))
            $fur = $black.Art[0][13 * 24 + 8]; $eye = $black.Art[0][$eyeAt]; $outline = $black.Art[0][19 * 24 + 6]
            Assert-True ($eye -ne $fur -and $eye -ne $outline) "$kind black coat must get a contrasting eye"
            $plain = $sheetType.GetMethod("BuiltIn").Invoke($null, @([string]$kind, "orange"))
            Assert-Equal $plain.Art[0][$eyeAt] $plain.Art[0][19 * 24 + 6] "$kind orange coat keeps dark eyes"
        }
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

    It "22. Reaction tags are optional: built-ins have their own poses, Aseprite tags are used when present" {
        $cat = $sheetType.GetMethod("BuiltIn").Invoke($null, @("cat", "orange"))
        $seq = { param($s, [string]$m) ($s.Sequence([Enum]::Parse($modeType, $m))) -join "," }
        Assert-Equal (& $seq $cat "Happy") "10,11" "Built-in joy has its own pose"
        Assert-Equal (& $seq $cat "Surprised") "12,13" "Built-in surprise has its own pose"
        Assert-Equal (& $seq $cat "Stretch") "14,15" "Built-in stretch has its own pose"
        Assert-Equal (& $seq $cat "Held") "16,17" "Built-in held pose has its own pose"
        Assert-Equal (& $seq $cat "Scratch") "18,19,18" "Built-in scratch swipes and returns to its wind-up"
        Assert-True ([Math]::Abs($cat.Length([Enum]::Parse($modeType, "Scratch")) - 0.4) -lt 1e-9) "Scratch lasts one dock hop"
        # Every reaction pose must look different from standing and from each other
        foreach ($kind in "cat", "dog") {
            $s = $sheetType.GetMethod("BuiltIn").Invoke($null, @([string]$kind, "orange"))
            $cells = @(0, 6, 10, 12, 14, 16, 18)
            for ($i = 0; $i -lt $cells.Count; $i++) {
                for ($j = $i + 1; $j -lt $cells.Count; $j++) {
                    $diff = 0; $a = $s.Art[$cells[$i]]; $o = $s.Art[$cells[$j]]
                    for ($p = 0; $p -lt $a.Length; $p++) { if ($a[$p] -ne $o[$p]) { $diff++ } }
                    Assert-True ($diff -gt 12) "$kind cells $($cells[$i]) and $($cells[$j]) must differ visibly ($diff px)"
                }
            }
        }
        Assert-True ($cat.Top -ge 1 -and $cat.Top -lt $cat.Base) "Top row lies above the ground row (top $($cat.Top), base $($cat.Base))"
        Assert-True ([Math]::Abs($cat.Length([Enum]::Parse($modeType, "Walk")) - 0.5) -lt 1e-9) "Walk cycle lasts 0.5 s"
        try {
            $frames = (0..3 | ForEach-Object { Frame-Json $_ ($_ * 8) 8 }) -join ","
            $dir = Write-Pet "reactions" ('{"frames":[' + $frames + '],"meta":{"frameTags":[{"name":"walk","from":0,"to":1},{"name":"Happy","from":2,"to":2},{"name":"stretch","from":3,"to":3},{"name":"held","from":1,"to":1},{"name":"scratch","from":2,"to":3}]}}')
            $sheet = $sheetType.GetMethod("Load").Invoke($null, @([string]$dir))
            Assert-Equal (& $seq $sheet "Happy") "2" "happy tag"
            Assert-Equal (& $seq $sheet "Stretch") "3" "stretch tag"
            Assert-Equal (& $seq $sheet "Held") "1" "held tag"
            Assert-Equal (& $seq $sheet "Scratch") "2,3" "scratch tag"
            Assert-Equal (& $seq $sheet "Surprised") "0" "Missing surprised borrows standing"
            Assert-True ([Math]::Abs($sheet.Length([Enum]::Parse($modeType, "Happy")) - 0.1) -lt 1e-9) "Tag length comes from frame durations"
        } finally { if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp } }
    }

    It "Automatic chase needs fast sideways movement, then times out and cools down" {
        $b = New-Brain 7 0 400
        $b.X = 100
        Assert-False ($b.ProbeAuto([Drawing.Point]::new(140, 20), 0.0, 24, $true)) "One sample cannot start play"
        Assert-False ($b.ProbeAuto([Drawing.Point]::new(150, 20), 0.1, 24, $true)) "Slow travel must not trigger"
        Assert-False ($b.ProbeAuto([Drawing.Point]::new(190, 55), 0.2, 24, $true)) "Diagonal travel must not trigger"
        Assert-False ($b.ProbeAuto([Drawing.Point]::new(230, 55), 0.3, 24, $false)) "Do not start outside the allowed band"
        Assert-False ($b.ProbeAuto([Drawing.Point]::new(150, 55), 0.4, 24, $true)) "Re-entry needs a fresh sample"
        Assert-True ($b.ProbeAuto([Drawing.Point]::new(200, 55), 0.5, 24, $true)) "Fast sideways sweep starts chase"
        Assert-True $b.Playing "Sweep starts a session"
        Assert-False ($b.StepPlay(0.1, 300, 24, $true)) "Cursor is not yet caught"
        Assert-True ($b.X -gt 100 -and $b.X -lt 110) "Automatic chase moves without teleporting"
        [void]$b.StepPlay(1.41, 300, 24, $true)
        Assert-False $b.Playing "Auto chase times out at 1.5 seconds"
        Assert-False ($b.CanProbeAuto(21.99)) "No automatic retrigger during cooldown"
        Assert-True ($b.CanProbeAuto(22.0)) "Automatic chase is available again after cooldown"
    }

    It "Chase band lies along the dock, not over the windows above it" {
        $winType = Get-DeskmateType "PetWindow"
        $band = $winType.GetMethod("InChaseBand")
        Assert-NotNull $band "InChaseBand must exist"
        # Lane top edge at y=1000, x 100..500, 48 px sprite, 150% scale
        $lane = [Drawing.Rectangle]::new(100, 1000, 400, 0)
        $in = { param([int]$x, [int]$y, [double]$above, [double]$margin) [bool]$band.Invoke($null, @($lane, [int]48, [single]1.5, [Drawing.Point]::new($x, $y), $above, $margin)) }
        Assert-True (& $in 300 1060 24 0) "Over the dock icons triggers"
        Assert-True (& $in 300 990 24 0) "Just above the dock edge triggers"
        Assert-False (& $in 300 950 24 0) "Bottom of an app window above the dock does not trigger"
        Assert-False (& $in 300 1200 24 0) "Below the dock bar does not trigger"
        Assert-False (& $in 40 1060 24 0) "Beside the lane does not trigger"
        Assert-True (& $in 300 820 24 160) "The near margin wakes sampling before the cursor arrives"
        Assert-False (& $in 300 500 24 160) "Far away keeps the slow glance"
    }

    It "Automatic chase does not start during focus or reduced motion and cancels when blocked" {
        $b = New-Brain 7 0 400
        $b.X = 100
        $b.Calm = $true
        Assert-False ($b.CanProbeAuto(0)) "Focus disables automatic sampling"
        $b.Calm = $false
        $b.NoWalk = $true
        Assert-False ($b.CanProbeAuto(0)) "Reduced motion disables automatic sampling"
        $b.NoWalk = $false
        [void]$b.ProbeAuto([Drawing.Point]::new(160, 20), 0.0, 24, $true)
        Assert-True ($b.ProbeAuto([Drawing.Point]::new(205, 20), 0.1, 24, $true)) "Start an automatic session"
        Assert-False ($b.StepPlay(0.1, 205, 24, $false)) "Blocked frame does not catch the cursor"
        Assert-False $b.Playing "Cancellation ends the automatic session"
        Assert-Equal $b.X 100 "Cancellation takes no step"
    }

    It "Focus stops an active walk or chase before any further movement" {
        $b = New-Brain 7 0 1000
        $b.X = 100
        $brainType.GetMethod("StartWalk", [Reflection.BindingFlags]'NonPublic,Instance').Invoke($b, $null) | Out-Null
        Assert-Equal (Get-Mode $b) "Walk" "Start with an active walk"
        $b.Calm = $true
        $b.Step(0.1)
        Assert-Equal (Get-Mode $b) "Sit" "Stop walking when focus starts"
        Assert-Equal $b.X 100 "Do not take another step"
        $b.Calm = $false
        [void]$b.ProbeAuto([Drawing.Point]::new(160, 20), 0.0, 24, $true)
        Assert-True ($b.ProbeAuto([Drawing.Point]::new(205, 20), 0.1, 24, $true)) "Start chase"
        $b.Calm = $true
        Assert-False ($b.StepPlay(0.1, 300, 24, $true)) "Focus cancels the chase"
        Assert-False $b.Playing "Chase stays cancelled"
        Assert-Equal $b.X 100 "No chase step during focus"
    }

    It "Click wakes an away sleeper with a stretch and no hop" {
        $b = New-Brain 7 0 1000
        $b.Hop()
        $b.SetAway($true, 0)
        Assert-False $b.Moving "Falling asleep cancels existing movement"
        $b.Click(1.2)
        Assert-False $b.Away "Click clears the idle-away flag"
        Assert-Equal (Get-Mode $b) "Stretch" "Show the wake pose"
        Assert-False $b.Moving "Do not hop on the wake click"
        $b.Step(1.3)
        Assert-Equal (Get-Mode $b) "Idle" "Return to idle"
    }

    It "Enabling reduced motion mid-hop lands and does not resume the old hop" {
        $b = New-Brain 7 0 1000
        $b.Hop()
        $b.Step(0.1)
        Assert-True ($b.Lift -gt 0) "Already in the air"
        $b.NoWalk = $true
        $b.Step(0.01)
        Assert-Equal $b.Lift 0 "Land on the next frame"
        Assert-False $b.Moving "Stop requesting moving frames"
        $b.NoWalk = $false
        $b.Step(0.01)
        Assert-Equal $b.Lift 0 "Do not resume the old hop"
    }

    It "Dragging clamps to the lane, drops with one bounce, and settles" {
        $b = New-Brain 7 50 300
        $b.X = 100
        $drag = $brainType.GetMethod("DragThreshold")
        $origin = [Drawing.Point]::new(100, 100)
        Assert-False ($drag.Invoke($null, @($origin, [Drawing.Point]::new(110, 100), [single]1.5))) "Small mouse jitter is still a click"
        Assert-True ($drag.Invoke($null, @($origin, [Drawing.Point]::new(100, 88), [single]1.5))) "Vertical travel can lift the pet"
        [void]$b.ProbeAuto([Drawing.Point]::new(160, 20), 0.0, 24, $true)
        Assert-True ($b.ProbeAuto([Drawing.Point]::new(205, 20), 0.1, 24, $true)) "Chase before grabbing"
        Assert-True ($b.BeginHold()) "The grab starts"
        Assert-False $b.Playing "Grab interrupts chase"
        Assert-Equal (Get-Mode $b) "Held" "Held pose"
        $b.MoveHeld(900, 200)
        Assert-Equal $b.X 300 "Right bound stays inside the lane"
        Assert-Equal $b.Lift 96 "Lift is limited"
        $b.MoveHeld(-100, 30)
        Assert-Equal $b.X 50 "Left bound stays inside the lane"
        $b.Step(0.3)
        Assert-Equal $b.Lift 30 "Held pet does not fall until released"
        $b.EndHold()
        Assert-False $b.Holding "Release clears holding"
        $b.Step(0.05)
        Assert-True ($b.Lift -lt 30 -and $b.Lift -gt 0) "Released pet falls"
        for ($i = 0; $i -lt 20 -and $b.Manipulating; $i++) { $b.Step(0.02) }
        Assert-Equal $b.Lift 0 "Fall reaches contact"
        Assert-False $b.Manipulating "Fall ends at contact"
        Assert-True $b.Moving "Contact starts the bounce"
        $b.Step(0.1)
        Assert-True ($b.Lift -gt 0 -and $b.Lift -lt 5) "Bounce is smaller than a normal click hop"
        for ($i = 0; $i -lt 5; $i++) { $b.Step(0.1) }
        Assert-False $b.Moving "No second bounce"
        Assert-Equal $b.X 50 "No horizontal drift after release"
    }

    It "Carried anywhere on screen, a pet released outside the lane falls back into it" {
        $b = New-Brain 7 50 300
        $b.X = 100
        Assert-True ($b.BeginHold()) "The grab starts"
        $b.MoveHeldFree(900, 500, 0, 1000, 400)
        Assert-Equal $b.X 900 "A carry can leave the lane"
        Assert-Equal $b.Lift 400 "Lift stops at the top of the screen"
        $b.MoveHeldFree(1200, -20, 0, 1000, 400)
        Assert-Equal $b.X 1000 "A carry stays on the screen"
        Assert-Equal $b.Lift 0 "A carry never goes below the ground"
        $b.MoveHeldFree(900, 400, 0, 1000, 400)
        $b.EndHold()
        $b.Step(0.05)
        Assert-True ($b.X -lt 900 -and $b.X -gt 300) "Drifts back toward the lane while falling"
        Assert-True ($b.Lift -lt 400 -and $b.Lift -gt 0) "Falls"
        for ($i = 0; $i -lt 100 -and $b.Manipulating; $i++) { $b.Step(0.02) }
        Assert-False $b.Manipulating "Lands"
        Assert-Equal $b.X 300 "Lands inside the lane"
        $b.Step(0.1)
        Assert-True ($b.Lift -gt 3.6) "A long fall bounces higher than a short drop"
        for ($i = 0; $i -lt 10; $i++) { $b.Step(0.1) }
        Assert-Equal $b.Lift 0 "Settles after one bounce"
    }

    It "Interrupted drag lands without a bounce and never resumes" {
        $b = New-Brain 7 0 500
        $b.Calm = $true
        Assert-False ($b.BeginHold()) "Focus blocks lifting"
        $b.Calm = $false
        Assert-True ($b.BeginHold()) "Lift before reduced motion"
        $b.MoveHeld(230, 80)
        $b.NoWalk = $true
        $b.Step(0.01)
        Assert-False $b.Moving "Reduced motion cancels without bounce"
        Assert-Equal $b.Lift 0 "Grounded after cancellation"
        $b.NoWalk = $false
        Assert-True ($b.BeginHold()) "Lift again"
        $b.MoveHeld(270, 80)
        $b.EndHold()
        $b.CancelHold()
        Assert-False $b.Manipulating "An interrupted drop clears its state"
        Assert-Equal $b.Lift 0 "Cancelled drop stays on the ground"
    }

    It "Throw velocity arcs through the air, bounces off walls and ceiling, slides on landing, and reports hard vs soft" {
        $b = New-Brain 7 50 400
        $b.X = 100
        Assert-True ($b.BeginHold()) "Start hold for throw"
        $b.MoveHeldFree(100, 60, 0, 500, 200)
        # Hard throw to the right and upward
        $b.EndHold(800, 600, 50, 400, 150)
        Assert-False $b.Holding "Throw clears holding"
        Assert-True $b.Manipulating "Throw stays active through arc, bounces, and skid"
        Assert-Equal $b.Dir 1 "Faces the throw direction"
        $hitWall = $false; $hitCeiling = $false; $bounced = $false; $skidded = $false
        $sawGround = $false; $lastGroundX = [double]::NaN
        for ($i = 0; $i -lt 300 -and $b.Manipulating; $i++) {
            $b.Step(0.02)
            if ($b.Lift -ge 149.9) { $hitCeiling = $true }
            if ($b.Dir -eq -1) { $hitWall = $true }
            if ($b.Lift -eq 0) {
                if (-not [double]::IsNaN($lastGroundX) -and $b.X -ne $lastGroundX) { $skidded = $true }
                $sawGround = $true
                $lastGroundX = $b.X
            } elseif ($sawGround -and $b.Lift -gt 5) {
                $bounced = $true
                $lastGroundX = [double]::NaN
            }
            Assert-True ($b.X -ge 50 -and $b.X -le 400) "Stays within throw bounds (x=$($b.X))"
            Assert-True ($b.Lift -le 150 + 1e-6) "Never exceeds maxLift (lift=$($b.Lift))"
        }
        Assert-False $b.Manipulating "Throw settles smoothly"
        Assert-True $hitCeiling "Fast upward throw hits ceiling"
        Assert-True $hitWall "Fast sideways throw bounces off wall and reverses facing"
        Assert-True $bounced "Fast throw bounces continuously on impact"
        Assert-True $skidded "After bouncing, pet skids smoothly to a stop"
        Assert-Equal $b.Lift 0 "Ends grounded"
        $hard = $false
        Assert-True ($b.ConsumeThrowLanding([ref]$hard)) "Landing signals a throw landing"
        Assert-True $hard "High/fast throw counts as hard landing"
        Assert-False ($b.ConsumeThrowLanding([ref]$hard)) "Landing signal is consumed once"

        # ShiftGround preserves screen Y continuity when crossing dock edge mid-air
        $b.X = 200
        Assert-True ($b.BeginHold()) "Start hold for ground shift test"
        $b.MoveHeld(200, 50)
        $b.EndHold(300, 200, 50, 400, 200)
        $beforeLift = $b.Lift
        $b.ShiftGround(60)
        Assert-Equal $b.Lift ($beforeLift + 60) "Shifting ground down increases lift by same amount"
        $b.CancelHold()

        # Tiny velocity below deadzone behaves as a plain vertical drop
        $b.X = 200
        Assert-True ($b.BeginHold()) "Start gentle hold"
        $b.MoveHeld(200, 40)
        $b.EndHold(20, 10, 50, 400, 200)
        for ($i = 0; $i -lt 40 -and $b.Manipulating; $i++) { $b.Step(0.02) }
        Assert-Equal $b.X 200 "Sub-deadzone velocity drops straight down"
        Assert-False ($b.ConsumeThrowLanding([ref]$hard)) "Gentle drop does not signal a throw landing"

        # Reduced motion mid-throw cancels cleanly without bounce or landing signal
        Assert-True ($b.BeginHold()) "Start hold before reduced motion"
        $b.MoveHeld(200, 50)
        $b.EndHold(400, 300, 50, 400, 200)
        $b.Step(0.02)
        $b.NoWalk = $true
        $b.Step(0.01)
        Assert-False $b.Manipulating "Reduced motion cancels active throw"
        Assert-Equal $b.Lift 0 "Grounded immediately"
        Assert-False ($b.ConsumeThrowLanding([ref]$hard)) "Cancelled throw emits no landing signal"
    }

    It "Dock launch hops toward a nearby icon without leaving the lane" {
        $b = New-Brain 7 50 150
        $b.X = 120
        Assert-False ($b.TryDockTrick(350, 24, $false, 0.75, 1)) "Ignore distant icons"
        Assert-True ($b.TryDockTrick(209, 24, $false, 0.75, 1)) "Follow a nearby icon"
        Assert-Equal $b.X 120 "No teleport when reacting"
        $b.Step(0.1)
        Assert-True ($b.X -gt 120 -and $b.X -lt 150 -and $b.Lift -gt 0) "Travel in the air"
        for ($i = 0; $i -lt 3; $i++) { $b.Step(0.1) }
        Assert-Equal $b.X 150 "Land within the lane"
        Assert-Equal $b.Lift 0 "Land on the floor"
        Assert-False ($b.TryDockTrick(172, 24, $true, 0.4, 6.99)) "No immediate second icon reaction"
    }

    It "Badge scratch replaces a generic surprise and stops without another step during focus" {
        $b = New-Brain 7 0 300
        $b.X = 100
        Assert-True ($b.React([Enum]::Parse($modeType, "Surprised"), 1, 0.5)) "A toast already arrived"
        Assert-True ($b.TryDockTrick(176, 24, $true, 0.4, 1)) "The same notification can trigger the icon-specific reaction"
        Assert-Equal (Get-Mode $b) "Scratch" "Use the scratch pose"
        $b.Step(0.1)
        $x = $b.X
        $b.Calm = $true
        $b.Step(0.1)
        Assert-Equal $b.X $x "Focus cancels without horizontal drift"
        Assert-Equal $b.Lift 0 "Focus grounds the pet"
        Assert-False $b.DockTrickActive "Focus clears the scratch"
        Assert-False ($b.TryDockTrick(176, 24, $true, 0.4, 8)) "Focus blocks new scratch"
        $b.Calm = $false
        $px = [int]$b.X + 24
        [void]$b.ProbeAuto([Drawing.Point]::new($px + 36, 20), 9.0, 24, $true)
        Assert-True ($b.ProbeAuto([Drawing.Point]::new($px + 81, 20), 9.1, 24, $true)) "Chase starts"
        Assert-False ($b.TryDockTrick(176, 24, $true, 0.4, 9.1)) "An icon does not interrupt a chase"
    }

    It "Action bar open: a walk stops, no new walk or auto chase starts, and a hop still plays" {
        $b = New-Brain 7 0 1000
        $b.Scale = 1
        for ($i = 0; $i -lt 2000 -and (Get-Mode $b) -ne "Walk"; $i++) { $b.Step(0.05) }
        Assert-Equal (Get-Mode $b) "Walk" "The pet walks at some point"
        $b.Attending = $true
        $b.Step(0.05)
        Assert-Equal (Get-Mode $b) "Idle" "Opening the bar stops the walk"
        $x = $b.X
        for ($i = 0; $i -lt 1200; $i++) { $b.Step(0.05) }
        Assert-Equal $b.X $x "While the bar is open the pet stays put (60 s)"
        Assert-False ($b.CanProbeAuto(100)) "No auto chase while the bar is open"
        $b.Hop()
        $b.Step(0.2)
        Assert-True ($b.Lift -gt 0) "A click hop still plays while attending"
        $b.Attending = $false
    }

    It "Action bar Nap and Chase: nap sleeps and a click wakes; chase is refused in focus, reduced motion, or away" {
        $b = New-Brain 3 0 1000
        $b.Scale = 1
        Assert-True ($b.Nap()) "Nap starts"
        Assert-Equal (Get-Mode $b) "Sleep" "Nap sleeps"
        Assert-False ($b.Nap()) "Nap while asleep does nothing"
        $b.Click(1.2)
        Assert-Equal (Get-Mode $b) "Idle" "A click wakes a napping pet"

        $b.Calm = $true
        Assert-False ($b.StartChase(3)) "No chase during focus"
        $b.Calm = $false
        $b.NoWalk = $true
        Assert-False ($b.StartChase(3)) "No chase with reduced motion"
        $b.NoWalk = $false
        $b.SetAway($true, 1.2)
        Assert-False ($b.StartChase(3)) "No chase while away"
        $b.SetAway($false, 1.2)
        Assert-True ($b.StartChase(3)) "Chase starts otherwise"
        Assert-True $b.Playing "Chasing"
        Assert-Equal (Get-Mode $b) "Walk" "Chase runs"
        $narrow = New-Brain 1 0 10
        $narrow.Scale = 1
        Assert-False ($narrow.StartChase(3)) "No chase on a lane narrower than 24 px"
    }

    It "Action bar layout: big cells that never overlap and fit every label, at 100% and 150%" {
        $barType = Get-DeskmateType "PetActionBar"
        $iconsType = Get-DeskmateType "PetIcons"
        foreach ($field in $iconsType.GetFields($flags)) {
            $icon = [string[]]$field.GetValue($null)
            Assert-Equal $icon.Length 7 "$($field.Name) has 7 rows"
            foreach ($row in $icon) { Assert-Equal $row.Length 7 "$($field.Name) rows are 7 wide" }
        }
        foreach ($scale in 1.0, 1.5) {
            $font = (Get-DeskmateType "Ink").GetMethod("Semi").Invoke($null, @([float](11 * $scale)))
            foreach ($labels in @(@("Elus", "Lompat", "Kejar", "Tidur", "Chat", "Atur"), @("Pet", "Hop", "Chase", "Nap", "Chat", "Settings"))) {
                $widths = [int[]]($labels | ForEach-Object { [Windows.Forms.TextRenderer]::MeasureText($_, $font).Width })
                $body = [Drawing.Size]::Empty
                $args2 = [object[]]@($widths, [float]$scale, $body)
                $cells = [Drawing.Rectangle[]]$barType.GetMethod("Cells").Invoke($null, $args2)
                $body = [Drawing.Size]$args2[2]
                Assert-Equal $cells.Length $labels.Length "One cell per action"
                for ($i = 0; $i -lt $cells.Length; $i++) {
                    $c = $cells[$i]
                    Assert-True ($c.Width -ge [Math]::Round(54 * $scale) -and $c.Height -ge [Math]::Round(54 * $scale)) "Cell $i is at least 54 logical px ($($c.Width)x$($c.Height))"
                    Assert-True ($c.Width -ge $widths[$i] + 4) "Label '$($labels[$i])' fits ($($widths[$i]) in $($c.Width))"
                    Assert-True ($c.Right -le $body.Width -and $c.Bottom -le $body.Height) "Cell $i inside the bar"
                    if ($i -gt 0) { Assert-True ($c.Left -gt $cells[$i - 1].Right) "Cell $i does not overlap the one before" }
                }
            }
        }
    }

    It "Emote glyphs: five outlined cells, each clearly drawn" {
        $count = [int]$emoteArtType.GetProperty("Count").GetValue($null)
        Assert-Equal $count 5 "Heart, bang, note, sparkle, scratch"
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
