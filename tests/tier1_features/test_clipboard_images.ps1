$commonDir = Join-Path (Split-Path -Parent $PSScriptRoot) "common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Clipboard image and text history" {
    $flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $type = Get-DeskmateType "Island"
    $read = $type.GetMethod("ReadClipboardData", $flags)
    $clear = $type.GetMethod("ClearClips", $flags)
    $images = $type.GetField("imageClips", $flags)
    $texts = $type.GetField("clips", $flags)

    function New-ClipboardIsland {
        $isle = [Activator]::CreateInstance($type, $true)
        $settings = $type.GetField("settings", $flags).GetValue($isle)
        $settings.Set("clipboard", "1")
        $settings.Set("shots", "0")
        return $isle
    }

    It "Records text and bitmap formats from the same copy in separate histories" {
        $isle = New-ClipboardIsland
        $bitmap = [Drawing.Bitmap]::new(40, 24)
        try {
            $bitmap.SetPixel(12, 8, [Drawing.Color]::CornflowerBlue)
            $data = [Windows.Forms.DataObject]::new()
            $data.SetText("label copied with the image")
            $data.SetImage($bitmap)
            [void]$read.Invoke($isle, @($data, [uint32]21))
            [void]$read.Invoke($isle, @($data, [uint32]21))

            $imageList = $images.GetValue($isle)
            $textList = $texts.GetValue($isle)
            Assert-Equal 1 $imageList.Count "A repeated clipboard notification must not duplicate the image"
            Assert-Equal 1 $textList.Count "Text must still be recorded"
            $entry = $imageList[0]
            Assert-Equal 40 $entry.Image.Width "Copyable image retains the original width"
            Assert-Equal ([Drawing.Color]::CornflowerBlue.ToArgb()) ($entry.Image.GetPixel(12, 8).ToArgb()) "Original pixels are retained"
            Assert-NotNull $entry.Thumb "The list must have a cached preview"

            [void]$clear.Invoke($isle, [object[]]@(,$imageList))
            Assert-Equal 0 $imageList.Count "Clearing images removes their history"
            Assert-Null $entry.Image "Clearing releases the original bitmap"
            Assert-Null $entry.Thumb "Clearing releases the thumbnail"
            Assert-Equal 1 $textList.Count "Text history survives clearing images"
        }
        finally {
            $isle.Dispose()
            $bitmap.Dispose()
        }
    }

    It "Records PNG-only input, respects private flags, and evicts old images at the count limit" {
        $isle = New-ClipboardIsland
        $bitmap = [Drawing.Bitmap]::new(32, 16)
        $png = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
            $data = [Windows.Forms.DataObject]::new()
            $data.SetData("PNG", $false, $png.ToArray())
            $data.SetData("ExcludeClipboardContentFromMonitorProcessing", $false, "1")
            [void]$read.Invoke($isle, @($data, [uint32]1))
            $list = $images.GetValue($isle)
            Assert-Equal 0 $list.Count "Excluded clipboard images must not be recorded"

            $data = [Windows.Forms.DataObject]::new()
            $data.SetData("PNG", $false, $png.ToArray())
            [void]$read.Invoke($isle, @($data, [uint32]2))
            Assert-Equal 1 $list.Count "PNG-only clipboard data must be accepted"
            $first = $list[0]
            [void]$read.Invoke($isle, @($data, [uint32]3))
            Assert-Equal 1 $list.Count "Identical pixels must deduplicate across clipboard sequences"
            foreach ($sequence in 4..11) {
                $different = [Drawing.Bitmap]::new(32, 16)
                $different.SetPixel(0, 0, [Drawing.Color]::FromArgb(255, $sequence, 0, 0))
                $differentData = [Windows.Forms.DataObject]::new()
                $differentData.SetImage($different)
                [void]$read.Invoke($isle, @($differentData, [uint32]$sequence))
            }
            Assert-Equal 8 $list.Count "Image history is limited to eight entries"
            Assert-Null $first.Image "Evicting the oldest entry releases the bitmap"
        }
        finally {
            $isle.Dispose()
            $png.Dispose()
            $bitmap.Dispose()
        }
    }

    It "The two Clipboard filters draw and target only their own entries" {
        $isle = New-ClipboardIsland
        $bitmap = [Drawing.Bitmap]::new(32, 16)
        $canvas = [Drawing.Bitmap]::new(480, 260)
        $graphics = [Drawing.Graphics]::FromImage($canvas)
        try {
            $data = [Windows.Forms.DataObject]::new()
            $data.SetText("text entry")
            $data.SetImage($bitmap)
            [void]$read.Invoke($isle, @($data, [uint32]42))
            $p = $type.GetField("p", $flags).GetValue($isle)
            $p.G = $graphics
            $p.T = (Get-DeskmateType "Ink").GetField("Base", $flags).GetValue($null)
            $type.GetField("g", $flags).SetValue($isle, $graphics)
            $type.GetField("hitsOn", $flags).SetValue($isle, $true)
            $type.GetField("pointerInside", $flags).SetValue($isle, $true)
            $draw = $type.GetMethod("DrawClipsPage", $flags)
            $choose = $type.GetMethod("ChooseClipKind", $flags)
            $hits = $type.GetField("hits", $flags).GetValue($isle)
            $textId = "clip" + $texts.GetValue($isle)[0].Id
            $imageId = "clip" + $images.GetValue($isle)[0].Id

            [void]$draw.Invoke($isle, @([single]480, [single]260, [DateTime]::UtcNow))
            $ids = @($hits | ForEach-Object Id)
            Assert-True ($ids -contains "historyText" -and $ids -contains "historyImage") "Both filters must be available"
            Assert-True ($ids -contains $textId -and $ids -notcontains $imageId) "The Text filter must only expose text rows"
            Assert-False (@($ids | Where-Object { $_ -like "clipEdit*" }).Count -gt 0) "Text cards must not have an image editor action"

            $hits.Clear()
            [void]$choose.Invoke($isle, @($true))
            [void]$draw.Invoke($isle, @([single]480, [single]260, [DateTime]::UtcNow))
            $ids = @($hits | ForEach-Object Id)
            Assert-True ($ids -contains $imageId -and $ids -notcontains $textId) "The Image filter must only expose image rows"
            $editId = "clipEdit" + $images.GetValue($isle)[0].Id
            $edit = $hits | Where-Object { $_.Id -eq $editId }
            Assert-NotNull $edit "Image cards must expose an Edit button"
            $edit.Act.Invoke()
            $editor = [Windows.Forms.Application]::OpenForms | Where-Object { $_.GetType().Name -eq "RedactWindow" } | Select-Object -First 1
            Assert-NotNull $editor "The Edit button must open the image editor"
            $editor.Close()
            Assert-Equal 32 $images.GetValue($isle)[0].Image.Width "Closing the editor must not release the clipboard bitmap"

            $record = $type.GetMethod("RecordImageClip", $flags)
            foreach ($n in 1..4) {
                $extra = [Drawing.Bitmap]::new(32 + $n, 16)
                try { [void]$record.Invoke($isle, @($extra, [uint32](42 + $n))) }
                finally { $extra.Dispose() }
            }
            $newestId = "clip" + $images.GetValue($isle)[0].Id
            $hits.Clear()
            $type.GetField("clipScroll", $flags).GetValue($isle).Snap([single]12)
            [void]$draw.Invoke($isle, @([single]480, [single]260, [DateTime]::UtcNow))
            $visibleCard = $hits | Where-Object { $_.Id -eq $newestId }
            Assert-NotNull $visibleCard "A partially clipped but visible card must still receive clicks"
            Assert-Equal 10 $visibleCard.Rect.Left "The hit must be clipped to the visible gallery"
            Assert-True (@($hits | ForEach-Object Id) -contains "historyNext") "An overflowing gallery must show navigation"
        }
        finally {
            $isle.Dispose()
            $graphics.Dispose()
            $canvas.Dispose()
            $bitmap.Dispose()
        }
    }
}
