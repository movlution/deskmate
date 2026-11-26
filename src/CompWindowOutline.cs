using System;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Deskmate
{
    static partial class CompGlass
    {
        // A transparent compositor surface with strokes only. The app's HWND
        // remains the sole source of window pixels throughout the transition.
        internal sealed class WindowOutline : NativeWindow, IDisposable
        {
            const int OpenMs = 500, CloseMs = 430;

            [DllImport("dwmapi.dll")]
            static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

            DesktopWindowTarget target;
            ContainerVisual root;
            readonly ShapeVisual[] layers = new ShapeVisual[2];
            readonly CompositionRoundedRectangleGeometry[] outlines = new CompositionRoundedRectangleGeometry[2];
            readonly Timer lifetime = new Timer();
            bool disposed;

            public WindowOutline()
            {
                lifetime.Tick += delegate { Hide(); };
            }

            void CreateSurface()
            {
                const int WsDisabled = 0x08000000, TransitionsForcedDisabled = 3;
                CreateHandle(new CreateParams
                {
                    Caption = "Deskmate Window Outline",
                    Style = Native.WS_POPUP | WsDisabled,
                    ExStyle = Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_TOOLWINDOW |
                        Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST,
                    Width = 1,
                    Height = 1
                });
                int disabled = 1;
                DwmSetWindowAttribute(Handle, TransitionsForcedDisabled, ref disabled, 4);
                target = CreateWindowTarget(Handle);
                root = compositor.CreateContainerVisual();
                target.Root = root;
                for (int i = 0; i < layers.Length; i++)
                {
                    var geometry = outlines[i] = compositor.CreateRoundedRectangleGeometry();
                    var shape = compositor.CreateSpriteShape(geometry);
                    shape.StrokeBrush = compositor.CreateColorBrush(i == 0
                        ? Windows.UI.Color.FromArgb(255, 151, 112, 182)
                        : Windows.UI.Color.FromArgb(255, 229, 211, 249));
                    var visual = layers[i] = compositor.CreateShapeVisual();
                    visual.Opacity = 0;
                    visual.Shapes.Add(shape);
                    root.Children.InsertAtTop(visual);
                }
            }

            public void Play(Rectangle frame, Rectangle monitor, Rectangle icon, float dpi, bool opening, bool maximized)
            {
                if (disposed || !Available) return;
                if (Handle == IntPtr.Zero) CreateSurface();

                int duration = opening ? OpenMs : CloseMs;
                int margin = (int)Math.Ceiling(42 * dpi);
                var viewport = Rectangle.Intersect(Rectangle.Inflate(frame, margin, margin), monitor);
                var origin = new Vector3(frame.Left - viewport.Left, frame.Top - viewport.Top, 0);
                var center = new Vector3(frame.Width / 2f, frame.Height / 2f, 0);
                float direction = icon.IsEmpty ? 0 : Math.Sign(icon.Left + icon.Width / 2 - frame.Left - frame.Width / 2);
                root.Size = new Vector2(viewport.Width, viewport.Height);

                using (var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.22f, .76f), new Vector2(.29f, 1)))
                {
                    for (int i = 0; i < layers.Length; i++)
                    {
                        var visual = layers[i];
                        var geometry = outlines[i];
                        float inset = (i == 0 ? 3.5f : 2f) * dpi;
                        visual.Size = root.Size;
                        visual.CenterPoint = center;
                        geometry.CornerRadius = new Vector2((maximized ? 3 : 11) * dpi);
                        geometry.Offset = new Vector2(inset);
                        geometry.Size = new Vector2(frame.Width - inset * 2, frame.Height - inset * 2);
                        ((CompositionSpriteShape)visual.Shapes[0]).StrokeThickness = (i == 0 ? 3.2f : 2.4f) * dpi;

                        float peak = i == 0 ? .72f : .94f;
                        float from = i == 0 ? .88f : .94f, to = i == 0 ? .76f : .83f;
                        float turn = (direction < 0 ? -1 : 1) * (i == 0 ? 2.2f : -.9f);
                        float settle = i == 0 ? .73f : .61f;
                        var drift = new Vector3(direction * (i == 0 ? 18 : 7) * dpi, (i == 0 ? 26 : 11) * dpi, 0);
                        var rest = new Vector3(1, 1, 1);
                        using (var opacity = compositor.CreateScalarKeyFrameAnimation())
                        using (var rotation = compositor.CreateScalarKeyFrameAnimation())
                        using (var offset = compositor.CreateVector3KeyFrameAnimation())
                        using (var zoom = compositor.CreateVector3KeyFrameAnimation())
                        {
                            opacity.Duration = rotation.Duration = offset.Duration = zoom.Duration = TimeSpan.FromMilliseconds(duration);
                            opacity.InsertKeyFrame(0, 0);
                            opacity.InsertKeyFrame(i == 0 ? .23f : .14f, peak, ease);
                            opacity.InsertKeyFrame(opening ? .60f : .45f, peak * (i == 0 ? .52f : .83f));
                            opacity.InsertKeyFrame(1, 0, ease);
                            offset.InsertKeyFrame(0, opening ? origin + drift * .65f : origin);
                            zoom.InsertKeyFrame(0, opening ? new Vector3(from, from, 1) : rest);
                            rotation.InsertKeyFrame(0, opening ? turn : 0);
                            if (opening)
                            {
                                offset.InsertKeyFrame(settle, origin, ease);
                                zoom.InsertKeyFrame(settle, rest, ease);
                                rotation.InsertKeyFrame(settle, 0, ease);
                            }
                            offset.InsertKeyFrame(1, opening ? origin : origin + drift, ease);
                            zoom.InsertKeyFrame(1, opening ? rest : new Vector3(to, to, 1), ease);
                            rotation.InsertKeyFrame(1, opening ? 0 : turn * 1.35f, ease);
                            visual.StartAnimation("Opacity", opacity);
                            visual.StartAnimation("Offset", offset);
                            visual.StartAnimation("Scale", zoom);
                            visual.StartAnimation("RotationAngleInDegrees", rotation);
                        }
                    }
                }

                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, viewport.X, viewport.Y, viewport.Width, viewport.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                lifetime.Stop();
                lifetime.Interval = duration + 32;
                lifetime.Start();
            }

            public void Hide()
            {
                lifetime.Stop();
                if (Handle != IntPtr.Zero) Native.ShowWindow(Handle, Native.SW_HIDE);
            }

            public void Dispose()
            {
                if (disposed) return;
                Hide();
                disposed = true;
                lifetime.Dispose();
                if (target != null) target.Root = null;
                if (root != null) root.Dispose();
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i] != null) layers[i].Dispose();
                    if (outlines[i] != null) outlines[i].Dispose();
                }
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }
    }
}
