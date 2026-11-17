using System;
using System.Drawing;
using System.IO;

namespace Deskmate
{
    // Edit a picture before sharing: annotate, blur, crop, frame, turn, mirror
    partial class Island
    {
        void OpenRedact(string path)
        {
            Bitmap pic;
            try
            {
                // Own copy: the file stays unlocked
                using (var fs = File.OpenRead(path))
                using (var img = Image.FromStream(fs)) pic = new Bitmap(img);
            }
            catch (Exception ex) { Log.Write("redact open: " + ex.Message); ShowInfo('', Lang.T("Gambar tidak bisa dibuka"), "", true); return; }
            OpenRedactBitmap(pic);
        }

        // The editor takes ownership of this bitmap.
        void OpenRedactBitmap(Bitmap pic)
        {
            DismissToast();
            var w = new RedactWindow(pic);
            // Shot preview already says it
            w.Copied += delegate { if (!ShotsOn) ShowInfo('', Lang.T("Gambar disalin"), "", true); };
            w.Show();
            w.Activate();
        }
    }

    enum ToolMode
    {
        Select,
        Arrow,
        Line,
        Rect,
        Ellipse,
        Pen,
        Highlight,
        Text,
        Step,
        Redact,
        Crop
    }
}
