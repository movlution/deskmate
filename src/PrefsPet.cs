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
                Action refresh = null;
                Action<string, object> change = (key, value) =>
                {
                    island.SetPetChoice(key, value);
                    refresh();
                    searchIndex.Remove("pet");
                };
                var petOn = Switch("pet.on", false, v => change("pet.on", v));
                var petRow = Row(Lang.T("Pet di dock"), Lang.T("Hewan kecil yang berjalan di atas dock, atau di tepi bawah layar tanpa dock"), petOn);
                petRow.Glyph = (char)0xEB51;
                add(petRow);

                string kind = settings.Get("pet.kind", "cat");
                var ids = new List<string> { "cat", "dog" };
                var names = new List<string> { Lang.T("Kucing"), Lang.T("Anjing") };
                foreach (var n in PetSheet.Custom()) { ids.Add("custom:" + n); names.Add(n); }
                int at = ids.IndexOf(kind);
                // A chosen folder that has gone still shows, with the reason below
                if (at < 0 && kind.StartsWith("custom:", StringComparison.Ordinal)) { ids.Add(kind); names.Add(kind.Substring(7)); at = ids.Count - 1; }
                var kindPop = Popup(names.ToArray(), Math.Max(0, at), i => change("pet.kind", ids[i]));
                var kindRow = Row(Lang.T("Hewan"), null, kindPop);
                // Reserve room for status text so changing pets never shifts the rows.
                kindRow.Height = (int)(54 * Ui);
                kindRow.Glyph = (char)0xE8EC;
                add(kindRow);

                string[] colorNames = { Lang.T("Oranye"), Lang.T("Cokelat"), Lang.T("Abu-abu"), Lang.T("Hitam"), Lang.T("Krem") };
                var colorPop = Popup(colorNames, Math.Max(0, Array.IndexOf(PetSheet.Colors, settings.Get("pet.color", "orange"))),
                    i => change("pet.color", PetSheet.Colors[i]));
                var colorRow = Row(Lang.T("Warna bulu"), null, colorPop);
                colorRow.Height = kindRow.Height;
                colorRow.Glyph = (char)0xE790;
                add(colorRow);

                string size = settings.Get("pet.size", "m");
                var sizeSeg = Seg(new[] { Lang.T("Kecil"), Lang.T("Sedang"), Lang.T("Besar") }, size == "s" ? 0 : size == "l" ? 2 : 1,
                    i => change("pet.size", i == 0 ? "s" : i == 2 ? "l" : "m"));
                var sizeRow = Row(Lang.T("Ukuran"), null, sizeSeg);
                sizeRow.Glyph = (char)0xE740;
                add(sizeRow);

                var speed = Slider(50, 200, 10, island.PetSpeed, v => v + "%", v => change("pet.speed", v));
                var speedRow = Row(Lang.T("Kecepatan jalan"), null, speed);
                speedRow.Glyph = (char)0xEC4A;
                add(speedRow);

                var react = Switch("pet.react", true, v => change("pet.react", v));
                var reactRow = Row(Lang.T("Reaksi"), Lang.T("Kaget, senang, bergoyang saat musik, dan tidur saat Anda pergi"), react);
                reactRow.Glyph = (char)0xE76E;
                add(reactRow);

                refresh = () =>
                {
                    bool on = petOn.Checked;
                    bool custom = ids[kindPop.Selected].StartsWith("custom:", StringComparison.Ordinal);
                    kindPop.Enabled = sizeSeg.Enabled = speed.Enabled = react.Enabled = on;
                    colorPop.Enabled = on && !custom;
                    SetPetDetail(kindRow, on && island.petError != null
                        ? Lang.T("Gagal dimuat, memakai kucing") + ": " + Lang.T(island.petError) : "");
                    SetPetDetail(colorRow, custom ? Lang.T("Pet buatan sendiri memakai warna dari gambarnya") : "");
                };
                refresh();

                cap(Lang.T("Pet buatan sendiri"));
                var folderRow = Row(Lang.T("Folder pet"), Lang.T("Per pet: sheet.png dan pet.json dari Aseprite; tag walk, idle, sit, sleep"),
                    Button(Lang.T("Buka folder"), false, delegate { island.OpenPetFolder(); }));
                folderRow.Glyph = (char)0xE8B7;
                add(folderRow);
            }

            static void SetPetDetail(KitRow row, string detail)
            {
                if (row.Detail == detail) return;
                row.Detail = detail;
                row.Field.AccessibleDescription = detail;
                row.Invalidate();
            }
        }
    }
}
