<#
.SYNOPSIS
    Renders Deskmate's major surfaces offscreen, in dark and light theme, to PNG.

.DESCRIPTION
    Visual-verification harness for design-token work. Loads a built Deskmate exe through
    tests/common/DeskmateLoader.ps1, creates each window hidden and off screen, sizes it to a
    fixed client size, renders it with DrawToBitmap, and disposes it. Nothing is ever shown or
    activated, so focus is never stolen.

    Usage: powershell -File tests/render_surfaces.ps1 -BinaryPath Deskmate.inv.exe [-OutDir dir] [-Only 'prefs-*','island-*']

    Output: <OutDir>/<surface>-dark.png, <OutDir>/<surface>-light.png,
            <OutDir>/contact-dark.png, <OutDir>/contact-light.png, <OutDir>/report.txt
    A surface that throws is listed as FAIL in report.txt and left out of the contact sheet;
    the script still exits 0 so one broken window never hides the rest.

    Isolation: the script always re-runs itself in a fresh 64-bit STA Windows PowerShell child
    with DESKMATE_DIR / DESKMATE_HOME pointing at a temp profile (the same mechanism
    tests/run_e2e.ps1 uses), DESKMATE_DOCK_DRY=1, and a seeded settings.ini (solid material,
    no wallpaper sync). The user's %APPDATA%\Deskmate is never read or written. The temp profile
    is wiped before and after the run.

    Determinism: the render thread is forced DPI-unaware (96 DPI, KitWindow.Ui = 1), DESKMATE_SCALE=1,
    reduced motion is on (no fade-in), language stays Indonesian (app default), accent is
    monochrome and theme packs are cleared by Reset-DeskmateTheme. Two runs in the same minute
    give byte-identical PNGs. Surfaces that print the live clock, today's date, or battery
    (island-panel-0, dock-bar, sheet-calendar, task-editor due date) change with time.

    WM_PRINT gaps: DrawToBitmap leaves hosted WPF content black and edit/rich-edit text blank.
    SurfaceHelp.Overlay repaints those children on the capture: ElementHost through WPF
    RenderTargetBitmap, RichTextBox through EM_FORMATRANGE, and non-empty TextBox text through
    TextRenderer with the box's own font and colours (an approximation of the native edit).

    Layered surfaces:
    - Island: it is a WS_EX_LAYERED window presented with UpdateLayeredWindow, so DrawToBitmap
      shows nothing. Instead the script points Island's private Graphics/Painter at an offscreen
      bitmap and calls the private DrawView(view, w, h, now) for selected views and every panel
      page. The shell is filled with the opaque Ink.Base.Bg token instead of live glass, because
      glass is a screen-capture composite and therefore not deterministic. The Island handle is
      never created, so no hooks, app bar, or tray are installed. Views render with empty data
      (no media, agents, messages), i.e. their empty states.
    - Dock bar: DockWindow's canvas is built with Place() on a fixed 1280x800 monitor (no handle
      needed) and the private Compose/DrawBar/Dividers/DrawSpan/DrawCenter/DrawTop steps of
      Render() run without Present(), which would create the layered handle. The app scan runs
      only when the handle exists, so the bar shows widgets and the Work button but no app icons.
    - Dock sheets (GlassSheet): GlassSheet.OpenAt minus CreateHandle/Show/Present - Opening(),
      MakeCanvas(), open spring snapped, then the private Render() into the sheet's own canvas.

.PARAMETER BinaryPath
    Deskmate exe to load. Default: Deskmate.exe in the repo root. It is loaded, never run; build
    to a separate name (build.cmd Deskmate.inv.exe) when the app itself may be running.
.PARAMETER OutDir
    Output folder. Default: tests/out/surfaces. Old PNGs there are deleted on a full run.
.PARAMETER Only
    Optional surface-name wildcards to render only a subset (e.g. 'prefs-*').
    Set DESKMATE_SURFACES_DEBUG=1 for stack traces and a step log in <OutDir>/debug.log.
.PARAMETER Accent
    Accent to render with. Omitted: Ink.DefaultAccent, what a new install gets. '' renders
    Grafit (monochrome); '#RRGGBB' renders that accent. Applied the way the app does it: saved as
    the 'accent' setting and passed to Ink.SetAccent after every theme reset.
.PARAMETER Seed
    Fill the isolated profile with sample work data before rendering: three task lists, open,
    starred, due, overdue, completed and sub-tasks, and notes with label colours, tags, a pin
    and a checklist, so the Tasks/Notes surfaces show their non-empty states.
#>
param(
    [string]$BinaryPath = '',
    [string]$OutDir = '',
    [string[]]$Only = @(),
    [string]$Accent = $null,
    [switch]$Seed
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $BinaryPath) { $BinaryPath = Join-Path $root 'Deskmate.exe' }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'out\surfaces' }

