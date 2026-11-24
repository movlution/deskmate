// Passive regression check. Compile with: build.cmd <temporary-output.exe> tests\task_note_images_check.cs
// Load the resulting assembly in a fresh STA PowerShell process and call Deskmate.TaskNoteImagesCheck.Run().
// Set DESKMATE_DIR to an isolated directory beneath TEMP\Sentinel before loading the assembly.
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Deskmate
{
    public static class TaskNoteImagesCheck
    {
        static int assertions;
        static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label);
            assertions++;
        }
        static FetchResult Reply(string text, int status = 200)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            return new FetchResult { Data = data, Length = data.Length, Status = status };
        }
        public static string Run()
        {
            string root = Path.GetFullPath(Settings.Dir);
            string temporary = Path.Combine(Path.GetTempPath(), "Sentinel") + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)) throw new Exception("Isolated data required");
            var pixels = new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 };
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
            string token = TaskNoteImages.Import(bitmap);
            Check(token.Length < 100, "Compact reference");
            Check(TaskNoteImages.Import(bitmap) == token, "Content deduplication");
            string hash = TaskNoteImages.References.Match(token).Groups[1].Value;
            string path = Path.Combine(root, "task-images", hash + ".png");
            byte[] png = File.ReadAllBytes(path);
            var doc = new FlowDocument();
            TaskNoteMarkdown.Load(doc, "before " + token + " after");
            Check(TaskNoteMarkdown.Save(doc) == "before " + token + " after", "Inline round trip");
            var mixed = new FlowDocument();
            string mixedMarkdown = "# Title\n- [ ] Todo\n| Name | Image |\n| --- | --- |\n| Example | " + token + " |\n\nAfter";
            TaskNoteMarkdown.Load(mixed, mixedMarkdown);
            var restored = new FlowDocument();
            TaskNoteMarkdown.Load(restored, TaskNoteMarkdown.Save(mixed));
            Check(TaskNoteMarkdown.Save(restored) == TaskNoteMarkdown.Save(mixed), "Tables and checklist remain stable with images");
            var imageInline = ((Paragraph)doc.Blocks.FirstBlock).Inlines.FirstInline.NextInline as InlineUIContainer;
            Check(imageInline != null && imageInline.Child is System.Windows.Controls.Image, "Image renders");
            using (var editor = new TaskNoteEditor())
            {
                editor.LoadMarkdown("before after");
                var field = typeof(TaskNoteEditor).GetField("editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var wpf = (System.Windows.Controls.RichTextBox)field.GetValue(editor);
                wpf.ApplyTemplate();
                wpf.Measure(new Size(600, 400));
                wpf.Arrange(new Rect(0, 0, 600, 400));
                wpf.UpdateLayout();
                wpf.CaretPosition = wpf.Document.Blocks.FirstBlock.ContentStart.GetPositionAtOffset(7);
                editor.InsertImage(bitmap);
                string saved = editor.SaveMarkdown("");
                Check(saved.Contains(token) && saved.Contains("before"), "Caret insertion preserves text");
                Check(wpf.CanUndo, "Image insertion undo available");
                wpf.Undo();
                Check(!editor.SaveMarkdown("").Contains(token), "Image insertion undo");
                wpf.Redo();
                Check(editor.SaveMarkdown("").Contains(token), "Image insertion redo");
            }
            bool rejected = false;
            try { TaskNoteImages.Decode(new byte[TaskNoteImages.MaxBytes + 1]); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Oversize rejected");
            var vault = new GoogleCalVault();
            int uploads = 0, downloads = 0, deletes = 0;
            bool exists = false, fail = false;
            GoogleWorkspaceHttp.Transport = delegate(string method, string url, string body)
            {
                if (method == "DELETE") deletes++;
                Check(method == "GET", "Metadata lookup only for existing folder");
                string query = Uri.UnescapeDataString(url);
                return Reply(query.Contains("folder-v1") ? "{\"files\":[{\"id\":\"folder\"}]}"
                    : exists ? "{\"files\":[{\"id\":\"image\"}]}" : "{\"files\":[]}");
            };
            TaskNoteImages.Transport = delegate(string method, string url, byte[] body)
            {
                if (method == "POST")
                {
                    uploads++;
                    Check(url.Contains("uploadType=multipart"), "Multipart upload");
                    Check(Encoding.UTF8.GetString(body).Contains("image/png"), "PNG metadata");
                    if (fail) return Reply("failure", 503);
                    exists = true;
                    return Reply("{\"id\":\"image\"}");
                }
                downloads++;
                return new FetchResult { Status = 200, Data = png, Length = png.Length };
            };
            try
            {
                string error;
                Check(TaskNoteImages.Sync(ref vault, token, true, out error), "Private upload succeeds");
                Check(uploads == 1, "One image uploaded");
                Check(TaskNoteImages.Sync(ref vault, token + " " + token, true, out error), "Retry and duplicate reference");
                Check(uploads == 1, "No duplicate upload on retry");
                File.Delete(path);
                TaskNoteMarkdown.Load(doc, token);
                var missing = ((Paragraph)doc.Blocks.FirstBlock).Inlines.FirstInline as InlineUIContainer;
                Check(missing.Child is System.Windows.Controls.TextBlock, "Offline placeholder");
                Check(TaskNoteMarkdown.Save(doc) == token, "Missing image reference preserved");
                Check(TaskNoteImages.Sync(ref vault, token, false, out error), "Download on second device");
                Check(downloads == 1 && File.Exists(path), "Cache downloaded");
                Check(TaskNoteImages.Sync(ref vault, token, false, out error) && downloads == 1, "Offline cache reused");
                File.WriteAllText(path, "corrupt");
                Check(TaskNoteImages.Sync(ref vault, token, false, out error) && downloads == 2, "Corrupt cache recovered");
                exists = false; fail = true;
                Check(!TaskNoteImages.Sync(ref vault, token, true, out error) && error != null, "Upload failure reported");
                Check(File.Exists(path), "Failure preserves local image");
                fail = false;
                Check(TaskNoteImages.Sync(ref vault, token, true, out error), "Failed upload retried");
                Check(deletes == 0, "Never delete another device's images");
                // Exercise the real task pipeline: a failed image must leave a new task pending.
                var metadataTransport = GoogleWorkspaceHttp.Transport;
                int taskPosts = 0;
                GoogleWorkspaceHttp.Transport = delegate(string method, string url, string body)
                {
                    if (url.Contains("/drive/")) return metadataTransport(method, url, body);
                    if (url.Contains("/tasks?")) return Reply("{\"items\":[]}");
                    if (method == "GET" && url.Contains("/@default")) return Reply("{\"id\":\"list\"}");
                    if (method == "GET") return Reply("{\"items\":[{\"id\":\"list\",\"title\":\"Tasks\"}]}");
                    if (method == "POST" && url.Contains("/tasks"))
                    {
                        taskPosts++;
                        Check(exists, "Image uploaded before task reference");
                        Check(body.Contains(hash) && !body.Contains(Convert.ToBase64String(png)), "Task carries reference only");
                        return Reply("{\"id\":\"task\"}");
                    }
                    return Reply("{}");
                };
                var task = new TodoItem { Title = "Image sync", Notes = token, Dirty = true };
                var tasks = new List<TodoItem> { task };
                var lists = new List<TaskListInfo>();
                bool changed;
                exists = false; fail = true;
                GoogleTasksClient.SyncWith(vault, tasks, new List<string[]>(), lists, new List<string>(), out changed, out error);
                Check(taskPosts == 0 && task.Dirty && error != null, "Failed upload blocks task and retains retry");
                fail = false;
                GoogleTasksClient.SyncWith(vault, tasks, new List<string[]>(), lists, new List<string>(), out changed, out error);
                Check(taskPosts == 1 && !task.Dirty && task.GoogleTaskId == "task", "Retry publishes task after image");
                GoogleWorkspaceHttp.Transport = metadataTransport;
                File.Delete(path);
                png = new byte[] { 1, 2, 3 };
                Check(!TaskNoteImages.Sync(ref vault, token, false, out error), "Download hash mismatch rejected");
                Check(!File.Exists(path), "Corrupt download not cached");
                Check(TaskNoteImages.Sync(ref vault, "![](deskmate-image:../../bad)", false, out error),
                    "Malformed reference ignored");
            }
            finally { GoogleWorkspaceHttp.Transport = null; TaskNoteImages.Transport = null; }
            return assertions + " assertions passed; no network or visible windows";
        }
    }
}
