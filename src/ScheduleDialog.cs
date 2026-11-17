using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class GoogleEventDialog : KitWindow
        {
            readonly Island island;
            readonly bool isEdit;
            readonly string eventId;
            readonly DateTime initialDay;
            readonly string accountEmail;

            readonly Label headerLabel = new Label { AutoSize = false };
            readonly Label emailLabel = new Label { AutoSize = false };
            readonly RoundedField titleField = new RoundedField();
            readonly RoundedField locationField = new RoundedField();

            readonly EventPillButton chipToday = new EventPillButton { Subtle = true };
            readonly EventPillButton chipTomorrow = new EventPillButton { Subtle = true };
            readonly EventPillButton chipWeek = new EventPillButton { Subtle = true };

            readonly EventPillButton prevDayBtn = new EventPillButton { Text = "‹", Radius = 9f };
            readonly EventPillButton dateBtn = new EventPillButton { Radius = 9f };
            readonly EventPillButton nextDayBtn = new EventPillButton { Text = "›", Radius = 9f };

            readonly KitCheck allDay = new KitCheck();
            readonly EventPillButton dur30 = new EventPillButton { Subtle = true };
            readonly EventPillButton dur60 = new EventPillButton { Subtle = true };
            readonly EventPillButton dur120 = new EventPillButton { Subtle = true };

            readonly EventPillButton startBtn = new EventPillButton { Radius = 9f };
            readonly Label timeSep = new Label { AutoSize = false, Text = "→", TextAlign = ContentAlignment.MiddleCenter };
            readonly EventPillButton endBtn = new EventPillButton { Radius = 9f };

            readonly Label warning = new Label { AutoSize = false };
            readonly EventPillButton deleteBtn = new EventPillButton { Negative = true };
            readonly KitButton cancel = new KitButton();
            readonly KitButton save = new KitButton { Primary = true };

            DateTime chosen;
            int startMinutes;
            int endMinutes;
            Font fHead, fBody, fField, fSmall, fChip;
            bool busy;

            public bool IsEdit { get { return isEdit; } }
            public string EventId { get { return eventId; } }
            public DateTime InitialDay { get { return initialDay; } }

            public GoogleEventDialog(Island island, DateTime initialDay)
                : this(island, initialDay, null)
            {
            }

            public GoogleEventDialog(Island island, ClassSlot existing)
                : this(island, existing != null && existing.Start != DateTime.MinValue ? existing.Start.Date : DateTime.Today, existing)
            {
            }

            GoogleEventDialog(Island island, DateTime day, ClassSlot existing)
            {
                this.island = island;
                isEdit = existing != null && !string.IsNullOrEmpty(existing.Id);
                eventId = isEdit ? existing.Id : "";
                initialDay = day.Date;
                chosen = initialDay;
                accountEmail = island != null ? (island.GoogleCalendarEmail ?? "").Trim() : "";

                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ShowInTaskbar = false;
                TopMost = true;

                if (isEdit)
                {
                    titleField.Box.Text = existing.Title ?? "";
                    locationField.Box.Text = existing.Room ?? "";
                    allDay.Checked = existing.AllDay;
                    if (existing.AllDay)
                    {
                        startMinutes = 9 * 60;
                        endMinutes = 10 * 60;
                    }
                    else
                    {
                        startMinutes = existing.Start.Hour * 60 + existing.Start.Minute;
                        int rawEnd = (int)Math.Round((existing.End - existing.Start.Date).TotalMinutes);
                        if (rawEnd <= startMinutes) rawEnd = Math.Min(1439, startMinutes + 60);
                        if (rawEnd > 1439) rawEnd = 1439;
                        endMinutes = rawEnd;
                    }
                }
                else
                {
                    if (chosen == DateTime.Today)
                    {
                        var now = DateTime.Now;
                        int nowMins = now.Hour * 60 + now.Minute;
                        int nextSlot = ((nowMins / 30) + 1) * 30;
                        if (nextSlot >= 23 * 60 + 30)
                        {
                            startMinutes = 23 * 60 + 30;
                            endMinutes = 23 * 60 + 59;
                        }
                        else
                        {
                            startMinutes = nextSlot;
                            endMinutes = Math.Min(23 * 60 + 59, startMinutes + 60);
                        }
                    }
                    else
                    {
                        startMinutes = 9 * 60;
                        endMinutes = 10 * 60;
                    }
                }

                chipToday.Click += delegate { SetDate(DateTime.Today); };
                chipTomorrow.Click += delegate { SetDate(DateTime.Today.AddDays(1)); };
                chipWeek.Click += delegate { SetDate(DateTime.Today.AddDays(7)); };
                prevDayBtn.Click += delegate { SetDate(chosen.AddDays(-1)); };
                nextDayBtn.Click += delegate { SetDate(chosen.AddDays(1)); };
                dateBtn.Click += delegate { ShowCalendarPopup(); };

                allDay.CheckedChanged += delegate { UpdateTimeState(); };
                dur30.Click += delegate { ApplyDuration(30); };
                dur60.Click += delegate { ApplyDuration(60); };
                dur120.Click += delegate { ApplyDuration(120); };
                startBtn.Click += delegate { ShowTimePopup(true); };
                endBtn.Click += delegate { ShowTimePopup(false); };

                titleField.Box.TextChanged += delegate
                {
                    if (titleField.Invalid) titleField.Invalid = false;
                    if (warning.Text.Length > 0) warning.Text = "";
                };
                titleField.Box.KeyDown += HandleSingleLineEnter;
                locationField.Box.KeyDown += HandleSingleLineEnter;

                deleteBtn.Click += delegate { DeleteCurrent(); };
                save.Click += delegate { Submit(); };
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

                Controls.AddRange(new Control[]
                {
                    headerLabel, emailLabel,
                    titleField, locationField,
                    chipToday, chipTomorrow, chipWeek,
                    prevDayBtn, dateBtn, nextDayBtn,
                    allDay, dur30, dur60, dur120,
                    startBtn, timeSep, endBtn,
                    warning, deleteBtn, cancel, save
                });

                Shown += delegate
                {
                    titleField.Box.Focus();
                    if (isEdit) titleField.Box.SelectAll();
                };

                ApplyTexts();
                UpdateDateControls();
                UpdateTimeState();
            }

            void HandleSingleLineEnter(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    Submit();
                }
            }

            void ApplyTexts()
            {
                Text = isEdit ? Lang.T("Edit acara") : Lang.T("Acara baru");
                headerLabel.Text = Text;
                emailLabel.Text = accountEmail;
                emailLabel.Visible = accountEmail.Length > 0;
                titleField.Box.AccessibleName = Lang.T("Judul acara");
                locationField.Box.AccessibleName = Lang.T("Lokasi (opsional)");
                chipToday.Text = Lang.T("Hari ini");
                chipTomorrow.Text = Lang.T("Besok");
                chipWeek.Text = Lang.T("+1 Minggu");
                allDay.Text = Lang.T("Sepanjang hari");
                dur30.Text = Lang.T("30 mnt");
                dur60.Text = Lang.T("1 jam");
                dur120.Text = Lang.T("2 jam");
                deleteBtn.Text = Lang.T("Hapus");
                deleteBtn.Visible = isEdit;
                cancel.Text = Lang.T("Batal");
                if (!busy) save.Text = Lang.T("Simpan");
            }

            void SetDate(DateTime day)
            {
                chosen = day.Date;
                if (warning.Text.Length > 0) warning.Text = "";
                UpdateDateControls();
            }

            void UpdateDateControls()
            {
                var today = DateTime.Today;
                chipToday.Active = chosen == today;
                chipTomorrow.Active = chosen == today.AddDays(1);
                chipWeek.Active = chosen == today.AddDays(7);
                dateBtn.Text = chosen.ToString("ddd, d MMM yyyy", Fmt.Id) + "  ▾";
                chipToday.Invalidate();
                chipTomorrow.Invalidate();
                chipWeek.Invalidate();
            }

            void ApplyDuration(int minutes)
            {
                if (allDay.Checked) return;
                endMinutes = Math.Min(1439, startMinutes + minutes);
                if (endMinutes <= startMinutes) endMinutes = Math.Min(1439, startMinutes + 15);
                if (warning.Text.Length > 0) warning.Text = "";
                UpdateTimeState();
            }

            void SetStartMinutes(int newStart)
            {
                int dur = endMinutes - startMinutes;
                if (dur <= 0) dur = 60;
                startMinutes = Math.Max(0, Math.Min(1438, newStart));
                endMinutes = Math.Min(1439, startMinutes + dur);
                if (endMinutes <= startMinutes) endMinutes = Math.Min(1439, startMinutes + 15);
                if (warning.Text.Length > 0) warning.Text = "";
                UpdateTimeState();
            }

            void SetEndMinutes(int newEnd)
            {
                endMinutes = Math.Max(0, Math.Min(1439, newEnd));
                if (warning.Text.Length > 0) warning.Text = "";
                UpdateTimeState();
            }

            static string FormatHm(int mins)
            {
                return (mins / 60).ToString("00") + ":" + (mins % 60).ToString("00");
            }

            void UpdateTimeState()
            {
                bool timed = !allDay.Checked;
                startBtn.Enabled = timed;
                endBtn.Enabled = timed;
                dur30.Enabled = timed;
                dur60.Enabled = timed;
                dur120.Enabled = timed;
                timeSep.ForeColor = timed ? KitInk.Sub : KitInk.Faint;

                startBtn.Text = FormatHm(startMinutes) + "  ▾";
                endBtn.Text = FormatHm(endMinutes) + "  ▾";
                int dur = endMinutes - startMinutes;
                dur30.Active = timed && dur == 30;
                dur60.Active = timed && dur == 60;
                dur120.Active = timed && dur == 120;
                dur30.Invalidate();
                dur60.Invalidate();
                dur120.Invalidate();
            }

            void ShowCalendarPopup()
            {
                var drop = new ToolStripDropDown
                {
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    BackColor = KitInk.Window,
                    AutoClose = true,
                    DropShadowEnabled = true
                };
                var cal = new CalendarPopupControl(chosen, Ui, delegate(DateTime picked)
                {
                    SetDate(picked);
                    drop.Close();
                });
                var host = new ToolStripControlHost(cal)
                {
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    AutoSize = false,
                    Size = cal.Size
                };
                drop.Items.Add(host);
                drop.HandleCreated += delegate { Native.RoundCorners(drop.Handle); };
                drop.Closed += delegate
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new Action(delegate { cal.Dispose(); drop.Dispose(); }));
                    else
                    {
                        cal.Dispose();
                        drop.Dispose();
                    }
                };
                drop.Show(dateBtn, new Point(0, dateBtn.Height + (int)Math.Round(4 * Ui)));
            }

            void ShowTimePopup(bool isStart)
            {
                if (allDay.Checked) return;
                var drop = new ToolStripDropDown
                {
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    BackColor = KitInk.Window,
                    AutoClose = true,
                    DropShadowEnabled = true
                };
                int current = isStart ? startMinutes : endMinutes;
                int after = isStart ? -1 : startMinutes;
                string hdr = isStart ? Lang.T("Mulai") : Lang.T("Waktu selesai");
                var picker = new TimePopupControl(current, after, hdr, Ui, delegate(int mins)
                {
                    if (isStart) SetStartMinutes(mins);
                    else SetEndMinutes(mins);
                    drop.Close();
                });
                var host = new ToolStripControlHost(picker)
                {
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    AutoSize = false,
                    Size = picker.Size
                };
                drop.Items.Add(host);
                drop.HandleCreated += delegate { Native.RoundCorners(drop.Handle); };
                drop.Closed += delegate
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new Action(delegate { picker.Dispose(); drop.Dispose(); }));
                    else
                    {
                        picker.Dispose();
                        drop.Dispose();
                    }
                };
                var anchor = isStart ? startBtn : endBtn;
                drop.Show(anchor, new Point(0, anchor.Height + (int)Math.Round(4 * Ui)));
            }

            void SetBusy(bool on, string saveLabel)
            {
                busy = on;
                save.Enabled = !on;
                deleteBtn.Enabled = !on;
                cancel.Enabled = !on;
                save.Text = saveLabel;
            }

            void Submit()
            {
                if (busy) return;
                string evTitle = titleField.Box.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (evTitle.Length == 0)
                {
                    titleField.Invalid = true;
                    warning.Text = Lang.T("Judul wajib diisi");
                    titleField.Box.Focus();
                    return;
                }
                string evLoc = locationField.Box.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                bool isAllDay = allDay.Checked;
                DateTime startLocal;
                DateTime endLocal;
                if (isAllDay)
                {
                    startLocal = chosen.Date;
                    endLocal = chosen.Date.AddDays(1);
                }
                else
                {
                    startLocal = chosen.Date + TimeSpan.FromMinutes(startMinutes);
                    endLocal = chosen.Date + TimeSpan.FromMinutes(endMinutes);
                    if (endLocal <= startLocal)
                    {
                        warning.Text = Lang.T("Waktu selesai harus setelah mulai");
                        return;
                    }
                }

                warning.Text = "";
                SetBusy(true, Lang.T("Menyimpan…"));
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string err;
                    bool ok = isEdit
                        ? island.UpdateGoogleCalendarEvent(eventId, evTitle, startLocal, endLocal, evLoc, isAllDay, out err)
                        : island.CreateGoogleCalendarEvent(evTitle, startLocal, endLocal, evLoc, isAllDay, out err);
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            if (IsDisposed) return;
                            if (ok)
                            {
                                DialogResult = DialogResult.OK;
                                Close();
                                return;
                            }
                            SetBusy(false, Lang.T("Simpan"));
                            warning.Text = err ?? Lang.T("Gagal");
                        }));
                    }
                    catch (InvalidOperationException) { }
                });
            }

            void DeleteCurrent()
            {
                if (!isEdit || busy) return;
                string evTitle = titleField.Box.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                warning.Text = "";
                SetBusy(true, Lang.T("Menghapus…"));
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string err;
                    bool ok = island.DeleteGoogleCalendarEvent(eventId, out err);
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            if (IsDisposed) return;
                            if (ok)
                            {
                                DialogResult = DialogResult.OK;
                                Close();
                                return;
                            }
                            SetBusy(false, Lang.T("Simpan"));
                            warning.Text = err ?? Lang.T("Gagal");
                        }));
                    }
                    catch (InvalidOperationException) { }
                });
            }

            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Relayout(); }

            void DisposeFonts()
            {
                if (fHead != null) { fHead.Dispose(); fHead = null; }
                if (fBody != null) { fBody.Dispose(); fBody = null; }
                if (fField != null) { fField.Dispose(); fField = null; }
                if (fSmall != null) { fSmall.Dispose(); fSmall = null; }
                if (fChip != null) { fChip.Dispose(); fChip = null; }
            }

            static int MeasureChip(string text, Font f, int padX)
            {
                return TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + padX;
            }

            public override void Relayout()
            {
                if (!IsHandleCreated) return;
                DisposeFonts();
                float s = Ui > 0 ? Ui : 1f;
                fHead = new Font(Ink.MenuFamily, 18f * s, FontStyle.Bold, GraphicsUnit.Pixel);
                fBody = UiFont(13f);
                fField = UiFont(14f);
                fSmall = UiFont(12f);
                fChip = UiFont(11.5f);

                Font = fBody;
                BackColor = KitInk.Window;
                ForeColor = KitInk.Text;
                ApplyTexts();

                int w = (int)Math.Round(460 * s);
                int m = (int)Math.Round(22 * s);
                int cw = w - 2 * m;
                int gap = (int)Math.Round(6 * s);
                int y = (int)Math.Round(18 * s);

                headerLabel.Font = fHead;
                headerLabel.BackColor = KitInk.Window;
                headerLabel.ForeColor = KitInk.Text;
                headerLabel.SetBounds(m, y, cw, (int)Math.Round(26 * s));
                y += (int)Math.Round(26 * s);

                if (emailLabel.Visible)
                {
                    emailLabel.Font = fSmall;
                    emailLabel.BackColor = KitInk.Window;
                    emailLabel.ForeColor = KitInk.Sub;
                    emailLabel.SetBounds(m, y, cw, (int)Math.Round(18 * s));
                    y += (int)Math.Round(26 * s);
                }
                else
                {
                    y += (int)Math.Round(10 * s);
                }

                titleField.SetBounds(m, y, cw, (int)Math.Round(40 * s));
                titleField.Place(s, fField, Lang.T("Judul acara…"));
                y += (int)Math.Round(50 * s);

                locationField.SetBounds(m, y, cw, (int)Math.Round(38 * s));
                locationField.Place(s, fBody, Lang.T("Lokasi atau tautan rapat…"));
                y += (int)Math.Round(54 * s);

                // Quick date chips
                int chipH = (int)Math.Round(26 * s);
                int chipPad = (int)Math.Round(20 * s);
                chipToday.Font = chipTomorrow.Font = chipWeek.Font = fChip;
                int wToday = MeasureChip(chipToday.Text, fChip, chipPad);
                int wTom = MeasureChip(chipTomorrow.Text, fChip, chipPad);
                int wWeek = MeasureChip(chipWeek.Text, fChip, chipPad);
                chipToday.SetBounds(m, y, wToday, chipH);
                chipTomorrow.SetBounds(m + wToday + gap, y, wTom, chipH);
                chipWeek.SetBounds(m + wToday + wTom + 2 * gap, y, wWeek, chipH);
                y += chipH + (int)Math.Round(8 * s);

                // Date stepper row
                int rowH = (int)Math.Round(34 * s);
                int stepW = (int)Math.Round(34 * s);
                prevDayBtn.Font = nextDayBtn.Font = fField;
                dateBtn.Font = fBody;
                prevDayBtn.SetBounds(m, y, stepW, rowH);
                dateBtn.SetBounds(m + stepW + gap, y, cw - 2 * stepW - 2 * gap, rowH);
                nextDayBtn.SetBounds(w - m - stepW, y, stepW, rowH);
                y += rowH + (int)Math.Round(16 * s);

                // All-day toggle + Duration chips
                allDay.Font = fSmall;
                int allW = Math.Min((int)Math.Round(165 * s), MeasureChip(allDay.Text, fSmall, (int)Math.Round(42 * s)));
                allDay.SetBounds(m, y, allW, chipH);
                dur30.Font = dur60.Font = dur120.Font = fChip;
                int w30 = MeasureChip(dur30.Text, fChip, chipPad);
                int w60 = MeasureChip(dur60.Text, fChip, chipPad);
                int w120 = MeasureChip(dur120.Text, fChip, chipPad);
                int dx = w - m - w120;
                dur120.SetBounds(dx, y, w120, chipH);
                dx -= w60 + gap;
                dur60.SetBounds(dx, y, w60, chipH);
                dx -= w30 + gap;
                dur30.SetBounds(dx, y, w30, chipH);
                y += chipH + (int)Math.Round(8 * s);

                // Start -> End time row
                int sepW = (int)Math.Round(24 * s);
                int timeW = (cw - sepW) / 2;
                startBtn.Font = endBtn.Font = fBody;
                timeSep.Font = fBody;
                timeSep.BackColor = KitInk.Window;
                timeSep.ForeColor = allDay.Checked ? KitInk.Faint : KitInk.Sub;
                startBtn.SetBounds(m, y, timeW, rowH);
                timeSep.SetBounds(m + timeW, y, sepW, rowH);
                endBtn.SetBounds(w - m - timeW, y, timeW, rowH);
                y += rowH + (int)Math.Round(10 * s);

                // Inline warning
                warning.Font = fSmall;
                warning.BackColor = KitInk.Window;
                warning.ForeColor = Ink.Base.Negative;
                warning.SetBounds(m, y, cw, (int)Math.Round(34 * s));
                y += (int)Math.Round(40 * s);

                // Footer actions
                int footH = (int)Math.Round(36 * s);
                int saveW = (int)Math.Round(116 * s);
                int cancelW = (int)Math.Round(96 * s);
                int delW = (int)Math.Round(92 * s);
                deleteBtn.Font = cancel.Font = save.Font = fBody;
                deleteBtn.SetBounds(m, y, delW, footH);
                save.SetBounds(w - m - saveW, y, saveW, footH);
                cancel.SetBounds(save.Left - (int)Math.Round(10 * s) - cancelW, y, cancelW, footH);
                y += footH + (int)Math.Round(20 * s);

                ClientSize = new Size(w, y);
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keys)
            {
                if (keys == (Keys.Control | Keys.Enter)) { save.PerformClick(); return true; }
                if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
                return base.ProcessCmdKey(ref msg, keys);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) DisposeFonts();
            }
        }
    }
}