# ---------------------------------------------------------------------------
# Parent: isolate in a fresh 64-bit STA child process
# ---------------------------------------------------------------------------
if ($env:DESKMATE_SURFACES_CHILD -ne '1') {
    $BinaryPath = (Resolve-Path -LiteralPath $BinaryPath).Path
    # An accent that does not parse would make Ink.SetAccent fall back to Grafit without a word,
    # so a shell-quoted or hash-less value ("'#AE9DEB'", "AE9DEB") silently rendered monochrome.
    # Normalise what is clearly meant, refuse the rest.
    if ($PSBoundParameters.ContainsKey('Accent') -and $Accent) {
        $Accent = $Accent.Trim().Trim("'", '"').Trim()
        if ($Accent -notmatch '^#') { $Accent = '#' + $Accent }
        if ($Accent -notmatch '^#[0-9A-Fa-f]{6}$') { throw "-Accent must be '' or #RRGGBB, got '$Accent'" }
    }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $OutDir = (Resolve-Path -LiteralPath $OutDir).Path
    # Stable per output folder: Settings > Umum prints the data folder, so a random name would
    # change the image, and one shared folder let a concurrent run into another output folder
    # wipe this run's profile underneath it. Wiped before and after.
    $outKey = [BitConverter]::ToString([Security.Cryptography.MD5]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($OutDir.ToLowerInvariant()))).Replace('-', '').Substring(0, 8)
    $profileDir = Join-Path ([IO.Path]::GetTempPath()) ('Deskmate\surfaces-profile-' + $outKey)
    Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue
    $dataDir = Join-Path $profileDir 'data'
    New-Item -ItemType Directory -Force -Path $dataDir, (Join-Path $profileDir 'home') | Out-Null
    # Opaque, wallpaper-independent look; the rest stays at app defaults
    Set-Content -LiteralPath (Join-Path $dataDir 'settings.ini') -Encoding UTF8 -Value @(
        'material=solid', 'glass=0', 'wall.sync=0', 'theme=gelap')

    $names = 'DESKMATE_SURFACES_CHILD', 'DESKMATE_DIR', 'DESKMATE_HOME', 'DESKMATE_DOCK_DRY', 'DESKMATE_SCALE', 'DESKMATE_TEST_BINARY',
        'DESKMATE_SURFACES_ACCENT', 'DESKMATE_SURFACES_SEED'
    $saved = @{}
    foreach ($n in $names) { $saved[$n] = [Environment]::GetEnvironmentVariable($n) }
    $ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $env:DESKMATE_SURFACES_CHILD = '1'
        $env:DESKMATE_DIR = $dataDir
        $env:DESKMATE_HOME = Join-Path $profileDir 'home'
        $env:DESKMATE_DOCK_DRY = '1'
        $env:DESKMATE_SCALE = '1'
        $env:DESKMATE_TEST_BINARY = $BinaryPath
        # An empty environment variable is a deleted one on Windows, so Grafit travels as 'none'
        $env:DESKMATE_SURFACES_ACCENT = $(if (-not $PSBoundParameters.ContainsKey('Accent')) { 'default' } elseif ([string]::IsNullOrEmpty($Accent)) { 'none' } else { $Accent })
        $env:DESKMATE_SURFACES_SEED = $(if ($Seed) { '1' } else { '0' })
        $childArgs = @('-NoProfile', '-NonInteractive', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-BinaryPath', $BinaryPath, '-OutDir', $OutDir)
        if ($Only.Count) { $childArgs += '-Only'; $childArgs += ($Only -join ',') }
        & $ps @childArgs
        $code = $LASTEXITCODE
    }
    finally {
        foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n, $saved[$n]) }
        Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host ("Runtime: {0:N1}s  Output: {1}" -f $sw.Elapsed.TotalSeconds, $OutDir)
    exit $code
}

# ---------------------------------------------------------------------------
# Child: render
# ---------------------------------------------------------------------------
if ($Only.Count -eq 1 -and $Only[0] -like '*,*') { $Only = $Only[0] -split ',' }

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, WindowsFormsIntegration
$wpfRefs = @('System.Windows.Forms', 'System.Drawing', 'System.Xaml',
    [Windows.Media.Visual].Assembly.Location, [Windows.Controls.Control].Assembly.Location,
    [Windows.DependencyObject].Assembly.Location, [Windows.Forms.Integration.ElementHost].Assembly.Location)
Add-Type -ReferencedAssemblies $wpfRefs -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

