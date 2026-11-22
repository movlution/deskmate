using System;
using System.Collections.Generic;

namespace Deskmate
{
    partial class Island
    {
        partial class PrefsWindow
        {
            // Dock page, Pet part: on and off, animal, fur, size, speed, and the folder for own pets
            void PetRows(Action<string> cap, Action<KitRow> add)
            {
                bool on = settings.GetBool("pet.on", false);
                var petRow = Row(Lang.T("Pet di dock"), Lang.T("Hewan kecil yang berjalan di atas dock, atau di tepi bawah layar tanpa dock"),
                    Switch("pet.on", false, v => { island.SetPetChoice("pet.on", v); Relayout(); }));
                petRow.Glyph = (char)0xEB51;
                add(petRow);

                string kind = settings.Get("pet.kind", "cat");
                var ids = new List<string> { "cat", "dog" };
                var names = new List<string> { Lang.T("Kucing"), Lang.T("Anjing") };
                foreach (var n in PetSheet.Custom()) { ids.Add("custom:" + n); names.Add(n); }
                int at = ids.IndexOf(kind);
                // A chosen folder that has gone still shows, with the reason below
                if (at < 0 && kind.StartsWith("custom:", StringComparison.Ordinal)) { ids.Add(kind); names.Add(kind.Substring(7)); at = ids.Count - 1; }
                var kindPop = Popup(names.ToArray(), Math.Max(0, at), i => { island.SetPetChoice("pet.kind", ids[i]); Relayout(); });
                kindPop.Enabled = on;
                string err = on && island.petError != null ? Lang.T("Gagal dimuat, memakai kucing") + ": " + Lang.T(island.petError) : null;
                var kindRow = Row(Lang.T("Hewan"), err, kindPop);
                kindRow.Glyph = (char)0xE8EC;
                add(kindRow);

                bool custom = kind.StartsWith("custom:", StringComparison.Ordinal);
                string[] colorNames = { Lang.T("Oranye"), Lang.T("Cokelat"), Lang.T("Abu-abu"), Lang.T("Hitam"), Lang.T("Krem") };
                var colorPop = Popup(colorNames, Math.Max(0, Array.IndexOf(PetSheet.Colors, settings.Get("pet.color", "orange"))),
                    i => island.SetPetChoice("pet.color", PetSheet.Colors[i]));
                colorPop.Enabled = on && !custom;
                var colorRow = Row(Lang.T("Warna bulu"), custom ? Lang.T("Pet buatan sendiri memakai warna dari gambarnya") : null, colorPop);
                colorRow.Glyph = (char)0xE790;
                add(colorRow);

                string size = settings.Get("pet.size", "m");
                var sizeSeg = Seg(new[] { Lang.T("Kecil"), Lang.T("Sedang"), Lang.T("Besar") }, size == "s" ? 0 : size == "l" ? 2 : 1,
                    i => island.SetPetChoice("pet.size", i == 0 ? "s" : i == 2 ? "l" : "m"));
                sizeSeg.Enabled = on;
                var sizeRow = Row(Lang.T("Ukuran"), null, sizeSeg);
                sizeRow.Glyph = (char)0xE740;
                add(sizeRow);

                var speed = Slider(50, 200, 10, island.PetSpeed, v => v + "%", v => island.SetPetChoice("pet.speed", v));
                speed.Enabled = on;
                var speedRow = Row(Lang.T("Kecepatan jalan"), null, speed);
                speedRow.Glyph = (char)0xEC4A;
                add(speedRow);

                cap(Lang.T("Pet buatan sendiri"));
                var folderRow = Row(Lang.T("Folder pet"), Lang.T("Per pet: sheet.png dan pet.json dari Aseprite; tag walk, idle, sit, sleep"),
                    Button(Lang.T("Buka folder"), false, delegate { island.OpenPetFolder(); }));
                folderRow.Glyph = (char)0xE8B7;
                add(folderRow);
            }
        }
    }
}
