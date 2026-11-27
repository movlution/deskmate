using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    // Opaque window colours from island tokens
    static class KitInk
    {
        // Last input was the mouse: no rings
        public static bool ByMouse;

        static Color O(Color c) { return Color.FromArgb(255, c.R, c.G, c.B); }
        // Calm material: a canvas in the wallpaper hue at the theme's lightness; null keeps the theme tone
        public static Color? Canvas;
        static Color Bg { get { return Canvas ?? Ink.Base.Bg; } }
        // Calm surfaces from the tinted tones: canvas, navigation, cards, separators, hover.
        public static Color Window { get { return O(Bg); } }
        public static Color Side { get { return O(Ink.Mix(Bg, Color.Black, Ink.Light ? 0.035f : 0.055f)); } }
        public static Color Group { get { return O(Ink.Light ? Ink.Mix(Bg, Color.White, 0.85f) : Ink.Mix(Bg, Ink.Base.Text, 0.07f)); } }
        public static Color Line { get { return O(Ink.Light ? Ink.Mix(Bg, Color.Black, 0.10f) : Ink.Mix(Bg, Ink.Base.Text, 0.12f)); } }
        public static Color Hover { get { return O(Ink.Light ? Ink.Mix(Bg, Color.Black, 0.06f) : Ink.Mix(Bg, Ink.Base.Text, 0.09f)); } }
        // Selected segment, lifted above its track
        public static Color Raised { get { return Ink.Light ? Color.White : O(Ink.Mix(Bg, Ink.Base.Text, 0.22f)); } }
        public static Color Text { get { return O(Ink.Base.Text); } }
        // Light Sub sits above Faint so secondary text keeps hierarchy and >= 4.5:1 on Line
        public static Color Sub { get { return O(Ink.Mix(Bg, Ink.Base.Text, Ink.Light ? 0.74f : 0.72f)); } }
        // 0.69 light / 0.66 dark guarantee >= 4.5:1 across Window, Side, Group, Hover, and Line
        public static Color Faint { get { return O(Ink.Mix(Bg, Ink.Base.Text, Ink.Light ? 0.69f : 0.66f)); } }
        // Disabled chrome (switch knob, slider fill): dimmer than tertiary text so
        // raising Faint for readability does not make disabled controls look enabled
        public static Color Disabled { get { return O(Ink.Mix(Bg, Ink.Base.Text, 0.30f)); } }
        // Grafit accent: switches and primary buttons use text colour
        public static Color Accent { get { return O(Ink.Base.Accent); } }
        public static Color OnAccent { get { return O(Ink.Base.OnAccent); } }
        // Keyboard focus: the accent where it reads on the canvas (>= 3:1), else primary ink
        public static Color Focus { get { var a = Accent; return Island.Contrast(a, Window) >= 3 ? a : Text; } }
        // Thumbs and knobs: paper white on light, ivory on night
        public static Color Knob { get { return Ink.Light ? Color.White : Text; } }
        // Canvas mixed toward primary ink: control fills that step evenly in both themes
        public static Color Tint(float k) { return O(Ink.Mix(Bg, Ink.Base.Text, k)); }
        // A colour pushed away from the ink drawn on it, so hover and press only gain contrast
        public static Color Deepen(Color fill, Color ink, float k) { return Ink.Mix(fill, ink.GetBrightness() < 0.5f ? Color.White : Color.Black, k); }

        // One brush, pen and path per UI thread: Kit paints allocate nothing
        [ThreadStatic] static SolidBrush brush;
        [ThreadStatic] static Pen pen;
        [ThreadStatic] static GraphicsPath shape;

        public static SolidBrush BrushOf(Color c)
        {
            if (brush == null) brush = new SolidBrush(c); else brush.Color = c;
            return brush;
        }

        public static Pen PenOf(Color c, float width)
        {
            if (pen == null) pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            else { pen.Color = c; pen.Width = width; }
            return pen;
        }

        public static GraphicsPath Shape(RectangleF r, float radius)
        {
            if (shape == null) shape = new GraphicsPath(); else shape.Reset();
            Ink.AddRound(shape, r, radius);
            return shape;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c) { g.FillPath(BrushOf(c), Shape(r, radius)); }
        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c, float width) { g.DrawPath(PenOf(c, width), Shape(r, radius)); }

        // Elevation 1: contact shadow plus a short falloff below, never a halo
        public static void Lift(Graphics g, RectangleF r, float radius, float s)
        {
            var ink = Ink.Light ? Color.FromArgb(255, 0x2A, 0x20, 0x38) : Color.Black;
            int a = Ink.Light ? 26 : 90;
            FillRound(g, new RectangleF(r.X - 0.5f * s, r.Y + 1.5f * s, r.Width + s, r.Height + 0.5f * s), radius + 0.5f * s, Color.FromArgb(a / 2, ink));
            FillRound(g, new RectangleF(r.X, r.Y + 0.75f * s, r.Width, r.Height), radius, Color.FromArgb(a, ink));
        }

        // Keyboard focus: 2px ring in Focus with a hairline of the surface inside it,
        // so the ring reads against the canvas and against whatever fill it hugs
        public static void FocusRing(Graphics g, RectangleF ring, float radius, Color surface, float s)
        {
            float inset = 1.5f * s;
            StrokeRound(g, RectangleF.Inflate(ring, -inset, -inset), Math.Max(0, radius - inset), surface, 1.25f * s);
            StrokeRound(g, ring, radius, Focus, 2f * s);
        }

        // First opaque colour behind a control
        public static Color Under(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
            {
                var row = p as KitRow;
                if (row != null) return row.SurfaceColor();
                if (p is KitGroup) return Group;
                if (p.BackColor.A == 255) return p.BackColor;
            }
            return Window;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        static StringFormat glyphFormat;

        public static void Draw(Graphics g, string s, Font f, Color c, RectangleF r, ContentAlignment align)
        {
            // Lone icon glyph: centre its ink, not its line box
            if (s != null && s.Length == 1 && align == ContentAlignment.MiddleCenter && f.Name == Ink.IconFamily)
            {
                if (glyphFormat == null)
                {
                    glyphFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
                    glyphFormat.FormatFlags |= StringFormatFlags.NoWrap;
                    glyphFormat.Alignment = StringAlignment.Center;
                    glyphFormat.LineAlignment = StringAlignment.Center;
                }
                var n = Ink.IconNudge(s[0], f.Size);
                float half = g.PixelOffsetMode == PixelOffsetMode.HighQuality || g.PixelOffsetMode == PixelOffsetMode.Half ? 0.5f : 0;
                float cx = r.X + r.Width / 2 + n.X + half, cy = r.Y + r.Height / 2 + n.Y + half;
                g.DrawString(s, f, KitInk.BrushOf(c), new RectangleF(cx - f.Size, cy - f.Size, f.Size * 2, f.Size * 2), glyphFormat);
                return;
            }
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            if (align == ContentAlignment.MiddleCenter) flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            else if (align == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
            else flags |= TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c, flags);
        }
    }

    // Base: own HWND, focusable, window DPI
    // TextBox with Ctrl+Backspace and Ctrl+Delete
    class KitText : TextBox
    {
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && !e.Alt && (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) && !ReadOnly)
            {
                DeleteWord(e.KeyCode == Keys.Back);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        static int Kind(char c) { return char.IsWhiteSpace(c) ? 0 : char.IsLetterOrDigit(c) || c == '_' ? 1 : 2; }

        void DeleteWord(bool back)
        {
            if (SelectionLength == 0)
            {
                string x = Text;
                int at = SelectionStart, i = at;
                if (back)
                {
                    while (i > 0 && Kind(x[i - 1]) == 0) i--;
                    if (i > 0) { int k = Kind(x[i - 1]); while (i > 0 && Kind(x[i - 1]) == k) i--; }
                    Select(i, at - i);
                }
                else
                {
                    while (i < x.Length && Kind(x[i]) == 0) i++;
                    if (i < x.Length) { int k = Kind(x[i]); while (i < x.Length && Kind(x[i]) == k) i++; }
                    Select(at, i - at);
                }
            }
            // Keeps undo working
            SelectedText = "";
        }
    }

    abstract class KitControl : Control
    {
        protected KitControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = true;
            BackColor = Color.Transparent;
        }

        public float Ui { get { var w = FindForm() as KitWindow; return w != null ? w.Ui : 1f; } }
        protected bool Hovered;

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { KitInk.ByMouse = true; base.OnMouseDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (KitInk.ByMouse) { KitInk.ByMouse = false; Invalidate(); } base.OnKeyUp(e); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // Focus ring only for keyboard use, kept clear of the fill by a hairline of the surface
        protected void Ring(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues || KitInk.ByMouse) return;
            float s = Ui, grow = 2.5f * s;
            var ring = Inside(RectangleF.Inflate(r, grow, grow), Width, Height);
            KitInk.FocusRing(g, ring, Math.Min(radius + grow, ring.Height / 2), KitInk.Under(this), s);
        }

        // Rings stay whole inside the control
        public static RectangleF Inside(RectangleF r, int w, int h)
        {
            float x0 = Math.Max(r.Left, 1f), y0 = Math.Max(r.Top, 1f), x1 = Math.Min(r.Right, w - 1.5f), y1 = Math.Min(r.Bottom, h - 1.5f);
            return RectangleF.FromLTRB(x0, y0, Math.Max(x0 + 1, x1), Math.Max(y0 + 1, y1));
        }

        protected void Fill(Graphics g, RectangleF r, float radius, Color c) { KitInk.FillRound(g, r, radius, c); }
    }

    // Timers only request frames. Late frames sample the current time, not the next step.
    struct KitGlide
    {
        float from;
        double start;
        public void Begin(float position) { from = position; start = KitAnim.Now; }

        public float Sample(float target, float seconds)
        {
            float t = (float)((KitAnim.Now - start) / Math.Max(0.01f, Anim.Response(seconds)));
            if (Anim.Reduced || t >= 1f) return target;
            return from + (target - from) * Ease.OutCubic(t);
        }
    }

    // Shared VSync-paced animation clock for Kit / Settings controls.
    // Sleeps at 0% CPU when idle; ticks once per DWM refresh (60/120/144Hz) with Stopwatch dt.
    static class KitAnim
    {
        static readonly Stopwatch clock = Stopwatch.StartNew();
        [ThreadStatic] static Clock threadClock;

        public static double Now { get { return clock.Elapsed.TotalSeconds; } }

        // Frame-rate-independent exponential smoothing factor
        public static float Decay(float dt, float tau)
        {
            float t = Math.Max(0.012f, Anim.Response(tau));
            return 1f - (float)Math.Exp(-dt / t);
        }

        internal static void Attach(KitPulse pulse)
        {
            var c = threadClock;
            if (c == null) threadClock = c = new Clock();
            c.Add(pulse);
        }

        internal static void Detach(KitPulse pulse)
        {
            var c = threadClock;
            if (c != null) c.Remove(pulse);
        }

        sealed class Clock
        {
            [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
            const int WmFrame = 0x8000 + 73;

            readonly List<KitPulse> pulses = new List<KitPulse>();
            readonly List<KitPulse> scratch = new List<KitPulse>();
            readonly AutoResetEvent wake = new AutoResetEvent(false);
            readonly Sink sink;
            volatile IntPtr hwnd;
            volatile bool run = true, want;
            int posted;

            public Clock()
            {
                sink = new Sink(this);
                hwnd = sink.Handle;
                Application.ApplicationExit += OnExit;
                new Thread(Loop) { IsBackground = true, Name = "kit frames" }.Start();
            }

            void OnExit(object sender, EventArgs e)
            {
                run = false;
                want = false;
                hwnd = IntPtr.Zero;
                wake.Set();
            }

            public void Add(KitPulse p)
            {
                if (!pulses.Contains(p)) pulses.Add(p);
                if (!run) return;
                if (hwnd == IntPtr.Zero)
                {
                    sink.EnsureHandle();
                    hwnd = sink.Handle;
                }
                if (!want)
                {
                    want = true;
                    wake.Set();
                }
            }

            public void Remove(KitPulse p)
            {
                pulses.Remove(p);
                if (pulses.Count == 0) want = false;
            }

            void OnFrame()
            {
                try
                {
                    double now = KitAnim.Now;
                    scratch.Clear();
                    for (int i = 0; i < pulses.Count; i++) scratch.Add(pulses[i]);
                    for (int i = 0; i < scratch.Count; i++)
                    {
                        var p = scratch[i];
                        if (!p.Tick(now)) pulses.Remove(p);
                    }
                    scratch.Clear();
                }
                finally
                {
                    bool more = pulses.Count > 0;
                    if (!more) want = false;
                    Interlocked.Exchange(ref posted, 0);
                    if (more && run) { want = true; wake.Set(); }
                }
            }

            void Loop()
            {
                var sw = Stopwatch.StartNew();
                double last = -100;
                const double MinFrameMs = 6.5;
                while (run)
                {
                    if (!want) { wake.WaitOne(); continue; }
                    if (Native.DwmFlush() != 0) Thread.Sleep(12);
                    if (!run || !want) continue;
                    double now = sw.Elapsed.TotalMilliseconds;
                    double gap = now - last;
                    if (gap < MinFrameMs)
                    {
                        int sleep = (int)Math.Ceiling(MinFrameMs - gap);
                        if (sleep > 0) Thread.Sleep(sleep);
                        now = sw.Elapsed.TotalMilliseconds;
                    }
                    if (!run || !want) continue;
                    if (Interlocked.CompareExchange(ref posted, 1, 0) != 0) continue;
                    last = now;
                    IntPtr h = hwnd;
                    if (h == IntPtr.Zero || !PostMessage(h, WmFrame, IntPtr.Zero, IntPtr.Zero))
                    {
                        Interlocked.Exchange(ref posted, 0);
                        want = false;
                    }
                }
            }

            sealed class Sink : NativeWindow
            {
                readonly Clock owner;
                public Sink(Clock owner)
                {
                    this.owner = owner;
                    EnsureHandle();
                }
                public void EnsureHandle()
                {
                    if (Handle == IntPtr.Zero)
                        CreateHandle(new CreateParams { Parent = (IntPtr)(-3) });
                }
                protected override void WndProc(ref Message m)
                {
                    if (m.Msg == WmFrame) { owner.OnFrame(); return; }
                    base.WndProc(ref m);
                }
            }
        }
    }

    sealed class KitPulse : IDisposable
    {
        readonly Func<float, bool> step;
        bool running, disposed;
        double lastT;

        public KitPulse(Func<float, bool> step) { this.step = step; }
        public bool Running { get { return running; } }

        public void Start()
        {
            if (disposed || running) return;
            running = true;
            lastT = KitAnim.Now;
            KitAnim.Attach(this);
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            KitAnim.Detach(this);
        }

        internal bool Tick(double now)
        {
            if (!running || disposed) return false;
            float dt = (float)Math.Max(0.001, Math.Min(0.04, now - lastT));
            lastT = now;
            bool keep = step(dt);
            if (!keep) running = false;
            return keep && running && !disposed;
        }

        public void Dispose()
        {
            disposed = true;
            Stop();
        }
    }

    // iOS switch over CheckBox for readers
    class KitSwitch : CheckBox
    {
        bool hovered, pressed;
        float knob, hoverT, pressT;
        readonly Spring knobSpring = new Spring(0, 1, 1, 0.002f);
        KitPulse pulse;

        bool AnimRunning { get { return pulse != null && pulse.Running; } }
        void StopAnim() { if (pulse != null) pulse.Stop(); }

        bool StepAnim(float dt)
        {
            if (IsDisposed || !Visible || Anim.Reduced)
            {
                knob = Checked ? 1f : 0f;
                knobSpring.Snap(knob);
                hoverT = hovered ? 1f : 0f;
                pressT = pressed ? 1f : 0f;
                Invalidate();
                return false;
            }
            knobSpring.Target = Checked ? 1f : 0f;
            knobSpring.Step(dt);
            knob = knobSpring.Value;
            float hWant = hovered ? 1f : 0f;
            float pWant = pressed ? 1f : 0f;
            float rate = KitAnim.Decay(dt, 0.065f);
            hoverT += (hWant - hoverT) * rate;
            pressT += (pWant - pressT) * rate;
            if (Math.Abs(hWant - hoverT) < 0.008f) hoverT = hWant;
            if (Math.Abs(pWant - pressT) < 0.008f) pressT = pWant;
            bool more = !(knobSpring.Settled && hoverT == hWant && pressT == pWant);
            if (!more)
            {
                knob = knobSpring.Target;
                knobSpring.Snap(knob);
            }
            Invalidate();
            return more;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; StartAnim(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; StartAnim(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            KitInk.ByMouse = true;
            if (e.Button == MouseButtons.Left && Enabled) { pressed = true; StartAnim(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (pressed) { pressed = false; StartAnim(); }
            base.OnMouseUp(e);
        }
        protected override void OnKeyUp(KeyEventArgs e) { if (KitInk.ByMouse) { KitInk.ByMouse = false; Invalidate(); } base.OnKeyUp(e); }

        void StartAnim()
        {
            if (!IsHandleCreated || !Visible || Anim.Reduced)
            {
                knob = Checked ? 1f : 0f;
                knobSpring.Snap(knob);
                hoverT = hovered ? 1f : 0f;
                pressT = pressed ? 1f : 0f;
                StopAnim();
                Invalidate();
                return;
            }
            Springs.Tune(knobSpring, 0.18f, 0.86f);
            knobSpring.Target = Checked ? 1f : 0f;
            if (pulse == null) pulse = new KitPulse(StepAnim);
            pulse.Start();
            Invalidate();
        }

        public KitSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = "";
            CheckedChanged += delegate
            {
                float target = Checked ? 1f : 0f;
                if (!IsHandleCreated || !Visible || Anim.Reduced)
                {
                    knob = target;
                    knobSpring.Snap(target);
                    StopAnim();
                    Invalidate();
                }
                else
                {
                    knobSpring.Value = knob;
                    StartAnim();
                }
            };
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            knob = Checked ? 1f : 0f;
            knobSpring.Snap(knob);
            hoverT = hovered ? 1f : 0f;
            pressT = pressed ? 1f : 0f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            var under = KitInk.Under(this);
            g.Clear(under);
            // Keep reflection-set states (tests) synchronized when no animation is running
            float ht = AnimRunning ? hoverT : (hovered ? 1f : 0f);
            float pt = AnimRunning ? pressT : (pressed ? 1f : 0f);
            float k = Ease.Clamp01(knob);
            float s = Height / 24f;
            var track = new RectangleF(1, (Height - 22 * s) / 2, 40 * s, 22 * s);
            bool live = Enabled;
            // Off: quiet tinted track; on: accent. Disabled keeps a trace of the state.
            Color trackBg = live ? Ink.Mix(KitInk.Line, KitInk.Accent, k) : Ink.Mix(KitInk.Line, KitInk.Accent, 0.3f * k);
            if (ht > 0.001f && live)
            {
                Color hoverBg = k > 0.5f ? KitInk.Deepen(trackBg, KitInk.OnAccent, 0.08f) : Ink.Mix(trackBg, KitInk.Text, 0.07f);
                trackBg = Ink.Mix(trackBg, hoverBg, ht);
            }
            KitInk.FillRound(g, track, track.Height / 2, trackBg);

            float r = (8f + 0.55f * ht * (live ? 1f : 0f)) * s;
            // Allow subtle spring overshoot on position, clamped inside the track pill
            float travel = Math.Max(-0.08f, Math.Min(1.08f, knob));
            float cx = track.X + 11 * s + travel * 18 * s, cy = track.Y + 11 * s;
            // Tactile stretch in transit + press-down elongation toward the opposite side
            float transitStretch = live && !Anim.Reduced ? 4.8f * s * k * (1f - k) : 0f;
            float pressStretch = live && !Anim.Reduced ? 2.8f * s * pt : 0f;
            float stretch = transitStretch + pressStretch;
            float leftGrow = Checked ? stretch : 0f;
            float rightGrow = Checked ? 0f : stretch;
            if (transitStretch > 0.01f) { leftGrow = Math.Max(leftGrow, transitStretch * 0.5f); rightGrow = Math.Max(rightGrow, transitStretch * 0.5f); }
            float minX = track.X + 2.5f * s, maxX = track.Right - 2.5f * s;
            float x0 = Math.Max(minX, cx - r - leftGrow);
            float x1 = Math.Min(maxX, cx + r + rightGrow);
            var dot = RectangleF.FromLTRB(x0, cy - r, Math.Max(x0 + r * 2, x1), cy + r);
            Color restKnob = Ink.Mix(KitInk.Sub, Ink.Mix(KitInk.Sub, KitInk.Text, 0.35f), ht);
            Color knobColor = live ? Ink.Mix(restKnob, KitInk.OnAccent, k) : KitInk.Disabled;
            if (live) KitInk.Lift(g, dot, r, s * (0.6f + 0.15f * ht));
            KitInk.FillRound(g, dot, r, knobColor);

            if (Focused && ShowFocusCues && !KitInk.ByMouse)
            {
                var ring = KitControl.Inside(RectangleF.Inflate(track, 2.5f * s, 2.5f * s), Width, Height);
                KitInk.FocusRing(g, ring, ring.Height / 2, under, Math.Max(1f, s));
            }
        }

        protected override void Dispose(bool disposing) { if (disposing && pulse != null) pulse.Dispose(); base.Dispose(disposing); }
    }

    // Soft segmented choices; the thumb glides with spring physics, arrows move between
    class KitSegmented : KitControl
    {
        public string[] Items = new string[0];
        int selected, hot = -1, pressedAt = -1;
        // Thumb position in segments, gliding toward the pick; -1 until first paint
        float glide = -1;
        readonly Spring glideSpring = new Spring(0, 1, 1, 0.002f);
        KitPulse pulse;
        public event Action<int> Changed;

        bool StepAnim(float dt)
        {
            if (IsDisposed || !Visible || Anim.Reduced)
            {
                glide = selected;
                glideSpring.Snap(selected);
                Invalidate();
                return false;
            }
            glideSpring.Target = selected;
            glideSpring.Step(dt);
            glide = glideSpring.Value;
            bool more = !glideSpring.Settled;
            if (!more)
            {
                glide = selected;
                glideSpring.Snap(selected);
            }
            Invalidate();
            return more;
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                if (IsHandleCreated && Visible && glide >= 0 && glide != selected && !Anim.Reduced)
                {
                    Springs.Tune(glideSpring, 0.19f, 0.88f);
                    glideSpring.Value = glide;
                    glideSpring.Target = selected;
                    if (pulse == null) pulse = new KitPulse(StepAnim);
                    pulse.Start();
                }
                else
                {
                    glide = selected;
                    glideSpring.Snap(selected);
                    if (pulse != null) pulse.Stop();
                }
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.Selection, selected);
            }
        }

        RectangleF Cell(int i)
        {
            float w = (Width - 1f) / Math.Max(1, Items.Length);
            return new RectangleF(i * w, 0, w, Height - 1);
        }

        int HitCell(Point pt)
        {
            for (int i = 0; i < Items.Length; i++) if (Cell(i).Contains(pt)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int at = HitCell(e.Location);
            if (at != hot) { hot = at; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled && e.Button == MouseButtons.Left)
            {
                pressedAt = HitCell(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; pressedAt = -1; base.OnMouseLeave(e); }

        void Pick(int i)
        {
            if (!Enabled || i < 0 || i >= Items.Length || i == selected) return;
            Selected = i;
            var h = Changed;
            if (h != null) h(i);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressedAt = -1;
            if (!Enabled || e.Button != MouseButtons.Left) { Invalidate(); return; }
            Focus();
            int at = HitCell(e.Location);
            if (at >= 0) Pick(at);
            else Invalidate();
        }

        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled || e.Modifiers != Keys.None) return;
            if (e.KeyCode == Keys.Left) Pick(selected - 1);
            else if (e.KeyCode == Keys.Right) Pick(selected + 1);
            else if (e.KeyCode == Keys.Home) Pick(0);
            else if (e.KeyCode == Keys.End) Pick(Items.Length - 1);
            else return;
            KitInk.ByMouse = false;
            e.Handled = e.SuppressKeyPress = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            float s = Ui, radius = Math.Min(Ink.RadiusControl * s, (Height - 1) * 0.35f), inset = 3 * s;
            var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, box, radius, KitInk.Line);
            if (glide < 0) { glide = selected; glideSpring.Snap(selected); }
            if (selected >= 0 && selected < Items.Length)
            {
                float w = (Width - 1f) / Items.Length;
                float clampedGlide = Math.Max(-0.04f, Math.Min(Items.Length - 0.96f, glide));
                float pressShrink = (Enabled && pressedAt == selected && !Anim.Reduced) ? 0.75f * s : 0f;
                var thumb = new RectangleF(clampedGlide * w + inset + pressShrink, inset + pressShrink * 0.5f, w - 2 * (inset + pressShrink), Height - 1 - 2 * (inset + pressShrink * 0.5f));
                if (Enabled) KitInk.Lift(g, thumb, Math.Max(2f, radius - inset), s);
                Fill(g, thumb, Math.Max(2f, radius - inset), Enabled ? KitInk.Raised : KitInk.Group);
            }
            Font f = Ink.Text(12.5f * s), fs = Ink.Semi(12.5f * s);
            for (int i = 0; i < Items.Length; i++)
                KitInk.Draw(g, Items[i], i == selected ? fs : f, !Enabled ? KitInk.Faint : i == selected || i == hot ? KitInk.Text : KitInk.Sub, Cell(i), ContentAlignment.MiddleCenter);
            Ring(g, box, radius);
        }

        protected override void Dispose(bool disposing) { if (disposing && pulse != null) pulse.Dispose(); base.Dispose(disposing); }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

        class Access : ControlAccessibleObject
        {
            readonly KitSegmented owner;
            public Access(KitSegmented owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
            public override int GetChildCount() { return owner.Items.Length; }
            public override AccessibleObject GetChild(int i) { return i >= 0 && i < owner.Items.Length ? new Item(owner, i) : null; }
            public override AccessibleObject GetSelected() { return GetChild(owner.selected); }
            public override AccessibleObject GetFocused() { return owner.Focused ? GetChild(owner.selected) : null; }
        }

        class Item : AccessibleObject
        {
            readonly KitSegmented owner;
            readonly int index;
            public Item(KitSegmented owner, int index) { this.owner = owner; this.index = index; }
            public override string Name { get { return owner.Items[index]; } }
            public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override AccessibleStates State
            {
                get { return AccessibleStates.Selectable | AccessibleStates.Focusable | (!owner.Enabled ? AccessibleStates.Unavailable : 0) | (owner.Focused && index == owner.selected ? AccessibleStates.Focused : 0) | (index == owner.selected ? AccessibleStates.Selected | AccessibleStates.Checked : 0); }
            }
            public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(index))); } }
            public override string DefaultAction { get { return Lang.T("Pilih"); } }
            public override void DoDefaultAction() { owner.Pick(index); }
        }
    }

}
