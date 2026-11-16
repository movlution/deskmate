using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.Graphics.DirectX;
using Windows.Graphics.Effects;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Deskmate
{
    static class CompGlass
    {
        public struct PillSpec
        {
            public float X, Y, W, H, Radius;
            public PillSpec(float x, float y, float w, float h, float radius)
            {
                X = x; Y = y; W = w; H = h; Radius = radius;
            }
        }

        public struct GlassSpec
        {
            public Color Tint;
            public float Blur;
            public float Saturation;
            public float Opacity;
        }

        // Dark liquid pills sat at alpha 138, which let roughly 45% of whatever sits behind them through. That is
        // fine for the island, which mostly floats over wallpaper, but the dock bar sits against arbitrary window
        // content: over a white page the bar measured ~100 luma and the page's table lines came through as a pale
        // stripe across its middle. The bar therefore gets a denser tint of its own.
        public static Color TintFor(Color veil, bool light, bool liquid, bool panelOrSheet, bool bar = false)
        {
            int a;
            if (light) a = bar ? (liquid ? 212 : 220) : panelOrSheet ? (liquid ? 184 : 198) : (liquid ? 168 : 186);
            else if (bar) a = liquid ? 214 : 220;
            else a = panelOrSheet ? (liquid ? 164 : 178) : (liquid ? 138 : 160);
            return Color.FromArgb(a, veil.R, veil.G, veil.B);
        }

        // One glass recipe for every surface: island, dock bar, cards and sheets share these numbers. The bar is
        // the exception: it floats over window content rather than wallpaper, so it gets a heavier blur so nothing
        // behind it stays legible as a shape, and a denser tint. A conventionally frosted dock, not a clear pane.
        public static GlassSpec Spec(bool liquid, bool panelOrSheet, float scale, float opacity, bool bar = false)
        {
            return new GlassSpec
            {
                Tint = TintFor(Ink.Veil, Ink.Light, liquid, panelOrSheet, bar),
                Blur = (bar ? 34f : panelOrSheet ? 24f : liquid ? 18f : 22f) * scale,
                Saturation = liquid ? 1.38f : 1.12f,
                Opacity = opacity
            };
        }

        public static bool Available { get { return EnsureInit(); } }

        // Plain CreateBackdropBrush: samples live content behind the window and keeps working while
        // the window is inactive. CreateHostBackdropBrush renders a flat fallback colour for a
        // NOACTIVATE window (verified by screenshot) and already carries its own system blur.
        static CompositionBackdropBrush NewBackdrop()
        {
            return compositor.CreateBackdropBrush();
        }
        static bool tried, ready;
        static IntPtr dqController, d3dDev, d3dCtx, dxgiDev, d2dDev;
        static Compositor compositor;
        static CompositionGraphicsDevice gfxDevice;
        static CompositionDrawingSurface noiseSurface;
        static CompositionSurfaceBrush noiseBrush;
        static CompositionEffectFactory clipFactory, maskFactory;

        static readonly Guid IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        static readonly Guid IID_ID2D1DeviceContext = new Guid("e8f7fe7a-191c-466d-ad95-975678bda998");

        static readonly Guid CLSID_GaussianBlur = new Guid("1feb6d69-2fe6-4ac9-8c58-1d7f93e7a6a5");
        static readonly Guid CLSID_Saturation = new Guid("5cb2d9cf-327d-459f-a0ce-40c0b2086bf7");
        static readonly Guid CLSID_Flood = new Guid("61c23c20-ae69-4d8e-94cf-50078df638f2");
        static readonly Guid CLSID_Composite = new Guid("48fc9f51-f6ac-48f1-8b58-3b28ac46f76d");
        static readonly Guid CLSID_Border = new Guid("2a2d49c0-4acf-43c7-8c6a-7c4a27874d27");

        static bool EnsureInit()
        {
            if (tried) return ready;
            tried = true;
            try
            {
                var opt = new DispatcherQueueOptions
                {
                    dwSize = Marshal.SizeOf(typeof(DispatcherQueueOptions)),
                    threadType = 2,    // DQTYPE_THREAD_CURRENT
                    apartmentType = 2  // DQTAT_COM_STA
                };
                CreateDispatcherQueueController(opt, out dqController);

                compositor = new Compositor();
                // Verify backdrop brush support (Windows 10 1607+)
                using (var test = NewBackdrop()) { }

                int fl;
                int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out d3dDev, out fl, out d3dCtx);
                if (hr != 0) throw new COMException("D3D11CreateDevice", hr);
                Guid iidDxgi = IID_IDXGIDevice;
                hr = Marshal.QueryInterface(d3dDev, ref iidDxgi, out dxgiDev);
                if (hr != 0) throw new COMException("IDXGIDevice", hr);
                hr = D2D1CreateDevice(dxgiDev, IntPtr.Zero, out d2dDev);
                if (hr != 0) throw new COMException("D2D1CreateDevice", hr);

                IntPtr gfxPtr;
                hr = ((ICompositorInterop)(object)compositor).CreateGraphicsDevice(d2dDev, out gfxPtr);
                if (hr != 0) throw new COMException("CreateGraphicsDevice", hr);
                gfxDevice = (CompositionGraphicsDevice)Marshal.GetObjectForIUnknown(gfxPtr);
                Marshal.Release(gfxPtr);

                noiseSurface = gfxDevice.CreateDrawingSurface(
                    new Windows.Foundation.Size(128, 128),
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    DirectXAlphaMode.Premultiplied);
                using (var noiseBmp = BuildNoiseTile(128))
                    UploadBitmap(noiseBmp, noiseSurface, 128, 128);
                noiseBrush = compositor.CreateSurfaceBrush(noiseSurface);
                noiseBrush.Stretch = CompositionStretch.None;

                clipFactory = BuildFactory(false);
                maskFactory = BuildFactory(true);
                if (clipFactory.ExtendedError != null || maskFactory.ExtendedError != null)
                    throw new InvalidOperationException("EffectFactory error");

                ready = true;
            }
            catch (Exception ex)
            {
                Log.Write("CompGlass disabled (fallback): " + ex.Message);
                ready = false;
            }
            return ready;
        }

        static CompositionEffectFactory BuildFactory(bool withMask)
        {
            var blur = new D2DEffect
            {
                Name = "Blur",
                EffectId = CLSID_GaussianBlur,
                Sources = { new CompositionEffectSourceParameter("backdrop") },
                Props = { 18.0f, 1u, 1u }, // Optimization=Balanced(1), BorderMode=Hard(1)
                Map = { { "BlurAmount", Tuple.Create(0u, GRAPHICS_EFFECT_PROPERTY_MAPPING.Direct) } }
            };
            var sat = new D2DEffect
            {
                Name = "Sat",
                EffectId = CLSID_Saturation,
                Sources = { blur },
                Props = { 1.25f },
                Map = { { "Saturation", Tuple.Create(0u, GRAPHICS_EFFECT_PROPERTY_MAPPING.Direct) } }
            };
            var tint = new D2DEffect
            {
                Name = "Tint",
                EffectId = CLSID_Flood,
                Props = { new float[] { 0.08f, 0.08f, 0.10f, 0.6f } },
                Map = { { "Color", Tuple.Create(0u, GRAPHICS_EFFECT_PROPERTY_MAPPING.ColorToVector4) } }
            };
            var compTint = new D2DEffect
            {
                Name = "CompTint",
                EffectId = CLSID_Composite,
                Sources = { sat, tint },
                Props = { 0u } // SourceOver
            };
            var wrap = new D2DEffect
            {
                Name = "Wrap",
                EffectId = CLSID_Border,
                Sources = { new CompositionEffectSourceParameter("noise") },
                Props = { 2u, 2u } // ExtendMode Wrap, Wrap
            };
            var compGrain = new D2DEffect
            {
                Name = "CompGrain",
                EffectId = CLSID_Composite,
                Sources = { compTint, wrap },
                Props = { 0u } // SourceOver
            };
            IGraphicsEffect root = compGrain;
            if (withMask)
            {
                root = new D2DEffect
                {
                    Name = "Mask",
                    EffectId = CLSID_Composite,
                    Sources = { compGrain, new CompositionEffectSourceParameter("mask") },
                    Props = { 3u } // D2D1_COMPOSITE_MODE_DESTINATION_IN (2 is SOURCE_IN)
                };
            }
            return compositor.CreateEffectFactory(root, new[] { "Blur.BlurAmount", "Sat.Saturation", "Tint.Color" });
        }

        static Bitmap BuildNoiseTile(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new byte[size * 4];
                var rng = new Random(0x54455049);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int v = rng.Next(256);
                        // Subtle 1.5% monochrome dither grain (premultiplied BGRA)
                        byte a = (byte)(v > 180 ? 5 : v < 75 ? 4 : 0);
                        byte c = v > 180 ? a : (byte)0;
                        int i = x * 4;
                        row[i] = c; row[i + 1] = c; row[i + 2] = c; row[i + 3] = a;
                    }
                    Marshal.Copy(row, 0, IntPtr.Add(bd.Scan0, y * bd.Stride), row.Length);
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return bmp;
        }

        static bool UploadBitmap(Bitmap bmp, CompositionDrawingSurface surface, int w, int h)
        {
            var si = (ICompositionDrawingSurfaceInterop)(object)surface;
            IntPtr rt;
            Point off;
            Guid iidD2d = IID_ID2D1DeviceContext;
            int hr = si.BeginDraw(IntPtr.Zero, ref iidD2d, out rt, out off);
            if (hr != 0) return false;
            bool uploaded = false;
            try
            {
                IntPtr vtbl = Marshal.ReadIntPtr(rt);
                var setDpi = Marshal.GetDelegateForFunctionPointer<D2DSetDpiFn>(Marshal.ReadIntPtr(vtbl, 51 * IntPtr.Size));
                var clear = Marshal.GetDelegateForFunctionPointer<D2DClearFn>(Marshal.ReadIntPtr(vtbl, 47 * IntPtr.Size));
                var createBmp = Marshal.GetDelegateForFunctionPointer<D2DCreateBitmapFn>(Marshal.ReadIntPtr(vtbl, 4 * IntPtr.Size));
                var drawBmp = Marshal.GetDelegateForFunctionPointer<D2DDrawBitmapFn>(Marshal.ReadIntPtr(vtbl, 26 * IntPtr.Size));

                setDpi(rt, 96f, 96f);
                var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var props = new D2D1_BITMAP_PROPERTIES
                    {
                        pixelFormat = new D2D1_PIXEL_FORMAT { format = 87, alphaMode = 1 }, // B8G8R8A8_UNORM, Premultiplied
                        dpiX = 96f,
                        dpiY = 96f
                    };
                    IntPtr d2bmp;
                    if (createBmp(rt, new D2D1_SIZE_U { w = (uint)w, h = (uint)h }, bd.Scan0, (uint)bd.Stride, ref props, out d2bmp) == 0)
                    {
                        var zero = new D2D1_COLOR_F();
                        clear(rt, ref zero);
                        var dst = new D2D1_RECT_F { l = off.X, t = off.Y, r = off.X + w, b = off.Y + h };
                        var src = new D2D1_RECT_F { l = 0, t = 0, r = w, b = h };
                        drawBmp(rt, d2bmp, ref dst, 1f, 0, ref src);
                        Marshal.Release(d2bmp);
                        uploaded = true;
                    }
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }
            }
            finally
            {
                Marshal.Release(rt);
                hr = si.EndDraw();
            }
            return uploaded && hr == 0;
        }

        public sealed class CompBackdrop : NativeWindow, IDisposable
        {
            sealed class PillLayer
            {
                public SpriteVisual Visual;
                public CompositionRoundedRectangleGeometry Geo;
                public CompositionGeometricClip Clip;
            }

            DesktopWindowTarget target;
            ContainerVisual root;
            CompositionBackdropBrush hostBackdrop;
            CompositionEffectBrush clipBrush, maskBrush;
            CompositionDrawingSurface maskSurface;
            CompositionSurfaceBrush maskSurfaceBrush;
            SpriteVisual maskVisual;
            readonly List<PillLayer> pills = new List<PillLayer>();

            Bitmap maskBmp;
            Graphics maskG;
            int maskW, maskH;
            string lastMaskKey = "";
            Rectangle lastBounds;
            bool shown, disposed;
            float lastBlur = -1, lastSat = -1, lastOp = -1;
            Color lastTint;

            Rectangle pillRegion;
            int pillDiameter = -1;

            [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
            [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

            void ClipToPill(PillSpec pill, Size size)
            {
                var rect = Rectangle.Intersect(new Rectangle(Point.Empty, size),
                    Rectangle.FromLTRB((int)Math.Floor(pill.X), (int)Math.Floor(pill.Y),
                        (int)Math.Ceiling(pill.X + pill.W), (int)Math.Ceiling(pill.Y + pill.H)));
                int diameter = (int)Math.Round(pill.Radius * 2);
                if (rect == pillRegion && diameter == pillDiameter) return;

                // Composition clips pixels, so give Windows the matching region for mouse input.
                IntPtr region = CreateRoundRectRgn(rect.Left, rect.Top, rect.Right, rect.Bottom, diameter, diameter);
                if (region == IntPtr.Zero) return;
                if (SetWindowRgn(Handle, region, true) == 0)
                {
                    Native.DeleteObject(region);
                    return;
                }
                pillRegion = rect;
                pillDiameter = diameter;
            }

            void EnsureCreated()
            {
                if (Handle != IntPtr.Zero || disposed || !Available) return;
                var cp = new CreateParams
                {
                    Caption = "",
                    Style = Native.WS_POPUP,
                    ExStyle = Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST,
                    X = -32000,
                    Y = -32000,
                    Width = 1,
                    Height = 1
                };
                CreateHandle(cp);

                IntPtr targetPtr;
                int hr = ((ICompositorDesktopInterop)(object)compositor).CreateDesktopWindowTarget(Handle, false, out targetPtr);
                if (hr != 0) throw new COMException("CreateDesktopWindowTarget", hr);
                target = (DesktopWindowTarget)Marshal.GetObjectForIUnknown(targetPtr);
                Marshal.Release(targetPtr);

                root = compositor.CreateContainerVisual();
                target.Root = root;

                hostBackdrop = NewBackdrop();
                clipBrush = clipFactory.CreateBrush();
                clipBrush.SetSourceParameter("backdrop", hostBackdrop);
                clipBrush.SetSourceParameter("noise", noiseBrush);
            }

            public void HideBackdrop()
            {
                if (disposed || Handle == IntPtr.Zero || !shown) return;
                Native.ShowWindow(Handle, Native.SW_HIDE);
                if (root != null) root.IsVisible = false;
                shown = false;
            }

            public void SyncOrder(IntPtr ownerHwnd)
            {
                if (disposed || !shown || Handle == IntPtr.Zero || ownerHwnd == IntPtr.Zero) return;
                if (Native.GetWindow(ownerHwnd, Native.GW_HWNDNEXT) != Handle)
                    Native.SetWindowPos(Handle, ownerHwnd, lastBounds.X, lastBounds.Y, lastBounds.Width, lastBounds.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }

            public void Sync(
                IntPtr ownerHwnd,
                Rectangle bounds,
                bool visible,
                GlassSpec spec,
                PillSpec[] pillList,
                int pillCount,
                GraphicsPath customPath,
                GraphicsPath customExtra,
                float scale,
                string maskKey,
                bool clipToPill = false)
            {
                if (disposed || !Available) return;
                if (!visible || spec.Opacity <= 0.01f || bounds.Width <= 0 || bounds.Height <= 0 || ownerHwnd == IntPtr.Zero)
                {
                    HideBackdrop();
                    return;
                }

                EnsureCreated();
                if (clipToPill) ClipToPill(pillList[0], bounds.Size);
                if (!shown || lastBounds != bounds || Native.GetWindow(ownerHwnd, Native.GW_HWNDNEXT) != Handle)
                {
                    Native.SetWindowPos(Handle, ownerHwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                    lastBounds = bounds;
                    shown = true;
                }

                root.Size = new Vector2(bounds.Width, bounds.Height);
                float op = spec.Opacity < 0f ? 0f : spec.Opacity > 1f ? 1f : spec.Opacity;
                if (Math.Abs(op - lastOp) > 0.002f) { root.Opacity = op; lastOp = op; }
                root.IsVisible = true;

                float blur = Math.Max(2f, Math.Min(48f, spec.Blur));
                float sat = Math.Max(0.5f, Math.Min(2.0f, spec.Saturation));
                bool blurChanged = Math.Abs(blur - lastBlur) > 0.05f;
                bool satChanged = Math.Abs(sat - lastSat) > 0.01f;
                bool tintChanged = spec.Tint.ToArgb() != lastTint.ToArgb();

                if (customPath != null)
                {
                    for (int i = 0; i < pills.Count; i++) pills[i].Visual.IsVisible = false;
                    EnsureMask(bounds.Width, bounds.Height);
                    ApplyBrushProps(maskBrush, blur, sat, spec.Tint, true);
                    maskVisual.Size = root.Size;
                    maskVisual.IsVisible = true;
                    string key = bounds.Width + "x" + bounds.Height + "|" + maskKey;
                    if (key != lastMaskKey && RenderMask(bounds.Width, bounds.Height, customPath, customExtra, scale))
                        lastMaskKey = key;
                }
                else
                {
                    if (maskVisual != null) maskVisual.IsVisible = false;
                    if (blurChanged || satChanged || tintChanged)
                    {
                        ApplyBrushProps(clipBrush, blur, sat, spec.Tint, false);
                        lastBlur = blur; lastSat = sat; lastTint = spec.Tint;
                    }
                    SyncPills(pillList, pillCount);
                }
            }

            static void ApplyBrushProps(CompositionEffectBrush b, float blur, float sat, Color tint, bool force)
            {
                if (b == null) return;
                b.Properties.InsertScalar("Blur.BlurAmount", blur);
                b.Properties.InsertScalar("Sat.Saturation", sat);
                b.Properties.InsertColor("Tint.Color", Windows.UI.Color.FromArgb(tint.A, tint.R, tint.G, tint.B));
            }

            void SyncPills(PillSpec[] list, int count)
            {
                while (pills.Count < count)
                {
                    var v = compositor.CreateSpriteVisual();
                    v.Brush = clipBrush;
                    var geo = compositor.CreateRoundedRectangleGeometry();
                    var clip = compositor.CreateGeometricClip(geo);
                    v.Clip = clip;
                    root.Children.InsertAtTop(v);
                    pills.Add(new PillLayer { Visual = v, Geo = geo, Clip = clip });
                }
                for (int i = 0; i < pills.Count; i++)
                {
                    var pl = pills[i];
                    if (i >= count || list == null || list[i].W <= 0.5f || list[i].H <= 0.5f)
                    {
                        pl.Visual.IsVisible = false;
                        continue;
                    }
                    var s = list[i];
                    float rad = Math.Max(0f, Math.Min(s.Radius, Math.Min(s.W, s.H) * 0.5f));
                    pl.Visual.Size = root.Size;
                    pl.Geo.Offset = new Vector2(s.X, s.Y);
                    pl.Geo.Size = new Vector2(s.W, s.H);
                    pl.Geo.CornerRadius = new Vector2(rad, rad);
                    pl.Visual.IsVisible = true;
                }
            }

            void EnsureMask(int w, int h)
            {
                if (maskBrush == null)
                {
                    maskSurface = gfxDevice.CreateDrawingSurface(
                        new Windows.Foundation.Size(w, h),
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        DirectXAlphaMode.Premultiplied);
                    maskSurfaceBrush = compositor.CreateSurfaceBrush(maskSurface);
                    maskSurfaceBrush.Stretch = CompositionStretch.None;
                    maskBrush = maskFactory.CreateBrush();
                    maskBrush.SetSourceParameter("backdrop", hostBackdrop);
                    maskBrush.SetSourceParameter("noise", noiseBrush);
                    maskBrush.SetSourceParameter("mask", maskSurfaceBrush);
                    maskVisual = compositor.CreateSpriteVisual();
                    maskVisual.Brush = maskBrush;
                    root.Children.InsertAtTop(maskVisual);
                    maskW = w; maskH = h;
                }
                else if (w != maskW || h != maskH)
                {
                    ((ICompositionDrawingSurfaceInterop)(object)maskSurface).Resize(new Size(w, h));
                    maskW = w; maskH = h;
                    lastMaskKey = "";
                }
                if (maskBmp == null || maskBmp.Width != w || maskBmp.Height != h)
                {
                    if (maskG != null) { maskG.Dispose(); maskG = null; }
                    if (maskBmp != null) { maskBmp.Dispose(); maskBmp = null; }
                    maskBmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                    maskG = Graphics.FromImage(maskBmp);
                    maskG.SmoothingMode = SmoothingMode.AntiAlias;
                    maskG.PixelOffsetMode = PixelOffsetMode.HighQuality;
                }
            }

            bool RenderMask(int w, int h, GraphicsPath primary, GraphicsPath extra, float scale)
            {
                maskG.ResetTransform();
                maskG.Clear(Color.Transparent);
                maskG.ScaleTransform(scale, scale);
                maskG.FillPath(Brushes.White, primary);
                if (extra != null) maskG.FillPath(Brushes.White, extra);
                maskG.Flush(FlushIntention.Sync);
                return UploadBitmap(maskBmp, maskSurface, w, h);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (maskG != null) { maskG.Dispose(); maskG = null; }
                if (maskBmp != null) { maskBmp.Dispose(); maskBmp = null; }
                foreach (var pl in pills)
                {
                    if (pl.Clip != null) pl.Clip.Dispose();
                    if (pl.Geo != null) pl.Geo.Dispose();
                    if (pl.Visual != null) pl.Visual.Dispose();
                }
                pills.Clear();
                if (maskVisual != null) { maskVisual.Dispose(); maskVisual = null; }
                if (maskBrush != null) { maskBrush.Dispose(); maskBrush = null; }
                if (maskSurfaceBrush != null) { maskSurfaceBrush.Dispose(); maskSurfaceBrush = null; }
                if (maskSurface != null) { maskSurface.Dispose(); maskSurface = null; }
                if (clipBrush != null) { clipBrush.Dispose(); clipBrush = null; }
                if (hostBackdrop != null) { hostBackdrop.Dispose(); hostBackdrop = null; }
                if (root != null) { root.Dispose(); root = null; }
                if (target != null) { target.Dispose(); target = null; }
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }

        #region WinRT / Direct2D COM Interop

        [StructLayout(LayoutKind.Sequential)]
        struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }

        [DllImport("CoreMessaging.dll")]
        static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint featureLevelsCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

        [DllImport("d2d1.dll")]
        static extern int D2D1CreateDevice(IntPtr dxgiDevice, IntPtr creationProperties, out IntPtr d2dDevice);

        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ICompositorDesktopInterop
        {
            [PreserveSig] int CreateDesktopWindowTarget(IntPtr hwndTarget, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr result);
            [PreserveSig] int EnsureOnThread(int threadId);
        }

        [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ICompositorInterop
        {
            [PreserveSig] int CreateCompositionSurfaceForHandle(IntPtr swapChain, out IntPtr result);
            [PreserveSig] int CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr result);
            [PreserveSig] int CreateGraphicsDevice(IntPtr renderingDevice, out IntPtr result);
        }

        [ComImport, Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ICompositionDrawingSurfaceInterop
        {
            [PreserveSig] int BeginDraw(IntPtr updateRect, ref Guid iid, out IntPtr updateObject, out Point updateOffset);
            [PreserveSig] int EndDraw();
            [PreserveSig] int Resize(Size sizePixels);
        }

        enum GRAPHICS_EFFECT_PROPERTY_MAPPING { Unknown = 0, Direct = 1, ColorToVector4 = 10 }

        [ComImport, Guid("2FC57384-A068-44D7-A331-30982FCF7177"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IGraphicsEffectD2D1Interop
        {
            [PreserveSig] int GetEffectId(out Guid id);
            [PreserveSig] int GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)] string name, out uint index, out GRAPHICS_EFFECT_PROPERTY_MAPPING mapping);
            [PreserveSig] int GetPropertyCount(out uint count);
            [PreserveSig] int GetProperty(uint index, out IntPtr value);
            [PreserveSig] int GetSource(uint index, out IGraphicsEffectSource source);
            [PreserveSig] int GetSourceCount(out uint count);
        }

        [ComImport, Guid("629BDBC8-D932-4FF4-96B9-8D96C5C1E858"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IPropertyValueStatics
        {
            [PreserveSig] int CreateEmpty(out IntPtr pv);
            [PreserveSig] int CreateUInt8(byte v, out IntPtr pv);
            [PreserveSig] int CreateInt16(short v, out IntPtr pv);
            [PreserveSig] int CreateUInt16(ushort v, out IntPtr pv);
            [PreserveSig] int CreateInt32(int v, out IntPtr pv);
            [PreserveSig] int CreateUInt32(uint v, out IntPtr pv);
            [PreserveSig] int CreateInt64(long v, out IntPtr pv);
            [PreserveSig] int CreateUInt64(ulong v, out IntPtr pv);
            [PreserveSig] int CreateSingle(float v, out IntPtr pv);
            [PreserveSig] int CreateDouble(double v, out IntPtr pv);
            [PreserveSig] int CreateChar16(char v, out IntPtr pv);
            [PreserveSig] int CreateBoolean([MarshalAs(UnmanagedType.U1)] bool v, out IntPtr pv);
            [PreserveSig] int CreateString(IntPtr v, out IntPtr pv);
            [PreserveSig] int CreateInspectable(IntPtr v, out IntPtr pv);
            [PreserveSig] int CreateGuid(Guid v, out IntPtr pv);
            [PreserveSig] int CreateDateTime(long v, out IntPtr pv);
            [PreserveSig] int CreateTimeSpan(long v, out IntPtr pv);
            [PreserveSig] int CreatePoint(PointF v, out IntPtr pv);
            [PreserveSig] int CreateSize(SizeF v, out IntPtr pv);
            [PreserveSig] int CreateRect(RectangleF v, out IntPtr pv);
            [PreserveSig] int CreateUInt8Array(uint len, [In] byte[] v, out IntPtr pv);
            [PreserveSig] int CreateInt16Array(uint len, [In] short[] v, out IntPtr pv);
            [PreserveSig] int CreateUInt16Array(uint len, [In] ushort[] v, out IntPtr pv);
            [PreserveSig] int CreateInt32Array(uint len, [In] int[] v, out IntPtr pv);
            [PreserveSig] int CreateUInt32Array(uint len, [In] uint[] v, out IntPtr pv);
            [PreserveSig] int CreateInt64Array(uint len, [In] long[] v, out IntPtr pv);
            [PreserveSig] int CreateUInt64Array(uint len, [In] ulong[] v, out IntPtr pv);
            [PreserveSig] int CreateSingleArray(uint len, [In] float[] v, out IntPtr pv);
        }

        static class NativePropVal
        {
            [DllImport("combase.dll")] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string src, uint len, out IntPtr hstring);
            [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
            [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

            static readonly IPropertyValueStatics Statics = Init();
            static IPropertyValueStatics Init()
            {
                IntPtr hs, fac;
                string name = "Windows.Foundation.PropertyValue";
                WindowsCreateString(name, (uint)name.Length, out hs);
                Guid iid = new Guid("629BDBC8-D932-4FF4-96B9-8D96C5C1E858");
                int hr = RoGetActivationFactory(hs, ref iid, out fac);
                WindowsDeleteString(hs);
                if (hr != 0) throw new COMException("RoGetActivationFactory", hr);
                var obj = (IPropertyValueStatics)Marshal.GetObjectForIUnknown(fac);
                Marshal.Release(fac);
                return obj;
            }

            public static IntPtr Create(object v)
            {
                IntPtr pv; int hr;
                if (v is float) hr = Statics.CreateSingle((float)v, out pv);
                else if (v is uint) hr = Statics.CreateUInt32((uint)v, out pv);
                else if (v is int) hr = Statics.CreateInt32((int)v, out pv);
                else if (v is bool) hr = Statics.CreateBoolean((bool)v, out pv);
                else if (v is float[]) { var a = (float[])v; hr = Statics.CreateSingleArray((uint)a.Length, a, out pv); }
                else throw new NotSupportedException(v.GetType().FullName);
                if (hr != 0) throw new COMException("CreatePropVal", hr);
                return pv;
            }
        }

        sealed class D2DEffect : IGraphicsEffect, IGraphicsEffectD2D1Interop
        {
            public string Name { get; set; }
            public Guid EffectId;
            public readonly List<object> Props = new List<object>();
            public readonly Dictionary<string, Tuple<uint, GRAPHICS_EFFECT_PROPERTY_MAPPING>> Map = new Dictionary<string, Tuple<uint, GRAPHICS_EFFECT_PROPERTY_MAPPING>>();
            public readonly List<IGraphicsEffectSource> Sources = new List<IGraphicsEffectSource>();

            public int GetEffectId(out Guid id) { id = EffectId; return 0; }
            public int GetNamedPropertyMapping(string name, out uint index, out GRAPHICS_EFFECT_PROPERTY_MAPPING mapping)
            {
                Tuple<uint, GRAPHICS_EFFECT_PROPERTY_MAPPING> t;
                if (Map.TryGetValue(name, out t)) { index = t.Item1; mapping = t.Item2; return 0; }
                index = 0; mapping = GRAPHICS_EFFECT_PROPERTY_MAPPING.Unknown;
                return unchecked((int)0x80070057);
            }
            public int GetPropertyCount(out uint count) { count = (uint)Props.Count; return 0; }
            public int GetProperty(uint index, out IntPtr value) { value = NativePropVal.Create(Props[(int)index]); return 0; }
            public int GetSource(uint index, out IGraphicsEffectSource source) { source = Sources[(int)index]; return 0; }
            public int GetSourceCount(out uint count) { count = (uint)Sources.Count; return 0; }
        }

        [StructLayout(LayoutKind.Sequential)] struct D2D1_SIZE_U { public uint w, h; }
        [StructLayout(LayoutKind.Sequential)] struct D2D1_PIXEL_FORMAT { public int format, alphaMode; }
        [StructLayout(LayoutKind.Sequential)] struct D2D1_BITMAP_PROPERTIES { public D2D1_PIXEL_FORMAT pixelFormat; public float dpiX, dpiY; }
        [StructLayout(LayoutKind.Sequential)] struct D2D1_RECT_F { public float l, t, r, b; }
        [StructLayout(LayoutKind.Sequential)] struct D2D1_COLOR_F { public float r, g, b, a; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int D2DCreateBitmapFn(IntPtr rt, D2D1_SIZE_U size, IntPtr src, uint pitch, ref D2D1_BITMAP_PROPERTIES props, out IntPtr bmp);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void D2DDrawBitmapFn(IntPtr rt, IntPtr bmp, ref D2D1_RECT_F dst, float op, int interp, ref D2D1_RECT_F src);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void D2DClearFn(IntPtr rt, ref D2D1_COLOR_F col);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void D2DSetDpiFn(IntPtr rt, float dx, float dy);

        #endregion
    }
}
