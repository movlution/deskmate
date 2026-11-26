using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Desktop pet: a small animal walking on the dock's top edge, or on the bottom
    // of the work area without a dock. Opt-in through pet.on.
    partial class Island
    {
        PetWindow pet;
        PetSheet petSheet;
        // petError: why the chosen custom pet fell back to the built-in one, as a Lang id
        string petSheetKey, petError;

        bool PetOn { get { return settings.GetBool("pet.on", false); } }
        string PetKind { get { return settings.Get("pet.kind", "cat"); } }
        // Logical px tall
        int PetHeight { get { string s = settings.Get("pet.size", "m"); return s == "s" ? 32 : s == "l" ? 64 : 48; } }
        // Percent of the base walking speed
        int PetSpeed { get { return Math.Max(50, Math.Min(200, settings.GetInt("pet.speed", 100))); } }
        // "screen": the whole bottom of the screen, hopping onto a floating dock; "dock": the dock's top edge only
        bool PetScreenArea { get { return settings.Get("pet.area", "dock") == "screen"; } }

        void SetPetChoice(string key, object value)
        {
            settings.Set(key, value);
            settings.Save();
            ApplyPet();
        }

        void ApplyPet()
        {
            if (!PetOn) { ClosePet(); return; }
            var sheet = LoadPetSheet();
            var dock = dockWin != null && !dockWin.IsDisposed ? dockWin : null;
            var screen = dock != null && dock.PetScreen != null ? dock.PetScreen : ChosenScreen();
            IntPtr mon = MonitorForScreen(screen);
            Rectangle work = screen.WorkingArea.IsEmpty ? screen.Bounds : screen.WorkingArea;
            float s = MonitorScale(mon);
            if (pet == null || pet.IsDisposed)
            {
                pet = new PetWindow(work, s, mon, () => dormant || hiddenByUser) { Isle = this };
                pet.Use(sheet, PetHeight, PetSpeed);
                pet.Show();
            }
            else
            {
                pet.Use(sheet, PetHeight, PetSpeed);
                pet.Retarget(work, s, mon);
            }
            // Owned by the dock: always just above it, and the dock's later windows stay above both
            pet.Owner = dock;
            pet.SetArea(PetScreenArea);
            pet.Floor(dock != null ? dock.PetFloor() : Rectangle.Empty, dock != null);
            pet.SetAutoChase(settings.GetBool("pet.autoChase", false));
            ApplyRemotePets(work, s, mon, dock);
            SyncLocalPetProfile();
        }

        // Built-in art, or the chosen folder under pets\. Reloaded only when the choice
        // or the files change; a broken custom pet falls back to the cat and says why.
        PetSheet LoadPetSheet()
        {
            string kind = PetKind, color = settings.Get("pet.color", "orange"), key = kind + "|" + color, dir = null;
            if (kind.StartsWith("custom:", StringComparison.Ordinal))
            {
                string name = kind.Substring(7);
                if (name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                    dir = Path.Combine(PetSheet.Folder, name);
                try
                {
                    if (dir != null)
                        key += "|" + File.GetLastWriteTimeUtc(Path.Combine(dir, "pet.json")).Ticks + "|" + File.GetLastWriteTimeUtc(Path.Combine(dir, "sheet.png")).Ticks;
                }
                catch (Exception) { }
            }
            if (petSheet != null && key == petSheetKey) return petSheet;
            petError = null;
            PetSheet sheet = null;
            if (kind.StartsWith("custom:", StringComparison.Ordinal))
            {
                if (dir == null || !Directory.Exists(dir)) petError = "folder pet tidak ditemukan";
                else
                {
                    try { sheet = PetSheet.Load(dir); }
                    catch (InvalidDataException ex) { petError = ex.Message; }
                    catch (Exception ex) { petError = "sheet.png tidak terbaca"; Log.Write("pet: " + ex.Message); }
                }
                if (petError != null) Log.Write("pet: " + kind + ": " + petError);
            }
            if (sheet == null) sheet = PetSheet.BuiltIn(PetSheet.NormalizeBuiltInKind(kind), color);
            petSheet = sheet;
            petSheetKey = key;
            return sheet;
        }

        void OpenPetFolder()
        {
            try
            {
                Directory.CreateDirectory(PetSheet.Folder);
                Process.Start("explorer.exe", "\"" + PetSheet.Folder + "\"");
            }
            catch (Exception ex) { Log.Write("pet folder: " + ex.Message); }
        }

        // A closing owner would take the pet with it
        void DetachPet()
        {
            if (pet != null && !pet.IsDisposed) pet.Owner = null;
            DetachRemotePets();
        }

        // Every dock frame: the pet rides the bar as it rises and drops
        bool PetUsesDock(DockWindow d) { return pet != null && !pet.IsDisposed && d == dockWin && PetReactOn; }

        void PetFollow(DockWindow d)
        {
            if (pet == null || pet.IsDisposed || d != dockWin) return;
            var fl = d.PetFloor();
            pet.Floor(fl, true);
            RemotePetsFollow(fl);
            d.ShowPetIconMoment();
        }

        void ClosePet()
        {
            ClosePetChatUi();
            CloseRemotePets();
            StopPetRoom();
            if (pet != null && !pet.IsDisposed) pet.Close();
            pet = null;
            // Switching off and on again reloads a custom pet
            petSheet = null;
            petSheetKey = null;
        }

        sealed partial class DockWindow
        {
            internal Screen PetScreen { get { return targetScreen; } }

            DockApp petIcon;
            bool petIconScratch, petIconBaseline, petBadgeBaseline;
            double petIconUntil;
            internal bool PetIconReady { get { return shown && !closing && RevealReady && reveal.Target > 0 && isle != null && isle.PetUsesDock(this); } }

            void QueuePetIcon(DockApp app, bool scratch)
            {
                if (Anim.Reduced || !PetIconReady || (scratch && isle.Quiet)) return;
                if (petIcon != null && !petIconScratch && scratch) return;
                petIcon = app;
                petIconScratch = scratch;
                petIconUntil = clock.Elapsed.TotalSeconds + 0.6;
            }

            internal void ShowPetIconMoment()
            {
                if (petIcon == null) return;
                if (!PetIconReady || petIcon.Leaving || clock.Elapsed.TotalSeconds >= petIconUntil ||
                    (petIconScratch ? petIcon.Badge == 0 : !petIcon.Running))
                {
                    petIcon = null;
                    return;
                }
                if (petIcon.Show.Value <= 0.5f) return;
                Rectangle icon = IconScreenRect(petIcon);
                var scratch = petIconScratch;
                petIcon = null;
                if (!icon.IsEmpty && (!scratch || !isle.Quiet)) isle.pet.DockTrick(icon.Left + icon.Width / 2.0, scratch);
            }

            // Screen px: the flat part of the bar's top edge, held at the screen bottom while the bar
            // is down. Empty while the dock is hidden or not laid out yet.
            internal Rectangle PetFloor()
            {
                if (!shown || closing || g == null || scale <= 0) return Rectangle.Empty;
                var bar = Full ? new RectangleF(0, barTop, cw / scale, BarH) : centerPill;
                float r = Radius;
                if (bar.Width <= r * 2 + 1) return Rectangle.Empty;
                var top = ToScreen(new RectangleF(bar.X + r, bar.Y, bar.Width - r * 2, 1));
                return new Rectangle(top.X, Math.Min(top.Y, mon.Bottom), top.Width, 1);
            }
        }

        // Happy, Surprised and Stretch are short reactions that return to Idle
        enum PetMode { Idle, Walk, Sit, Sleep, Happy, Surprised, Stretch, Held, Scratch }

        // Time-driven behaviour with no window, so tests can step it
        sealed class PetBrain
        {
            public PetMode Mode = PetMode.Idle;
            // Left edge of the sprite and its walkable range, px
            public double X, Left, Right;
            public int Dir = 1;
            // Logical px per second; scaled by Scale
            public double Speed = BaseSpeed;
            public const double BaseSpeed = 36;
            public float Scale = 1;
            // Reduced motion: the pet stays where it is
            public bool NoWalk;
            double modeLeft = 2, animT, targetX, hopT = -1, hopScale = 1;
            public const double HopTime = 0.4, HopHeight = 12, BobStep = 0.25;
            // Ambient cues, refreshed once a second: music playing, a focus session
            public bool Groove, Calm;
            // The action bar is open: stay put and look up, so its buttons don't walk away from the pointer
            public bool Attending;
            bool away;
            double playLeft, autoAfter, autoSampleTime = -1;
            Point autoSample;
            double dockHopFrom, dockHopTo, nextDockTrick;
            bool dockHop;
            bool held, throwing, throwLanded, throwLandHard;
            int throwBounces;
            double heldLift, dropHeight, dropTime, dropFromX, dropToX;
            double throwVx, throwVy, throwLeft, throwRight, throwMaxLift, throwPeak, throwMaxSpeed;
            // Logical px per second squared: a drop from the top of a 1080p screen takes about a second
            public const double Gravity = 1600, MaxThrowVx = 850, MaxThrowVy = 680, ThrowDeadzone = 100;
            readonly Random rnd;

            public PetBrain(int seed) { rnd = new Random(seed); }

            // Seconds into the current mode, for the sheet's frame timing
            public double AnimT { get { return animT; } }

            // Seconds until the mode changes on its own; a walk ends where it arrives
            public double ModeLeft { get { return Mode == PetMode.Walk ? double.MaxValue : Math.Max(0.001, modeLeft); } }

            // The walk cycle follows ground speed
            double PlaySpeed { get { return Math.Max(2 * Speed, 2 * BaseSpeed); } }
            public double Pace { get { return Mode == PetMode.Walk ? (Playing ? PlaySpeed : Speed) / BaseSpeed : 1; } }

            public bool Playing { get { return playLeft > 0; } }

            public bool CanProbeAuto(double now)
            {
                return !Playing && !Away && !NoWalk && !Calm && !Attending && !Reacting && !Manipulating &&
                    Mode != PetMode.Sleep && hopT < 0 && Right - Left >= 24 * Scale && now >= autoAfter;
            }

            public bool ProbeAuto(Point at, double now, double halfWidth, bool nearby)
            {
                if (!nearby || !CanProbeAuto(now))
                {
                    autoSampleTime = -1;
                    return false;
                }
                double dt = now - autoSampleTime;
                int dx = Math.Abs(at.X - autoSample.X), dy = Math.Abs(at.Y - autoSample.Y);
                bool fast = autoSampleTime >= 0 && dt > 0 && dt <= 0.25 &&
                    dx >= Math.Max(24 * Scale, 360 * Scale * dt) && dx >= 2 * dy;
                autoSampleTime = now;
                autoSample = at;
                double gap = Math.Abs(at.X - (X + halfWidth));
                if (!fast || gap < 24 * Scale || gap > 160 * Scale) return false;

                Enter(PetMode.Walk);
                hopT = -1;
                playLeft = 1.5;
                autoAfter = now + playLeft + 20;
                autoSampleTime = -1;
                return true;
            }

            // Play receives cursor samples from the existing frame pacer only during a session.
            public bool StepPlay(double dt, double pointerX, double halfWidth, bool allowed)
            {
                if (!Playing) return false;
                if (!allowed || NoWalk || Calm || away || Right - Left < 24 * Scale || dt >= playLeft)
                {
                    Enter(PetMode.Idle);
                    return false;
                }
                playLeft -= dt;
                double gap = Clamp(pointerX - halfWidth) - X;
                double step = PlaySpeed * Scale * Math.Min(dt, 0.1);
                if (Math.Abs(pointerX - (X + halfWidth)) <= 8 * Scale)
                {
                    Enter(PetMode.Idle);
                    return true;
                }
                if (gap != 0) Dir = Math.Sign(gap);
                if (gap != 0) animT += dt;
                X += Math.Sign(gap) * Math.Min(Math.Abs(gap), step);
                return false;
            }

            public void StopPlay() { if (Playing) Enter(PetMode.Idle); }

            // Asked for from the action bar: chase the pointer for a few seconds
            public bool StartChase(double seconds)
            {
                if (NoWalk || Calm || away || Manipulating || Right - Left < 24 * Scale) return false;
                Enter(PetMode.Walk);
                hopT = -1;
                playLeft = seconds;
                return true;
            }

            // Asked for from the action bar: curl up until the usual sleep ends or a click wakes it
            public bool Nap()
            {
                if (away || Manipulating || Mode == PetMode.Sleep) return false;
                hopT = -1;
                Enter(PetMode.Sleep);
                return true;
            }

            // Walking or mid-hop: frames run on vsync
            public bool Moving { get { return Mode == PetMode.Walk || hopT >= 0 || Manipulating; } }
            public bool Holding { get { return held; } }
            public bool Throwing { get { return throwing; } }
            public bool Manipulating { get { return Mode == PetMode.Held; } }

            // Adjust lift when the ground surface under an airborne throw changes, keeping screen Y continuous
            public void ShiftGround(double deltaY)
            {
                if (!throwing || deltaY == 0) return;
                heldLift = Math.Max(0, heldLift + deltaY);
                throwMaxLift = Math.Max(heldLift, throwMaxLift + deltaY);
            }

            public static bool DragThreshold(Point start, Point at, float scale)
            {
                return Math.Max(Math.Abs(at.X - start.X), Math.Abs(at.Y - start.Y)) >= 8 * scale;
            }

            public bool BeginHold()
            {
                if (NoWalk || Calm || away || Manipulating) return false;
                Enter(PetMode.Held);
                hopT = -1;
                held = true;
                throwing = false;
                throwLanded = false;
                throwBounces = 0;
                heldLift = 0;
                return true;
            }

            public void MoveHeld(double x, double lift)
            {
                MoveHeldFree(x, lift, Left, Right, 96 * Scale);
            }

            // Carried anywhere in left..right and up to maxLift; a release outside the lane
            // drifts back into it while falling
            public void MoveHeldFree(double x, double lift, double left, double right, double maxLift)
            {
                if (!held) return;
                X = Math.Max(left, Math.Min(Math.Max(left, right), x));
                heldLift = Math.Max(0, Math.Min(Math.Max(0, maxLift), lift));
            }

            public void EndHold()
            {
                EndHold(0, 0, Left, Right, 0);
            }

            // Release with optional logical px/s throw velocity; small velocities fall straight down as a gentle drop
            public void EndHold(double vx, double vy, double minX, double maxX, double maxLift)
            {
                if (!held) return;
                if (vx * vx + vy * vy < ThrowDeadzone * ThrowDeadzone)
                {
                    held = false;
                    throwing = false;
                    throwVx = throwVy = 0;
                    dropHeight = heldLift;
                    dropTime = 0;
                    dropFromX = X;
                    dropToX = Clamp(X);
                    if (dropHeight <= 0) { X = dropToX; CancelHold(); }
                    return;
                }
                vx = Math.Max(-MaxThrowVx, Math.Min(MaxThrowVx, vx));
                vy = Math.Max(-MaxThrowVy, Math.Min(MaxThrowVy, vy));
                // A sideways flick along the floor pops the pet into a gentle arc
                if (heldLift <= 0 && vy <= 0) vy = Math.Min(220, Math.Abs(vx) * 0.32);
                held = false;
                throwing = true;
                throwBounces = 0;
                throwVx = vx;
                throwVy = vy;
                throwLeft = Math.Min(Left, Math.Min(minX, X));
                throwRight = Math.Max(Right, Math.Max(maxX, X));
                throwMaxLift = maxLift > 0 ? maxLift : Math.Max(heldLift, 400 * Scale);
                throwPeak = heldLift;
                throwMaxSpeed = Math.Abs(vx);
                if (Math.Abs(vx) > 40) Dir = Math.Sign(vx);
            }

            public bool ConsumeThrowLanding(out bool hard)
            {
                hard = throwLandHard;
                if (!throwLanded) return false;
                throwLanded = false;
                return true;
            }

            public void CancelHold()
            {
                if (!Manipulating) return;
                held = false;
                throwing = false;
                throwLanded = false;
                throwBounces = 0;
                throwVx = throwVy = 0;
                heldLift = 0;
                Enter(PetMode.Idle);
            }

            public bool Reacting { get { return Mode == PetMode.Happy || Mode == PetMode.Surprised || Mode == PetMode.Stretch || Mode == PetMode.Scratch; } }

            public bool TryDockTrick(double iconX, double halfWidth, bool scratch, double seconds, double now)
            {
                // The app badge and the Island toast may describe the same notification.
                bool replaceSurprise = scratch && Mode == PetMode.Surprised;
                if (NoWalk || Calm || Away || Playing || Manipulating || Mode == PetMode.Sleep || now < nextDockTrick ||
                    (!replaceSurprise && (Reacting || hopT >= 0))) return false;
                double gap = iconX - (X + halfWidth);
                if (Math.Abs(gap) > 96 * Scale) return false;
                if (gap != 0) Dir = Math.Sign(gap);
                double target = scratch && Math.Abs(gap) > 12 * Scale ? iconX - Dir * 12 * Scale : iconX;
                React(scratch ? PetMode.Scratch : PetMode.Happy, Math.Max(HopTime, seconds), scratch ? 0.45 : 0.7);
                dockHopFrom = X;
                dockHopTo = Clamp(target - halfWidth);
                dockHop = true;
                nextDockTrick = now + 6;
                return true;
            }

            public bool DockTrickActive { get { return dockHop || Mode == PetMode.Scratch; } }

            public void CancelDockTrick()
            {
                if (!DockTrickActive) return;
                hopT = -1;
                Enter(PetMode.Idle);
            }

            // Asleep because nobody is at the keyboard
            public bool Away { get { return away; } }

            // Bobs to music while standing or sitting; never with reduced motion
            bool Bobbing { get { return Groove && !NoWalk && (Mode == PetMode.Idle || Mode == PetMode.Sit); } }

            // Seconds until the bob flips, or MaxValue when not bobbing
            public double NextBob { get { return Bobbing ? BobStep - animT % BobStep : double.MaxValue; } }

            // A reaction pose for a while, with a hop scaled by hop (0 for none). Nobody to react to while away.
            public bool React(PetMode m, double seconds, double hop)
            {
                if (away || Manipulating) return false;
                Enter(m);
                modeLeft = seconds;
                if (hop > 0 && !NoWalk && !Calm) { hopT = 0; hopScale = hop; }
                return true;
            }

            // Nobody at the keyboard: sleep until they return, then stretch
            public void SetAway(bool on, double stretchSeconds)
            {
                if (on == away) return;
                away = on;
                CancelHold();
                hopT = -1;
                Enter(on ? PetMode.Sleep : PetMode.Stretch);
                modeLeft = on ? 1e9 : stretchSeconds;
            }

            // Screen px above the ground: one parabolic hop
            public double Lift
            {
                get
                {
                    if (Manipulating) return heldLift;
                    if (hopT >= 0)
                    {
                        double u = hopT / HopTime;
                        return 4 * u * (1 - u) * HopHeight * hopScale * Scale;
                    }
                    return Bobbing && (int)(animT / BobStep) % 2 == 1 ? 2 * Scale : 0;
                }
            }

            // Click: a sleeping or sitting pet wakes; reduced motion skips the hop itself
            public void Click(double stretchSeconds)
            {
                StopPlay();
                if (Away) SetAway(false, stretchSeconds);
                else Hop();
            }

            public void Hop()
            {
                if (Mode == PetMode.Sleep || Mode == PetMode.Sit) Enter(PetMode.Idle);
                if (!NoWalk && !Calm) { hopT = 0; hopScale = 1; }
            }

            // Pointer over the pet: a walk stops and it looks that way (dx > 0: pointer to the right).
            // True when the picture changes. A sleeping pet is not woken by hover.
            public bool Notice(int dx)
            {
                // A reaction keeps its pose; petting must not flip it side to side
                if (Mode == PetMode.Sleep || Reacting || Manipulating) return false;
                int face = dx >= 0 ? 1 : -1;
                bool changed = face != Dir;
                Dir = face;
                if (Mode == PetMode.Walk) { Enter(PetMode.Idle); return true; }
                if (modeLeft < 2) modeLeft = 2;
                return changed;
            }

            public void Bounds(double left, double right, bool scatter)
            {
                Left = left;
                Right = Math.Max(left, right);
                X = scatter ? Left + rnd.NextDouble() * (Right - Left) : Clamp(X);
                targetX = Clamp(targetX);
            }

            double Clamp(double v) { return Math.Max(Left, Math.Min(Right, v)); }

            public void Step(double dt)
            {
                if (dt < 0) dt = 0;
                animT += dt;
                if (Manipulating)
                {
                    if (NoWalk || Calm) { CancelHold(); return; }
                    if (held) return;
                    if (throwing)
                    {
                        double step = Math.Min(dt, 0.033);
                        bool grounded = heldLift <= 0 && throwVy <= 0;
                        if (!grounded)
                        {
                            // Midpoint gravity integration + subtle air drag for a smooth, natural arc
                            double nextVy = throwVy - Gravity * step;
                            heldLift += 0.5 * (throwVy + nextVy) * Scale * step;
                            throwVy = nextVy;
                            throwVx *= Math.Exp(-0.75 * step);
                        }
                        else
                        {
                            // Smooth exponential ground skid after vertical bounces settle
                            throwVx *= Math.Exp(-9.0 * step);
                        }
                        X += throwVx * Scale * step;
                        if (heldLift > throwPeak) throwPeak = heldLift;
                        if (X >= Left && X <= Right) { throwLeft = Left; throwRight = Right; }
                        if (X < throwLeft)
                        {
                            X = throwLeft;
                            if (throwVx < 0)
                            {
                                throwVx = -throwVx * 0.5;
                                if (throwVx > 60) Dir = 1;
                            }
                        }
                        else if (X > throwRight)
                        {
                            X = throwRight;
                            if (throwVx > 0)
                            {
                                throwVx = -throwVx * 0.5;
                                if (throwVx < -60) Dir = -1;
                            }
                        }
                        if (heldLift > throwMaxLift)
                        {
                            heldLift = throwMaxLift;
                            if (throwVy > 0) throwVy = -throwVy * 0.3;
                        }
                        if (heldLift <= 0)
                        {
                            heldLift = 0;
                            double impactVy = -throwVy;
                            if (impactVy > 180 && throwBounces < 2)
                            {
                                // Continuous physics bounce: preserves velocity continuity with zero snap
                                throwVy = impactVy * 0.42;
                                throwVx *= 0.68;
                                throwBounces++;
                            }
                            else
                            {
                                throwVy = 0;
                                if (Math.Abs(throwVx) < 18)
                                {
                                    double peak = throwPeak, maxSpd = throwMaxSpeed;
                                    double targetX = Clamp(X);
                                    bool outside = Math.Abs(targetX - X) > 1;
                                    double fromX = X;
                                    CancelHold();
                                    if (outside)
                                    {
                                        // Smoothly hop back into the lane instead of teleporting
                                        hopT = 0;
                                        hopScale = 0.7;
                                        dockHopFrom = fromX;
                                        dockHopTo = targetX;
                                        dockHop = true;
                                    }
                                    throwLanded = true;
                                    throwLandHard = peak > 140 * Scale || maxSpd > 500;
                                }
                            }
                        }
                        return;
                    }
                    dropTime += Math.Min(dt, 0.05);
                    heldLift = dropHeight - Gravity * Scale * dropTime * dropTime;
                    double fall = Math.Sqrt(dropHeight / (Gravity * Scale));
                    X = dropFromX + (dropToX - dropFromX) * Math.Min(1, fall > 0 ? dropTime / fall : 1);
                    if (heldLift <= 0)
                    {
                        X = dropToX;
                        CancelHold();
                        hopT = 0;
                        // A long fall lands with a bigger bounce
                        hopScale = 0.4 + 0.4 * Math.Min(1, Math.Max(0, dropHeight - 96 * Scale) / (400 * Scale));
                    }
                    return;
                }
                if (NoWalk || Calm) { CancelDockTrick(); hopT = -1; }
                if (hopT >= 0)
                {
                    hopT += Math.Min(dt, 0.1);
                    if (dockHop)
                    {
                        double u = Math.Min(1, hopT / HopTime);
                        X = Clamp(dockHopFrom + (dockHopTo - dockHopFrom) * u);
                    }
                    if (hopT >= HopTime) { hopT = -1; dockHop = false; }
                }
                if (Mode == PetMode.Walk)
                {
                    if (NoWalk || Calm) { Enter(PetMode.Sit); return; }
                    if (Attending) { Enter(PetMode.Idle); return; }
                    // A stalled frame slows the step instead of teleporting the pet
                    double v = Speed * Scale * Math.Min(dt, 0.1), gap = targetX - X;
                    if (Math.Abs(gap) <= v) { X = targetX; Enter(rnd.NextDouble() < 0.6 ? PetMode.Idle : PetMode.Sit); }
                    else X += Math.Sign(gap) * v;
                    return;
                }
                modeLeft -= dt;
                if (modeLeft <= 0 && !dockHop) Next();
            }

            void Next()
            {
                double r = rnd.NextDouble();
                // A focus session keeps the pet off its feet
                if (Reacting || (Attending && Mode != PetMode.Sleep)) Enter(PetMode.Idle);
                else if (Mode == PetMode.Idle) { if (r < 0.65 && !Calm) StartWalk(); else Enter(PetMode.Sit); }
                else if (Mode == PetMode.Sit) { if (r < (Calm ? 0.5 : 0.3)) Enter(PetMode.Sleep); else if (r < 0.65 && !Calm) StartWalk(); else Enter(PetMode.Idle); }
                else Enter(PetMode.Sit);
            }

            void StartWalk()
            {
                double min = 60 * Scale, reach = 420 * Scale;
                double lo = Math.Max(Left, X - reach), hi = Math.Min(Right, X + reach);
                if (NoWalk || Calm || hi - lo < min) { Enter(PetMode.Sit); return; }
                double t = X;
                for (int i = 0; i < 8 && Math.Abs(t - X) < min; i++) t = lo + rnd.NextDouble() * (hi - lo);
                if (Math.Abs(t - X) < min) { Enter(PetMode.Idle); return; }
                targetX = t;
                Dir = t > X ? 1 : -1;
                Enter(PetMode.Walk);
            }

            void Enter(PetMode m)
            {
                playLeft = 0;
                if (dockHop) hopT = -1;
                dockHop = false;
                Mode = m;
                animT = 0;
                double r = rnd.NextDouble();
                modeLeft = m == PetMode.Idle ? 2 + r * 3 : m == PetMode.Sit ? 4 + r * 5 : m == PetMode.Sleep ? 20 + r * 25 : m == PetMode.Walk ? 0 : 1.2;
            }
        }

        // Layered window: clear pixels let clicks through, the pet itself takes hover and
        // clicks. The atlas is uploaded once; a frame picks its cell through pptSrc, and a
        // plain move skips the bitmap entirely.
        sealed partial class PetWindow : Form
        {
            const uint SWP_NOZORDER = 0x4;
            // Atlas memory ceiling; a large custom sheet is drawn smaller instead
            const long AtlasBytes = 32L << 20;
            static readonly int FrameMsg = Taskbar.RegisterWindowMessage("DeskmatePetFrame");
            readonly PetBrain brain = new PetBrain(Environment.TickCount);
            readonly Stopwatch clock = Stopwatch.StartNew();
            readonly System.Windows.Forms.Timer rest = new System.Windows.Forms.Timer(), watch = new System.Windows.Forms.Timer { Interval = 1000 };
            DockPacer pacer;
            // floor: dock top edge in screen px, or Empty; lane: walkable left edges (X..Right) on ground Y
            Rectangle work, floor, lane;
            // screenArea: the lane is the whole work area bottom and a floating dock is a step to hop onto.
            // groundY: the ground drawn last, climb included; standY: the same without the climb's arc.
            bool screenArea, groundKnown, groundOnDock;
            int groundY = int.MinValue, standY = int.MinValue;
            double climbT = -1;
            int climbFrom;
            const double ClimbTime = 0.36;
            readonly Func<bool> away;
            PetSheet sheet;
            int height = 48;
            float scale;
            IntPtr monitor, memDC, dib, oldBmp;
            // f: screen px per art px; cw, ch: one atlas cell
            int f, cw, ch;
            double lastT;
            bool hidden, closing, wasMoving, placed, rebuild;
            int waitTicks;
            double scratchLastFrame;
            int drawnX = int.MinValue, drawnY = int.MinValue, drawnCell = -1, drawnRow = -1;
            // Trial counters: frames run, bitmap uploads, moves without a bitmap, worst walking gap
            public int Frames, Uploads, Moves;
            public double MaxGapMs;

            // Remote pet state (when IsRemote == true, driven by network updates instead of local PetBrain)
            public bool IsRemote;
            public string PeerId, PeerName;
            double remoteX, remoteTargetNorm = 0.5, remoteAnimT, remoteHopT = -1;
            int remoteDir = 1;
            PetMode remoteMode = PetMode.Idle;
            bool remotePlaced;
            // Last packet: where the friend's pet was, when it arrived (clock s) and how fast it went (lane widths/s)
            double recvNorm = 0.5, recvT = -1, recvVel;
            // How far past the last packet the pet keeps walking before it waits for the next one
            const double RemoteHorizon = 0.25;
            PetMode lastPubMode = PetMode.Idle;
            int lastPubDir = 1;
            double lastPubNorm = -1, lastPubT = -1;
            bool lastPubHop;
            PetChatBubble chatBubble;

            public double NormX
            {
                get
                {
                    double x = IsRemote ? remoteX : brain.X;
                    return lane.Width > 0 ? Math.Max(0, Math.Min(1, (x - lane.Left) / lane.Width)) : 0.5;
                }
            }
            public int DrawnCenterX { get { return drawnX == int.MinValue ? lane.Left + cw / 2 : drawnX + cw / 2; } }
            int BasePx { get { return sheet == null ? 0 : (int)Math.Round((sheet.Base + 1) * (double)ch / sheet.H); } }
            int TopPx { get { return sheet == null ? 0 : (int)Math.Round(sheet.Top * (double)ch / sheet.H); } }
            public int BubbleAnchorY { get { return sheet == null ? StandY : StandY - (BasePx - TopPx); } }

            // Where the pet's feet are for bubbles and bars: the ground it stands on, or under the paws while carried
            int StandY
            {
                get
                {
                    if (IsRemote || standY == int.MinValue) return lane.Y;
                    if (brain.Manipulating && drawnY != int.MinValue && sheet != null) return drawnY + BasePx;
                    return standY;
                }
            }

            // The lane at the height the pet stands now, for the chase band
            Rectangle StandLane { get { return new Rectangle(lane.X, StandY, lane.Width, 0); } }

            // The flat top of a dock that floats above the work area bottom, wide enough to stand on
            bool DockStep { get { return screenArea && !IsRemote && !floor.IsEmpty && floor.Top < work.Bottom - 2 && floor.Width >= cw; } }

            // Screen Y of the ground under a sprite centred at centerX
            int GroundAt(double centerX, out bool onDock)
            {
                onDock = DockStep && centerX >= floor.Left && centerX <= floor.Right;
                if (onDock) return floor.Top;
                return screenArea && !IsRemote ? work.Bottom : lane.Y;
            }

            // Keep airborne throw screen Y continuous when crossing onto or off a floating dock
            void SyncThrowGround()
            {
                if (!brain.Throwing || !groundKnown || groundY == int.MinValue) return;
                bool onDock;
                int target = GroundAt(brain.X + cw / 2.0, out onDock);
                if (target == groundY) return;
                if (target > groundY || brain.Lift >= groundY - target)
                {
                    brain.ShiftGround(target - groundY);
                    groundY = target;
                    groundOnDock = onDock;
                    standY = target;
                }
            }

            // The ground to draw on. Walking onto or off a floating dock hops between the two heights;
            // a dock that rises or sinks under the pet carries it along without a hop.
            int Ground(double centerX)
            {
                if (brain.Throwing && groundKnown && groundY != int.MinValue)
                {
                    SyncThrowGround();
                    return groundY;
                }
                bool onDock;
                int target = GroundAt(centerX, out onDock);
                standY = target;
                if (!groundKnown || brain.Manipulating || groundY == int.MinValue)
                {
                    groundKnown = true;
                    groundOnDock = onDock;
                    climbT = -1;
                    return groundY = target;
                }
                if (onDock != groundOnDock)
                {
                    groundOnDock = onDock;
                    climbFrom = groundY;
                    climbT = 0;
                }
                if (climbT < 0) return groundY = target;
                double u = Math.Min(1, climbT / ClimbTime);
                double arc = 4 * u * (1 - u) * Math.Max(10 * scale, Math.Abs(target - climbFrom) * 0.45);
                return groundY = (int)Math.Round(climbFrom + (target - climbFrom) * u - arc);
            }

            // Dock only, or the whole screen bottom with the dock as a step
            public void SetArea(bool screen)
            {
                if (screen == screenArea) return;
                screenArea = screen;
                groundKnown = false;
                climbT = -1;
                if (IsHandleCreated && sheet != null && placed) Settle();
            }
            public Rectangle WorkArea { get { return work; } }
            public float UiScale { get { return scale; } }
            public bool CanShowBubble { get { return placed && !hidden && !closing && Visible && !Native.IsFullscreenOn(monitor); } }

            // Where the friend's pet should be now: the last packet carried forward at its speed, a short while at most
            double RemoteBaseNorm
            {
                get
                {
                    double n = remoteTargetNorm;
                    if (recvVel != 0 && recvT >= 0)
                        n += recvVel * Math.Min(RemoteHorizon, Math.Max(0, clock.Elapsed.TotalSeconds - recvT));
                    return n;
                }
            }
            // Plus the shift from a drop here, if the user moved the pet out of the way
            double RemoteTargetX { get { return lane.Left + Math.Max(0, Math.Min(1, RemoteBaseNorm + remoteOffset)) * lane.Width; } }
            bool RemotePredicting { get { return recvVel != 0 && recvT >= 0 && clock.Elapsed.TotalSeconds - recvT < RemoteHorizon; } }
            bool RemoteMoving { get { return Math.Abs(RemoteTargetX - remoteX) > 0.5 || remoteHopT >= 0 || RemotePredicting || RemoteDragging; } }
            double RemoteLift { get { return remoteHopT < 0 ? 0 : Math.Sin(Math.PI * Math.Min(1, remoteHopT / PetBrain.HopTime)) * PetBrain.HopHeight * scale; } }

            public void ApplyRemoteState(double xNorm, int dir, PetMode mode, bool hop, double v)
            {
                if (!IsRemote) return;
                xNorm = Math.Max(0, Math.Min(1, xNorm));
                double now = clock.Elapsed.TotalSeconds;
                if (double.IsNaN(v))
                {
                    // An older Deskmate sends no speed: guess it from the last two packets while walking
                    double gapT = now - recvT;
                    v = mode == PetMode.Walk && recvT >= 0 && gapT >= 0.05 && gapT <= 0.6 ? (xNorm - recvNorm) / gapT : 0;
                }
                if (mode != PetMode.Walk && mode != PetMode.Held) v = 0;
                recvVel = Math.Max(-3, Math.Min(3, v));
                recvNorm = xNorm;
                recvT = now;
                remoteTargetNorm = xNorm;
                remoteDir = dir >= 0 ? 1 : -1;
                if (mode != remoteMode) { remoteMode = mode; remoteAnimT = 0; }
                if (hop && remoteHopT < 0) remoteHopT = 0;
                if (!remotePlaced && placed)
                {
                    remoteX = RemoteTargetX;
                    remotePlaced = true;
                }
                if (placed && !closing && !hidden) { lastT = clock.Elapsed.TotalSeconds; Frame(); }
            }

            public void FacePoint(int screenX)
            {
                int dir = screenX >= DrawnCenterX ? 1 : -1;
                if (IsRemote) remoteDir = dir;
                else brain.Dir = dir;
                if (placed && !closing && !hidden) Frame();
            }

            public void PopEmote(PetEmote kind)
            {
                if (placed && !closing && !hidden) { Emote(kind); Frame(); }
            }

            public void ShowChatBubble(string sender, string text, bool masked, Action onReply)
            {
                ShowChatBubble(sender, text, masked, onReply, 0);
            }

            // seconds > 0: how long it stays (the pet's own answers)
            public void ShowChatBubble(string sender, string text, bool masked, Action onReply, double seconds)
            {
                if (!CanShowBubble || sheet == null) return;
                CloseChatBubble();
                var b = new PetChatBubble(sender, text, masked, scale, work.Width, () => CanShowBubble, onReply);
                if (seconds > 0) b.Seconds = seconds;
                if (!b.MoveAbove(work, new Point(DrawnCenterX, BubbleAnchorY)))
                {
                    b.Dispose();
                    return;
                }
                b.FormClosed += delegate { if (chatBubble == b) chatBubble = null; PlaceNameTag(); };
                b.Show(this);
                chatBubble = b;
                PlaceNameTag();
            }

            public bool ChatOpen { get { return chatBubble != null && !chatBubble.IsDisposed; } }

            public void CloseChatBubble()
            {
                if (chatBubble != null && !chatBubble.IsDisposed) chatBubble.Close();
                chatBubble = null;
            }

            void PlaceChatBubble()
            {
                if (chatBubble != null && !chatBubble.IsDisposed && !chatBubble.MoveAbove(work, new Point(DrawnCenterX, BubbleAnchorY)))
                    CloseChatBubble();
            }

            void StepRemote(double dt)
            {
                if (dt < 0) dt = 0;
                double clamped = Math.Min(dt, 0.1);
                remoteAnimT += clamped;
                if (remoteHopT >= 0)
                {
                    remoteHopT += clamped;
                    if (remoteHopT >= PetBrain.HopTime) remoteHopT = -1;
                }
                UpdateRemotePress();
                StepRemoteDrop(clamped);
                // While carried, the pointer places the pet
                if (remoteHeld) return;
                double target = RemoteTargetX;
                if (!remotePlaced) { remoteX = target; remotePlaced = true; }
                double gap = target - remoteX;
                if (Math.Abs(gap) <= 0.5 || Math.Abs(gap) > lane.Width * 0.3)
                {
                    // Arrived, or a jump too far to walk (thrown, another screen): just be there
                    remoteX = target;
                }
                else
                {
                    // Follow the predicted spot with an easing that keeps a steady pace while it moves,
                    // instead of dashing to each packet and standing still until the next one
                    remoteX += gap * (1 - Math.Exp(-clamped * 12));
                    // Only a real distance turns the pet; small corrections keep the facing the friend sent
                    if (Math.Abs(gap) > 8 * scale) remoteDir = Math.Sign(gap);
                }
            }

            void PublishLocalState()
            {
                if (IsRemote || Isle == null || !Isle.PetRoomConnected) return;
                double n = NormX, now = clock.Elapsed.TotalSeconds;
                bool hop = brain.Lift > 1.0;
                bool changed = brain.Mode != lastPubMode || brain.Dir != lastPubDir || hop != lastPubHop || Math.Abs(n - lastPubNorm) >= 0.002;
                if (!changed) return;
                bool force = brain.Mode != lastPubMode || brain.Dir != lastPubDir || (hop && !lastPubHop);
                // Speed since the last packet, on this clock, so the friend's copy can walk on between packets
                double dtPub = now - lastPubT;
                double v = brain.Moving && lastPubNorm >= 0 && dtPub > 0.02 && dtPub < 0.6 ? (n - lastPubNorm) / dtPub : 0;
                // A throttled packet keeps the last sent values, so the next frame tries again
                if (!Isle.PublishLocalPetState(n, brain.Dir, (int)brain.Mode, hop, force, v)) return;
                lastPubMode = brain.Mode;
                lastPubDir = brain.Dir;
                lastPubNorm = n;
                lastPubT = now;
                lastPubHop = hop;
            }

            public PetWindow(Rectangle work, float scale, IntPtr monitor, Func<bool> away)
            {
                this.work = work;
                this.scale = scale;
                this.monitor = monitor;
                this.away = away;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Text = "Deskmate Pet";
                Bounds = new Rectangle(-32000, -32000, 1, 1);
                rest.Tick += delegate { rest.Stop(); Frame(); };
                watch.Tick += delegate { Watch(); };
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.Style = Native.WS_POPUP;
                    cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            protected override void OnPaint(PaintEventArgs e) { }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                pacer = new DockPacer(Handle, FrameMsg);
                Fit();
                lastT = clock.Elapsed.TotalSeconds;
                watch.Start();
                Frame();
            }

            // Look and pace. A new sheet or size rebuilds the atlas; speed alone does not.
            public void Use(PetSheet sheet, int height, int speedPercent)
            {
                if (sheet != this.sheet || height != this.height) rebuild = true;
                if (sheet != this.sheet) scratchLastFrame = Math.Max(0, sheet.Length(PetMode.Scratch) - 0.001);
                this.sheet = sheet;
                this.height = height;
                brain.Speed = PetBrain.BaseSpeed * speedPercent / 100.0;
                if (!IsHandleCreated || !rebuild) return;
                Fit();
                if (placed) Settle();
            }

            public void Retarget(Rectangle work, float scale, IntPtr monitor)
            {
                this.work = work;
                this.scale = scale;
                this.monitor = monitor;
                Fit();
                if (placed) Settle();
            }

            // Where the pet may stand: on the dock floor, else the work area bottom.
            // A floor narrower than the sprite pins it to the middle.
            public static Rectangle Lane(Rectangle work, Rectangle floor, int cell)
            {
                if (floor.IsEmpty) return new Rectangle(work.Left, work.Bottom, Math.Max(0, work.Width - cell), 0);
                int room = floor.Width - cell;
                if (room < 0) return new Rectangle(floor.Left + room / 2, floor.Top, 0, 0);
                return new Rectangle(floor.Left, floor.Top, room, 0);
            }

            // Whole screen px per art px for a sheet drawn height logical px tall, kept under the atlas ceiling
            public static int Factor(PetSheet sheet, int height, float scale)
            {
                int n = Math.Max(1, (int)Math.Round(height * scale / sheet.H));
                while (n > 1 && (long)sheet.W * sheet.H * n * n * sheet.Count * 8 > AtlasBytes) n--;
                return n;
            }

            // Target cell size in screen px for a sheet drawn height logical px tall, kept under the atlas ceiling
            public static Size CellSize(PetSheet sheet, int height, float scale)
            {
                int th = Math.Max(1, (int)Math.Round(height * scale));
                int tw = Math.Max(1, (int)Math.Round((double)sheet.W * th / sheet.H));
                long bytes = (long)tw * th * sheet.Count * 8;
                if (bytes > AtlasBytes && bytes > 0)
                {
                    double k = Math.Sqrt((double)AtlasBytes / bytes);
                    th = Math.Max(1, (int)Math.Floor(th * k));
                    tw = Math.Max(1, (int)Math.Floor(tw * k));
                }
                return new Size(tw, th);
            }

            // dockPresent with an Empty floor: the dock is hidden or has not drawn yet, so the pet
            // keeps its lane (or waits for the first frame; Watch falls back after 3 s)
            public void Floor(Rectangle f, bool dockPresent)
            {
                // Walking the screen bottom, a hidden dock simply stops being a step
                if (f.IsEmpty && dockPresent && !(screenArea && !IsRemote && placed)) return;
                if (f == floor && placed) return;
                floor = f;
                Settle();
            }

            void Settle()
            {
                if (sheet == null) return;
                lane = screenArea && !IsRemote ? Lane(work, Rectangle.Empty, cw) : Lane(work, floor, cw);
                brain.Bounds(lane.Left, lane.Right, !placed);
                if (IsRemote)
                {
                    remoteX = RemoteTargetX;
                    remotePlaced = true;
                }
                if (placed)
                {
                    Draw();
                    // A dock that slid in under the pet starts a hop, which needs frames
                    if (climbT >= 0 && !hidden) Schedule();
                    return;
                }
                placed = true;
                lastT = clock.Elapsed.TotalSeconds;
                drawnCell = -1;
                Frame();
            }

            void Fit()
            {
                if (sheet == null) return;
                Size cell = CellSize(sheet, height, scale);
                int nf = Math.Max(1, (int)Math.Round((double)cell.Height / sheet.H));
                if (cell.Width != cw || cell.Height != ch || rebuild || memDC == IntPtr.Zero)
                {
                    f = nf;
                    cw = cell.Width;
                    ch = cell.Height;
                    rebuild = false;
                    Build();
                }
                brain.Scale = scale;
                drawnCell = -1;
            }

            void Build()
            {
                Free();
                int w = cw * sheet.Count, h = ch * 2;
                int[] px = sheet.AtlasScaled(cw, ch);
                var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                memDC = Native.CreateCompatibleDC(screen);
                Native.ReleaseDC(IntPtr.Zero, screen);
                IntPtr bits;
                dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) { Native.DeleteDC(memDC); memDC = IntPtr.Zero; Log.Write("pet: atlas " + w + "x" + h + " failed"); return; }
                oldBmp = Native.SelectObject(memDC, dib);
                Marshal.Copy(px, 0, bits, px.Length);
            }

            void Free()
            {
                if (memDC == IntPtr.Zero) return;
                Native.SelectObject(memDC, oldBmp);
                Native.DeleteObject(dib);
                Native.DeleteDC(memDC);
                memDC = dib = oldBmp = IntPtr.Zero;
            }

            void Frame()
            {
                if (closing || !placed) return;
                double now = clock.Elapsed.TotalSeconds, dt = now - lastT;
                lastT = now;
                if (IsRemote)
                {
                    Frames++;
                    StepRemote(dt);
                    Draw();
                    if (emote != null) emote.Step(now);
                    Schedule();
                    return;
                }
                if (wasMoving && brain.Moving && dt * 1000 > MaxGapMs) MaxGapMs = dt * 1000;
                Frames++;
                brain.NoWalk = Anim.Reduced;
                brain.Calm = FocusActive;
                brain.Attending = actionBar != null;
                bool caught = false;
                if (brain.DockTrickActive && (PlayBlocked || !DockTrickVisible(now)))
                {
                    brain.CancelDockTrick();
                    if (emote != null) emote.Stop();
                }
                if (petPressed) UpdatePetPress();
                if (brain.Playing)
                {
                    Point at = Cursor.Position;
                    caught = brain.StepPlay(dt, at.X, cw / 2.0,
                        !PlayBlocked && (manualChase ? ManualChaseAllowed(now) : AutoChaseAllowed(at, now)));
                    if (caught) brain.React(PetMode.Happy, Math.Max(1.4, sheet.Length(PetMode.Happy)), 0);
                    if (!brain.Playing) manualChase = false;
                }
                else
                {
                    if (brain.Throwing) SyncThrowGround();
                    if (!petPressed || brain.Holding) brain.Step(dt);
                    if (brain.Throwing) SyncThrowGround();
                    if (!petPressed) ProbeAutoChase(now);
                }
                bool throwLanded = false, throwHard = false;
                if (brain.ConsumeThrowLanding(out throwHard))
                {
                    throwLanded = true;
                    var mode = throwHard ? PetMode.Surprised : PetMode.Happy;
                    brain.React(mode, Math.Max(throwHard ? 0.9 : 1.2, sheet.Length(mode)), 0);
                }
                wasMoving = brain.Moving;
                if (climbT >= 0)
                {
                    climbT += Math.Min(dt, 0.1);
                    if (climbT >= ClimbTime) climbT = -1;
                }
                Draw();
                PublishLocalState();
                if (caught) Emote(PetEmote.Spark);
                else if (throwLanded) Emote(throwHard ? PetEmote.Bang : PetEmote.Spark);
                if (emote != null) emote.Step(now);
                Schedule();
            }

            // Walking runs on vsync; standing still sleeps until the picture changes
            void Schedule()
            {
                if (pacer == null) return;
                if (hidden || closing) { pacer.Done(false); rest.Stop(); return; }
                if (IsRemote)
                {
                    if (RemoteMoving || (emote != null && emote.Active))
                    {
                        rest.Stop();
                        pacer.Want();
                        pacer.Done(true);
                        return;
                    }
                    pacer.Done(false);
                    double rwait = sheet != null ? sheet.NextChange(remoteMode, remoteAnimT, 1.0) : 0.25;
                    rest.Interval = Math.Max(16, (int)Math.Min(1000, Math.Ceiling(rwait * 1000)));
                    rest.Start();
                    return;
                }
                if (petPressed || brain.Moving || climbT >= 0 || (emote != null && emote.Active))
                {
                    rest.Stop();
                    pacer.Want();
                    pacer.Done(true);
                    return;
                }
                pacer.Done(false);
                double wait = Math.Min(brain.Mode == PetMode.Scratch && brain.AnimT >= scratchLastFrame
                    ? double.MaxValue : sheet.NextChange(brain.Mode, brain.AnimT, brain.Pace), brain.ModeLeft);
                wait = Math.Min(wait, brain.NextBob);
                // Auto chase: about 16 samples a second only while the cursor is near the dock, 4 otherwise
                if (CanProbeAutoChase(clock.Elapsed.TotalSeconds)) wait = Math.Min(wait, AutoChaseNear(Cursor.Position) ? 0.06 : 0.25);
                rest.Interval = Math.Max(16, (int)Math.Min(int.MaxValue, Math.Ceiling(wait * 1000)));
                rest.Start();
            }

            void Draw()
            {
                if (memDC == IntPtr.Zero || hidden || !placed) return;
                int x, c, row, y;
                if (IsRemote)
                {
                    PetMode drawMode = remoteHeld || remoteDragLift > 0 ? PetMode.Held
                        : Math.Abs(RemoteTargetX - remoteX) > 2 * scale ? PetMode.Walk : remoteMode;
                    x = (int)Math.Round(remoteX);
                    c = sheet.Frame(drawMode, remoteAnimT, 1.0);
                    row = remoteDir > 0 ? 0 : 1;
                    y = lane.Y - BasePx - (int)Math.Round(RemoteLift + remoteDragLift);
                }
                else
                {
                    double poseTime = brain.Mode == PetMode.Scratch ? Math.Min(brain.AnimT, scratchLastFrame) : brain.AnimT;
                    x = (int)Math.Round(brain.X);
                    c = sheet.Frame(brain.Mode, poseTime, brain.Pace);
                    row = brain.Dir > 0 ? 0 : 1;
                    // The lowest opaque row of the sheet stands on the ground
                    y = Ground(brain.X + cw / 2.0) - BasePx - (int)Math.Round(brain.Lift);
                }
                bool art = c != drawnCell || row != drawnRow;
                if (!art && x == drawnX && y == drawnY) return;
                if (art)
                {
                    var dst = new Native.POINT(x, y);
                    var size = new Native.SIZE(cw, ch);
                    var src = new Native.POINT(c * cw, row * ch);
                    var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
                    Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
                    Uploads++;
                }
                else
                {
                    Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    Moves++;
                }
                drawnX = x;
                drawnY = y;
                drawnCell = c;
                drawnRow = row;
                PlaceWellness();
                PlaceChatBubble();
                PlaceActions();
                PlaceNameTag();
            }

            // Out of the way of fullscreen games, video and presentations, and gone with a hidden Deskmate
            void Watch()
            {
                if (!placed && ++waitTicks >= 3) { floor = Rectangle.Empty; Settle(); }
                bool hide = (away != null && away()) || Native.IsFullscreenOn(monitor);
                if (!hide && !IsRemote) Cue();
                // Also catches the action bar or a reminder opening and closing over the name
                if (hide == hidden) { PlaceNameTag(); return; }
                hidden = hide;
                if (hide)
                {
                    brain.StopPlay();
                    brain.CancelDockTrick();
                    CancelPetPress();
                    CancelRemotePress();
                    if (emote != null) emote.Stop();
                    CloseWellness();
                    CloseChatBubble();
                    CloseActions();
                    PlaceNameTag();
                    Native.ShowWindow(Handle, Native.SW_HIDE);
                    Schedule();
                    return;
                }
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                lastT = clock.Elapsed.TotalSeconds;
                drawnCell = -1;
                Frame();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == FrameMsg) { Frame(); return; }
                if (IsRemote)
                {
                    // Press and drag moves the friend's pet out of the way; a click or right-click opens the chat
                    if ((m.Msg == 0x201 || m.Msg == 0x203) && placed && sheet != null) { BeginRemotePress(); return; }
                    if (m.Msg == 0x202 && remotePressed) { EndRemotePress(Cursor.Position); Frame(); return; }
                    if ((m.Msg == 0x001F || m.Msg == 0x0215) && remotePressed) { CancelRemotePress(); Frame(); }
                    if (m.Msg == 0x205 && placed && Isle != null && !remotePressed)
                    {
                        Isle.OpenPetChatInput(this);
                        return;
                    }
                    if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                    base.WndProc(ref m);
                    return;
                }
                if (m.Msg == 0x84 && brain != null && brain.DockTrickActive) { m.Result = (IntPtr)(-1); return; }
                if (m.Msg == 0x200 && placed)
                {
                    if (petPressed || brain.Manipulating || brain.Playing) return;
                    bool turned = brain.Notice(Cursor.Position.X - (Left + cw / 2));
                    if (stroke.Feed(Cursor.Position.X, clock.Elapsed.TotalSeconds, Math.Max(2, f))) Petted();
                    else if (turned) Frame();
                    return;
                }
                if ((m.Msg == 0x201 || m.Msg == 0x203) && placed && sheet != null) { BeginPetPress(); return; }
                if (m.Msg == 0x202 && petPressed) { EndPetPress(Cursor.Position); Frame(); return; }
                if ((m.Msg == 0x001F || m.Msg == 0x0215) && petPressed) { CancelPetPress(); Frame(); }
                if (m.Msg == 0x205 && placed && !petPressed && !brain.Manipulating) { ToggleActions(); return; }
                if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                base.WndProc(ref m);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                closing = true;
                CloseWellness();
                CloseChatBubble();
                CloseActions();
                CloseNameTag();
                CancelPetPress();
                rest.Stop();
                watch.Stop();
                if (pacer != null) pacer.Stop();
                if (emote != null && !emote.IsDisposed) { emote.Close(); emote.Dispose(); }
                emote = null;
                Free();
                base.OnFormClosed(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { rest.Dispose(); watch.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
