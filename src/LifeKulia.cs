using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Windows.UI.Notifications.Management;

namespace Deskmate
{
    partial class Island
    {
        // Kulia

        void RefreshTasks(bool manual)
        {
            if (manual) refreshAsked = 1;
            if (kuliaLoading || kulia == null || !kulia.SignedIn) return;
            kuliaLoading = true;
            Kick();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err, schedErr = null;
                List<KuliaTask> list;
                List<ClassSlot> sched = null;
                try { list = kulia.Feed(out err); }
                catch (Exception ex) { Log.Write("kulia feed: " + ex.Message); list = null; err = "Kulia sedang bermasalah · dicoba lagi otomatis"; }
                try { if (list != null) sched = kulia.Schedule(out schedErr); }
                catch (Exception ex) { Log.Write("kulia schedule: " + ex.Message); schedErr = "Jadwal gagal dimuat"; }
                Post(() =>
                {
                    kuliaLoading = false;
                    // Signed out meanwhile: drop the result
                    if (!kulia.SignedIn) { MergeSources(); Kick(); return; }
                    if (list != null) { kuliaTasks = list; tasksError = null; tasksAtUtc = DateTime.UtcNow; }
                    if (sched != null) { kuliaClasses = sched; classesError = null; }
                    else if (list != null) classesError = schedErr;
                    else tasksError = err;
                    nextTasksUtc = DateTime.UtcNow.AddMinutes(list != null ? 10 : 2);
                    MergeSources();
                    Kick();
                });
            });
        }

        void OpenLogin()
        {
            if (kulia == null || !kulia.Configured)
            {
                ShowInfo(Ink.IcoLock, Lang.T("Kulia belum diatur"), Lang.T("kulia.url / kulia.key kosong"), true);
                return;
            }
            expanded = false;
            Kick();
            using (var f = new KuliaLogin(kulia))
            {
                if (f.ShowDialog() != DialogResult.OK) return;
            }
            kuliaTasks = null;
            kuliaClasses = null;
            tasksError = null;
            MergeSources();
            RefreshTasks(false);
            ShowInfo('\0', Lang.T("Kulia tersambung"), kulia.Email, true);
        }

        void SignOutKulia()
        {
            var k = kulia;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { k.SignOut(); } catch (Exception ex) { Log.Write("kulia signout: " + ex.Message); }
                Post(Kick);
            });
            kuliaTasks = null;
            kuliaClasses = null;
            tasksError = null;
            classesError = null;
            MergeSources();
            Kick();
        }

        void MarkDone(KuliaTask task)
        {
            if (task.ReadOnly) return;
            if (task.Source != "kulia") { CloseExternal(task); return; }
            string old = task.Status;
            task.Status = "selesai";
            if (tasks != null) tasks.Remove(task);
            Kick();
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok;
                try { ok = kulia.SetStatus(task, "selesai"); }
                catch (Exception ex) { Log.Write("kulia status: " + ex.Message); ok = false; }
                Post(() =>
                {
                    if (ok) TaskFinished(task);
                    else
                    {
                        task.Status = old;
                        ShowInfo(Ink.IcoLock, Lang.T("Gagal tersimpan di Kulia"), task.Title, true);
                        RefreshTasks(false);
                    }
                });
            });
        }

        KuliaTask SoonTask()
        {
            if (tasks == null) return null;
            var now = DateTime.UtcNow;
            // Date-only tasks have no countdown
            foreach (var t in tasks)
                if (!t.DateOnly && !t.NoDue && t.DeadlineUtc > now && (t.DeadlineUtc - now).TotalHours <= 6) return t;
            return null;
        }

        void CheckDeadlines(DateTime now)
        {
            if (tasks == null) return;
            foreach (var t in tasks)
            {
                if (t.DateOnly || t.NoDue || t.DeadlineUtc == DateTime.MinValue) continue;
                double mins = (t.DeadlineUtc - now).TotalMinutes;
                if (mins <= 0 || mins > 60 || warnedTasks.Contains(t.Id)) continue;
                warnedTasks.Add(t.Id);
                var ts = ShowToast(View.Deadline, 8, 3, false);
                if (ts != null) ts.Data = t;
                break;
            }
        }

        // Web links only; a path would run
        internal static void OpenUrl(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
            try { Process.Start(u.AbsoluteUri); } catch (Exception ex) { Log.Write("open url: " + ex.Message); }
        }
    }
}
