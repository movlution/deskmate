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
    partial class RedactWindow : KitWindow
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

    }
}
