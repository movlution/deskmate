using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace Deskmate
{
    class QuotaWindow
    {
        public bool Has, Stale;
        public float Pct;
        public DateTime ResetUtc;
        public int Told, Minutes;
        public string PctText = "", ResetText = "", Kind = "", Label = "";
    }

    class QuotaInfo
    {
        // Agent empty for Claude profiles
        public string Profile = "", Model = "", Tail = "", Agent = "", Who = "";
        // Signed in, no readable window: "Terhubung · Pro"
        public string Note = "";
        public bool FromLive;
        public DateTime AtUtc;
        public readonly QuotaWindow[] W = { new QuotaWindow(), new QuotaWindow(), new QuotaWindow() };
    }

}
