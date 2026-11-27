using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Deskmate
{
    sealed class GoogleTasksSource : Source
    {
        public volatile bool Enabled = true;
        public override bool GivesTasks { get { return true; } }
        public override bool GivesEvents { get { return false; } }

        public GoogleTasksSource()
        {
            Key = "gtasks";
            Name = "Google Tasks";
            Every = TimeSpan.FromMinutes(10);
        }

        public override object Snapshot()
        {
            return Enabled;
        }

        public override SourceResult Fetch(object snap, DateTime nowUtc)
        {
            if (!(snap is bool && (bool)snap))
                return new SourceResult { Tasks = new List<KuliaTask>() };
            string error;
            List<KuliaTask> tasks = GoogleTasksClient.FetchNotchTasks(out error);
            if (error != null) return SourceResult.Fail(error, error, false);
            return new SourceResult { Tasks = tasks };
        }

        public override string Close(object snap, KuliaTask t)
        {
            string id = t != null && t.Id != null && t.Id.StartsWith("gtasks:", StringComparison.Ordinal) ? t.Id.Substring(7) : (t != null ? t.Id : "");
            string listId = t != null && !string.IsNullOrEmpty(t.ListId) ? t.ListId : "@default";
            return GoogleTasksClient.CompleteTask(listId, id);
        }
    }
}
