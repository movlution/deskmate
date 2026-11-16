using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Edit a picture before sharing: annotate, blur, crop, frame, turn, mirror
    partial class Island
    {
        void OpenRedact(string path)
        {
            Bitmap pic;
            try
            {
                // Own copy: the file stays unlocked
                using (var fs = File.OpenRead(path))
                using (var img = Image.FromStream(fs)) pic = new Bitmap(img);
            }
            catch (Exception ex) { Log.Write("redact open: " + ex.Message); ShowInfo('', Lang.T("Gambar tidak bisa dibuka"), "", true); return; }
            OpenRedactBitmap(pic);
        }

        // The editor takes ownership of this bitmap.
        void OpenRedactBitmap(Bitmap pic)
        {
            DismissToast();
            var w = new RedactWindow(pic);
            // Shot preview already says it
            w.Copied += delegate { if (!ShotsOn) ShowInfo('', Lang.T("Gambar disalin"), "", true); };
            w.Show();
            w.Activate();
        }
    }

    enum ToolMode
    {
        Select,
        Arrow,
        Line,
        Rect,
        Ellipse,
        Pen,
        Highlight,
        Text,
        Step,
        Redact,
        Crop
    }

    // Crisp GDI+ vector tool strip matching Deskmate's KitControl language
    class RedactToolStrip : KitControl
    {
        struct ToolItem
        {
            public ToolMode Mode;
            public string LabelKey;
            public char Shortcut;
        }

        static readonly ToolItem[] Items =
        {
            new ToolItem { Mode = ToolMode.Select, LabelKey = "Pilih", Shortcut = 'V' },
            new ToolItem { Mode = ToolMode.Arrow, LabelKey = "Panah", Shortcut = 'A' },
            new ToolItem { Mode = ToolMode.Line, LabelKey = "Garis", Shortcut = 'L' },
            new ToolItem { Mode = ToolMode.Rect, LabelKey = "Kotak", Shortcut = 'R' },
            new ToolItem { Mode = ToolMode.Ellipse, LabelKey = "Elips", Shortcut = 'O' },
            new ToolItem { Mode = ToolMode.Pen, LabelKey = "Coret", Shortcut = 'P' },
            new ToolItem { Mode = ToolMode.Highlight, LabelKey = "Stabilo", Shortcut = 'H' },
            new ToolItem { Mode = ToolMode.Text, LabelKey = "Teks", Shortcut = 'T' },
            new ToolItem { Mode = ToolMode.Step, LabelKey = "Langkah", Shortcut = 'N' },
            new ToolItem { Mode = ToolMode.Redact, LabelKey = "Samarkan", Shortcut = 'B' },
            new ToolItem { Mode = ToolMode.Crop, LabelKey = "Potong", Shortcut = 'C' },
        };

        readonly ToolTip tip = new ToolTip { InitialDelay = 350, ReshowDelay = 120, AutoPopDelay = 5000 };
        int selected;
        int hovered = -1;

        public event Action<ToolMode> ToolChanged;

        public ToolMode SelectedTool
        {
            get { return Items[selected].Mode; }
            set
            {
                int idx = IndexOf(value);
                if (idx >= 0 && idx != selected)
                {
                    selected = idx;
                    Invalidate();
                    AccessibilityNotifyClients(AccessibleEvents.Selection, selected);
                }
            }
        }

        public int PreferredWidth(float ui)
        {
            return (int)Math.Ceiling(Items.Length * 32 * ui + 8 * ui);
        }

        static int IndexOf(ToolMode m)
        {
            for (int i = 0; i < Items.Length; i++)
                if (Items[i].Mode == m) return i;
            return 0;
        }

        public void Pick(ToolMode m)
        {
            PickIndex(IndexOf(m));
        }

        void PickIndex(int i)
        {
            if (i < 0 || i >= Items.Length || i == selected) return;
            selected = i;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Selection, selected);
            var h = ToolChanged;
            if (h != null) h(Items[i].Mode);
        }

        RectangleF Cell(int i)
        {
            float pad = 3f * Ui;
            float avail = Math.Max(1f, Width - 1f - pad * 2f);
            float w = avail / Items.Length;
            return new RectangleF(pad + i * w, pad, w, Math.Max(1f, Height - 1f - pad * 2f));
        }

        int HitCell(Point p)
        {
            for (int i = 0; i < Items.Length; i++)
                if (Cell(i).Contains(p)) return i;
            return -1;
        }

        string TipFor(int i)
        {
            return Lang.T(Items[i].LabelKey) + " (" + Items[i].Shortcut + ")";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitCell(e.Location);
            if (hit != hovered)
            {
                hovered = hit;
                tip.SetToolTip(this, hit >= 0 ? TipFor(hit) : "");
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered != -1)
            {
                hovered = -1;
                tip.SetToolTip(this, "");
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int hit = HitCell(e.Location);
            if (hit >= 0) PickIndex(hit);
        }

        protected override bool IsInputKey(Keys key)
        {
            return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) PickIndex((selected - 1 + Items.Length) % Items.Length);
            else if (e.KeyCode == Keys.Right) PickIndex((selected + 1) % Items.Length);
            else if (e.KeyCode == Keys.Home) PickIndex(0);
            else if (e.KeyCode == Keys.End) PickIndex(Items.Length - 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = track.Height / 2f;
            Fill(g, track, rad, KitInk.Line);

            float s = Ui;
            for (int i = 0; i < Items.Length; i++)
            {
                var c = Cell(i);
                var pill = RectangleF.Inflate(c, -1f * s, 0f);
                if (i == selected)
                    Fill(g, pill, pill.Height / 2f, KitInk.Raised);
                else if (i == hovered)
                    Fill(g, pill, pill.Height / 2f, KitInk.Hover);

                Color fg = i == selected ? KitInk.Text : (i == hovered ? KitInk.Text : KitInk.Sub);
                DrawIcon(g, Items[i].Mode, c, fg, s);
            }

            Ring(g, track, rad);
        }

        static void DrawIcon(Graphics g, ToolMode mode, RectangleF cell, Color fg, float s)
        {
            float cx = cell.X + cell.Width / 2f;
            float cy = cell.Y + cell.Height / 2f;
            float u = Math.Min(cell.Width, cell.Height) * 0.58f;
            float x0 = cx - u / 2f;
            float y0 = cy - u / 2f;
            float stroke = Math.Max(1.3f, 1.55f * s);

            switch (mode)
            {
                case ToolMode.Select:
                    {
                        var pts = new[]
                        {
                            new PointF(x0 + u * 0.22f, y0 + u * 0.12f),
                            new PointF(x0 + u * 0.22f, y0 + u * 0.86f),
                            new PointF(x0 + u * 0.42f, y0 + u * 0.67f),
                            new PointF(x0 + u * 0.56f, y0 + u * 0.90f),
                            new PointF(x0 + u * 0.68f, y0 + u * 0.84f),
                            new PointF(x0 + u * 0.54f, y0 + u * 0.60f),
                            new PointF(x0 + u * 0.80f, y0 + u * 0.58f),
                        };
                        using (var b = new SolidBrush(fg)) g.FillPolygon(b, pts);
                        break;
                    }
                case ToolMode.Arrow:
                    {
                        float ax = x0 + u * 0.18f, ay = y0 + u * 0.82f;
                        float bx = x0 + u * 0.82f, by = y0 + u * 0.18f;
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(p, ax, ay, bx - u * 0.18f, by + u * 0.18f);
                        var head = new[]
                        {
                            new PointF(bx, by),
                            new PointF(bx - u * 0.36f, by + u * 0.04f),
                            new PointF(bx - u * 0.04f, by + u * 0.36f),
                        };
                        using (var b = new SolidBrush(fg)) g.FillPolygon(b, head);
                        break;
                    }
                case ToolMode.Line:
                    {
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(p, x0 + u * 0.18f, y0 + u * 0.82f, x0 + u * 0.82f, y0 + u * 0.18f);
                        using (var b = new SolidBrush(fg))
                        {
                            float r = 1.6f * s;
                            g.FillEllipse(b, x0 + u * 0.18f - r, y0 + u * 0.82f - r, r * 2, r * 2);
                            g.FillEllipse(b, x0 + u * 0.82f - r, y0 + u * 0.18f - r, r * 2, r * 2);
                        }
                        break;
                    }
                case ToolMode.Rect:
                    {
                        var r = new RectangleF(x0 + u * 0.14f, y0 + u * 0.20f, u * 0.72f, u * 0.60f);
                        using (var path = Ink.Round(r, 2.2f * s))
                        using (var p = new Pen(fg, stroke))
                            g.DrawPath(p, path);
                        break;
                    }
                case ToolMode.Ellipse:
                    {
                        var r = new RectangleF(x0 + u * 0.14f, y0 + u * 0.18f, u * 0.72f, u * 0.64f);
                        using (var p = new Pen(fg, stroke))
                            g.DrawEllipse(p, r);
                        break;
                    }
                case ToolMode.Pen:
                    {
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            g.DrawBezier(p,
                                new PointF(x0 + u * 0.15f, y0 + u * 0.76f),
                                new PointF(x0 + u * 0.36f, y0 + u * 0.18f),
                                new PointF(x0 + u * 0.60f, y0 + u * 0.86f),
                                new PointF(x0 + u * 0.85f, y0 + u * 0.24f));
                        }
                        break;
                    }
                case ToolMode.Highlight:
                    {
                        using (var b = new SolidBrush(Color.FromArgb(110, fg)))
                            g.FillRectangle(b, x0 + u * 0.14f, y0 + u * 0.68f, u * 0.72f, u * 0.20f);
                        var nib = new[]
                        {
                            new PointF(x0 + u * 0.26f, y0 + u * 0.62f),
                            new PointF(x0 + u * 0.62f, y0 + u * 0.14f),
                            new PointF(x0 + u * 0.78f, y0 + u * 0.28f),
                            new PointF(x0 + u * 0.42f, y0 + u * 0.74f),
                        };
                        using (var p = new Pen(fg, stroke) { LineJoin = LineJoin.Round })
                            g.DrawPolygon(p, nib);
                        break;
                    }
                case ToolMode.Text:
                    {
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(p, x0 + u * 0.18f, y0 + u * 0.22f, x0 + u * 0.82f, y0 + u * 0.22f);
                            g.DrawLine(p, cx, y0 + u * 0.22f, cx, y0 + u * 0.84f);
                            g.DrawLine(p, x0 + u * 0.36f, y0 + u * 0.84f, x0 + u * 0.64f, y0 + u * 0.84f);
                        }
                        break;
                    }
                case ToolMode.Step:
                    {
                        var r = new RectangleF(x0 + u * 0.14f, y0 + u * 0.14f, u * 0.72f, u * 0.72f);
                        using (var b = new SolidBrush(fg)) g.FillEllipse(b, r);
                        Color inv = KitInk.Window;
                        if (Island.Contrast(inv, fg) < 3.0)
                            inv = Island.Contrast(Color.White, fg) >= Island.Contrast(Color.Black, fg) ? Color.White : Color.Black;
                        using (var f = new Font(Ink.MenuFamily, Math.Max(8f, u * 0.56f), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(inv))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("1", f, b, new RectangleF(r.X, r.Y + 0.5f * s, r.Width, r.Height), sf);
                        break;
                    }
                case ToolMode.Redact:
                    {
                        float cellSz = u * 0.24f;
                        float gap = u * 0.04f;
                        float sx = cx - (cellSz * 3 + gap * 2) / 2f;
                        float sy = cy - (cellSz * 3 + gap * 2) / 2f;
                        using (var b1 = new SolidBrush(fg))
                        using (var b2 = new SolidBrush(Color.FromArgb(120, fg)))
                        {
                            for (int row = 0; row < 3; row++)
                                for (int col = 0; col < 3; col++)
                                    g.FillRectangle((row + col) % 2 == 0 ? b1 : b2,
                                        sx + col * (cellSz + gap), sy + row * (cellSz + gap), cellSz, cellSz);
                        }
                        break;
                    }
                case ToolMode.Crop:
                    {
                        using (var p = new Pen(fg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(p, x0 + u * 0.26f, y0 + u * 0.10f, x0 + u * 0.26f, y0 + u * 0.74f);
                            g.DrawLine(p, x0 + u * 0.26f, y0 + u * 0.74f, x0 + u * 0.90f, y0 + u * 0.74f);
                            g.DrawLine(p, x0 + u * 0.10f, y0 + u * 0.26f, x0 + u * 0.74f, y0 + u * 0.26f);
                            g.DrawLine(p, x0 + u * 0.74f, y0 + u * 0.26f, x0 + u * 0.74f, y0 + u * 0.90f);
                        }
                        break;
                    }
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new StripAccess(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        class StripAccess : ControlAccessibleObject
        {
            readonly RedactToolStrip owner;
            public StripAccess(RedactToolStrip owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.ToolBar; } }
            public override int GetChildCount() { return Items.Length; }
            public override AccessibleObject GetChild(int i) { return new ItemAccess(owner, i); }
            public override AccessibleObject GetSelected() { return GetChild(owner.selected); }
            public override AccessibleObject GetFocused() { return owner.Focused ? GetChild(owner.selected) : null; }
        }

        class ItemAccess : AccessibleObject
        {
            readonly RedactToolStrip owner;
            readonly int index;
            public ItemAccess(RedactToolStrip owner, int index) { this.owner = owner; this.index = index; }
            public override string Name { get { return owner.TipFor(index); } }
            public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override AccessibleStates State
            {
                get
                {
                    return AccessibleStates.Selectable | AccessibleStates.Focusable |
                        (index == owner.selected ? AccessibleStates.Selected | AccessibleStates.Checked : 0);
                }
            }
            public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(index))); } }
            public override string DefaultAction { get { return Lang.T("Pilih"); } }
            public override void DoDefaultAction() { owner.PickIndex(index); }
        }
    }

    class RedactWindow : KitWindow
    {
        enum RedactMode { Pixelate, Blur, Block }
        enum FramePreset { None, Card, BlueGrad, SunsetGrad, MintGrad, Dark }
        enum EditKind { Redact, Pen, Highlight, Line, Arrow, Rect, Ellipse, Text, Step, Crop, Turn, MirrorX, MirrorY, Frame }

        class Edit
        {
            public EditKind Kind;
            public RedactMode RedactStyle;
            public FramePreset Frame;
            public Rectangle Box;
            public PointF A;
            public PointF B;
            public PointF[] Points;
            public PointF Pos;
            public Color StrokeColor;
            public float StrokeWidth;
            public bool Filled;
            public string TextValue;
            public float FontSize;
            public int StepNumber;

            public Edit Clone()
            {
                var copy = (Edit)MemberwiseClone();
                if (Points != null) copy.Points = (PointF[])Points.Clone();
                return copy;
            }
        }

        enum SelectDragKind { None, Move, Handle, EndpointA, EndpointB }

        static readonly int[] StrokeWidths = { 3, 6, 12 };
        static readonly string[][] StrokeColors =
        {
            new[] { "Merah", "#FF3B30" }, new[] { "Kuning", "#FFD24A" }, new[] { "Hijau", "#3DD68C" }, new[] { "Biru", "#4C8DFF" },
            new[] { "Oranye", "#FF9F43" }, new[] { "Ungu", "#A78BFA" }, new[] { "Putih", "#FFFFFF" }, new[] { "Hitam", "#111111" },
        };

        readonly Bitmap original;
        Bitmap baseWork;
        int cachedBaseCount = -1;
        Bitmap work;

        readonly List<Edit> edits = new List<Edit>();
        readonly Stack<List<Edit>> undoStack = new Stack<List<Edit>>();
        readonly Stack<List<Edit>> redoStack = new Stack<List<Edit>>();
        readonly List<PointF> stroke = new List<PointF>();

        readonly RedactToolStrip toolStrip = new RedactToolStrip();
        readonly KitSwatch inkColor = new KitSwatch();
        readonly KitPopup inkWidth = new KitPopup();
        readonly KitCheck fillToggle = new KitCheck();
        readonly KitPopup redactMode = new KitPopup();
        readonly KitButton stepResetBtn = new KitButton();
        readonly KitButton applyCropBtn = new KitButton();
        readonly KitButton cancelCropBtn = new KitButton();
        readonly Label frameLabel = new Label();
        readonly KitPopup framePopup = new KitPopup();
        readonly KitButton turn = new KitButton();
        readonly KitButton mirrorX = new KitButton();
        readonly KitButton mirrorY = new KitButton();
        readonly Label hint = new Label();

        readonly KitButton zoomOutBtn = new KitButton();
        readonly KitButton zoomFitBtn = new KitButton();
        readonly KitButton zoomInBtn = new KitButton();
        readonly Label zoomLabel = new Label();
        readonly KitButton undoBtn = new KitButton();
        readonly KitButton redoBtn = new KitButton();
        readonly KitButton resetBtn = new KitButton();
        readonly KitButton cancelBtn = new KitButton();
        readonly KitButton saveBtn = new KitButton();
        readonly KitButton copyBtn = new KitButton();
        readonly ToolTip tips = new ToolTip { InitialDelay = 400, ReshowDelay = 120, AutoPopDelay = 5000 };

        KitText inlineText;
        bool inlineTextActive;
        PointF textImgPos;
        int editingTextIndex = -1;

        Rectangle viewport;
        RectangleF canvas;
        float baseFit = 1f;
        float scale = 1f;
        bool fitMode = true;
        PointF pan = PointF.Empty;

        Bitmap checkerBmp;
        TextureBrush checkerBrush;

        bool initialSized;
        bool dragging;
        bool panning;
        bool spaceDown;
        Point panStartMouse;
        PointF panStartOffset;

        PointF dragStartImg;
        PointF dragEndImg;
        Rectangle dragBox;

        // Pending adjustable crop
        Rectangle pendingCrop;
        int cropDragHandle = -1; // -1 none, 0..7 handles, 8 move, 9 new
        Rectangle cropOrigBox;

        // Selection tool state
        int selectedIndex = -1;
        SelectDragKind selectDrag = SelectDragKind.None;
        int selectHandle = -1;
        Edit selectOrigEdit;
        RectangleF selectOrigBounds;
        List<Edit> selectUndoSnap;
        bool selectModified;

        // Step sequence floor after Reset #1
        int stepFloorIndex;

        public event Action Copied;

        ToolMode ActiveTool { get { return toolStrip.SelectedTool; } }

        public RedactWindow(Bitmap pic)
        {
            original = pic;
            baseWork = new Bitmap(pic);
            cachedBaseCount = 0;
            work = new Bitmap(pic);

            Text = Lang.T("Edit gambar");
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = MaximizeBox = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            toolStrip.AccessibleName = Lang.T("Alat");
            toolStrip.ToolChanged += OnToolChanged;

            inkColor.Presets = StrokeColors;
            inkColor.Value = ColorTranslator.FromHtml("#FF3B30");
            inkColor.AccessibleName = Lang.T("Warna coretan");
            inkColor.Picked += delegate { OnStyleOptionChanged(); };

            inkWidth.Items = new[] { Lang.T("Tipis"), Lang.T("Sedang"), Lang.T("Tebal") };
            inkWidth.Selected = 1;
            inkWidth.AccessibleName = Lang.T("Ketebalan");
            inkWidth.Changed += delegate { OnStyleOptionChanged(); };

            fillToggle.Text = Lang.T("Isi");
            fillToggle.CheckedChanged += delegate { OnStyleOptionChanged(); };

            redactMode.Items = new[] { Lang.T("Piksel"), Lang.T("Buram"), Lang.T("Blok") };
            redactMode.Selected = 0;
            redactMode.AccessibleName = Lang.T("Gaya samaran");
            redactMode.Changed += delegate
            {
                UpdateContextBar();
                LayoutControls();
                Invalidate();
            };

            stepResetBtn.Text = Lang.T("Reset #1");
            stepResetBtn.AccessibleDescription = Lang.T("Reset nomor langkah ke 1");
            stepResetBtn.Click += delegate
            {
                stepFloorIndex = edits.Count;
                UpdateHint();
                Invalidate();
            };

            applyCropBtn.Text = Lang.T("Terapkan");
            applyCropBtn.Primary = true;
            applyCropBtn.Click += delegate { ApplyPendingCrop(); };

            cancelCropBtn.Text = Lang.T("Batal potong");
            cancelCropBtn.Click += delegate { ClearPendingCrop(); };

            frameLabel.AutoSize = false;
            frameLabel.UseMnemonic = false;
            frameLabel.Text = Lang.T("Bingkai");

            framePopup.Items = new[]
            {
                Lang.T("Tanpa"),
                Lang.T("Kartu"),
                Lang.T("Gradien Biru"),
                Lang.T("Gradien Senja"),
                Lang.T("Gradien Mint"),
                Lang.T("Gelap")
            };
            framePopup.Selected = 0;
            framePopup.AccessibleName = Lang.T("Bingkai");
            framePopup.Changed += OnFramePicked;

            turn.Text = Lang.T("Putar 90°");
            turn.AccessibleDescription = Lang.T("Putar 90 derajat searah jarum jam");
            turn.Click += delegate { AddEdit(new Edit { Kind = EditKind.Turn }); };

            mirrorX.Text = Lang.T("Balik ⇆");
            mirrorX.AccessibleName = Lang.T("Balik kiri-kanan");
            mirrorX.Click += delegate { AddEdit(new Edit { Kind = EditKind.MirrorX }); };

            mirrorY.Text = Lang.T("Balik ⇅");
            mirrorY.AccessibleName = Lang.T("Balik atas-bawah");
            mirrorY.Click += delegate { AddEdit(new Edit { Kind = EditKind.MirrorY }); };

            hint.UseMnemonic = false;
            hint.AutoSize = false;
            hint.AutoEllipsis = true;

            zoomOutBtn.Text = "-";
            zoomOutBtn.AccessibleName = Lang.T("Perkecil");
            zoomOutBtn.Click += delegate { StepZoom(1f / 1.25f); };

            zoomFitBtn.Text = Lang.T("Pas");
            zoomFitBtn.AccessibleDescription = Lang.T("Paskan ke jendela");
            zoomFitBtn.Click += delegate { ZoomFit(); };

            zoomInBtn.Text = "+";
            zoomInBtn.AccessibleName = Lang.T("Perbesar");
            zoomInBtn.Click += delegate { StepZoom(1.25f); };

            zoomLabel.AutoSize = false;
            zoomLabel.UseMnemonic = false;

            undoBtn.Text = Lang.T("Urungkan");
            undoBtn.Click += delegate { Undo(); };

            redoBtn.Text = Lang.T("Ulangi");
            redoBtn.Click += delegate { Redo(); };

            resetBtn.Text = Lang.T("Reset");
            resetBtn.AccessibleDescription = Lang.T("Kembalikan gambar ke awal");
            resetBtn.Click += delegate { ResetAll(); };

            cancelBtn.Text = Lang.T("Batal");
            cancelBtn.Click += delegate { Close(); };

            saveBtn.Text = Lang.T("Simpan…");
            saveBtn.Click += delegate { SaveOut(); };

            copyBtn.Text = Lang.T("Salin");
            copyBtn.Primary = true;
            copyBtn.Click += delegate { CopyOut(); };

            Control[] all =
            {
                toolStrip, frameLabel, framePopup, turn, mirrorX, mirrorY,
                inkColor, inkWidth, fillToggle, redactMode, stepResetBtn, applyCropBtn, cancelCropBtn, hint,
                zoomOutBtn, zoomFitBtn, zoomInBtn, zoomLabel, undoBtn, redoBtn, resetBtn, cancelBtn, saveBtn, copyBtn
            };
            foreach (Control c in all)
            {
                c.Cursor = Cursors.Default;
                Controls.Add(c);
            }

            tips.SetToolTip(turn, Lang.T("Putar 90°") + " (Ctrl+R)");
            tips.SetToolTip(mirrorX, Lang.T("Balik kiri-kanan"));
            tips.SetToolTip(mirrorY, Lang.T("Balik atas-bawah"));
            tips.SetToolTip(framePopup, Lang.T("Bingkai"));
            tips.SetToolTip(stepResetBtn, Lang.T("Reset nomor langkah ke 1"));
            tips.SetToolTip(applyCropBtn, Lang.T("Terapkan") + " (Enter)");
            tips.SetToolTip(cancelCropBtn, Lang.T("Batal potong") + " (Esc)");
            tips.SetToolTip(zoomOutBtn, Lang.T("Perkecil") + " (Ctrl+-)");
            tips.SetToolTip(zoomFitBtn, Lang.T("Paskan ke jendela") + " (Ctrl+0)");
            tips.SetToolTip(zoomInBtn, Lang.T("Perbesar") + " (Ctrl++)");
            tips.SetToolTip(undoBtn, Lang.T("Urungkan") + " (Ctrl+Z)");
            tips.SetToolTip(redoBtn, Lang.T("Ulangi") + " (Ctrl+Y)");
            tips.SetToolTip(resetBtn, Lang.T("Kembalikan gambar ke awal"));
            tips.SetToolTip(saveBtn, Lang.T("Simpan…") + " (Ctrl+S)");
            tips.SetToolTip(copyBtn, Lang.T("Salin") + " (Ctrl+C)");
            tips.SetToolTip(cancelBtn, Lang.T("Batal") + " (Esc)");

            UpdateContextBar();
            UpdateActionButtons();
        }

        void OnToolChanged(ToolMode mode)
        {
            if (inlineTextActive) CommitInlineText();
            if (mode != ToolMode.Crop && !pendingCrop.IsEmpty) pendingCrop = Rectangle.Empty;
            if (mode != ToolMode.Select) selectedIndex = -1;
            dragging = false;
            Capture = false;
            stroke.Clear();
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        void OnStyleOptionChanged()
        {
            if (inlineTextActive && inlineText != null)
            {
                ApplyInlineTextStyle();
                return;
            }
            if (ActiveTool == ToolMode.Select && selectedIndex >= GeometryBaseIndex() && selectedIndex < edits.Count)
            {
                var e = edits[selectedIndex];
                if (e.Kind == EditKind.Turn || e.Kind == EditKind.MirrorX || e.Kind == EditKind.MirrorY || e.Kind == EditKind.Crop || e.Kind == EditKind.Frame)
                    return;
                PushUndo();
                e.StrokeColor = inkColor.Value;
                if (e.Kind == EditKind.Text)
                {
                    e.FontSize = CurrentFontSize();
                    e.Filled = fillToggle.Checked;
                }
                else
                {
                    e.StrokeWidth = CurrentStrokeWidth();
                    if (e.Kind == EditKind.Rect || e.Kind == EditKind.Ellipse)
                        e.Filled = fillToggle.Checked;
                }
                RebuildWork(false);
                Invalidate();
            }
        }

        void OnFramePicked(int idx)
        {
            var preset = (FramePreset)Math.Max(0, Math.Min(5, idx));
            if (inlineTextActive) CommitInlineText();
            ClearPendingCrop();
            selectedIndex = -1;

            // Replace consecutive Frame edit or remove when None is picked right after Frame
            if (edits.Count > 0 && edits[edits.Count - 1].Kind == EditKind.Frame)
            {
                if (edits[edits.Count - 1].Frame == preset) return;
                PushUndo();
                if (preset == FramePreset.None) edits.RemoveAt(edits.Count - 1);
                else edits[edits.Count - 1].Frame = preset;
                cachedBaseCount = -1;
                RebuildWork(true);
                return;
            }
            if (preset == FramePreset.None) return;
            AddEdit(new Edit { Kind = EditKind.Frame, Frame = preset });
        }

        void UpdateContextBar()
        {
            var t = ActiveTool;
            bool hasSel = t == ToolMode.Select && selectedIndex >= GeometryBaseIndex() && selectedIndex < edits.Count;
            Edit sel = hasSel ? edits[selectedIndex] : null;

            bool showColor = t == ToolMode.Arrow || t == ToolMode.Line || t == ToolMode.Rect || t == ToolMode.Ellipse ||
                             t == ToolMode.Pen || t == ToolMode.Highlight || t == ToolMode.Text || t == ToolMode.Step ||
                             (t == ToolMode.Redact && redactMode.Selected == (int)RedactMode.Block) ||
                             (hasSel && (sel.Kind != EditKind.Redact || sel.RedactStyle == RedactMode.Block));

            bool showWidth = t == ToolMode.Arrow || t == ToolMode.Line || t == ToolMode.Rect || t == ToolMode.Ellipse ||
                             t == ToolMode.Pen || t == ToolMode.Highlight || t == ToolMode.Text || t == ToolMode.Step ||
                             (hasSel && sel.Kind != EditKind.Redact);

            bool showFill = t == ToolMode.Rect || t == ToolMode.Ellipse || t == ToolMode.Text ||
                            (hasSel && (sel.Kind == EditKind.Rect || sel.Kind == EditKind.Ellipse || sel.Kind == EditKind.Text));

            bool isText = t == ToolMode.Text || (hasSel && sel.Kind == EditKind.Text);
            fillToggle.Text = isText ? Lang.T("Latar") : Lang.T("Isi");
            fillToggle.AccessibleName = isText ? Lang.T("Latar teks") : Lang.T("Isi");

            inkColor.Visible = showColor;
            inkWidth.Visible = showWidth;
            fillToggle.Visible = showFill;
            redactMode.Visible = t == ToolMode.Redact;
            stepResetBtn.Visible = t == ToolMode.Step;
            bool cropPending = t == ToolMode.Crop && pendingCrop.Width >= 8 && pendingCrop.Height >= 8;
            applyCropBtn.Visible = cropPending;
            cancelCropBtn.Visible = cropPending;

            UpdateHint();
        }

        void UpdateHint()
        {
            string h = HintText();
            hint.Text = h + "  (" + work.Width + " × " + work.Height + ")";
        }

        string HintText()
        {
            switch (ActiveTool)
            {
                case ToolMode.Select: return Lang.T("Klik anotasi untuk pindah atau ubah ukuran; klik dua kali teks untuk ubah.");
                case ToolMode.Arrow: return Lang.T("Seret untuk membuat panah (Shift: sudut 45°).");
                case ToolMode.Line: return Lang.T("Seret untuk membuat garis (Shift: sudut 45°).");
                case ToolMode.Rect: return Lang.T("Seret untuk membuat kotak (Shift: persegi).");
                case ToolMode.Ellipse: return Lang.T("Seret untuk membuat elips (Shift: lingkaran).");
                case ToolMode.Pen: return Lang.T("Seret pada gambar untuk mencoret.");
                case ToolMode.Highlight: return Lang.T("Seret untuk menandai stabilo (Shift: garis lurus).");
                case ToolMode.Text: return Lang.T("Klik pada gambar untuk menulis teks (Enter: simpan, Shift+Enter: baris baru).");
                case ToolMode.Step: return Lang.T("Klik pada gambar untuk menaruh nomor langkah.") + " (#" + NextStepNumber() + ")";
                case ToolMode.Redact: return Lang.T("Seret di atas bagian yang mau disamarkan.");
                case ToolMode.Crop:
                    return pendingCrop.Width >= 8 && pendingCrop.Height >= 8
                        ? Lang.T("Seret sudut untuk sesuaikan, lalu tekan Enter atau Terapkan.")
                        : Lang.T("Seret untuk memilih bagian yang disimpan.");
            }
            return "";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Relayout();
        }

        public override void Relayout()
        {
            float s = Ui;
            Func<float, int> u = v => (int)Math.Round(v * s);
            BackColor = KitInk.Window;
            Native.DarkFrame(Handle, !Ink.Light);
            RebuildCheckerBrush();

            MinimumSize = new Size(u(760), u(520));
            var font = UiFont(12.5f);
            var smallFont = UiFont(11.5f);

            int btnH = u(30);
            foreach (var b in new[] { stepResetBtn, applyCropBtn, cancelCropBtn, turn, mirrorX, mirrorY, undoBtn, redoBtn, resetBtn, cancelBtn, saveBtn, copyBtn })
            {
                b.Font = font;
                b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + u(24), btnH);
            }
            foreach (var b in new[] { zoomOutBtn, zoomFitBtn, zoomInBtn })
            {
                b.Font = font;
                int bw = b == zoomFitBtn ? TextRenderer.MeasureText(b.Text, b.Font).Width + u(22) : u(30);
                b.Size = new Size(bw, btnH);
            }

            toolStrip.Size = new Size(toolStrip.PreferredWidth(s), btnH);

            inkColor.Font = font;
            inkColor.Size = new Size(TextRenderer.MeasureText("      #DDDDDD", font).Width + u(24), btnH);

            inkWidth.Font = font;
            int ww = 0;
            foreach (var it in inkWidth.Items) ww = Math.Max(ww, TextRenderer.MeasureText(it + "  ▾", font).Width);
            inkWidth.Size = new Size(ww + u(24), btnH);

            redactMode.Font = font;
            int rw = 0;
            foreach (var it in redactMode.Items) rw = Math.Max(rw, TextRenderer.MeasureText(it + "  ▾", font).Width);
            redactMode.Size = new Size(rw + u(24), btnH);

            fillToggle.Font = font;
            fillToggle.Size = new Size(TextRenderer.MeasureText(Lang.T("Latar"), font).Width + u(42), btnH);

            frameLabel.Font = smallFont;
            frameLabel.ForeColor = KitInk.Sub;
            frameLabel.BackColor = KitInk.Window;
            frameLabel.TextAlign = ContentAlignment.MiddleLeft;
            frameLabel.Size = new Size(TextRenderer.MeasureText(frameLabel.Text, smallFont).Width + u(6), btnH);

            framePopup.Font = font;
            int fw = 0;
            foreach (var it in framePopup.Items) fw = Math.Max(fw, TextRenderer.MeasureText(it + "  ▾", font).Width);
            framePopup.Size = new Size(fw + u(24), btnH);

            hint.Font = smallFont;
            hint.ForeColor = KitInk.Sub;
            hint.BackColor = KitInk.Window;
            hint.TextAlign = ContentAlignment.MiddleLeft;

            zoomLabel.Font = smallFont;
            zoomLabel.ForeColor = KitInk.Sub;
            zoomLabel.BackColor = KitInk.Window;
            zoomLabel.TextAlign = ContentAlignment.MiddleLeft;
            zoomLabel.Size = new Size(u(48), btnH);

            if (!initialSized)
            {
                initialSized = true;
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                int topChrome = u(16 + 30 + 8 + 30 + 10);
                int botChrome = u(10 + 30 + 16);
                int maxWinW = (int)(area.Width * 0.86f);
                int maxWinH = (int)(area.Height * 0.86f);
                int prefW = Math.Max(u(960), Math.Min(maxWinW, work.Width + u(64)));
                int prefH = Math.Max(u(620), Math.Min(maxWinH, work.Height + topChrome + botChrome + u(32)));
                ClientSize = new Size(Math.Max(MinimumSize.Width, prefW), Math.Max(MinimumSize.Height, prefH));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            }

            LayoutControls();
            Invalidate(true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated)
            {
                LayoutControls();
                Invalidate();
            }
        }

        void LayoutControls()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            float s = Ui;
            Func<float, int> u = v => (int)Math.Round(v * s);
            int m = u(16), gap = u(6), btnH = u(30);
            int cw = Math.Max(u(400), ClientSize.Width - m * 2);

            // Row 1: toolStrip left, frame + transforms right
            int y1 = u(14);
            toolStrip.Location = new Point(m, y1);
            int rx = m + cw;
            foreach (var b in new Control[] { mirrorY, mirrorX, turn, framePopup, frameLabel })
            {
                rx -= b.Width;
                b.Location = new Point(rx, y1);
                rx -= gap;
            }

            // Row 2: contextual tool options + hint
            int y2 = y1 + btnH + u(8);
            int lx = m;
            Control[] ctx = { redactMode, inkColor, inkWidth, fillToggle, stepResetBtn, applyCropBtn, cancelCropBtn };
            foreach (var c in ctx)
            {
                if (!c.Visible) continue;
                c.Location = new Point(lx, y2);
                lx += c.Width + gap;
            }
            int hintW = Math.Max(u(60), (m + cw) - lx);
            hint.SetBounds(lx, y2, hintW, btnH);

            // Bottom bar
            int yBot = ClientSize.Height - u(14) - btnH;
            int bx = m;
            foreach (var c in new Control[] { zoomOutBtn, zoomFitBtn, zoomInBtn, zoomLabel })
            {
                c.Location = new Point(bx, yBot);
                bx += c.Width + u(4);
            }
            bx += u(8);
            foreach (var b in new[] { undoBtn, redoBtn, resetBtn })
            {
                b.Location = new Point(bx, yBot);
                bx += b.Width + gap;
            }

            int ex = m + cw;
            foreach (var b in new[] { copyBtn, saveBtn, cancelBtn })
            {
                ex -= b.Width;
                b.Location = new Point(ex, yBot);
                ex -= gap;
            }

            // Viewport between Row 2 and Bottom bar
            int vTop = y2 + btnH + u(10);
            int vBot = yBot - u(10);
            viewport = new Rectangle(m, vTop, cw, Math.Max(u(80), vBot - vTop));

            UpdateCanvas();
        }

        void UpdateCanvas()
        {
            if (viewport.Width <= 4 || viewport.Height <= 4) return;
            float pad = 16f * Ui;
            float availW = Math.Max(16f, viewport.Width - pad * 2f);
            float availH = Math.Max(16f, viewport.Height - pad * 2f);
            baseFit = Math.Max(0.05f, Math.Min(1f, Math.Min(availW / work.Width, availH / work.Height)));

            if (fitMode)
            {
                scale = baseFit;
                pan = PointF.Empty;
            }
            else
            {
                float minZoom = Math.Min(0.25f, baseFit);
                scale = Math.Max(minZoom, Math.Min(4.0f, scale));
            }

            float imgW = work.Width * scale;
            float imgH = work.Height * scale;
            ClampPan(imgW, imgH);
            canvas = new RectangleF(
                viewport.X + (viewport.Width - imgW) / 2f + pan.X,
                viewport.Y + (viewport.Height - imgH) / 2f + pan.Y,
                imgW,
                imgH);

            zoomLabel.Text = (int)Math.Round(scale * 100f) + "%";
            if (inlineTextActive && inlineText != null && inlineText.Visible)
                PositionInlineText();
        }

        void ClampPan(float imgW, float imgH)
        {
            if (imgW <= viewport.Width) pan.X = 0f;
            else
            {
                float maxX = (imgW - viewport.Width) / 2f + 32f * Ui;
                pan.X = Math.Max(-maxX, Math.Min(maxX, pan.X));
            }
            if (imgH <= viewport.Height) pan.Y = 0f;
            else
            {
                float maxY = (imgH - viewport.Height) / 2f + 32f * Ui;
                pan.Y = Math.Max(-maxY, Math.Min(maxY, pan.Y));
            }
        }

        void RebuildCheckerBrush()
        {
            if (checkerBrush != null) { checkerBrush.Dispose(); checkerBrush = null; }
            if (checkerBmp != null) { checkerBmp.Dispose(); checkerBmp = null; }
            int sq = Math.Max(8, (int)Math.Round(10f * Ui));
            int sz = sq * 2;
            checkerBmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            Color c1 = Ink.Light ? Color.FromArgb(255, 242, 242, 245) : Color.FromArgb(255, 26, 26, 30);
            Color c2 = Ink.Light ? Color.FromArgb(255, 230, 230, 236) : Color.FromArgb(255, 34, 34, 40);
            using (var g = Graphics.FromImage(checkerBmp))
            using (var b1 = new SolidBrush(c1))
            using (var b2 = new SolidBrush(c2))
            {
                g.FillRectangle(b1, 0, 0, sq, sq);
                g.FillRectangle(b1, sq, sq, sq, sq);
                g.FillRectangle(b2, sq, 0, sq, sq);
                g.FillRectangle(b2, 0, sq, sq, sq);
            }
            checkerBrush = new TextureBrush(checkerBmp, WrapMode.Tile);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(KitInk.Window);
            if (viewport.Width <= 4 || viewport.Height <= 4) return;

            KitInk.Smooth(g);
            var vpRect = new RectangleF(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            using (var vpPath = Ink.Round(vpRect, 10f * Ui))
            {
                using (var bg = new SolidBrush(KitInk.Side)) g.FillPath(bg, vpPath);

                var st = g.Save();
                g.SetClip(vpPath, CombineMode.Intersect);

                // Subtle checkerboard behind transparent/framed areas inside viewport
                if (checkerBrush != null)
                {
                    var imgClip = RectangleF.Intersect(vpRect, canvas);
                    if (imgClip.Width > 0 && imgClip.Height > 0)
                        g.FillRectangle(checkerBrush, imgClip);
                }

                g.InterpolationMode = scale < 1f ? InterpolationMode.HighQualityBilinear : (scale >= 2f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear);
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(work, canvas);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Live annotation preview while dragging
                if (dragging)
                    PaintLiveDrag(g);

                // Pending adjustable crop overlay
                if (ActiveTool == ToolMode.Crop && pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
                    PaintCropOverlay(g, pendingCrop);

                // Selection overlay
                if (ActiveTool == ToolMode.Select && selectedIndex >= GeometryBaseIndex() && selectedIndex < edits.Count)
                    PaintSelectionOverlay(g, edits[selectedIndex]);

                g.Restore(st);

                using (var border = new Pen(KitInk.Line, 1f)) g.DrawPath(border, vpPath);
            }
        }

        void PaintLiveDrag(Graphics g)
        {
            var t = ActiveTool;
            if (t == ToolMode.Pen || t == ToolMode.Highlight)
            {
                if (stroke.Count == 0) return;
                var st = g.Save();
                g.SetClip(canvas, CombineMode.Intersect);
                Color c = t == ToolMode.Highlight ? HighlightColor(inkColor.Value) : inkColor.Value;
                float w = t == ToolMode.Highlight ? CurrentStrokeWidth() * 2.4f : CurrentStrokeWidth();
                DrawStroke(g, stroke.ToArray(), c, w, scale, canvas.Location);
                g.Restore(st);
                return;
            }
            if (t == ToolMode.Line || t == ToolMode.Arrow)
            {
                var st = g.Save();
                g.SetClip(canvas, CombineMode.Intersect);
                var preview = new Edit
                {
                    Kind = t == ToolMode.Arrow ? EditKind.Arrow : EditKind.Line,
                    A = dragStartImg,
                    B = dragEndImg,
                    StrokeColor = inkColor.Value,
                    StrokeWidth = CurrentStrokeWidth()
                };
                DrawEditVector(g, preview, scale, canvas.Location);
                g.Restore(st);
                return;
            }
            if (t == ToolMode.Rect || t == ToolMode.Ellipse)
            {
                var st = g.Save();
                g.SetClip(canvas, CombineMode.Intersect);
                var preview = new Edit
                {
                    Kind = t == ToolMode.Rect ? EditKind.Rect : EditKind.Ellipse,
                    A = new PointF(Math.Min(dragStartImg.X, dragEndImg.X), Math.Min(dragStartImg.Y, dragEndImg.Y)),
                    B = new PointF(Math.Max(dragStartImg.X, dragEndImg.X), Math.Max(dragStartImg.Y, dragEndImg.Y)),
                    StrokeColor = inkColor.Value,
                    StrokeWidth = CurrentStrokeWidth(),
                    Filled = fillToggle.Checked
                };
                DrawEditVector(g, preview, scale, canvas.Location);
                g.Restore(st);
                return;
            }
            if (t == ToolMode.Redact && dragBox.Width >= 1 && dragBox.Height >= 1)
            {
                var r = ToScreen(dragBox);
                Color fillCol = redactMode.Selected == (int)RedactMode.Block
                    ? Color.FromArgb(140, inkColor.Value)
                    : Color.FromArgb(44, KitInk.Text);
                using (var fill = new SolidBrush(fillCol)) g.FillRectangle(fill, r);
                using (var pen = new Pen(KitInk.Text, 1.5f * Ui) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
        }

        void PaintCropOverlay(Graphics g, Rectangle crop)
        {
            var r = ToScreen(crop);
            using (var dim = new Region(canvas))
            using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                dim.Exclude(r);
                g.FillRegion(shade, dim);
            }

            // Rule-of-thirds gridlines
            if (r.Width > 24 && r.Height > 24)
            {
                using (var grid = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
                {
                    float x1 = r.X + r.Width / 3f, x2 = r.X + r.Width * 2f / 3f;
                    float y1 = r.Y + r.Height / 3f, y2 = r.Y + r.Height * 2f / 3f;
                    g.DrawLine(grid, x1, r.Y, x1, r.Bottom);
                    g.DrawLine(grid, x2, r.Y, x2, r.Bottom);
                    g.DrawLine(grid, r.X, y1, r.Right, y1);
                    g.DrawLine(grid, r.X, y2, r.Right, y2);
                }
            }

            using (var pen = new Pen(Color.White, 1.5f * Ui))
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

            // 8 draggable handles
            var handles = GetRectHandles(r);
            DrawHandles(g, handles);

            // Live W x H pill
            string size = crop.Width + " × " + crop.Height;
            using (var f = UiFont(11f))
            {
                var sz = TextRenderer.MeasureText(g, size, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                float pw = sz.Width + 14f * Ui, ph = sz.Height + 6f * Ui;
                float px = Math.Max(viewport.X + 4, Math.Min(viewport.Right - pw - 4, r.X + 6f * Ui));
                float py = r.Y - ph - 6f * Ui >= viewport.Y + 4 ? r.Y - ph - 6f * Ui : r.Y + 6f * Ui;
                var pill = new RectangleF(px, py, pw, ph);
                using (var p = Ink.Round(pill, ph / 2f))
                using (var bg = new SolidBrush(Color.FromArgb(205, 16, 16, 20)))
                    g.FillPath(bg, p);
                TextRenderer.DrawText(g, size, f, Rectangle.Round(pill), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        void PaintSelectionOverlay(Graphics g, Edit e)
        {
            if (e.Kind == EditKind.Line || e.Kind == EditKind.Arrow)
            {
                PointF sa = ToScreen(e.A), sb = ToScreen(e.B);
                using (var halo = new Pen(Color.FromArgb(140, 0, 0, 0), 3f * Ui) { DashStyle = DashStyle.Dash })
                using (var pen = new Pen(Color.White, 1.5f * Ui) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(halo, sa, sb);
                    g.DrawLine(pen, sa, sb);
                }
                DrawHandles(g, new[] { sa, sb });
                return;
            }

            RectangleF imgBounds = GetEditBounds(e);
            if (imgBounds.Width < 1 && imgBounds.Height < 1) return;
            var r = ToScreen(imgBounds);
            using (var halo = new Pen(Color.FromArgb(130, 0, 0, 0), 2.5f * Ui) { DashStyle = DashStyle.Dash })
            using (var pen = new Pen(Color.White, 1.4f * Ui) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(halo, r.X, r.Y, r.Width, r.Height);
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
            var all = GetRectHandles(r);
            if (e.Kind == EditKind.Rect || e.Kind == EditKind.Ellipse || e.Kind == EditKind.Redact)
                DrawHandles(g, all);
            else
                DrawHandles(g, new[] { all[0], all[2], all[4], all[6] });
        }

        void DrawHandles(Graphics g, PointF[] pts)
        {
            float hs = 8f * Ui;
            using (var fill = new SolidBrush(Color.White))
            using (var edge = new Pen(Color.FromArgb(220, 24, 24, 30), 1.4f * Ui))
            {
                for (int i = 0; i < pts.Length; i++)
                {
                    var hr = new RectangleF(pts[i].X - hs / 2f, pts[i].Y - hs / 2f, hs, hs);
                    g.FillEllipse(fill, hr);
                    g.DrawEllipse(edge, hr);
                }
            }
        }

        static PointF[] GetRectHandles(RectangleF r)
        {
            float mx = r.X + r.Width / 2f, my = r.Y + r.Height / 2f;
            return new[]
            {
                new PointF(r.Left, r.Top),     // 0 NW
                new PointF(mx, r.Top),         // 1 N
                new PointF(r.Right, r.Top),    // 2 NE
                new PointF(r.Right, my),       // 3 E
                new PointF(r.Right, r.Bottom), // 4 SE
                new PointF(mx, r.Bottom),      // 5 S
                new PointF(r.Left, r.Bottom),  // 6 SW
                new PointF(r.Left, my),        // 7 W
            };
        }

        RectangleF ToScreen(Rectangle r)
        {
            return new RectangleF(canvas.X + r.X * scale, canvas.Y + r.Y * scale, r.Width * scale, r.Height * scale);
        }

        RectangleF ToScreen(RectangleF r)
        {
            return new RectangleF(canvas.X + r.X * scale, canvas.Y + r.Y * scale, r.Width * scale, r.Height * scale);
        }

        PointF ToScreen(PointF p)
        {
            return new PointF(canvas.X + p.X * scale, canvas.Y + p.Y * scale);
        }

        Point ToImage(Point p)
        {
            int x = (int)Math.Round((p.X - canvas.X) / scale);
            int y = (int)Math.Round((p.Y - canvas.Y) / scale);
            return new Point(Math.Max(0, Math.Min(work.Width, x)), Math.Max(0, Math.Min(work.Height, y)));
        }

        PointF ToImageF(Point p)
        {
            float x = (p.X - canvas.X) / scale;
            float y = (p.Y - canvas.Y) / scale;
            return new PointF(Math.Max(0f, Math.Min(work.Width, x)), Math.Max(0f, Math.Min(work.Height, y)));
        }

        PointF ToImageUnclampedF(Point p)
        {
            return new PointF((p.X - canvas.X) / scale, (p.Y - canvas.Y) / scale);
        }

        float CurrentStrokeWidth()
        {
            int idx = Math.Max(0, Math.Min(StrokeWidths.Length - 1, inkWidth.Selected));
            return Math.Max(1.5f, StrokeWidths[idx] * Ui / Math.Max(0.05f, baseFit));
        }

        float CurrentFontSize()
        {
            int idx = Math.Max(0, Math.Min(StrokeWidths.Length - 1, inkWidth.Selected));
            float[] sizes = { 16f, 22f, 32f };
            return Math.Max(12f, sizes[idx] * Ui / Math.Max(0.05f, baseFit));
        }

        static Color HighlightColor(Color c)
        {
            return Color.FromArgb(105, c.R, c.G, c.B);
        }

        static PointF Snap45(PointF anchor, PointF target)
        {
            float dx = target.X - anchor.X;
            float dy = target.Y - anchor.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 0.001) return target;
            double angle = Math.Atan2(dy, dx);
            double snapped = Math.Round(angle / (Math.PI / 4.0)) * (Math.PI / 4.0);
            return new PointF(anchor.X + (float)(Math.Cos(snapped) * dist), anchor.Y + (float)(Math.Sin(snapped) * dist));
        }

        static PointF SnapAxis(PointF anchor, PointF target)
        {
            float dx = target.X - anchor.X;
            float dy = target.Y - anchor.Y;
            if (Math.Abs(dx) >= Math.Abs(dy)) return new PointF(target.X, anchor.Y);
            return new PointF(anchor.X, target.Y);
        }

        static PointF SnapSquare(PointF anchor, PointF target)
        {
            float dx = target.X - anchor.X;
            float dy = target.Y - anchor.Y;
            float side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new PointF(anchor.X + (dx < 0 ? -side : side), anchor.Y + (dy < 0 ? -side : side));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (inlineTextActive) return;
            if (!viewport.Contains(e.Location) && !canvas.Contains(e.Location)) return;
            float factor = e.Delta > 0 ? 1.15f : (1f / 1.15f);
            ZoomAt(e.Location, factor);
        }

        void StepZoom(float factor)
        {
            var center = new Point(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
            ZoomAt(center, factor);
        }

        void ZoomAt(Point screenPt, float factor)
        {
            float imgX = (screenPt.X - canvas.X) / scale;
            float imgY = (screenPt.Y - canvas.Y) / scale;

            float minZoom = Math.Min(0.25f, baseFit);
            float nextScale = Math.Max(minZoom, Math.Min(4.0f, scale * factor));
            if (Math.Abs(nextScale - scale) < 0.0005f) return;

            fitMode = false;
            scale = nextScale;

            float imgW = work.Width * scale;
            float imgH = work.Height * scale;
            float baseX = viewport.X + (viewport.Width - imgW) / 2f;
            float baseY = viewport.Y + (viewport.Height - imgH) / 2f;
            pan = new PointF(screenPt.X - imgX * scale - baseX, screenPt.Y - imgY * scale - baseY);
            UpdateCanvas();
            Invalidate();
        }

        void ZoomFit()
        {
            fitMode = true;
            pan = PointF.Empty;
            UpdateCanvas();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!viewport.Contains(e.Location) && !canvas.Contains(e.Location)) return;

            if (inlineTextActive)
                CommitInlineText();

            // Give focus to form so Space/arrow/delete shortcuts work cleanly after button clicks
            if (ActiveControl != null && !inlineTextActive) Focus();

            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && spaceDown))
            {
                panning = true;
                panStartMouse = e.Location;
                panStartOffset = pan;
                Capture = true;
                Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            var t = ActiveTool;
            if (t == ToolMode.Crop)
            {
                HandleCropMouseDown(e.Location);
                return;
            }

            if (t == ToolMode.Select)
            {
                HandleSelectMouseDown(e.Location);
                return;
            }

            if (!canvas.Contains(e.Location)) return;

            if (t == ToolMode.Text)
            {
                OpenInlineText(ToImageF(e.Location), -1);
                return;
            }

            if (t == ToolMode.Step)
            {
                var p = ToImageF(e.Location);
                AddEdit(new Edit
                {
                    Kind = EditKind.Step,
                    Pos = p,
                    StrokeColor = inkColor.Value,
                    StrokeWidth = CurrentStrokeWidth(),
                    StepNumber = NextStepNumber()
                });
                UpdateHint();
                return;
            }

            dragging = true;
            Capture = true;
            if (t == ToolMode.Pen || t == ToolMode.Highlight)
            {
                stroke.Clear();
                stroke.Add(ToImageF(e.Location));
                Invalidate();
                return;
            }
            if (t == ToolMode.Redact)
            {
                var pt = ToImage(e.Location);
                dragStartImg = pt;
                dragBox = Rectangle.Empty;
                return;
            }
            dragStartImg = ToImageF(e.Location);
            dragEndImg = dragStartImg;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || !viewport.Contains(e.Location)) return;

            if (ActiveTool == ToolMode.Crop && pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
            {
                if (ToScreen(pendingCrop).Contains(e.Location)) ApplyPendingCrop();
                return;
            }

            if (ActiveTool == ToolMode.Select)
            {
                int hit = HitTestAnnotation(e.Location);
                if (hit >= GeometryBaseIndex() && edits[hit].Kind == EditKind.Text)
                {
                    selectedIndex = hit;
                    OpenInlineText(edits[hit].Pos, hit);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (panning)
            {
                pan = new PointF(panStartOffset.X + (e.X - panStartMouse.X), panStartOffset.Y + (e.Y - panStartMouse.Y));
                UpdateCanvas();
                Invalidate();
                return;
            }

            if (ActiveTool == ToolMode.Crop)
            {
                if (dragging) HandleCropMouseMove(e.Location);
                else UpdateViewportCursor(e.Location);
                return;
            }

            if (ActiveTool == ToolMode.Select)
            {
                if (dragging) HandleSelectMouseMove(e.Location);
                else UpdateViewportCursor(e.Location);
                return;
            }

            UpdateViewportCursor(e.Location);
            if (!dragging) return;

            var t = ActiveTool;
            if (t == ToolMode.Pen || t == ToolMode.Highlight)
            {
                var pt = ToImageF(e.Location);
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                if (t == ToolMode.Highlight && shift && stroke.Count > 0)
                {
                    var s0 = stroke[0];
                    stroke.Clear();
                    stroke.Add(s0);
                    stroke.Add(SnapAxis(s0, pt));
                }
                else
                {
                    if (stroke.Count == 0) stroke.Add(pt);
                    else
                    {
                        var last = stroke[stroke.Count - 1];
                        float dx = pt.X - last.X, dy = pt.Y - last.Y, minStep = 0.75f / Math.Max(0.05f, scale);
                        if (dx * dx + dy * dy >= minStep * minStep) stroke.Add(pt);
                    }
                }
                Invalidate();
                return;
            }

            if (t == ToolMode.Redact)
            {
                var p = ToImage(e.Location);
                int fx = (int)Math.Round(dragStartImg.X), fy = (int)Math.Round(dragStartImg.Y);
                dragBox = Rectangle.FromLTRB(Math.Min(fx, p.X), Math.Min(fy, p.Y), Math.Max(fx, p.X), Math.Max(fy, p.Y));
                Invalidate();
                return;
            }

            var cur = ToImageF(e.Location);
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                if (t == ToolMode.Line || t == ToolMode.Arrow) cur = Snap45(dragStartImg, cur);
                else if (t == ToolMode.Rect || t == ToolMode.Ellipse) cur = SnapSquare(dragStartImg, cur);
            }
            dragEndImg = cur;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (panning)
            {
                panning = false;
                Capture = false;
                UpdateViewportCursor(e.Location);
                return;
            }

            if (!dragging) return;
            dragging = false;
            Capture = false;

            var t = ActiveTool;
            if (t == ToolMode.Crop)
            {
                HandleCropMouseUp();
                return;
            }
            if (t == ToolMode.Select)
            {
                HandleSelectMouseUp();
                return;
            }

            if (t == ToolMode.Pen || t == ToolMode.Highlight)
            {
                var pt = ToImageF(e.Location);
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                if (t == ToolMode.Highlight && shift && stroke.Count > 0)
                {
                    var s0 = stroke[0];
                    stroke.Clear();
                    stroke.Add(s0);
                    stroke.Add(SnapAxis(s0, pt));
                }
                else if (stroke.Count == 0) stroke.Add(pt);
                else
                {
                    var last = stroke[stroke.Count - 1];
                    if (Math.Abs(pt.X - last.X) > 0.05f || Math.Abs(pt.Y - last.Y) > 0.05f) stroke.Add(pt);
                }
                var pts = stroke.ToArray();
                stroke.Clear();
                if (pts.Length > 0)
                {
                    AddEdit(new Edit
                    {
                        Kind = t == ToolMode.Highlight ? EditKind.Highlight : EditKind.Pen,
                        Points = pts,
                        StrokeColor = inkColor.Value,
                        StrokeWidth = CurrentStrokeWidth()
                    });
                }
                else Invalidate();
                return;
            }

            if (t == ToolMode.Redact)
            {
                if (dragBox.Width >= 4 && dragBox.Height >= 4)
                {
                    AddEdit(new Edit
                    {
                        Kind = EditKind.Redact,
                        RedactStyle = (RedactMode)Math.Max(0, Math.Min(2, redactMode.Selected)),
                        Box = dragBox,
                        StrokeColor = inkColor.Value
                    });
                }
                else Invalidate();
                return;
            }

            float dx = dragEndImg.X - dragStartImg.X;
            float dy = dragEndImg.Y - dragStartImg.Y;
            if (t == ToolMode.Line || t == ToolMode.Arrow)
            {
                if (dx * dx + dy * dy >= 9f)
                {
                    AddEdit(new Edit
                    {
                        Kind = t == ToolMode.Arrow ? EditKind.Arrow : EditKind.Line,
                        A = dragStartImg,
                        B = dragEndImg,
                        StrokeColor = inkColor.Value,
                        StrokeWidth = CurrentStrokeWidth()
                    });
                }
                else Invalidate();
                return;
            }

            if (t == ToolMode.Rect || t == ToolMode.Ellipse)
            {
                if (Math.Abs(dx) >= 4f && Math.Abs(dy) >= 4f)
                {
                    AddEdit(new Edit
                    {
                        Kind = t == ToolMode.Rect ? EditKind.Rect : EditKind.Ellipse,
                        A = new PointF(Math.Min(dragStartImg.X, dragEndImg.X), Math.Min(dragStartImg.Y, dragEndImg.Y)),
                        B = new PointF(Math.Max(dragStartImg.X, dragEndImg.X), Math.Max(dragStartImg.Y, dragEndImg.Y)),
                        StrokeColor = inkColor.Value,
                        StrokeWidth = CurrentStrokeWidth(),
                        Filled = fillToggle.Checked
                    });
                }
                else Invalidate();
            }
        }

        void UpdateViewportCursor(Point loc)
        {
            if (!viewport.Contains(loc)) { Cursor = Cursors.Default; return; }
            if (spaceDown) { Cursor = Cursors.SizeAll; return; }

            var t = ActiveTool;
            if (t == ToolMode.Crop)
            {
                if (pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
                {
                    var r = ToScreen(pendingCrop);
                    int h = HitRectHandle(r, loc, 8f * Ui);
                    if (h >= 0) { Cursor = CursorForHandle(h); return; }
                    if (r.Contains(loc)) { Cursor = Cursors.SizeAll; return; }
                }
                Cursor = canvas.Contains(loc) ? Cursors.Cross : Cursors.Default;
                return;
            }

            if (t == ToolMode.Select)
            {
                if (selectedIndex >= GeometryBaseIndex() && selectedIndex < edits.Count)
                {
                    var sel = edits[selectedIndex];
                    if (sel.Kind == EditKind.Line || sel.Kind == EditKind.Arrow)
                    {
                        if (DistSq(ToScreen(sel.A), loc) <= 64f * Ui * Ui || DistSq(ToScreen(sel.B), loc) <= 64f * Ui * Ui)
                        {
                            Cursor = Cursors.SizeAll;
                            return;
                        }
                    }
                    else
                    {
                        var sr = ToScreen(GetEditBounds(sel));
                        int h = HitRectHandle(sr, loc, 8f * Ui);
                        bool cornerOnly = !(sel.Kind == EditKind.Rect || sel.Kind == EditKind.Ellipse || sel.Kind == EditKind.Redact);
                        if (h >= 0 && (!cornerOnly || h % 2 == 0))
                        {
                            Cursor = CursorForHandle(h);
                            return;
                        }
                    }
                }
                int hit = HitTestAnnotation(loc);
                Cursor = hit >= 0 ? Cursors.SizeAll : Cursors.Default;
                return;
            }

            if (t == ToolMode.Text)
            {
                Cursor = canvas.Contains(loc) ? Cursors.IBeam : Cursors.Default;
                return;
            }

            Cursor = canvas.Contains(loc) ? Cursors.Cross : Cursors.Default;
        }

        static Cursor CursorForHandle(int h)
        {
            switch (h)
            {
                case 0:
                case 4: return Cursors.SizeNWSE;
                case 2:
                case 6: return Cursors.SizeNESW;
                case 1:
                case 5: return Cursors.SizeNS;
                case 3:
                case 7: return Cursors.SizeWE;
            }
            return Cursors.Default;
        }

        static int HitRectHandle(RectangleF r, Point loc, float tol)
        {
            var pts = GetRectHandles(r);
            float tolSq = tol * tol;
            for (int i = 0; i < pts.Length; i++)
                if (DistSq(pts[i], loc) <= tolSq) return i;
            return -1;
        }

        static float DistSq(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        // Crop handlers
        void HandleCropMouseDown(Point loc)
        {
            if (pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
            {
                var r = ToScreen(pendingCrop);
                int h = HitRectHandle(r, loc, 9f * Ui);
                if (h >= 0)
                {
                    dragging = true;
                    Capture = true;
                    cropDragHandle = h;
                    cropOrigBox = pendingCrop;
                    dragStartImg = ToImage(loc);
                    return;
                }
                if (r.Contains(loc))
                {
                    dragging = true;
                    Capture = true;
                    cropDragHandle = 8;
                    cropOrigBox = pendingCrop;
                    dragStartImg = ToImage(loc);
                    return;
                }
            }
            if (!canvas.Contains(loc)) return;
            dragging = true;
            Capture = true;
            cropDragHandle = 9;
            dragStartImg = ToImage(loc);
            pendingCrop = Rectangle.Empty;
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        void HandleCropMouseMove(Point loc)
        {
            var pt = ToImage(loc);
            if (cropDragHandle == 9)
            {
                int sx = (int)dragStartImg.X, sy = (int)dragStartImg.Y;
                pendingCrop = Rectangle.FromLTRB(Math.Min(sx, pt.X), Math.Min(sy, pt.Y), Math.Max(sx, pt.X), Math.Max(sy, pt.Y));
            }
            else if (cropDragHandle == 8)
            {
                int dx = pt.X - (int)dragStartImg.X;
                int dy = pt.Y - (int)dragStartImg.Y;
                int nx = Math.Max(0, Math.Min(work.Width - cropOrigBox.Width, cropOrigBox.X + dx));
                int ny = Math.Max(0, Math.Min(work.Height - cropOrigBox.Height, cropOrigBox.Y + dy));
                pendingCrop = new Rectangle(nx, ny, cropOrigBox.Width, cropOrigBox.Height);
            }
            else if (cropDragHandle >= 0 && cropDragHandle <= 7)
            {
                pendingCrop = ResizeRectByHandle(cropOrigBox, cropDragHandle, pt, work.Width, work.Height, 8);
            }
            Invalidate();
        }

        void HandleCropMouseUp()
        {
            cropDragHandle = -1;
            if (pendingCrop.Width < 8 || pendingCrop.Height < 8)
                pendingCrop = Rectangle.Empty;
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        static Rectangle ResizeRectByHandle(Rectangle orig, int h, Point pt, int maxW, int maxH, int minSz)
        {
            int l = orig.Left, t = orig.Top, r = orig.Right, b = orig.Bottom;
            if (h == 0 || h == 6 || h == 7) l = Math.Max(0, Math.Min(r - minSz, pt.X));
            if (h == 2 || h == 3 || h == 4) r = Math.Min(maxW, Math.Max(l + minSz, pt.X));
            if (h == 0 || h == 1 || h == 2) t = Math.Max(0, Math.Min(b - minSz, pt.Y));
            if (h == 4 || h == 5 || h == 6) b = Math.Min(maxH, Math.Max(t + minSz, pt.Y));
            return Rectangle.FromLTRB(l, t, r, b);
        }

        void ApplyPendingCrop()
        {
            if (pendingCrop.Width < 8 || pendingCrop.Height < 8) return;
            var box = Rectangle.Intersect(pendingCrop, new Rectangle(0, 0, work.Width, work.Height));
            pendingCrop = Rectangle.Empty;
            if (box.Width >= 8 && box.Height >= 8)
                AddEdit(new Edit { Kind = EditKind.Crop, Box = box });
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        void ClearPendingCrop()
        {
            if (pendingCrop.IsEmpty) return;
            pendingCrop = Rectangle.Empty;
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        // Select handlers
        void HandleSelectMouseDown(Point loc)
        {
            int baseIdx = GeometryBaseIndex();
            selectDrag = SelectDragKind.None;
            selectHandle = -1;
            selectModified = false;

            if (selectedIndex >= baseIdx && selectedIndex < edits.Count)
            {
                var sel = edits[selectedIndex];
                if (sel.Kind == EditKind.Line || sel.Kind == EditKind.Arrow)
                {
                    if (DistSq(ToScreen(sel.A), loc) <= 64f * Ui * Ui) selectDrag = SelectDragKind.EndpointA;
                    else if (DistSq(ToScreen(sel.B), loc) <= 64f * Ui * Ui) selectDrag = SelectDragKind.EndpointB;
                }
                else
                {
                    var sr = ToScreen(GetEditBounds(sel));
                    int h = HitRectHandle(sr, loc, 8f * Ui);
                    bool cornerOnly = !(sel.Kind == EditKind.Rect || sel.Kind == EditKind.Ellipse || sel.Kind == EditKind.Redact);
                    if (h >= 0 && (!cornerOnly || h % 2 == 0))
                    {
                        selectDrag = SelectDragKind.Handle;
                        selectHandle = h;
                    }
                }
            }

            if (selectDrag == SelectDragKind.None)
            {
                int hit = HitTestAnnotation(loc);
                if (hit >= baseIdx)
                {
                    selectedIndex = hit;
                    selectDrag = SelectDragKind.Move;
                }
                else
                {
                    selectedIndex = -1;
                    UpdateContextBar();
                    LayoutControls();
                    Invalidate();
                    return;
                }
            }

            var target = edits[selectedIndex];
            selectOrigEdit = target.Clone();
            selectOrigBounds = GetEditBounds(target);
            selectUndoSnap = CloneEdits(edits);
            dragStartImg = ToImageUnclampedF(loc);
            dragging = true;
            Capture = true;

            SyncControlsToSelection(target);
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        void SyncControlsToSelection(Edit sel)
        {
            if (sel.Kind != EditKind.Redact || sel.RedactStyle == RedactMode.Block)
                inkColor.Value = Color.FromArgb(255, sel.StrokeColor.R, sel.StrokeColor.G, sel.StrokeColor.B);
            if (sel.Kind == EditKind.Rect || sel.Kind == EditKind.Ellipse || sel.Kind == EditKind.Text)
                fillToggle.Checked = sel.Filled;
        }

        void HandleSelectMouseMove(Point loc)
        {
            if (selectedIndex < GeometryBaseIndex() || selectedIndex >= edits.Count) return;
            var cur = ToImageUnclampedF(loc);
            float dx = cur.X - dragStartImg.X;
            float dy = cur.Y - dragStartImg.Y;
            if (Math.Abs(dx) > 0.25f || Math.Abs(dy) > 0.25f) selectModified = true;

            var e = edits[selectedIndex];
            bool shift = (ModifierKeys & Keys.Shift) != 0;

            if (selectDrag == SelectDragKind.Move)
            {
                if (e.Kind == EditKind.Line || e.Kind == EditKind.Arrow || e.Kind == EditKind.Rect || e.Kind == EditKind.Ellipse)
                {
                    e.A = new PointF(selectOrigEdit.A.X + dx, selectOrigEdit.A.Y + dy);
                    e.B = new PointF(selectOrigEdit.B.X + dx, selectOrigEdit.B.Y + dy);
                }
                else if (e.Kind == EditKind.Redact)
                {
                    int nx = Math.Max(0, Math.Min(work.Width - selectOrigEdit.Box.Width, selectOrigEdit.Box.X + (int)Math.Round(dx)));
                    int ny = Math.Max(0, Math.Min(work.Height - selectOrigEdit.Box.Height, selectOrigEdit.Box.Y + (int)Math.Round(dy)));
                    e.Box = new Rectangle(nx, ny, selectOrigEdit.Box.Width, selectOrigEdit.Box.Height);
                }
                else if (e.Kind == EditKind.Text || e.Kind == EditKind.Step)
                {
                    e.Pos = new PointF(selectOrigEdit.Pos.X + dx, selectOrigEdit.Pos.Y + dy);
                }
                else if (e.Kind == EditKind.Pen || e.Kind == EditKind.Highlight)
                {
                    for (int i = 0; i < e.Points.Length; i++)
                        e.Points[i] = new PointF(selectOrigEdit.Points[i].X + dx, selectOrigEdit.Points[i].Y + dy);
                }
            }
            else if (selectDrag == SelectDragKind.EndpointA)
            {
                PointF target = new PointF(selectOrigEdit.A.X + dx, selectOrigEdit.A.Y + dy);
                e.A = shift ? Snap45(e.B, target) : target;
            }
            else if (selectDrag == SelectDragKind.EndpointB)
            {
                PointF target = new PointF(selectOrigEdit.B.X + dx, selectOrigEdit.B.Y + dy);
                e.B = shift ? Snap45(e.A, target) : target;
            }
            else if (selectDrag == SelectDragKind.Handle)
            {
                ResizeEditByHandle(e, selectOrigEdit, selectOrigBounds, selectHandle, cur, shift);
            }

            RebuildWork(false);
            Invalidate();
        }

        void HandleSelectMouseUp()
        {
            if (selectModified && selectUndoSnap != null)
            {
                undoStack.Push(selectUndoSnap);
                redoStack.Clear();
                UpdateActionButtons();
            }
            selectUndoSnap = null;
            selectDrag = SelectDragKind.None;
            selectHandle = -1;
            Invalidate();
        }

        void ResizeEditByHandle(Edit e, Edit orig, RectangleF ob, int h, PointF pt, bool shift)
        {
            float l = ob.Left, t = ob.Top, r = ob.Right, b = ob.Bottom;
            float minSz = e.Kind == EditKind.Redact ? 4f : 8f;
            if (h == 0 || h == 6 || h == 7) l = Math.Min(r - minSz, pt.X);
            if (h == 2 || h == 3 || h == 4) r = Math.Max(l + minSz, pt.X);
            if (h == 0 || h == 1 || h == 2) t = Math.Min(b - minSz, pt.Y);
            if (h == 4 || h == 5 || h == 6) b = Math.Max(t + minSz, pt.Y);

            if (shift && (h == 0 || h == 2 || h == 4 || h == 6) && (e.Kind == EditKind.Rect || e.Kind == EditKind.Ellipse))
            {
                float side = Math.Max(r - l, b - t);
                if (h == 0 || h == 6) l = r - side; else r = l + side;
                if (h == 0 || h == 2) t = b - side; else b = t + side;
            }

            var nb = RectangleF.FromLTRB(l, t, r, b);
            if (e.Kind == EditKind.Rect || e.Kind == EditKind.Ellipse)
            {
                e.A = new PointF(nb.Left, nb.Top);
                e.B = new PointF(nb.Right, nb.Bottom);
            }
            else if (e.Kind == EditKind.Redact)
            {
                var clamped = Rectangle.Intersect(Rectangle.Round(nb), new Rectangle(0, 0, work.Width, work.Height));
                if (clamped.Width >= 4 && clamped.Height >= 4) e.Box = clamped;
            }
            else if (e.Kind == EditKind.Pen || e.Kind == EditKind.Highlight)
            {
                float ow = Math.Max(1f, ob.Width), oh = Math.Max(1f, ob.Height);
                for (int i = 0; i < e.Points.Length; i++)
                {
                    float nx = nb.Left + (orig.Points[i].X - ob.Left) / ow * nb.Width;
                    float ny = nb.Top + (orig.Points[i].Y - ob.Top) / oh * nb.Height;
                    e.Points[i] = new PointF(nx, ny);
                }
            }
            else if (e.Kind == EditKind.Text)
            {
                float ratio = nb.Height / Math.Max(1f, ob.Height);
                e.FontSize = Math.Max(10f, Math.Min(160f, orig.FontSize * ratio));
                e.Pos = new PointF(nb.Left, nb.Top);
            }
            else if (e.Kind == EditKind.Step)
            {
                float ratio = Math.Max(nb.Width, nb.Height) / Math.Max(1f, Math.Max(ob.Width, ob.Height));
                e.StrokeWidth = Math.Max(3f, Math.Min(36f, orig.StrokeWidth * ratio));
                e.Pos = new PointF(nb.Left + nb.Width / 2f, nb.Top + nb.Height / 2f);
            }
        }

        int HitTestAnnotation(Point screenPt)
        {
            var pt = ToImageUnclampedF(screenPt);
            float pad = 7f / Math.Max(0.05f, scale);
            int baseIdx = GeometryBaseIndex();
            for (int i = edits.Count - 1; i >= baseIdx; i--)
            {
                if (HitTestEdit(edits[i], pt, pad)) return i;
            }
            return -1;
        }

        static bool HitTestEdit(Edit e, PointF pt, float pad)
        {
            switch (e.Kind)
            {
                case EditKind.Line:
                case EditKind.Arrow:
                    return DistPointToSegment(pt, e.A, e.B) <= Math.Max(pad, e.StrokeWidth * 0.75f);
                case EditKind.Rect:
                    {
                        var r = RectangleF.FromLTRB(Math.Min(e.A.X, e.B.X), Math.Min(e.A.Y, e.B.Y), Math.Max(e.A.X, e.B.X), Math.Max(e.A.Y, e.B.Y));
                        float tol = Math.Max(pad, e.StrokeWidth * 0.7f);
                        if (e.Filled) return RectangleF.Inflate(r, tol, tol).Contains(pt);
                        if (!RectangleF.Inflate(r, tol, tol).Contains(pt)) return false;
                        return !RectangleF.Inflate(r, -tol, -tol).Contains(pt);
                    }
                case EditKind.Ellipse:
                    {
                        var r = RectangleF.FromLTRB(Math.Min(e.A.X, e.B.X), Math.Min(e.A.Y, e.B.Y), Math.Max(e.A.X, e.B.X), Math.Max(e.A.Y, e.B.Y));
                        float rx = r.Width / 2f, ry = r.Height / 2f;
                        if (rx < 1f || ry < 1f) return false;
                        float cx = r.X + rx, cy = r.Y + ry;
                        float tol = Math.Max(pad, e.StrokeWidth * 0.7f);
                        float nxOut = (pt.X - cx) / (rx + tol), nyOut = (pt.Y - cy) / (ry + tol);
                        if (nxOut * nxOut + nyOut * nyOut > 1f) return false;
                        if (e.Filled || rx <= tol || ry <= tol) return true;
                        float nxIn = (pt.X - cx) / Math.Max(0.5f, rx - tol), nyIn = (pt.Y - cy) / Math.Max(0.5f, ry - tol);
                        return nxIn * nxIn + nyIn * nyIn >= 1f;
                    }
                case EditKind.Redact:
                    return RectangleF.Inflate(e.Box, pad, pad).Contains(pt);
                case EditKind.Pen:
                case EditKind.Highlight:
                    {
                        if (e.Points == null || e.Points.Length == 0) return false;
                        float tol = Math.Max(pad, e.StrokeWidth * (e.Kind == EditKind.Highlight ? 1.3f : 0.75f));
                        if (e.Points.Length == 1) return DistSq(pt, e.Points[0]) <= tol * tol;
                        for (int i = 1; i < e.Points.Length; i++)
                            if (DistPointToSegment(pt, e.Points[i - 1], e.Points[i]) <= tol) return true;
                        return false;
                    }
                case EditKind.Text:
                    return RectangleF.Inflate(GetEditBounds(e), pad, pad).Contains(pt);
                case EditKind.Step:
                    {
                        float r = StepRadius(e) + pad;
                        return DistSq(pt, e.Pos) <= r * r;
                    }
            }
            return false;
        }

        static float DistPointToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 0.0001f) return (float)Math.Sqrt(DistSq(p, a));
            float t = Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq));
            float px = a.X + t * dx, py = a.Y + t * dy;
            return (float)Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
        }

        static RectangleF GetEditBounds(Edit e)
        {
            switch (e.Kind)
            {
                case EditKind.Line:
                case EditKind.Arrow:
                case EditKind.Rect:
                case EditKind.Ellipse:
                    return RectangleF.FromLTRB(Math.Min(e.A.X, e.B.X), Math.Min(e.A.Y, e.B.Y), Math.Max(e.A.X, e.B.X), Math.Max(e.A.Y, e.B.Y));
                case EditKind.Redact:
                    return e.Box;
                case EditKind.Pen:
                case EditKind.Highlight:
                    {
                        if (e.Points == null || e.Points.Length == 0) return RectangleF.Empty;
                        float x0 = e.Points[0].X, y0 = e.Points[0].Y, x1 = x0, y1 = y0;
                        for (int i = 1; i < e.Points.Length; i++)
                        {
                            if (e.Points[i].X < x0) x0 = e.Points[i].X;
                            if (e.Points[i].X > x1) x1 = e.Points[i].X;
                            if (e.Points[i].Y < y0) y0 = e.Points[i].Y;
                            if (e.Points[i].Y > y1) y1 = e.Points[i].Y;
                        }
                        float hw = (e.Kind == EditKind.Highlight ? e.StrokeWidth * 2.4f : e.StrokeWidth) * 0.5f;
                        return RectangleF.FromLTRB(x0 - hw, y0 - hw, x1 + hw, y1 + hw);
                    }
                case EditKind.Text:
                    return MeasureTextBounds(e);
                case EditKind.Step:
                    {
                        float r = StepRadius(e);
                        return new RectangleF(e.Pos.X - r, e.Pos.Y - r, r * 2f, r * 2f);
                    }
            }
            return RectangleF.Empty;
        }

        static float StepRadius(Edit e)
        {
            return Math.Max(11f, e.StrokeWidth * 2.1f);
        }

        static RectangleF MeasureTextBounds(Edit e)
        {
            float fs = Math.Max(10f, e.FontSize);
            string txt = string.IsNullOrEmpty(e.TextValue) ? " " : e.TextValue;
            using (var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font(Ink.MenuFamily, fs, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var sz = g.MeasureString(txt, f, PointF.Empty, StringFormat.GenericTypographic);
                float padX = e.Filled ? fs * 0.45f : fs * 0.15f;
                float padY = e.Filled ? fs * 0.28f : fs * 0.1f;
                return new RectangleF(e.Pos.X, e.Pos.Y, Math.Max(14f, sz.Width + padX * 2f), Math.Max(fs + padY * 2f, sz.Height + padY * 2f));
            }
        }

        // Inline KitText for Text tool & double-click edit
        void EnsureInlineText()
        {
            if (inlineText != null) return;
            inlineText = new KitText
            {
                Multiline = true,
                AcceptsReturn = true,
                WordWrap = false,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            inlineText.TextChanged += delegate { if (inlineTextActive) PositionInlineText(); };
            inlineText.LostFocus += delegate { if (inlineTextActive) CommitInlineText(); };
            Controls.Add(inlineText);
        }

        void OpenInlineText(PointF imgPos, int existingIndex)
        {
            EnsureInlineText();
            editingTextIndex = existingIndex;
            textImgPos = imgPos;
            inlineTextActive = true;
            if (existingIndex >= 0)
            {
                var e = edits[existingIndex];
                inkColor.Value = Color.FromArgb(255, e.StrokeColor.R, e.StrokeColor.G, e.StrokeColor.B);
                fillToggle.Checked = e.Filled;
                inlineText.Text = (e.TextValue ?? "").Replace("\n", "\r\n");
            }
            else
            {
                inlineText.Text = "";
            }
            ApplyInlineTextStyle();
            inlineText.Visible = true;
            inlineText.BringToFront();
            PositionInlineText();
            inlineText.Focus();
            inlineText.SelectionStart = inlineText.Text.Length;
            inlineText.SelectionLength = 0;
        }

        void ApplyInlineTextStyle()
        {
            if (inlineText == null) return;
            float imgFs = editingTextIndex >= 0 ? edits[editingTextIndex].FontSize : CurrentFontSize();
            float scrFs = Math.Max(12f, Math.Min(48f, imgFs * scale));
            inlineText.Font = new Font(Ink.MenuFamily, scrFs, FontStyle.Bold, GraphicsUnit.Pixel);
            if (fillToggle.Checked)
            {
                inlineText.BackColor = inkColor.Value;
                inlineText.ForeColor = ContrastInk(inkColor.Value);
            }
            else
            {
                inlineText.BackColor = KitInk.Window;
                inlineText.ForeColor = Island.Contrast(inkColor.Value, KitInk.Window) >= 2.8 ? inkColor.Value : KitInk.Text;
            }
            PositionInlineText();
        }

        void PositionInlineText()
        {
            if (inlineText == null || !inlineTextActive) return;
            var sp = ToScreen(textImgPos);
            string sample = string.IsNullOrEmpty(inlineText.Text) ? "Abc" : inlineText.Text + " ";
            var measured = TextRenderer.MeasureText(sample, inlineText.Font, new Size(Math.Max(120, viewport.Width - 20), viewport.Height), TextFormatFlags.NoPrefix);
            int w = Math.Max((int)Math.Round(110 * Ui), measured.Width + (int)Math.Round(24 * Ui));
            int h = Math.Max(inlineText.Font.Height + (int)Math.Round(14 * Ui), measured.Height + (int)Math.Round(14 * Ui));
            int x = Math.Max(viewport.Left + 4, Math.Min(viewport.Right - w - 4, (int)Math.Round(sp.X)));
            int y = Math.Max(viewport.Top + 4, Math.Min(viewport.Bottom - h - 4, (int)Math.Round(sp.Y)));
            inlineText.SetBounds(x, y, Math.Min(w, viewport.Width - 8), Math.Min(h, viewport.Height - 8));
        }

        void CommitInlineText()
        {
            if (!inlineTextActive) return;
            inlineTextActive = false;
            string raw = inlineText.Text ?? "";
            string txt = raw.Replace("\r\n", "\n").TrimEnd('\r', '\n');
            inlineText.Visible = false;
            Focus();

            int idx = editingTextIndex;
            editingTextIndex = -1;

            if (idx >= GeometryBaseIndex() && idx < edits.Count)
            {
                if (txt.Trim().Length == 0)
                {
                    PushUndo();
                    edits.RemoveAt(idx);
                    selectedIndex = -1;
                    RebuildWork(false);
                    UpdateContextBar();
                    LayoutControls();
                    Invalidate();
                    return;
                }
                var existing = edits[idx];
                if (existing.TextValue != txt || existing.StrokeColor != inkColor.Value || existing.Filled != fillToggle.Checked)
                {
                    PushUndo();
                    existing.TextValue = txt;
                    existing.StrokeColor = inkColor.Value;
                    existing.Filled = fillToggle.Checked;
                    RebuildWork(false);
                    Invalidate();
                }
                return;
            }

            if (txt.Trim().Length == 0) return;
            AddEdit(new Edit
            {
                Kind = EditKind.Text,
                Pos = textImgPos,
                TextValue = txt,
                StrokeColor = inkColor.Value,
                FontSize = CurrentFontSize(),
                Filled = fillToggle.Checked
            });
        }

        void CancelInlineText()
        {
            if (!inlineTextActive) return;
            inlineTextActive = false;
            editingTextIndex = -1;
            inlineText.Visible = false;
            Focus();
            Invalidate();
        }

        void DeleteSelected()
        {
            if (ActiveTool != ToolMode.Select || selectedIndex < GeometryBaseIndex() || selectedIndex >= edits.Count) return;
            PushUndo();
            edits.RemoveAt(selectedIndex);
            selectedIndex = -1;
            RebuildWork(false);
            UpdateContextBar();
            LayoutControls();
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            // Guard shortcuts while inline KitText is active/focused
            if (inlineTextActive || (inlineText != null && inlineText.Focused))
            {
                if (keys == Keys.Escape) { CancelInlineText(); return true; }
                if (keys == Keys.Enter) { CommitInlineText(); return true; }
                if (keys == (Keys.Shift | Keys.Enter))
                {
                    inlineText.SelectedText = Environment.NewLine;
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keys);
            }

            if (keys == Keys.Space && !spaceDown && !(ActiveControl is ButtonBase))
            {
                spaceDown = true;
                UpdateViewportCursor(PointToClient(Cursor.Position));
                return true;
            }

            if (keys == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keys == (Keys.Control | Keys.Y) || keys == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
            if (keys == (Keys.Control | Keys.R)) { AddEdit(new Edit { Kind = EditKind.Turn }); return true; }
            if (keys == (Keys.Control | Keys.C)) { CopyOut(); return true; }
            if (keys == (Keys.Control | Keys.S)) { SaveOut(); return true; }
            if (keys == (Keys.Control | Keys.D0) || keys == (Keys.Control | Keys.NumPad0)) { ZoomFit(); return true; }
            if (keys == (Keys.Control | Keys.Oemplus) || keys == (Keys.Control | Keys.Add)) { StepZoom(1.25f); return true; }
            if (keys == (Keys.Control | Keys.OemMinus) || keys == (Keys.Control | Keys.Subtract)) { StepZoom(1f / 1.25f); return true; }

            if (keys == Keys.Enter)
            {
                if (ActiveTool == ToolMode.Crop && pendingCrop.Width >= 8 && pendingCrop.Height >= 8)
                {
                    ApplyPendingCrop();
                    return true;
                }
                if (ActiveControl is KitButton && ActiveControl != copyBtn)
                    return base.ProcessCmdKey(ref msg, keys);
                CopyOut();
                return true;
            }

            if (keys == Keys.Delete || keys == Keys.Back)
            {
                if (ActiveTool == ToolMode.Select && selectedIndex >= GeometryBaseIndex() && selectedIndex < edits.Count)
                {
                    DeleteSelected();
                    return true;
                }
            }

            if (keys == Keys.Escape)
            {
                if (panning) { panning = false; Capture = false; UpdateViewportCursor(PointToClient(Cursor.Position)); return true; }
                if (dragging) { dragging = false; Capture = false; stroke.Clear(); dragBox = Rectangle.Empty; Invalidate(); return true; }
                if (ActiveTool == ToolMode.Crop && !pendingCrop.IsEmpty) { ClearPendingCrop(); return true; }
                if (ActiveTool == ToolMode.Select && selectedIndex >= 0) { selectedIndex = -1; UpdateContextBar(); LayoutControls(); Invalidate(); return true; }
                Close();
                return true;
            }

            // Single-key tool shortcuts (when no modifiers held)
            if ((keys & Keys.Modifiers) == Keys.None)
            {
                switch (keys)
                {
                    case Keys.V: toolStrip.Pick(ToolMode.Select); return true;
                    case Keys.A: toolStrip.Pick(ToolMode.Arrow); return true;
                    case Keys.L: toolStrip.Pick(ToolMode.Line); return true;
                    case Keys.R: toolStrip.Pick(ToolMode.Rect); return true;
                    case Keys.O: toolStrip.Pick(ToolMode.Ellipse); return true;
                    case Keys.P: toolStrip.Pick(ToolMode.Pen); return true;
                    case Keys.H: toolStrip.Pick(ToolMode.Highlight); return true;
                    case Keys.T: toolStrip.Pick(ToolMode.Text); return true;
                    case Keys.N: toolStrip.Pick(ToolMode.Step); return true;
                    case Keys.B: toolStrip.Pick(ToolMode.Redact); return true;
                    case Keys.C: toolStrip.Pick(ToolMode.Crop); return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keys);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && spaceDown)
            {
                spaceDown = false;
                UpdateViewportCursor(PointToClient(Cursor.Position));
            }
        }

        // Geometry boundary & lightweight Undo/Redo/Reset
        static bool IsGeometryEdit(EditKind k)
        {
            return k == EditKind.Crop || k == EditKind.Turn || k == EditKind.MirrorX || k == EditKind.MirrorY || k == EditKind.Frame;
        }

        int GeometryBaseIndex()
        {
            for (int i = edits.Count - 1; i >= 0; i--)
                if (IsGeometryEdit(edits[i].Kind)) return i + 1;
            return 0;
        }

        int NextStepNumber()
        {
            int max = 0;
            int start = Math.Max(GeometryBaseIndex(), Math.Min(stepFloorIndex, edits.Count));
            for (int i = start; i < edits.Count; i++)
                if (edits[i].Kind == EditKind.Step && edits[i].StepNumber > max) max = edits[i].StepNumber;
            return max + 1;
        }

        static List<Edit> CloneEdits(List<Edit> src)
        {
            var list = new List<Edit>(src.Count);
            for (int i = 0; i < src.Count; i++) list.Add(src[i].Clone());
            return list;
        }

        void PushUndo()
        {
            undoStack.Push(CloneEdits(edits));
            redoStack.Clear();
            UpdateActionButtons();
        }

        void AddEdit(Edit e)
        {
            if (inlineTextActive) CommitInlineText();
            PushUndo();
            edits.Add(e);
            var before = work.Size;
            if (IsGeometryEdit(e.Kind))
            {
                selectedIndex = -1;
                cachedBaseCount = -1;
                RebuildWork(true);
            }
            else
            {
                work = Apply(work, e);
                if (work.Size != before) { fitMode = true; pan = PointF.Empty; UpdateCanvas(); }
                UpdateHint();
                Invalidate();
            }
            UpdateActionButtons();
        }

        void Undo()
        {
            if (undoStack.Count == 0) return;
            CancelPendingOps();
            redoStack.Push(CloneEdits(edits));
            edits.Clear();
            edits.AddRange(undoStack.Pop());
            if (stepFloorIndex > edits.Count) stepFloorIndex = 0;
            selectedIndex = -1;
            cachedBaseCount = -1;
            RebuildWork(true);
        }

        void Redo()
        {
            if (redoStack.Count == 0) return;
            CancelPendingOps();
            undoStack.Push(CloneEdits(edits));
            edits.Clear();
            edits.AddRange(redoStack.Pop());
            if (stepFloorIndex > edits.Count) stepFloorIndex = 0;
            selectedIndex = -1;
            cachedBaseCount = -1;
            RebuildWork(true);
        }

        void ResetAll()
        {
            CancelPendingOps();
            if (edits.Count == 0) return;
            PushUndo();
            edits.Clear();
            stepFloorIndex = 0;
            selectedIndex = -1;
            cachedBaseCount = -1;
            RebuildWork(true);
        }

        void CancelPendingOps()
        {
            if (inlineTextActive) CancelInlineText();
            dragging = false;
            panning = false;
            Capture = false;
            stroke.Clear();
            dragBox = Rectangle.Empty;
            pendingCrop = Rectangle.Empty;
        }

        void RebuildWork(bool resetFitOnSizeChange)
        {
            var before = work.Size;
            int geomIdx = GeometryBaseIndex();
            if (cachedBaseCount != geomIdx || baseWork == null)
            {
                if (baseWork != null) baseWork.Dispose();
                baseWork = new Bitmap(original);
                for (int i = 0; i < geomIdx; i++)
                    baseWork = Apply(baseWork, edits[i]);
                cachedBaseCount = geomIdx;
            }

            work.Dispose();
            work = new Bitmap(baseWork);
            for (int i = geomIdx; i < edits.Count; i++)
                work = Apply(work, edits[i]);

            // Sync framePopup selection to current latest Frame edit
            FramePreset activeFrame = FramePreset.None;
            for (int i = edits.Count - 1; i >= 0; i--)
                if (edits[i].Kind == EditKind.Frame) { activeFrame = edits[i].Frame; break; }
            framePopup.Selected = (int)activeFrame;

            if (resetFitOnSizeChange && work.Size != before)
            {
                fitMode = true;
                pan = PointF.Empty;
            }
            UpdateContextBar();
            UpdateActionButtons();
            LayoutControls();
            Invalidate();
        }

        void UpdateActionButtons()
        {
            undoBtn.Enabled = undoStack.Count > 0;
            redoBtn.Enabled = redoStack.Count > 0;
            resetBtn.Enabled = edits.Count > 0;
        }

        static Bitmap Apply(Bitmap b, Edit e)
        {
            switch (e.Kind)
            {
                case EditKind.Redact:
                    if (e.RedactStyle == RedactMode.Pixelate) Pixelate(b, e.Box);
                    else if (e.RedactStyle == RedactMode.Blur)
                    {
                        int rad = Math.Max(10, Math.Min(22, Math.Min(e.Box.Width, e.Box.Height) / 4));
                        BoxBlur(b, e.Box, rad);
                        BoxBlur(b, e.Box, rad);
                    }
                    else
                    {
                        var r = Rectangle.Intersect(e.Box, new Rectangle(0, 0, b.Width, b.Height));
                        if (r.Width > 0 && r.Height > 0)
                            using (var g = Graphics.FromImage(b))
                            using (var br = new SolidBrush(Color.FromArgb(255, e.StrokeColor)))
                                g.FillRectangle(br, r);
                    }
                    return b;

                case EditKind.Pen:
                    using (var g = Graphics.FromImage(b))
                        DrawStroke(g, e.Points, e.StrokeColor, e.StrokeWidth, 1f, PointF.Empty);
                    return b;

                case EditKind.Highlight:
                    using (var g = Graphics.FromImage(b))
                        DrawStroke(g, e.Points, HighlightColor(e.StrokeColor), e.StrokeWidth * 2.4f, 1f, PointF.Empty);
                    return b;

                case EditKind.Line:
                case EditKind.Arrow:
                case EditKind.Rect:
                case EditKind.Ellipse:
                case EditKind.Text:
                case EditKind.Step:
                    using (var g = Graphics.FromImage(b))
                        DrawEditVector(g, e, 1f, PointF.Empty);
                    return b;

                case EditKind.Crop:
                    {
                        var box = Rectangle.Intersect(e.Box, new Rectangle(0, 0, b.Width, b.Height));
                        if (box.Width < 2 || box.Height < 2) return b;
                        var cut = b.Clone(box, PixelFormat.Format32bppArgb);
                        b.Dispose();
                        return cut;
                    }
                case EditKind.Turn: b.RotateFlip(RotateFlipType.Rotate90FlipNone); return b;
                case EditKind.MirrorX: b.RotateFlip(RotateFlipType.RotateNoneFlipX); return b;
                case EditKind.MirrorY: b.RotateFlip(RotateFlipType.RotateNoneFlipY); return b;
                case EditKind.Frame: return ApplyFrame(b, e.Frame);
            }
            return b;
        }

        static void DrawEditVector(Graphics g, Edit e, float scale, PointF origin)
        {
            var prevSmooth = g.SmoothingMode;
            var prevOffset = g.PixelOffsetMode;
            var prevText = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            try
            {
                float w = Math.Max(1f, e.StrokeWidth * scale);
                PointF a = new PointF(origin.X + e.A.X * scale, origin.Y + e.A.Y * scale);
                PointF b = new PointF(origin.X + e.B.X * scale, origin.Y + e.B.Y * scale);

                switch (e.Kind)
                {
                    case EditKind.Line:
                        using (var p = new Pen(e.StrokeColor, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(p, a, b);
                        break;

                    case EditKind.Arrow:
                        {
                            float dx = b.X - a.X, dy = b.Y - a.Y;
                            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (dist < 1f) break;
                            float ux = dx / dist, uy = dy / dist;
                            float headLen = Math.Min(dist * 0.65f, Math.Max(10f * scale, w * 3.3f));
                            float headHalfW = headLen * 0.44f;
                            PointF shaftEnd = new PointF(b.X - ux * headLen * 0.72f, b.Y - uy * headLen * 0.72f);
                            using (var p = new Pen(e.StrokeColor, w) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
                                g.DrawLine(p, a, shaftEnd);
                            PointF basePt = new PointF(b.X - ux * headLen, b.Y - uy * headLen);
                            PointF left = new PointF(basePt.X - uy * headHalfW, basePt.Y + ux * headHalfW);
                            PointF right = new PointF(basePt.X + uy * headHalfW, basePt.Y - ux * headHalfW);
                            using (var br = new SolidBrush(e.StrokeColor))
                                g.FillPolygon(br, new[] { b, left, right });
                            break;
                        }

                    case EditKind.Rect:
                        {
                            var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                            if (e.Filled)
                                using (var br = new SolidBrush(Color.FromArgb(68, e.StrokeColor))) g.FillRectangle(br, r);
                            using (var p = new Pen(e.StrokeColor, w) { LineJoin = LineJoin.Round })
                                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                            break;
                        }

                    case EditKind.Ellipse:
                        {
                            var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                            if (e.Filled)
                                using (var br = new SolidBrush(Color.FromArgb(68, e.StrokeColor))) g.FillEllipse(br, r);
                            using (var p = new Pen(e.StrokeColor, w))
                                g.DrawEllipse(p, r);
                            break;
                        }

                    case EditKind.Text:
                        {
                            if (string.IsNullOrEmpty(e.TextValue)) break;
                            float fs = Math.Max(8f, e.FontSize * scale);
                            float px = origin.X + e.Pos.X * scale;
                            float py = origin.Y + e.Pos.Y * scale;
                            using (var f = new Font(Ink.MenuFamily, fs, FontStyle.Bold, GraphicsUnit.Pixel))
                            {
                                var sz = g.MeasureString(e.TextValue, f, PointF.Empty, StringFormat.GenericTypographic);
                                float padX = e.Filled ? fs * 0.45f : fs * 0.15f;
                                float padY = e.Filled ? fs * 0.28f : fs * 0.1f;
                                var box = new RectangleF(px, py, Math.Max(14f * scale, sz.Width + padX * 2f), Math.Max(fs + padY * 2f, sz.Height + padY * 2f));
                                if (e.Filled)
                                {
                                    using (var path = Ink.Round(box, Math.Min(10f * scale, box.Height * 0.28f)))
                                    using (var br = new SolidBrush(Color.FromArgb(240, e.StrokeColor)))
                                        g.FillPath(br, path);
                                    Color ink = ContrastInk(e.StrokeColor);
                                    using (var br = new SolidBrush(ink))
                                        g.DrawString(e.TextValue, f, br, new PointF(px + padX, py + padY), StringFormat.GenericTypographic);
                                }
                                else
                                {
                                    Color shadow = Island.Contrast(Color.Black, e.StrokeColor) >= 2.0 ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(120, 255, 255, 255);
                                    float off = Math.Max(1f, fs * 0.06f);
                                    using (var sh = new SolidBrush(shadow))
                                        g.DrawString(e.TextValue, f, sh, new PointF(px + padX + off, py + padY + off), StringFormat.GenericTypographic);
                                    using (var br = new SolidBrush(e.StrokeColor))
                                        g.DrawString(e.TextValue, f, br, new PointF(px + padX, py + padY), StringFormat.GenericTypographic);
                                }
                            }
                            break;
                        }

                    case EditKind.Step:
                        {
                            float r = StepRadius(e) * scale;
                            float cx = origin.X + e.Pos.X * scale;
                            float cy = origin.Y + e.Pos.Y * scale;
                            var shadowRect = new RectangleF(cx - r, cy - r + 1.8f * scale, r * 2f, r * 2f);
                            using (var sh = new SolidBrush(Color.FromArgb(65, 0, 0, 0))) g.FillEllipse(sh, shadowRect);
                            var circle = new RectangleF(cx - r, cy - r, r * 2f, r * 2f);
                            using (var br = new SolidBrush(Color.FromArgb(255, e.StrokeColor))) g.FillEllipse(br, circle);
                            Color ink = ContrastInk(e.StrokeColor);
                            using (var ring = new Pen(Color.FromArgb(170, ink), Math.Max(1f, 1.4f * scale))) g.DrawEllipse(ring, circle);
                            string num = e.StepNumber.ToString();
                            float fs = num.Length > 1 ? r * 0.92f : r * 1.1f;
                            using (var f = new Font(Ink.MenuFamily, Math.Max(8f, fs), FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var br = new SolidBrush(ink))
                            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                g.DrawString(num, f, br, new RectangleF(circle.X, circle.Y + 0.5f * scale, circle.Width, circle.Height), sf);
                            break;
                        }
                }
            }
            finally
            {
                g.SmoothingMode = prevSmooth;
                g.PixelOffsetMode = prevOffset;
                g.TextRenderingHint = prevText;
            }
        }

        static Color ContrastInk(Color bg)
        {
            Color white = Color.FromArgb(255, 255, 255, 255);
            Color dark = Color.FromArgb(255, 18, 18, 22);
            return Island.Contrast(white, bg) >= Island.Contrast(dark, bg) ? white : dark;
        }

        static Bitmap ApplyFrame(Bitmap src, FramePreset preset)
        {
            if (preset == FramePreset.None) return src;
            int pad = Math.Max(28, Math.Min(96, (int)Math.Round(Math.Min(src.Width, src.Height) * 0.085f)));
            int outW = src.Width + pad * 2;
            int outH = src.Height + pad * 2;
            float radius = Math.Max(10f, Math.Min(22f, Math.Min(src.Width, src.Height) * 0.025f));

            var dst = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var full = new RectangleF(0, 0, outW, outH);
                Color c1, c2, c3;
                switch (preset)
                {
                    case FramePreset.Card:
                        c1 = Color.FromArgb(255, 246, 247, 250);
                        c2 = Color.FromArgb(255, 234, 237, 244);
                        c3 = Color.FromArgb(255, 226, 230, 240);
                        break;
                    case FramePreset.BlueGrad:
                        c1 = Color.FromArgb(255, 59, 130, 246);
                        c2 = Color.FromArgb(255, 99, 102, 241);
                        c3 = Color.FromArgb(255, 6, 182, 212);
                        break;
                    case FramePreset.SunsetGrad:
                        c1 = Color.FromArgb(255, 249, 115, 22);
                        c2 = Color.FromArgb(255, 236, 72, 153);
                        c3 = Color.FromArgb(255, 139, 92, 246);
                        break;
                    case FramePreset.MintGrad:
                        c1 = Color.FromArgb(255, 16, 185, 129);
                        c2 = Color.FromArgb(255, 20, 184, 166);
                        c3 = Color.FromArgb(255, 14, 165, 233);
                        break;
                    default: // Dark
                        c1 = Color.FromArgb(255, 32, 36, 44);
                        c2 = Color.FromArgb(255, 21, 24, 30);
                        c3 = Color.FromArgb(255, 13, 15, 19);
                        break;
                }

                using (var lg = new LinearGradientBrush(full, c1, c3, 40f))
                {
                    var cb = new ColorBlend(3);
                    cb.Colors = new[] { c1, c2, c3 };
                    cb.Positions = new[] { 0f, 0.52f, 1f };
                    lg.InterpolationColors = cb;
                    g.FillRectangle(lg, full);
                }

                var imgRect = new RectangleF(pad, pad, src.Width, src.Height);

                // Soft multi-pass drop shadow
                float shY = pad * 0.14f;
                int[] alphas = { 4, 8, 13, 19, 26 };
                float[] spreads = { 1.0f, 0.72f, 0.48f, 0.28f, 0.14f };
                float maxSpread = Math.Max(10f, pad * 0.45f);
                for (int i = 0; i < alphas.Length; i++)
                {
                    float sp = maxSpread * spreads[i];
                    var sr = new RectangleF(imgRect.X - sp * 0.6f, imgRect.Y + shY - sp * 0.35f, imgRect.Width + sp * 1.2f, imgRect.Height + sp * 1.25f);
                    using (var spath = Ink.Round(sr, radius + sp * 0.35f))
                    using (var sb = new SolidBrush(Color.FromArgb(alphas[i], 0, 0, 0)))
                        g.FillPath(sb, spath);
                }

                using (var clipPath = Ink.Round(imgRect, radius))
                {
                    var st = g.Save();
                    g.SetClip(clipPath, CombineMode.Intersect);
                    g.DrawImage(src, imgRect);
                    g.Restore(st);

                    Color rim = preset == FramePreset.Card ? Color.FromArgb(36, 0, 0, 0) : Color.FromArgb(46, 255, 255, 255);
                    using (var pen = new Pen(rim, 1.2f)) g.DrawPath(pen, clipPath);
                }
            }
            src.Dispose();
            return dst;
        }

        internal static void DrawStroke(Graphics g, PointF[] pts, Color color, float width, float scale, PointF origin)
        {
            if (pts == null || pts.Length == 0) return;
            float w = Math.Max(1f, width * scale);
            bool moved = false;
            for (int i = 1; i < pts.Length; i++)
            {
                float dx = pts[i].X - pts[0].X, dy = pts[i].Y - pts[0].Y;
                if (dx * dx + dy * dy > 0.04f) { moved = true; break; }
            }
            var prevSmooth = g.SmoothingMode;
            var prevOffset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            try
            {
                if (!moved)
                {
                    float cx = origin.X + pts[0].X * scale, cy = origin.Y + pts[0].Y * scale;
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, cx - w / 2f, cy - w / 2f, w, w);
                    return;
                }
                var mapped = new PointF[pts.Length];
                for (int i = 0; i < pts.Length; i++) mapped[i] = new PointF(origin.X + pts[i].X * scale, origin.Y + pts[i].Y * scale);
                using (var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, mapped);
            }
            finally
            {
                g.SmoothingMode = prevSmooth;
                g.PixelOffsetMode = prevOffset;
            }
        }

        void CopyOut()
        {
            if (inlineTextActive) CommitInlineText();
            try { Clipboard.SetImage(work); }
            catch (Exception ex) { Log.Write("redact copy: " + ex.Message); return; }
            var h = Copied;
            if (h != null) h();
            Close();
        }

        void SaveOut()
        {
            if (inlineTextActive) CommitInlineText();
            using (var d = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp",
                FileName = "gambar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png",
                RestoreDirectory = true
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string ext = (Path.GetExtension(d.FileName) ?? "").ToLowerInvariant();
                    ImageFormat fmt = ImageFormat.Png;
                    if (d.FilterIndex == 2 || ext == ".jpg" || ext == ".jpeg") fmt = ImageFormat.Jpeg;
                    else if (d.FilterIndex == 3 || ext == ".bmp") fmt = ImageFormat.Bmp;

                    if (fmt == ImageFormat.Jpeg || fmt == ImageFormat.Bmp)
                    {
                        using (var flat = new Bitmap(work.Width, work.Height, PixelFormat.Format24bppRgb))
                        using (var g = Graphics.FromImage(flat))
                        {
                            g.Clear(Color.White);
                            g.DrawImageUnscaled(work, 0, 0);
                            flat.Save(d.FileName, fmt);
                        }
                    }
                    else
                    {
                        work.Save(d.FileName, fmt);
                    }
                }
                catch (Exception ex) { Log.Write("redact save: " + ex.Message); return; }
            }
            Close();
        }

        // Blocks too big to read text back
        internal static void Pixelate(Bitmap bmp, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (r.Width < 2 || r.Height < 2) return;
            int block = Math.Max(10, Math.Min(48, Math.Min(r.Width, r.Height) / 3));
            var data = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var buf = new byte[stride * r.Height];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                for (int by = 0; by < r.Height; by += block)
                    for (int bx = 0; bx < r.Width; bx += block)
                    {
                        int ex = Math.Min(r.Width, bx + block), ey = Math.Min(r.Height, by + block);
                        long sb = 0, sg = 0, sr = 0, sa = 0, n = 0;
                        for (int y = by; y < ey; y++)
                            for (int x = bx, i = y * stride + bx * 4; x < ex; x++, i += 4) { sb += buf[i]; sg += buf[i + 1]; sr += buf[i + 2]; sa += buf[i + 3]; n++; }
                        byte b = (byte)(sb / n), gr = (byte)(sg / n), rd = (byte)(sr / n), a = (byte)(sa / n);
                        for (int y = by; y < ey; y++)
                            for (int x = bx, i = y * stride + bx * 4; x < ex; x++, i += 4) { buf[i] = b; buf[i + 1] = gr; buf[i + 2] = rd; buf[i + 3] = a; }
                    }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        // Fast 2-pass separable LockBits box blur (sliding window O(W * H))
        internal static void BoxBlur(Bitmap bmp, Rectangle r, int radius)
        {
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (r.Width < 2 || r.Height < 2 || radius < 1) return;
            int rad = Math.Max(1, Math.Min(60, radius));
            var data = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int w = r.Width, h = r.Height, stride = data.Stride;
                var src = new byte[stride * h];
                var tmp = new byte[stride * h];
                Marshal.Copy(data.Scan0, src, 0, src.Length);

                // Pass 1: Horizontal (src -> tmp)
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    int sb = 0, sg = 0, sr = 0, sa = 0, count = 0;
                    int rightEnd = Math.Min(w - 1, rad);
                    for (int kx = 0; kx <= rightEnd; kx++)
                    {
                        int idx = row + kx * 4;
                        sb += src[idx]; sg += src[idx + 1]; sr += src[idx + 2]; sa += src[idx + 3];
                        count++;
                    }
                    for (int x = 0; x < w; x++)
                    {
                        int outIdx = row + x * 4;
                        tmp[outIdx] = (byte)(sb / count);
                        tmp[outIdx + 1] = (byte)(sg / count);
                        tmp[outIdx + 2] = (byte)(sr / count);
                        tmp[outIdx + 3] = (byte)(sa / count);

                        int remX = x - rad;
                        if (remX >= 0)
                        {
                            int idx = row + remX * 4;
                            sb -= src[idx]; sg -= src[idx + 1]; sr -= src[idx + 2]; sa -= src[idx + 3];
                            count--;
                        }
                        int addX = x + rad + 1;
                        if (addX < w)
                        {
                            int idx = row + addX * 4;
                            sb += src[idx]; sg += src[idx + 1]; sr += src[idx + 2]; sa += src[idx + 3];
                            count++;
                        }
                    }
                }

                // Pass 2: Vertical (tmp -> src)
                for (int x = 0; x < w; x++)
                {
                    int col = x * 4;
                    int sb = 0, sg = 0, sr = 0, sa = 0, count = 0;
                    int botEnd = Math.Min(h - 1, rad);
                    for (int ky = 0; ky <= botEnd; ky++)
                    {
                        int idx = ky * stride + col;
                        sb += tmp[idx]; sg += tmp[idx + 1]; sr += tmp[idx + 2]; sa += tmp[idx + 3];
                        count++;
                    }
                    for (int y = 0; y < h; y++)
                    {
                        int outIdx = y * stride + col;
                        src[outIdx] = (byte)(sb / count);
                        src[outIdx + 1] = (byte)(sg / count);
                        src[outIdx + 2] = (byte)(sr / count);
                        src[outIdx + 3] = (byte)(sa / count);

                        int remY = y - rad;
                        if (remY >= 0)
                        {
                            int idx = remY * stride + col;
                            sb -= tmp[idx]; sg -= tmp[idx + 1]; sr -= tmp[idx + 2]; sa -= tmp[idx + 3];
                            count--;
                        }
                        int addY = y + rad + 1;
                        if (addY < h)
                        {
                            int idx = addY * stride + col;
                            sb += tmp[idx]; sg += tmp[idx + 1]; sr += tmp[idx + 2]; sa += tmp[idx + 3];
                            count++;
                        }
                    }
                }

                Marshal.Copy(src, 0, data.Scan0, src.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                if (checkerBrush != null) checkerBrush.Dispose();
                if (checkerBmp != null) checkerBmp.Dispose();
                if (baseWork != null) baseWork.Dispose();
                original.Dispose();
                work.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