public static class SurfaceHelp
{
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref FORMATRANGE l);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    // DPI_AWARENESS_CONTEXT_UNAWARE: every window on this thread is 96 DPI
    public static void Unaware() { try { SetThreadDpiAwarenessContext(new IntPtr(-1)); } catch (EntryPointNotFoundException) { } }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct CHARRANGE { public int Min, Max; }
    [StructLayout(LayoutKind.Sequential)] struct FORMATRANGE { public IntPtr hdc, hdcTarget; public RECT rc, rcPage; public CHARRANGE chrg; }

    // WM_PRINT leaves hosted WPF content and rich edit text black or blank; paint those
    // children again on top of the captured client bitmap with their own renderers.
    public static void Overlay(Control root, Bitmap client)
    {
        using (var g = Graphics.FromImage(client)) Walk(root, root, g);
    }

    static void Walk(Control root, Control parent, Graphics g)
    {
        foreach (Control c in parent.Controls)
        {
            if (!c.IsHandleCreated || !IsVisibleStyle(c)) continue;
            var at = root.PointToClient(c.Parent.PointToScreen(c.Location));
            var r = new Rectangle(at, c.Size);
            var host = c as ElementHost;
            if (host != null && host.Child != null) { DrawWpf(host, g, r); continue; }
            var rich = c as RichTextBox;
            if (rich != null) { DrawRich(rich, g, r); continue; }
            var box = c as TextBox;
            if (box != null) { DrawBox(box, g, r); continue; }
            Walk(root, c, g);
        }
    }

    static bool IsVisibleStyle(Control c) { return (GetWindowLong(c.Handle, -16) & 0x10000000) != 0; }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);

    static void DrawWpf(ElementHost host, Graphics g, Rectangle r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using (var b = new SolidBrush(host.BackColor)) g.FillRectangle(b, r);
        var child = host.Child;
        var size = new System.Windows.Size(r.Width, r.Height);
        child.Measure(size);
        child.Arrange(new System.Windows.Rect(size));
        child.UpdateLayout();
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(r.Width, r.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(child);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using (var ms = new MemoryStream())
        {
            enc.Save(ms);
            ms.Position = 0;
            using (var img = new Bitmap(ms)) g.DrawImageUnscaled(img, r.Location);
        }
    }

    // Edit controls skip their text under a parent's WM_PRINT; empty boxes keep the capture
    // so cue banners survive.
    static void DrawBox(TextBox box, Graphics g, Rectangle r)
    {
        if (box.TextLength == 0) return;
        string s = box.UseSystemPasswordChar || box.PasswordChar != '\0' ? new string('\u25CF', box.TextLength) : box.Text;
        var inner = new Rectangle(r.X + (r.Width - box.ClientSize.Width) / 2, r.Y + (r.Height - box.ClientSize.Height) / 2, box.ClientSize.Width, box.ClientSize.Height);
        using (var b = new SolidBrush(box.BackColor)) g.FillRectangle(b, inner);
        var flags = TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | (box.Multiline ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine)
            | (box.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : box.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
        TextRenderer.DrawText(g, s, box.Font, inner, box.ForeColor, box.BackColor, flags);
    }

    static void DrawRich(RichTextBox rich, Graphics g, Rectangle r)
    {
        using (var b = new SolidBrush(rich.BackColor)) g.FillRectangle(b, r);
        if (rich.TextLength == 0 || !rich.IsHandleCreated) return;
        // EM_FORMATRANGE works in twips
        const double Twips = 1440.0 / 96.0;
        var inner = rich.ClientRectangle;
        var area = new RECT
        {
            Left = (int)((r.X + 1) * Twips), Top = (int)((r.Y + 1) * Twips),
            Right = (int)((r.X + inner.Width - 1) * Twips), Bottom = (int)((r.Y + inner.Height - 1) * Twips)
        };
        IntPtr hdc = g.GetHdc();
        try
        {
            var fr = new FORMATRANGE { hdc = hdc, hdcTarget = hdc, rc = area, rcPage = area, chrg = new CHARRANGE { Min = 0, Max = -1 } };
            SendMessage(rich.Handle, 0x0439, (IntPtr)1, ref fr);
            SendMessage(rich.Handle, 0x0439, IntPtr.Zero, IntPtr.Zero);
        }
        finally { g.ReleaseHdc(hdc); }
    }
}
'@
[SurfaceHelp]::Unaware()

. (Join-Path $PSScriptRoot 'common\DeskmateLoader.ps1')

$F = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$Ctl = [Windows.Forms.Control]
$createControl = $Ctl.GetMethod('CreateControl', [Reflection.BindingFlags]'NonPublic,Instance', $null, [Type[]]@([bool]), $null)
$onLoad = [Windows.Forms.Form].GetMethod('OnLoad', [Reflection.BindingFlags]'NonPublic,Instance')

Set-DeskmateStaticField 'Anim' 'Reduced' $true
$langType = Get-DeskmateType 'Lang'
$langType.GetField('English', $F).SetValue($null, $false)

# Type lookup without touching PowerShell's member adapter: some Deskmate types have members
# differing only by case (SelectedDay/selectedDay), which the adapter refuses to bind.
function TypeOf($obj) { return [Type]::GetTypeArray([object[]]@($obj))[0] }

function Get-Member2($obj, [string]$name) {
    $t = TypeOf $obj
    while ($t) {
        $fld = $t.GetField($name, $F); if ($fld) { return $fld.GetValue($obj) }
        $p = $t.GetProperty($name, $F); if ($p) { return $p.GetValue($obj, $null) }
        $t = $t.BaseType
    }
    throw "Member $name not found on $((TypeOf $obj).FullName)"
}

function Set-Member2($obj, [string]$name, $value) {
    $t = TypeOf $obj
    if ($value -is [psobject]) { $value = $value.psobject.BaseObject }
    while ($t) {
        $fld = $t.GetField($name, $F); if ($fld) { $fld.SetValue($obj, $value); return }
        $p = $t.GetProperty($name, $F); if ($p -and $p.CanWrite) { $p.SetValue($obj, $value, $null); return }
        $t = $t.BaseType
    }
    throw "Member $name not found on $((TypeOf $obj).FullName)"
}

function Invoke-Member2($obj, [string]$name, [object[]]$argv = @()) {
    $t = TypeOf $obj
    while ($t) {
        foreach ($m in $t.GetMethods($F)) {
            if ($m.Name -eq $name -and $m.DeclaringType -eq $t -and $m.GetParameters().Length -eq $argv.Length) { return $m.Invoke($obj, (Unwrap $argv)) }
        }
        $t = $t.BaseType
    }
    throw "Method $name/$($argv.Length) not found on $((TypeOf $obj).FullName)"
}

# PowerShell wraps pipeline values in PSObject; reflection binders need the real objects
function Unwrap([object[]]$argv) {
    $out = New-Object object[] $argv.Length
    for ($i = 0; $i -lt $argv.Length; $i++) {
        $a = $argv[$i]
        # No $(...) here: a subexpression would enumerate List arguments into object[]
        if ($a -is [psobject]) { $out[$i] = $a.psobject.BaseObject } else { $out[$i] = $a }
    }
    return , $out
}

function New-Deskmate([string]$typeName, [object[]]$argv = @()) {
    $t = Get-DeskmateType $typeName
    if (-not $t) { throw "Type $typeName not found" }
    # Instance only: with Static the type initializer also matches and binding turns ambiguous
    return [Activator]::CreateInstance($t, [Reflection.BindingFlags]'Public,NonPublic,Instance', $null, (Unwrap $argv), $null)
}

# Hidden, never shown: create handles, raise Load, size, render client area, dispose
function Trace([string]$msg) {
    if ($env:DESKMATE_SURFACES_DEBUG) { Add-Content -LiteralPath (Join-Path $OutDir 'debug.log') -Value "  $msg" }
}

function Ensure-Handles([Windows.Forms.Control]$c) {
    foreach ($child in @($c.Controls)) {
        if (-not $child.IsHandleCreated) { [void]$createControl.Invoke($child, @($true)) }
        Ensure-Handles $child
    }
}

function Render-Form([Windows.Forms.Form]$form, [int]$w, [int]$h, [scriptblock]$prep) {
    try {
        Trace 'created'
        $form.StartPosition = 'Manual'
        $form.ShowInTaskbar = $false
        $form.Location = New-Object Drawing.Point(-32000, -32000)
        [void]$createControl.Invoke($form, @($true))
        Trace 'handles'
        [void]$onLoad.Invoke($form, @([EventArgs]::Empty))
        Trace 'loaded'
        $form.Opacity = 1
        $form.Location = New-Object Drawing.Point(-32000, -32000)
        $form.ClientSize = New-Object Drawing.Size($w, $h)
        if ($prep) { & $prep $form }
        Trace 'prepared'
        $form.ClientSize = New-Object Drawing.Size($w, $h)
        $form.PerformLayout()
        # Controls added after the form got its handle stay handle-less while the form is
        # hidden, and WM_PRINT only reaches children that have a window: create them all.
        Ensure-Handles $form
        # No DoEvents: pumping lets deferred UI work (BeginInvoke, timers) run and some
        # surfaces never go idle; everything rendered here is laid out synchronously.
        $form.PerformLayout()
        Trace 'laid out'
        $full = New-Object Drawing.Bitmap($form.Width, $form.Height)
        try {
            $form.DrawToBitmap($full, (New-Object Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
            $origin = $form.PointToScreen([Drawing.Point]::Empty)
            $cx = $origin.X - $form.Left; $cy = $origin.Y - $form.Top
            $client = $full.Clone((New-Object Drawing.Rectangle($cx, $cy, $w, $h)), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            [SurfaceHelp]::Overlay($form, $client)
            return $client
        }
        finally { $full.Dispose() }
    }
    finally {
        if (-not $form.IsDisposed) { $form.Dispose() }
    }
}

# Island stand-in: never gets a handle; only its fields and drawing code are used
$script:isle = $null
function Get-Island {
    if (-not $script:isle) { $script:isle = New-Deskmate 'Island' }
    return $script:isle
}

$viewType = Get-DeskmateType 'View'

function Render-IslandView([string]$viewName, [int]$page = -1) {
    $isle = Get-Island
    $view = [Enum]::Parse($viewType, $viewName)
    if ($page -ge 0) { Set-Member2 $isle 'page' $page }
    Set-Member2 $isle 'current' $view
    Set-Member2 $isle 'previous' $view
    $size = Invoke-Member2 $isle 'ViewSize' @($view)
    $radius = [single](Invoke-Member2 $isle 'ViewRadius' @($view, $size))
    $pad = 24
    $bw = [int][Math]::Ceiling($size.Width) + $pad * 2
    $bh = [int][Math]::Ceiling($size.Height) + $pad * 2
    $bmp = New-Object Drawing.Bitmap($bw, $bh, [Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'AntiAlias'
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.PixelOffsetMode = 'HighQuality'
        $g.CompositingQuality = 'AssumeLinear'
        # Neutral backdrop so light and dark shells both read
        $g.Clear([Drawing.Color]::FromArgb(255, 128, 128, 128))
        $base = Get-DeskmateStaticField 'Ink' 'Base'
        $shell = New-Object Drawing.RectangleF($pad, $pad, $size.Width, $size.Height)
        $path = Invoke-DeskmateStaticMethod 'Ink' 'Round' @($shell, $radius)
        $brush = New-Object Drawing.SolidBrush($base.Bg)
        $g.FillPath($brush, $path); $brush.Dispose()
        $painter = Get-Member2 $isle 'p'
        Set-Member2 $isle 'g' $g
        Set-Member2 $painter 'G' $g
        Set-Member2 $painter 'T' $base
        Set-Member2 $painter 'Alpha' ([single]1)
        Set-Member2 $painter 'HalfPx' ([single]0.5)
        Set-Member2 $isle 'shell' $shell
        $g.SetClip($path)
        $g.TranslateTransform($pad, $pad)
        Set-Member2 $isle 'originX' ([single]$pad)
        Set-Member2 $isle 'originY' ([single]$pad)
        [void](Invoke-Member2 $isle 'DrawView' @($view, [single]$size.Width, [single]$size.Height, [DateTime]::Now))
        $path.Dispose()
    }
    finally {
        Set-Member2 $isle 'g' $null
        $g.Dispose()
    }
    $out = $bmp.Clone((New-Object Drawing.Rectangle(0, 0, $bw, $bh)), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bmp.Dispose()
    return $out
}

# Dock pieces get a fixed 1280x800 monitor so layout does not depend on the real screen
function New-DockWindow {
    $dw = New-Deskmate 'DockWindow' @((Get-Island))
    $dt = $dw.GetType()
    $iconsField = $dt.GetField('fallbackIcons', $F)
    if ($iconsField -and $null -eq $iconsField.GetValue($dw)) { $iconsField.SetValue($dw, [Activator]::CreateInstance($iconsField.FieldType, $true)) }
    $want = [int][Math]::Ceiling([double](Get-Member2 $dw 'BarH') + [double](Get-Member2 $dw 'Edge'))
    $dt.GetField('mon', $F).SetValue($dw, (New-Object Drawing.Rectangle(0, 0, 1280, 800)))
    $dt.GetField('area', $F).SetValue($dw, (New-Object Drawing.Rectangle(0, (800 - $want), 1280, $want)))
    $dt.GetField('scale', $F).SetValue($dw, [single]1)
    return $dw
}

# GlassSheet.OpenAt minus CreateHandle/Show/Present/Activate
function Render-Sheet([string]$typeName, [object[]]$extra = @()) {
    $dw = New-DockWindow
    $sheet = $null
    try {
        $sheet = New-Deskmate $typeName (@((Get-Island), $dw) + $extra)
        $gt = Get-DeskmateType 'GlassSheet'
        $mon = New-Object Drawing.Rectangle(0, 0, 1280, 800)
        $barTop = 800 - 64
        $gt.GetField('scale', $F).SetValue($sheet, [single]1)
        [void](Invoke-Member2 $sheet 'Opening' @($mon, $barTop))
        $W = [single](Get-Member2 $sheet 'W'); $H = [single](Get-Member2 $sheet 'H')
        $H = [Math]::Max(80, [Math]::Min($H, ($barTop - $mon.Top) - 40))
        Set-Member2 $sheet 'H' ([single]$H)
        [void](Invoke-Member2 $sheet 'MakeCanvas' @([int][Math]::Ceiling($W), [int][Math]::Ceiling($H)))
        $open = $gt.GetField('open', $F).GetValue($sheet)
        [void]$open.GetType().GetMethod('Snap', $F).Invoke($open, @([single]1))
        [void]$gt.GetMethod('Render', $F).Invoke($sheet, $null)
        $canvas = $gt.GetField('canvas', $F).GetValue($sheet)
        if (-not $canvas) { throw 'GlassSheet.canvas is null after Render()' }
        return Flatten $canvas
    }
    finally {
        if ($sheet) { [void]([IDisposable].GetMethod('Dispose').Invoke($sheet, $null)) }
        $dw.Dispose()
    }
}

# Layered canvases are premultiplied with transparent edges: put them on the neutral backdrop
function Flatten([Drawing.Bitmap]$src) {
    $out = New-Object Drawing.Bitmap($src.Width, $src.Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($out)
    $g.Clear([Drawing.Color]::FromArgb(255, 128, 128, 128))
    $g.DrawImageUnscaled($src, 0, 0)
    $g.Dispose()
    return $out
}

function Render-DockBar {
    $dw = New-DockWindow
    try {
        Set-Member2 $dw 'shown' $true
        Invoke-Member2 $dw 'Place'
        $reveal = Get-Member2 $dw 'reveal'
        [void]$reveal.GetType().GetMethod('Snap', $F).Invoke($reveal, @([single]1))
        $g = Get-Member2 $dw 'g'
        $painter = Get-Member2 $dw 'p'
        Set-Member2 $painter 'T' (Get-DeskmateStaticField 'Ink' 'Base')
        Set-Member2 $painter 'Alpha' ([single]1)
        Set-Member2 $painter 'HalfPx' ([single]0.5)
        $g.Clear([Drawing.Color]::Transparent)
        Invoke-Member2 $dw 'Compose'
        $center = Get-Member2 $dw 'centerPill'
        $left = Get-Member2 $dw 'leftPill'
        $right = Get-Member2 $dw 'rightPill'
        Invoke-Member2 $dw 'DrawBar' @($center, [single](Get-Member2 $dw 'Radius'), [bool](-not $left.IsEmpty -or -not $right.IsEmpty))
        Invoke-Member2 $dw 'Dividers'
        Invoke-Member2 $dw 'DrawSpan' @($left, $true)
        Invoke-Member2 $dw 'DrawSpan' @($right, $false)
        Invoke-Member2 $dw 'DrawCenter'
        Invoke-Member2 $dw 'DrawTop'
        $g.Flush()
        # Crop the full-monitor-wide canvas to the capsule plus a margin
        $bar = [Drawing.RectangleF]::Union([Drawing.RectangleF]::Union($center, $(if ($left.IsEmpty) { $center } else { $left })), $(if ($right.IsEmpty) { $center } else { $right }))
        $flat = Flatten (Get-Member2 $dw 'canvas')
        $crop = [Drawing.Rectangle]::Intersect((New-Object Drawing.Rectangle(0, 0, $flat.Width, $flat.Height)),
            [Drawing.Rectangle]::Round((New-Object Drawing.RectangleF(($bar.X - 24), ($bar.Y - 24), ($bar.Width + 48), ($bar.Height + 48)))))
        $out = $flat.Clone($crop, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $flat.Dispose()
        return $out
    }
    finally { $dw.Dispose() }
}

function New-SampleBitmap([int]$w, [int]$h) {
    $bmp = New-Object Drawing.Bitmap($w, $h)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $lg = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Rectangle(0, 0, $w, $h)), [Drawing.Color]::FromArgb(255, 40, 70, 120), [Drawing.Color]::FromArgb(255, 220, 160, 90), 35)
    $g.FillRectangle($lg, 0, 0, $w, $h); $lg.Dispose()
    $font = New-Object Drawing.Font('Segoe UI', 20)
    $g.DrawString('Sample screenshot: nama@contoh.id  0812-3456-7890', $font, [Drawing.Brushes]::White, 30, 40)
    $g.FillRectangle([Drawing.Brushes]::WhiteSmoke, 30, 110, $w - 60, $h - 150)
    $g.DrawString('Lorem ipsum dolor sit amet, rahasia token = abc123', $font, [Drawing.Brushes]::Black, 50, 140)
    $font.Dispose(); $g.Dispose()
    return $bmp
}

function Invoke-Static([string]$typeName, [string]$method, [object[]]$argv = @()) {
    $t = Get-DeskmateType $typeName
    $m = $t.GetMethods($F) | Where-Object { $_.Name -eq $method -and $_.GetParameters().Length -eq $argv.Length } | Select-Object -First 1
    if (-not $m) { throw "Method $typeName.$method/$($argv.Length) not found" }
    # Comma: an empty collection returned bare would unroll to $null
    return , $m.Invoke($null, (Unwrap $argv))
}

# Accent: omitted = what a new install gets (no saved key); otherwise saved like the picker does
$accentMode = $env:DESKMATE_SURFACES_ACCENT
if (-not $accentMode -or $accentMode -eq 'default') { $accentHex = [string](Get-DeskmateType 'Ink').GetField('DefaultAccent', $F).GetValue($null) }
elseif ($accentMode -eq 'none') { $accentHex = '' }
else { $accentHex = $accentMode }
$accentSaved = $accentMode -and $accentMode -ne 'default'

# Applied after every theme reset and checked, so a wrong accent fails the surface instead of
# quietly rendering Grafit
function Apply-Accent {
    Invoke-DeskmateStaticMethod 'Ink' 'SetAccent' @([string]$accentHex)
    if ($accentSaved) { (Get-Member2 (Get-Island) 'settings').Set('accent', $accentHex) }
    $base = Get-DeskmateStaticField 'Ink' 'Base'
    if ($accentHex -and $base.Accent.ToArgb() -eq $base.Text.ToArgb()) { throw "Accent $accentHex did not apply: Ink.Base.Accent is still the text ink" }
}

# Sample work data through the app's own model, so the files match whatever format it writes
function Seed-WorkData {
    $todoType = Get-DeskmateType 'TodoItem'
    $todos = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($todoType))
    $today = [DateTime]::Today
    $rows = @(
        @{ Title = 'Siapkan slide rapat mingguan'; Star = $true; Due = $today.AddHours(15) },
        @{ Title = 'Balas email dosen pembimbing'; Due = $today.AddDays(-1).AddHours(9) },
        @{ Title = 'Revisi bab 3: metodologi'; ListName = 'Kuliah'; Due = $today.AddDays(3).AddHours(23).AddMinutes(59); Notes = 'Tambahkan diagram alur dan contoh data.' },
        @{ Title = 'Baca jurnal pembanding'; ListName = 'Kuliah' },
        @{ Title = 'Bayar listrik'; ListName = 'Rumah'; Star = $true },
        @{ Title = 'Beli galon'; ListName = 'Rumah'; Done = $true; DoneAt = $today.AddHours(8) },
        @{ Title = 'Rapikan folder proyek'; Done = $true; DoneAt = $today.AddDays(-1).AddHours(17) },
        @{ Title = 'Kirim laporan keuangan'; Due = $today.AddDays(1).AddHours(10) }
    )
    $parent = $null
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $t = [Activator]::CreateInstance($todoType)
        Set-Member2 $t 'Position' $i
        Set-Member2 $t 'Created' $today.AddDays(-2).AddMinutes($i)
        foreach ($k in $rows[$i].Keys) {
            $v = $rows[$i][$k]
            if ($v -is [DateTime] -and $k -in 'Due', 'DoneAt') { $v = [Nullable[DateTime]]$v }
            Set-Member2 $t $k $v
        }
        if ($i -eq 2) { $parent = $t }
        [void]$todos.Add($t)
    }
    # One subtask under the thesis chapter
    $sub = [Activator]::CreateInstance($todoType)
    Set-Member2 $sub 'Title' 'Cek ulang rumus regresi'
    Set-Member2 $sub 'ListName' 'Kuliah'
    Set-Member2 $sub 'ParentId' (Get-Member2 $parent 'Id')
    [void]$todos.Add($sub)
    [void](Invoke-Static 'TodoFile' 'Save' (, $todos))
    $reg = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType((Get-DeskmateType 'TaskListInfo')))
    [void](Invoke-Static 'TaskLists' 'Ensure' @($reg, $todos))
    [void](Invoke-Static 'TaskLists' 'Save' (, $reg))

    $metaType = Get-DeskmateType 'NoteMeta'
    $meta = Invoke-Static 'NoteFiles' 'LoadMeta'
    $pins = Invoke-Static 'NoteFiles' 'LoadPins'
    $notes = @(
        @{ Title = 'Ide fitur rilis 2.9'; Body = "Dock bisa menampilkan cuaca per jam.`nTema mengikuti wallpaper.`n`n- [x] Kumpulkan masukan`n- [ ] Tulis spesifikasi`n- [ ] Prototipe"; Color = 'violet'; Labels = 'kerja,ide'; Pin = $true },
        @{ Title = 'Belanja mingguan'; Body = "- [ ] Beras 5 kg`n- [x] Telur`n- [ ] Sabun cuci"; Color = 'emerald'; Labels = 'rumah' },
        @{ Title = 'Catatan rapat 18 Nov'; Body = 'Target sprint: perbaiki sinkron Google Tasks, lalu rapikan tampilan catatan.'; Color = 'amber'; Labels = 'kerja' },
        @{ Title = 'Kutipan'; Body = 'Kesederhanaan adalah kecanggihan tertinggi.'; Color = ''; Labels = '' }
    )
    foreach ($n in $notes) {
        $path = Invoke-Static 'NoteFiles' 'NewPath'
        [void](Invoke-Static 'NoteFiles' 'Write' @($path, (Invoke-Static 'NoteFiles' 'JoinNote' @($n.Title, $n.Body))))
        $key = Invoke-Static 'NoteFiles' 'KeyOf' @($path)
        $m = [Activator]::CreateInstance($metaType)
        Set-Member2 $m 'Key' $key; Set-Member2 $m 'Color' $n.Color; Set-Member2 $m 'Labels' $n.Labels
        $meta[$key] = $m
        if ($n.Pin) { [void](Invoke-Static 'NoteFiles' 'SetPin' @($pins, $path, $true)) }
    }
    [void](Invoke-Static 'NoteFiles' 'SaveMeta' (, $meta))
}
if ($env:DESKMATE_SURFACES_SEED -eq '1') {
    try { Seed-WorkData }
    catch {
        $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
        throw "Seeding sample work data failed: $($e.GetType().Name): $($e.Message)`n$($e.StackTrace)`n$($_.ScriptStackTrace)"
    }
}

# ---------------------------------------------------------------------------
# Surface catalogue: name -> scriptblock returning a Bitmap
# ---------------------------------------------------------------------------
$surfaces = [ordered]@{}

foreach ($sec in 'general', 'look', 'island', 'dock', 'work', 'about') {
    $id = $sec
    $surfaces["prefs-$id"] = [scriptblock]::Create("Render-Form (New-Deskmate 'PrefsWindow' @(Get-Island)) 1040 720 { param(`$win) `$win.Go('$id') }")
}
# The board/list choice is saved in settings, so every task surface sets it explicitly
function Set-TaskMode($win, [int]$mode) {
    $win.SelectTab(0)
    (Get-Member2 $win 'taskMode').Selected = $mode
    [void](Invoke-Member2 $win 'ApplyTab')
    [void](Invoke-Member2 $win 'RefillTasks' @([string]$null))
}
$surfaces['work-tasks'] = { Render-Form (New-Deskmate 'WorkWindow' @(Get-Island)) 980 680 { param($win) Set-TaskMode $win 0 } }
$surfaces['work-notes'] = { Render-Form (New-Deskmate 'WorkWindow' @(Get-Island)) 980 680 { param($win) $win.SelectTab(1) } }
$surfaces['work-board'] = { Render-Form (New-Deskmate 'WorkWindow' @(Get-Island)) 980 680 { param($win) Set-TaskMode $win 1 } }
# Narrow window: rail hides and the title doubles as the list picker
$surfaces['work-tasks-compact'] = { Render-Form (New-Deskmate 'WorkWindow' @(Get-Island)) 560 600 { param($win) Set-TaskMode $win 0 } }
$surfaces['tour'] = {
    $stepType = Get-DeskmateType 'TourStep'
    $steps = [Array]::CreateInstance($stepType, 2)
    $titles = @('Ini Deskmate', 'Island di tepi atas ini membesar saat ada yang terjadi: lagu diputar, pesan masuk, agent minta izin, atau unduhan berjalan.'),
              @('Buka panel', 'Arahkan kursor ke island di atas. Panelnya terbuka sendiri, dan tertutup lagi saat kursor pergi.')
    for ($i = 0; $i -lt 2; $i++) {
        $s = [Activator]::CreateInstance($stepType, $true)
        Set-Member2 $s 'Title' $titles[$i][0]; Set-Member2 $s 'Body' $titles[$i][1]
        $steps.SetValue($s, $i)
    }
    Render-Form (New-Deskmate 'TourCard' @((Get-Island), $steps)) 420 260 $null
}
$surfaces['whatsnew'] = {
    $noteType = Get-DeskmateType 'WhatsNew+Note'
    $notes = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($noteType))
    $all = (Get-DeskmateType 'WhatsNew').GetField('Notes', $F).GetValue($null)
    [void]$notes.Add($all[0]); [void]$notes.Add($all[1])
    Render-Form (New-Deskmate 'WhatsNewWindow' (, $notes)) 560 520 $null
}
$surfaces['kitask-confirm'] = { Render-Form (New-Deskmate 'KitAsk' @('Hapus tugas "Laporan mingguan"? Tindakan ini tidak bisa dibatalkan.', $true, 'Hapus', 'Batal', $null, 'Tutup')) 420 150 $null }
$surfaces['kitask-notice'] = { Render-Form (New-Deskmate 'KitAsk' @('Pengaturan berhasil diekspor ke Dokumen.', $false, '', '', $null, 'Tutup')) 420 130 $null }
$surfaces['google-event'] = { Render-Form (New-Deskmate 'GoogleEventDialog' @((Get-Island), [DateTime]'2026-11-18')) 520 560 $null }
$surfaces['prompter-setup'] = { Render-Form (New-Deskmate 'PrompterSetup' @("Selamat pagi semuanya.`nHari ini kita membahas rilis 2.9.", 140, 34, $false, 85, $false)) 720 560 $null }
$surfaces['suruh-chat'] = { Render-Form (New-Deskmate 'ChatWindow' @(Get-Island)) 520 640 $null }
$surfaces['agent-prompt'] = {
    $session = [Activator]::CreateInstance((Get-DeskmateType 'AgentSession'), $true)
    Render-Form (New-Deskmate 'AgentPromptBox' @((Get-Island), $session)) 560 360 $null
}
$surfaces['redact'] = { Render-Form (New-Deskmate 'RedactWindow' @(New-SampleBitmap 900 500)) 1100 720 $null }
$surfaces['wifi-pass'] = {
    $net = [Activator]::CreateInstance((Get-DeskmateType 'Wifi+Net'), $true)
    Set-Member2 $net 'Ssid' 'Kantor-5G'
    Render-Form (New-Deskmate 'WifiPassBox' @($net, $null)) 400 220 $null
}
$surfaces['feedback'] = { Render-Form (New-Deskmate 'FeedbackWindow') 520 460 $null }
$surfaces['words'] = { Render-Form (New-Deskmate 'WordsWindow') 520 520 $null }
$surfaces['task-editor'] = {
    $item = [Activator]::CreateInstance((Get-DeskmateType 'TodoItem'), $true)
    Set-Member2 $item 'Title' 'Siapkan slide rapat'
    Render-Form (New-Deskmate 'TaskEditor' @($item, $null)) 520 560 $null
}
$surfaces['list-name'] = { Render-Form (New-Deskmate 'ListNameDialog' @('Daftar baru', 'Kuliah', $false, '')) 380 170 $null }
$surfaces['priority-prompt'] = {
    $l = New-Object 'Collections.Generic.List[string]'; $l.Add('Revisi bab 3'); $l.Add('Balas email dosen')
    Render-Form (New-Deskmate 'PriorityPromptDialog' (, $l)) 460 360 $null
}
$surfaces['note-box'] = {
    $t = New-Object 'Collections.Generic.List[string]'; $t.Add('Catatan'); $t.Add('Ide')
    Render-Form (New-Deskmate 'NoteBox' @($t, 'Catatan', $null)) 460 300 $null
}
$surfaces['sheet-calendar'] = { Render-Sheet 'CalendarSheet' }
$surfaces['sheet-quick'] = { Render-Sheet 'QuickSheet' }
$surfaces['sheet-start'] = { Render-Sheet 'StartCard' }
$surfaces['sheet-weather'] = { Render-Sheet 'WeatherSheet' }
$surfaces['dock-bar'] = { Render-DockBar }
foreach ($v in 'Idle', 'Volume', 'Timer') {
    $surfaces["island-$($v.ToLower())"] = [scriptblock]::Create("Render-IslandView '$v'")
}
$pageNames = (Get-DeskmateType 'Island').GetField('PageNames', $F).GetValue($null)
for ($i = 0; $i -lt $pageNames.Length; $i++) {
    $surfaces["island-panel-$i-$($pageNames[$i].ToLower())"] = [scriptblock]::Create("Render-IslandView 'Panel' $i")
}

if ($Only.Count) {
    $keep = [ordered]@{}
    foreach ($k in $surfaces.Keys) { foreach ($pat in $Only) { if ($k -like $pat) { $keep[$k] = $surfaces[$k]; break } } }
    $surfaces = $keep
}

# ---------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
# Stale images from an earlier run would hide a surface that now fails
if (-not $Only.Count) { Get-ChildItem -LiteralPath $OutDir -Filter '*.png' | Remove-Item -Force }
$report = New-Object Collections.Generic.List[string]
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($theme in 'dark', 'light') {
    $rendered = New-Object Collections.Generic.List[object]
    foreach ($name in $surfaces.Keys) {
        Reset-DeskmateTheme -Light ($theme -eq 'light')
        # Theme pickers read the saved mode: keep it in step with what is being rendered
        (Get-Member2 (Get-Island) 'settings').Set('theme', $(if ($theme -eq 'light') { 'terang' } else { 'gelap' }))
        if ($env:DESKMATE_SURFACES_DEBUG) { Add-Content -LiteralPath (Join-Path $OutDir 'debug.log') -Value "BEGIN $name $theme" }
        $t0 = $sw.ElapsedMilliseconds
        try {
            Apply-Accent
            $bmp = & $surfaces[$name]
            if ($bmp -is [array]) { $bmp = $bmp | Where-Object { $_ -is [Drawing.Bitmap] } | Select-Object -Last 1 }
            if (-not ($bmp -is [Drawing.Bitmap])) { throw 'renderer returned no bitmap' }
            $file = Join-Path $OutDir "$name-$theme.png"
            $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
            $rendered.Add(@($name, $bmp))
            $line = "OK    {0,-34} {1,-5} {2}x{3}  {4} ms" -f $name, $theme, $bmp.Width, $bmp.Height, ($sw.ElapsedMilliseconds - $t0)
        }
        catch {
            $e = $_.Exception
            while ($e.InnerException) { $e = $e.InnerException }
            $line = "FAIL  {0,-34} {1,-5} {2}: {3}" -f $name, $theme, $e.GetType().Name, ($e.Message -replace '\s+', ' ')
            if ($env:DESKMATE_SURFACES_DEBUG) { Write-Host $_.ScriptStackTrace; Write-Host $e.StackTrace }
        }
        Write-Host $line
        $report.Add($line)
    }

    # Contact sheet: fixed 3-column grid, each cell scaled to fit 480x360, labelled
    $cols = 3; $cellW = 480; $cellH = 360; $label = 22; $gap = 12
    if ($rendered.Count) {
        $rows = [Math]::Ceiling($rendered.Count / $cols)
        $sheet = New-Object Drawing.Bitmap(($cols * ($cellW + $gap) + $gap), ($rows * ($cellH + $label + $gap) + $gap))
        $g = [Drawing.Graphics]::FromImage($sheet)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.Clear($(if ($theme -eq 'light') { [Drawing.Color]::FromArgb(255, 210, 210, 210) } else { [Drawing.Color]::FromArgb(255, 18, 18, 18) }))
        $font = New-Object Drawing.Font('Segoe UI', 10, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
        $ink = $(if ($theme -eq 'light') { [Drawing.Brushes]::Black } else { [Drawing.Brushes]::White })
        for ($i = 0; $i -lt $rendered.Count; $i++) {
            $n = $rendered[$i][0]; $b = $rendered[$i][1]
            $x = $gap + ($i % $cols) * ($cellW + $gap)
            $y = $gap + [Math]::Floor($i / $cols) * ($cellH + $label + $gap)
            $g.DrawString("$n  ($($b.Width)x$($b.Height))", $font, $ink, $x, $y + 4)
            $k = [Math]::Min(1.0, [Math]::Min($cellW / $b.Width, $cellH / $b.Height))
            $g.DrawImage($b, [single]$x, [single]($y + $label), [single]($b.Width * $k), [single]($b.Height * $k))
            $b.Dispose()
        }
        $font.Dispose(); $g.Dispose()
        $sheet.Save((Join-Path $OutDir "contact-$theme.png"), [Drawing.Imaging.ImageFormat]::Png)
        $sheet.Dispose()
    }
}
if ($script:isle) { $script:isle.Dispose() }
$ok = ($report | Where-Object { $_ -like 'OK*' }).Count
$fail = ($report | Where-Object { $_ -like 'FAIL*' }).Count
$report.Add("TOTAL ok=$ok fail=$fail elapsed=$([Math]::Round($sw.Elapsed.TotalSeconds, 1))s")
$report | Set-Content -LiteralPath (Join-Path $OutDir 'report.txt') -Encoding UTF8
Write-Host "TOTAL ok=$ok fail=$fail"
exit 0
