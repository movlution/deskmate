using System;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Moving a friend's pet out of the way. The drag stays on this screen: the friend's pet keeps
    // walking as they see it, only shifted by where it was dropped here.
    partial class Island
    {
        sealed partial class PetWindow
        {
            bool remotePressed, remoteHeld;
            Point remotePressAt;
            int remotePressButton;
            double remotePressX, remotePressLift;
            // Height above the ground while carried or falling back, px, and the fall speed, px/s
            double remoteDragLift, remoteDropV;
            // Lane widths added to the friend's position after a drop here
            double remoteOffset;

            bool RemoteDragging { get { return remotePressed || remoteDragLift > 0; } }

            void BeginRemotePress()
            {
                if (hidden || closing || remotePressed || sheet == null) return;
                remotePressed = true;
                remoteHeld = false;
                remotePressAt = Cursor.Position;
                remotePressButton = SystemInformation.MouseButtonsSwapped ? 0x02 : 0x01;
                remotePressX = remoteX;
                remotePressLift = remoteDragLift;
                remoteDropV = 0;
                lastT = clock.Elapsed.TotalSeconds;
                Frame();
            }

            // A background window gets no mouse capture, so the frame pacer watches the button while pressed
            void UpdateRemotePress()
            {
                if (!remotePressed) return;
                Point at = Cursor.Position;
                if (KeyState(remotePressButton) >= 0) { EndRemotePress(at); return; }
                if (!remoteHeld && PetBrain.DragThreshold(remotePressAt, at, scale)) remoteHeld = true;
                if (remoteHeld) RemoteCarryTo(at);
            }

            void RemoteCarryTo(Point at)
            {
                remoteX = Math.Max(lane.Left, Math.Min(lane.Right, remotePressX + at.X - remotePressAt.X));
                double maxLift = Math.Max(0, lane.Y - work.Top - (sheet.Base + 1 - sheet.Top) * f);
                remoteDragLift = Math.Max(0, Math.Min(maxLift, remotePressLift + 8 * scale - (at.Y - remotePressAt.Y)));
                remotePlaced = true;
            }

            void EndRemotePress(Point at)
            {
                if (!remotePressed) return;
                remotePressed = false;
                if (remoteHeld)
                {
                    RemoteCarryTo(at);
                    remoteHeld = false;
                    // Keep the pet where it was dropped: from now on it walks this far from the friend's spot
                    double dropped = lane.Width > 0 ? (remoteX - lane.Left) / lane.Width : 0.5;
                    remoteOffset = dropped - RemoteBaseNorm;
                    remoteDropV = 0;
                }
                else if (placed && Isle != null && !PetBrain.DragThreshold(remotePressAt, at, scale))
                {
                    // A click still opens the chat
                    Isle.OpenPetChatInput(this);
                }
                lastT = clock.Elapsed.TotalSeconds;
            }

            void CancelRemotePress()
            {
                if (!remotePressed) return;
                remotePressed = false;
                if (remoteHeld)
                {
                    remoteHeld = false;
                    double dropped = lane.Width > 0 ? (remoteX - lane.Left) / lane.Width : 0.5;
                    remoteOffset = dropped - RemoteBaseNorm;
                }
            }

            // After a drop the pet falls back to the ground
            void StepRemoteDrop(double dt)
            {
                if (remotePressed || remoteDragLift <= 0) return;
                remoteDropV += 2400 * scale * dt;
                remoteDragLift -= remoteDropV * dt;
                if (remoteDragLift <= 0)
                {
                    remoteDragLift = 0;
                    remoteDropV = 0;
                }
            }
        }
    }
}
