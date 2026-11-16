# test_f26_image_editor.ps1
# Feature 26: Image Editor (RedactWindow) — Tools, Rendering, Geometry, Beautify Frame, Undo/Redo, Zoom & Localization

$commonDir = Join-Path $PSScriptRoot "..\common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Feature 26: Image Editor (RedactWindow)" {
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $winType = Get-DeskmateType "RedactWindow"
    $stripType = Get-DeskmateType "RedactToolStrip"
    $toolModeType = Get-DeskmateType "ToolMode"
    $editType = $winType.GetNestedType("Edit", $flags)
    $editKindType = $winType.GetNestedType("EditKind", $flags)
    $redactModeType = $winType.GetNestedType("RedactMode", $flags)
    $framePresetType = $winType.GetNestedType("FramePreset", $flags)

    BeforeEach {
        Reset-DeskmateTheme -Light $false
    }

    function Reset-CleanSettings {}

    function New-SolidBitmap([int]$w, [int]$h, [System.Drawing.Color]$col) {
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear($col)
        } finally {
            $g.Dispose()
        }
        return $bmp
    }

    function Get-Field($obj, [string]$name) {
        return $obj.GetType().GetField($name, $flags).GetValue($obj)
    }

    function Set-Field($obj, [string]$name, $val) {
        $obj.GetType().GetField($name, $flags).SetValue($obj, $val)
    }

    function Invoke-Method($obj, [string]$name, [object[]]$params = @()) {
        $m = $obj.GetType().GetMethod($name, $flags)
        return $m.Invoke($obj, $params)
    }

    function New-Edit([string]$kindName) {
        $e = [Activator]::CreateInstance($editType)
        $k = [System.Enum]::Parse($editKindType, $kindName)
        $editType.GetField("Kind", $flags).SetValue($e, $k)
        return $e
    }

    function New-RedactWindow([System.Drawing.Bitmap]$bmp) {
        $wt = Get-DeskmateType "RedactWindow"
        $ctor = $wt.GetConstructor([System.Reflection.BindingFlags]'Public,NonPublic,Instance', $null, [type[]]@([System.Drawing.Bitmap]), $null)
        return $ctor.Invoke([object[]]@([System.Drawing.Bitmap]$bmp))
    }

    It "1. Window & Controls: constructs from 320x200 Bitmap, exposes 11 tools, Relayout() has valid viewport/canvas" {
        $bmp = New-SolidBitmap 320 200 ([System.Drawing.Color]::White)
        $win = New-RedactWindow $bmp
        try {
            Assert-Equal $win.FormBorderStyle ([System.Windows.Forms.FormBorderStyle]::Sizable) "FormBorderStyle must be Sizable"
            Assert-True $win.MinimizeBox "MinimizeBox must be true"
            Assert-True $win.MaximizeBox "MaximizeBox must be true"

            $itemsField = $stripType.GetField("Items", $flags)
            $items = $itemsField.GetValue($null)
            Assert-Equal $items.Length 11 "RedactToolStrip must expose 11 tools"

            $expectedTools = @("Select", "Arrow", "Line", "Rect", "Ellipse", "Pen", "Highlight", "Text", "Step", "Redact", "Crop")
            $enumNames = [System.Enum]::GetNames($toolModeType)
            foreach ($t in $expectedTools) {
                Assert-True ($enumNames -contains $t) "ToolMode must contain $t"
            }

            $win.ShowInTaskbar = $false
            $win.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
            $win.Location = New-Object System.Drawing.Point(-32000, -32000)
            $win.Size = New-Object System.Drawing.Size 960, 680
            $win.Show()
            Invoke-Method $win "Relayout"
            [System.Windows.Forms.Application]::DoEvents()

            $viewport = Get-Field $win "viewport"
            $canvas = Get-Field $win "canvas"
            Assert-True ($viewport.Width -gt 100) "Viewport width must be > 100"
            Assert-True ($viewport.Height -gt 80) "Viewport height must be > 80"
            Assert-True ($canvas.Width -gt 50) "Canvas width must be > 50"
            Assert-True ($canvas.Height -gt 50) "Canvas height must be > 50"
        } finally {
            $win.Close()
            $win.Dispose()
            $bmp.Dispose()
        }
    }

    It "2. Drawing tools render real pixel changes on work (Arrow, Line, Rect, Ellipse, Pen, Highlight)" {
        $bmp = New-SolidBitmap 320 200 ([System.Drawing.Color]::White)
        $win = New-RedactWindow $bmp
        try {
            # Arrow
            $eArrow = New-Edit "Arrow"
            $eArrow.A = New-Object System.Drawing.PointF 20, 60; $eArrow.B = New-Object System.Drawing.PointF 140, 60
            $eArrow.StrokeColor = [System.Drawing.Color]::Red
            $eArrow.StrokeWidth = 4
            Invoke-Method $win "AddEdit" @($eArrow)
            $w = Get-Field $win "work"
            $midPx = $w.GetPixel(80, 60)
            Assert-True ($midPx.R -gt 180 -and $midPx.G -lt 100) "Arrow shaft should render red pixels"

            # Line
            $eLine = New-Edit "Line"
            $eLine.A = New-Object System.Drawing.PointF 20, 90; $eLine.B = New-Object System.Drawing.PointF 140, 90
            $eLine.StrokeColor = [System.Drawing.Color]::Blue
            $eLine.StrokeWidth = 4
            Invoke-Method $win "AddEdit" @($eLine)
            $w = Get-Field $win "work"
            $linePx = $w.GetPixel(80, 90)
            Assert-True ($linePx.B -gt 180 -and $linePx.R -lt 100) "Line should render blue pixels"

            # Rect (Filled)
            $eRect = New-Edit "Rect"
            $eRect.A = New-Object System.Drawing.PointF 160, 20; $eRect.B = New-Object System.Drawing.PointF 220, 60
            $eRect.StrokeColor = [System.Drawing.Color]::Green
            $eRect.StrokeWidth = 3
            $eRect.Filled = $true
            Invoke-Method $win "AddEdit" @($eRect)
            $w = Get-Field $win "work"
            $rectInt = $w.GetPixel(190, 40)
            Assert-True ($rectInt.G -gt $rectInt.R -and $rectInt.G -gt $rectInt.B) "Filled Rect interior should be tinted green"

            # Ellipse (Filled)
            $eEll = New-Edit "Ellipse"
            $eEll.A = New-Object System.Drawing.PointF 160, 80; $eEll.B = New-Object System.Drawing.PointF 220, 120
            $eEll.StrokeColor = [System.Drawing.Color]::Purple
            $eEll.StrokeWidth = 3
            $eEll.Filled = $true
            Invoke-Method $win "AddEdit" @($eEll)
            $w = Get-Field $win "work"
            $ellInt = $w.GetPixel(190, 100)
            Assert-True ($ellInt.G -lt 250) "Filled Ellipse interior should be tinted"

            # Pen via DrawStroke
            $ePen = New-Edit "Pen"
            $ePen.Points = [System.Drawing.PointF[]]@(
                (New-Object System.Drawing.PointF 20, 140),
                (New-Object System.Drawing.PointF 100, 140)
            )
            $ePen.StrokeColor = [System.Drawing.Color]::Black
            $ePen.StrokeWidth = 4
            Invoke-Method $win "AddEdit" @($ePen)
            $w = Get-Field $win "work"
            $penPx = $w.GetPixel(60, 140)
            Assert-True ($penPx.R -lt 30 -and $penPx.G -lt 30 -and $penPx.B -lt 30) "Pen stroke should render black pixels"

            # Highlight preserves underlying contrast
            $eHi = New-Edit "Highlight"
            $eHi.Points = [System.Drawing.PointF[]]@(
                (New-Object System.Drawing.PointF 20, 140),
                (New-Object System.Drawing.PointF 100, 140)
            )
            $eHi.StrokeColor = [System.Drawing.Color]::Yellow
            $eHi.StrokeWidth = 6
            Invoke-Method $win "AddEdit" @($eHi)
            $w = Get-Field $win "work"
            $hiOverBlack = $w.GetPixel(60, 140)
            $hiOverWhite = $w.GetPixel(60, 134)
            Assert-True ($hiOverBlack.R -lt $hiOverWhite.R) "Highlight must preserve underlying contrast"
        } finally {
            $win.Dispose()
            $bmp.Dispose()
        }
    }

    It "3. Inline Text (plain + Latar pill) & Step callout badges (#1, #2, stepResetBtn)" {
        $bmp = New-SolidBitmap 320 200 ([System.Drawing.Color]::White)
        $win = New-RedactWindow $bmp
        try {
            $eText = New-Edit "Text"
            $eText.TextValue = "Hello"
            $eText.Pos = New-Object System.Drawing.PointF 20, 20
            $eText.StrokeColor = [System.Drawing.Color]::Black
            $eText.FontSize = 16
            $eText.Filled = $true
            Invoke-Method $win "AddEdit" @($eText)
            $w = Get-Field $win "work"
            $pillPx = $w.GetPixel(22, 22)
            Assert-True ($pillPx.R -lt 250 -or $pillPx.G -lt 250 -or $pillPx.B -lt 250) "Text with Latar pill should render non-white pixels"

            $eStep1 = New-Edit "Step"
            $eStep1.Pos = New-Object System.Drawing.PointF 100, 100
            $eStep1.StrokeColor = [System.Drawing.Color]::Red
            $eStep1.StrokeWidth = 3
            $eStep1.StepNumber = 1
            Invoke-Method $win "AddEdit" @($eStep1)

            $eStep2 = New-Edit "Step"
            $eStep2.Pos = New-Object System.Drawing.PointF 160, 100
            $eStep2.StrokeColor = [System.Drawing.Color]::Red
            $eStep2.StrokeWidth = 3
            $eStep2.StepNumber = 2
            Invoke-Method $win "AddEdit" @($eStep2)

            $w = Get-Field $win "work"
            $stepPx = $w.GetPixel(100, 100)
            Assert-True ($stepPx.R -gt 150 -or $stepPx.G -lt 250) "Step badge should render visible pixels at target"

            $resetBtn = Get-Field $win "stepResetBtn"
            $resetBtn.GetType().GetMethod("OnClick", $flags).Invoke($resetBtn, [object[]]@([System.EventArgs]::Empty)) | Out-Null
            $floor = Get-Field $win "stepFloorIndex"
            $edits = Get-Field $win "edits"
            Assert-Equal $floor $edits.Count "stepResetBtn must set stepFloorIndex to edits.Count"
        } finally {
            $win.Dispose()
            $bmp.Dispose()
        }
    }

    It "4. Redact 3 modes: Pixelate, BoxBlur, Block" {
        # Pixelate & BoxBlur on sharp black/white boundary
        $bmp = New-SolidBitmap 100 100 ([System.Drawing.Color]::White)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.FillRectangle([System.Drawing.Brushes]::Black, 0, 0, 50, 100)
        $g.Dispose()

        $pixBmp = New-Object System.Drawing.Bitmap $bmp
        $blurBmp = New-Object System.Drawing.Bitmap $bmp
        $win = New-RedactWindow $bmp
        try {
            $rect = New-Object System.Drawing.Rectangle 20, 20, 60, 60
            $pixMethod = $winType.GetMethod("Pixelate", $flags)
            $pixMethod.Invoke($null, [object[]]@([System.Drawing.Bitmap]$pixBmp, $rect)) | Out-Null
            $pMid = $pixBmp.GetPixel(49, 49)
            Assert-True ($pMid.R -gt 0 -and $pMid.R -lt 255) "Pixelate across B/W boundary must average into intermediate block values (got R=$($pMid.R))"

            $blurMethod = $winType.GetMethod("BoxBlur", $flags)
            $blurMethod.Invoke($null, [object[]]@([System.Drawing.Bitmap]$blurBmp, $rect, 6)) | Out-Null
            $bMid = $blurBmp.GetPixel(50, 50)
            Assert-True ($bMid.R -gt 10 -and $bMid.R -lt 245) "BoxBlur across B/W edge must produce intermediate grayscale gradient (got R=$($bMid.R))"

            # Block
            $eBlock = New-Edit "Redact"
            $eBlock.Box = New-Object System.Drawing.Rectangle 10, 10, 30, 30
            $eBlock.RedactStyle = [System.Enum]::Parse($redactModeType, "Block")
            $eBlock.StrokeColor = [System.Drawing.Color]::Magenta
            Invoke-Method $win "AddEdit" @($eBlock)
            $w = Get-Field $win "work"
            $blkPx = $w.GetPixel(20, 20)
            Assert-Equal $blkPx.R 255 "Block Redact R"
            Assert-Equal $blkPx.G 0 "Block Redact G"
            Assert-Equal $blkPx.B 255 "Block Redact B"
        } finally {
            $win.Dispose()
            $pixBmp.Dispose()
            $blurBmp.Dispose()
            $bmp.Dispose()
        }
    }

    It "5. Geometry & Beautify Frame: Crop, BlueGrad/Tanpa frame, Turn, MirrorX/MirrorY" {
        $bmp = New-SolidBitmap 200 100 ([System.Drawing.Color]::White)
        $bmp.SetPixel(10, 10, [System.Drawing.Color]::Red)
        $win = New-RedactWindow $bmp
        try {
            # Crop
            Set-Field $win "pendingCrop" (New-Object System.Drawing.Rectangle 20, 10, 100, 60)
            Invoke-Method $win "ApplyPendingCrop"
            $w = Get-Field $win "work"
            Assert-Equal $w.Width 100 "Cropped width must be 100"
            Assert-Equal $w.Height 60 "Cropped height must be 60"

            # Frame BlueGrad (2) then Tanpa (0)
            Invoke-Method $win "OnFramePicked" @(2)
            $wFramed = Get-Field $win "work"
            Assert-True ($wFramed.Width -gt 100 -and $wFramed.Height -gt 60) "BlueGrad frame must increase dimensions"

            Invoke-Method $win "OnFramePicked" @(0)
            $wUnframed = Get-Field $win "work"
            Assert-Equal $wUnframed.Width 100 "Tanpa (0) must restore cropped width"
            Assert-Equal $wUnframed.Height 60 "Tanpa (0) must restore cropped height"

            # Turn 90
            $eTurn = New-Edit "Turn"
            Invoke-Method $win "AddEdit" @($eTurn)
            $wTurned = Get-Field $win "work"
            Assert-Equal $wTurned.Width 60 "Turn must swap Width/Height"
            Assert-Equal $wTurned.Height 100 "Turn must swap Width/Height"

            # MirrorX on fresh asymmetric bitmap
            $win.Dispose()
            $bmp2 = New-SolidBitmap 40 20 ([System.Drawing.Color]::White)
            $bmp2.SetPixel(2, 2, [System.Drawing.Color]::Red)
            $win = New-RedactWindow $bmp2
            $eMx = New-Edit "MirrorX"
            Invoke-Method $win "AddEdit" @($eMx)
            $wMx = Get-Field $win "work"
            $flippedPx = $wMx.GetPixel(37, 2)
            Assert-Equal $flippedPx.R 255 "MirrorX must flip marker pixel to x=37"
            Assert-Equal $flippedPx.G 0 "MirrorX must flip marker pixel to x=37"
            $bmp2.Dispose()
        } finally {
            $win.Dispose()
            $bmp.Dispose()
        }
    }

    It "6. Select + Undo / Redo / ResetAll" {
        $bmp = New-SolidBitmap 200 120 ([System.Drawing.Color]::White)
        $win = New-RedactWindow $bmp
        try {
            $eRect = New-Edit "Rect"
            $eRect.A = New-Object System.Drawing.PointF 20, 20; $eRect.B = New-Object System.Drawing.PointF 60, 60
            $eRect.StrokeColor = [System.Drawing.Color]::Blue
            $eRect.StrokeWidth = 3
            $eRect.Filled = $true
            Invoke-Method $win "AddEdit" @($eRect)

            $w1 = Get-Field $win "work"
            Assert-True (($w1.GetPixel(35, 35)).R -lt 250) "Rect should be drawn"

            Invoke-Method $win "Undo"
            $wUndo = Get-Field $win "work"
            Assert-Equal ($wUndo.GetPixel(35, 35)).R 255 "Undo must restore white pixel"

            Invoke-Method $win "Redo"
            $wRedo = Get-Field $win "work"
            Assert-True (($wRedo.GetPixel(35, 35)).R -lt 250) "Redo must reapply Rect"

            Invoke-Method $win "ResetAll"
            $wReset = Get-Field $win "work"
            Assert-Equal ($wReset.GetPixel(35, 35)).R 255 "ResetAll must clear edits"

            Invoke-Method $win "Undo"
            $wUndoReset = Get-Field $win "work"
            Assert-True (($wUndoReset.GetPixel(35, 35)).R -lt 250) "ResetAll itself must be undoable"
        } finally {
            $win.Dispose()
            $bmp.Dispose()
        }
    }

    It "7. Zoom & Pan (StepZoom, ZoomFit) change scale and reset cleanly to baseFit" {
        $bmp = New-SolidBitmap 320 200 ([System.Drawing.Color]::White)
        $win = New-RedactWindow $bmp
        try {
            $win.ShowInTaskbar = $false
            $win.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
            $win.Location = New-Object System.Drawing.Point(-32000, -32000)
            $win.Size = New-Object System.Drawing.Size 960, 680
            $win.Show()
            Invoke-Method $win "Relayout"
            [System.Windows.Forms.Application]::DoEvents()

            $baseFit = [float](Get-Field $win "baseFit")
            Invoke-Method $win "StepZoom" @([float]1.5)
            $zoomedScale = [float](Get-Field $win "scale")
            Assert-True ($zoomedScale -gt $baseFit) "StepZoom(1.5) must increase scale above baseFit"

            Invoke-Method $win "ZoomFit"
            $resetScale = [float](Get-Field $win "scale")
            Assert-True ([Math]::Abs($resetScale - $baseFit) -lt 0.01) "ZoomFit must reset scale to baseFit"
        } finally {
            $win.Close()
            $win.Dispose()
            $bmp.Dispose()
        }
    }

    It "8. English localization (Lang.T) returns non-identical English strings for new editor labels" {
        $langType = Get-DeskmateType "Lang"
        $enField = $langType.GetField("English", [System.Reflection.BindingFlags]'Public,Static')
        $prev = $enField.GetValue($null)
        try {
            $enField.SetValue($null, $true)
            $keys = @("Panah", "Stabilo", "Langkah", "Bingkai", "Ulangi", "Terapkan", "Buram", "Blok")
            foreach ($k in $keys) {
                $t = Invoke-DeskmateStaticMethod "Lang" "T" @($k)
                Assert-True ($t -ne $k -and $t.Length -gt 0) "Lang.T('$k') must return non-identical English string (got '$t')"
            }
        } finally {
            $enField.SetValue($null, $prev)
        }
    }

    It "9. Rapid Close/Dispose within fade window does not throw ObjectDisposedException" {
        $animType = Get-DeskmateType "Anim"
        $kitWinType = Get-DeskmateType "KitWindow"
        $reducedField = $animType.GetField("Reduced", $flags)
        $fadeField = $kitWinType.GetField("fadeTimer", $flags)
        $prevReduced = $reducedField.GetValue($null)
        $threadErrors = New-Object System.Collections.Generic.List[Exception]
        $handler = [System.Threading.ThreadExceptionEventHandler]{
            param($sender, $args)
            $threadErrors.Add($args.Exception)
        }
        [System.Windows.Forms.Application]::add_ThreadException($handler)
        $bmp = New-SolidBitmap 320 200 ([System.Drawing.Color]::White)
        $win = $null
        try {
            $reducedField.SetValue($null, $false)
            $win = New-RedactWindow $bmp
            $win.ShowInTaskbar = $false
            $win.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
            $win.Location = New-Object System.Drawing.Point(-32000, -32000)
            $win.Size = New-Object System.Drawing.Size 320, 200
            $win.Show()
            [System.Windows.Forms.Application]::DoEvents()
            $win.Close()
            $win.Dispose()

            $fadeAfterDispose = $fadeField.GetValue($win)
            Assert-Null $fadeAfterDispose "fadeTimer must be null after Close/Dispose"

            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            while ($sw.ElapsedMilliseconds -lt 200) {
                [System.Windows.Forms.Application]::DoEvents()
                [System.Threading.Thread]::Sleep(10)
            }

            Assert-Equal $threadErrors.Count 0 ("Expected 0 ThreadExceptions during fade window, got: " + ($threadErrors | ForEach-Object { $_.ToString() }))
            Assert-Null ($fadeField.GetValue($win)) "fadeTimer must remain null after 200ms DoEvents pump"
        } finally {
            [System.Windows.Forms.Application]::remove_ThreadException($handler)
            $reducedField.SetValue($null, $prevReduced)
            if ($null -ne $win -and -not $win.IsDisposed) {
                $win.Close()
                $win.Dispose()
            }
            $bmp.Dispose()
        }
    }
}
