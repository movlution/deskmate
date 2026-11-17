# test_x06_pack_due_notif.ps1
# Tier 3 pairwise: custom pack x theme flip, overdue task x semantic ink, and notification typography.

$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Tier 3 Pairwise: Pack x Due x Notification" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    $inkType = Get-DeskmateType "Ink"
    $islandType = Get-DeskmateType "Island"
    $taskRowType = Get-DeskmateType "TaskRow"
    $todoType = Get-DeskmateType "TodoItem"
    $taskInkType = Get-DeskmateType "TaskInk"

    function New-GenericList($itemType, $items = @()) {
        $listType = [System.Collections.Generic.List``1].MakeGenericType($itemType)
        $list = [Activator]::CreateInstance($listType)
        foreach ($item in $items) { [void]$list.Add($item) }
        return ,$list
    }

    function New-TaskItem($title, $due) {
        $item = [Activator]::CreateInstance($todoType, $true)
        $item.Title = $title
        $item.Due = $due
        return $item
    }

    function New-TaskRow($item) {
        $row = [Activator]::CreateInstance($taskRowType, $true)
        $row.Item = $item
        return $row
    }

    function Has-UnpairedSurrogate($s) {
        for ($i = 0; $i -lt $s.Length; $i++) {
            if ([char]::IsHighSurrogate($s[$i])) {
                if ($i + 1 -ge $s.Length -or -not [char]::IsLowSurrogate($s[$i + 1])) { return $true }
                $i++
            }
            elseif ([char]::IsLowSurrogate($s[$i])) { return $true }
        }
        return $false
    }

    BeforeEach {
        Reset-DeskmateTheme -Light $false
        $langType = Get-DeskmateType "Lang"
        if ($langType) { $langType.GetField("English", $flags).SetValue($null, $false) }
    }

    It "applies a custom pack and then returns to the default dark/light surfaces on a theme flip" {
        $derive = $islandType.GetMethod("DerivePalette", $flags)
        $setPack = $inkType.GetMethod("SetPack", $flags)
        $apply = $inkType.GetMethod("Apply", $flags)
        try {
            $dict = $derive.Invoke($null, @(
                [System.Drawing.ColorTranslator]::FromHtml("#101820"),
                [System.Drawing.ColorTranslator]::FromHtml("#202830"),
                [System.Drawing.ColorTranslator]::FromHtml("#FFD24A")))
            $setPack.Invoke($null, (,$dict))
            $apply.Invoke($null, @([bool]$false))
            $base = Get-DeskmateStaticField "Ink" "Base"
            Assert-Equal ([System.Drawing.ColorTranslator]::FromHtml("#101820").ToArgb()) $base.Bg.ToArgb() "Custom pack bg must reach Ink.Base"
            Assert-Equal ([System.Drawing.ColorTranslator]::FromHtml($dict["text"]).ToArgb()) $base.Text.ToArgb() "Derived pack text must reach Ink.Base"
            Assert-True ((Get-DeskmateContrast $base.Text $base.Bg) -ge 7.0) "Custom pack primary text must keep its 7:1 floor"
            Assert-True ((Get-DeskmateContrast $base.Accent $base.Bg) -ge 3.5) "Custom pack accent must keep its 3.5:1 floor"
            Assert-True ((Get-DeskmateContrast $base.OnAccent $base.Accent) -ge 4.5) "Custom pack OnAccent must keep 4.5:1"

            $setPack.Invoke($null, (,[System.Collections.Generic.Dictionary[string,string]]::new()))
            $apply.Invoke($null, @([bool]$true))
            $lightBase = Get-DeskmateStaticField "Ink" "Base"
            Assert-True ((Get-DeskmateStaticField "Ink" "Light") -eq $true) "Theme flip must set Ink.Light"
            Assert-True ($lightBase.Bg.R -gt 220 -and $lightBase.Text.R -lt 40) "Clearing the pack must restore the light paper surfaces"
            $apply.Invoke($null, @([bool]$false))
            $darkBase = Get-DeskmateStaticField "Ink" "Base"
            Assert-True ($darkBase.Bg.R -lt 30 -and $darkBase.Text.R -gt 200) "Theme flip back must restore dark surfaces"
            Assert-True ((Get-DeskmateContrast (Get-DeskmateStaticProperty "KitInk" "Faint") (Get-DeskmateStaticProperty "KitInk" "Window")) -ge 4.5) "Faint must remain readable after the pack round-trip"
        }
        finally { Reset-DeskmateTheme -Light $false }
    }

    It "paints overdue chips with Negative ink which stays readable against the window and the tinted chip" {
        $overdue = $taskInkType.GetMethod("Overdue", $flags)
        foreach ($light in @($false, $true)) {
            Reset-DeskmateTheme -Light $light
            $late = New-TaskItem "Tugas terlambat" ([DateTime]::Today.AddDays(-1).AddHours(9))
            $future = New-TaskItem "Tugas nanti" ([DateTime]::Today.AddDays(1).AddHours(9))
            Assert-True ([bool]$overdue.Invoke($null, @($late))) "Yesterday must be overdue"
            Assert-False ([bool]$overdue.Invoke($null, @($future))) "Tomorrow must not be overdue"

            $lateView = New-DeskmateControl "TaskView"
            $futureView = New-DeskmateControl "TaskView"
            try {
                $lateOpen = New-GenericList $taskRowType @((New-TaskRow $late))
                $futureOpen = New-GenericList $taskRowType @((New-TaskRow $future))
                $done = New-GenericList $todoType
                $lateView.SetData($lateOpen, $done, $false, $true, "Kosong", "")
                $futureView.SetData($futureOpen, $done, $false, $true, "Kosong", "")
                $lateBmp = Render-DeskmateControlToBitmap -Control $lateView -Width 380 -Height 180
                $futureBmp = Render-DeskmateControlToBitmap -Control $futureView -Width 380 -Height 180
                try {
                    $diff = 0; $negativePixels = 0
                    $negative = (Get-DeskmateStaticField "Ink" "Base").Negative
                    for ($y = 0; $y -lt $lateBmp.Height; $y++) {
                        for ($x = 0; $x -lt $lateBmp.Width; $x++) {
                            $a = $lateBmp.GetPixel($x, $y)
                            if ($a.ToArgb() -ne $futureBmp.GetPixel($x, $y).ToArgb()) { $diff++ }
                            if ($a.R -gt $a.G + 25 -and $a.R -gt $a.B + 25) { $negativePixels++ }
                        }
                    }
                    Assert-True ($diff -gt 40) "An overdue row must render differently from a future row (light=$light)"
                    Assert-True ($negativePixels -gt 2) "The overdue chip must contribute Negative-hued pixels (light=$light)"

                    $card = Get-DeskmateStaticProperty "TaskInk" "Card"
                    $window = Get-DeskmateStaticProperty "KitInk" "Window"
                    Assert-True ((Get-DeskmateContrast $negative $window) -ge 4.5) "Overdue Negative must keep 4.5:1 against the dialog window"
                    $mix = $inkType.GetMethod("Mix", $flags).Invoke($null, @([System.Drawing.Color]$card, [System.Drawing.Color]$negative, [single]0.10))
                    $ratio = Get-DeskmateContrast $negative $mix
                    Assert-True ($ratio -ge 4.0) "Overdue Negative on its tinted chip in light=$light is $ratio, expected >= 4.0"
                }
                finally { $lateBmp.Dispose(); $futureBmp.Dispose() }
            }
            finally { $lateView.Dispose(); $futureView.Dispose() }
        }
    }

    It "truncates emoji notification titles on grapheme boundaries and clips NotifText to its box" {
        $long = ("A" * 79) + "🚀"
        $cleaned = [string]$islandType.GetMethod("CleanText", $flags).Invoke($null, @([string]$long, [int]80))
        Assert-True ($cleaned.Length -le 80) "CleanText must respect its maximum length"
        Assert-False (Has-UnpairedSurrogate $cleaned) "CleanText must not leave a dangling surrogate at the cut"
        $full = ("A" * 78) + "🚀"
        $cleanedFull = [string]$islandType.GetMethod("CleanText", $flags).Invoke($null, @([string]$full, [int]80))
        Assert-Equal $full $cleanedFull "CleanText must keep a whole emoji that fits exactly"

        $oneLine = [string](Get-DeskmateType "Fmt").GetMethod("OneLine", $flags).Invoke($null, @([string]"Baris satu`nBaris dua 🚀", [int]40))
        Assert-False ($oneLine.Contains("`n")) "Fmt.OneLine must flatten newlines"
        Assert-True ($oneLine.Contains("🚀")) "Fmt.OneLine must keep the trailing emoji"

        $painter = [Activator]::CreateInstance((Get-DeskmateType "Painter"), $true)
        $bmp = New-Object System.Drawing.Bitmap 260, 64
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $painter.G = $g
            $painter.T = (Get-DeskmateStaticField "Ink" "Base")
            $painter.Alpha = 1
            $painter.HalfPx = 0.5
            $font = New-Object System.Drawing.Font "Segoe UI", 13
            try {
                $text = "🚀 " + ("Notifikasi panjang " * 12) + " ✅"
                $painter.NotifText($text, $font, [System.Drawing.Color]::White, 24, 32, 120, [System.Drawing.StringAlignment]::Near)
                $maxX = -1; $inkPixels = 0
                for ($y = 0; $y -lt $bmp.Height; $y++) {
                    for ($x = 0; $x -lt $bmp.Width; $x++) {
                        if ($bmp.GetPixel($x, $y).A -gt 16) { $inkPixels++; if ($x -gt $maxX) { $maxX = $x } }
                    }
                }
                Assert-True ($inkPixels -gt 40) "NotifText must draw visible emoji/text ink"
                Assert-True ($maxX -le 148) "NotifText ink must clip to the 24+120 box; maxX=$maxX"
            }
            finally { $font.Dispose() }
        }
        finally { $g.Dispose(); $bmp.Dispose(); $painter.Dispose() }
    }
}
