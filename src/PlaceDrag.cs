using System;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Drag the compact island along its edge; the spot is saved on release
    partial class Island
    {
        const int ShiftMax = 100;
        bool moveArmed, moving;
        Point moveFrom;
        float moveStart;

        bool DragMoveOn { get { return settings.GetBool("place.drag", true); } }

        // Press on the bare compact island: arm a move
        void ArmMove(PointF pt, Hit h)
        {
            moveArmed = false;
            if (!DragMoveOn || expanded || PrompterOn || MirrorOn || !InShape(pt)) return;
            if (h != null && h.Drag != null) return;
            moveArmed = true;
            moveFrom = Control.MousePosition;
            int at = placeMode;
            moveStart = at == PlaceTop ? shiftPct : at == PlaceLeft || at == PlaceRight ? placeY : placeInset;
        }

        // True while the move owns the pointer
        bool StepMove()
        {
            if (!moveArmed) return false;
            var now = Control.MousePosition;
            int dx = now.X - moveFrom.X, dy = now.Y - moveFrom.Y;
            if (!moving)
            {
                float slop = 6 * scale;
                if (dx * dx + dy * dy < slop * slop) return false;
                moving = true;
                hoverTimer.Stop();
                leaveTimer.Stop();
                pressedId = null;
                pressedCompactBounds = RectangleF.Empty;
                shellDown = false;
                Capture = true;
            }
            var mon = monitorPx;
            switch (placeMode)
            {
                case PlaceTop:
                    float free = Math.Max(1, mon.Width / 2f - (BaseHalf + Pad) * scale);
                    shiftPct = (int)Math.Round(Clamp(moveStart + dx / free * 100, -ShiftMax, ShiftMax));
                    break;
                case PlaceTopLeft:
                    placeInset = Clamp(moveStart + dx / scale, 0, 320);
                    break;
                case PlaceTopRight:
                    placeInset = Clamp(moveStart - dx / scale, 0, 320);
                    break;
                default:
                    placeY = Clamp(moveStart + dy / (float)Math.Max(1, mon.Height), 0, 1);
                    break;
            }
            if (GlassOn) NeedFrost(sh.Target);
            Kick();
            return true;
        }

        // Release: keep the new spot
        bool EndMove()
        {
            bool was = moving;
            moveArmed = moving = false;
            if (!was) return false;
            switch (placeMode)
            {
                case PlaceTop: settings.Set("position", shiftPct); break;
                case PlaceTopLeft:
                case PlaceTopRight: settings.Set("place.inset", (int)Math.Round(placeInset)); break;
                default: settings.Set("place.y", (int)Math.Round(placeY * 100)); break;
            }
            settings.Save();
            ResetFrost();
            ApplyReserve();
            Kick();
            return true;
        }
    }
}
