using System;
using System.Collections.Generic;
using System.Globalization;

namespace Deskmate
{
    // UI text: Indonesian source, English on request
    static partial class Lang
    {
        public static bool English;
        static readonly Dictionary<string, string> en = Build();

        static Dictionary<string, string> Build()
        {
            var d = new Dictionary<string, string>();
            AddCore(d);
            AddIntegrations(d);
            Add20(d);
            AddCardsAndPlacement(d);
            AddDockAndSettings(d);
            AddPictureAndReadability(d);
            AddCombo(d);
            AddWhatsNew(d);
            AddPet(d);
            AddIngat(d);
            AddGuard(d);
            AddWork(d);
            AddQuota(d);
            AddSetup(d);
            return d;
        }

        public static string T(string id)
        {
            string s;
            return English && en.TryGetValue(id, out s) ? s : id;
        }

        public static string F(string id, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, T(id), args);
        }
    }
}
