using System.Collections.Generic;

namespace Deskmate
{
    static partial class Lang
    {
        // Registered last; words other tables already translate are left to them
        static void AddPet(Dictionary<string, string> en)
        {
            AddPetText(en, "Pet di dock", "Dock pet");
            AddPetText(en, "Hewan kecil yang berjalan di atas dock, atau di tepi bawah layar tanpa dock", "A little animal that walks on top of the dock, or along the bottom of the screen without one");
            AddPetText(en, "Hewan", "Animal");
            AddPetText(en, "Kucing", "Cat");
            AddPetText(en, "Anjing", "Dog");
            AddPetText(en, "Gagal dimuat, memakai kucing", "Could not load, showing the cat");
            AddPetText(en, "Warna bulu", "Fur color");
            AddPetText(en, "Oranye", "Orange");
            AddPetText(en, "Cokelat", "Brown");
            AddPetText(en, "Abu-abu", "Gray");
            AddPetText(en, "Hitam", "Black");
            AddPetText(en, "Krem", "Cream");
            AddPetText(en, "Pet buatan sendiri memakai warna dari gambarnya", "Your own pets use the colors in their image");
            AddPetText(en, "Ukuran", "Size");
            AddPetText(en, "Kecil", "Small");
            AddPetText(en, "Sedang", "Medium");
            AddPetText(en, "Besar", "Large");
            AddPetText(en, "Kecepatan jalan", "Walking speed");
            AddPetText(en, "Pet buatan sendiri", "Your own pets");
            AddPetText(en, "Folder pet", "Pet folder");
            AddPetText(en, "Per pet: sheet.png dan pet.json dari Aseprite; tag walk, idle, sit, sleep", "Per pet: sheet.png and pet.json from Aseprite; tags walk, idle, sit, sleep");
            AddPetText(en, "Buka folder", "Open folder");
            // Load problems, shown under the animal picker
            AddPetText(en, "butuh sheet.png dan pet.json", "needs sheet.png and pet.json");
            AddPetText(en, "berkas terlalu besar", "file too large");
            AddPetText(en, "pet.json tidak terbaca", "pet.json could not be read");
            AddPetText(en, "sheet.png tidak terbaca", "sheet.png could not be read");
            AddPetText(en, "jumlah frame tidak sesuai", "between 1 and 128 frames are supported");
            AddPetText(en, "ukuran frame tidak sesuai", "all frames need one size, up to 256 px");
            AddPetText(en, "frame di luar gambar", "a frame lies outside the image");
            AddPetText(en, "butuh tag walk", "needs a walk tag");
            AddPetText(en, "folder pet tidak ditemukan", "pet folder not found");
        }

        static void AddPetText(Dictionary<string, string> en, string id, string text)
        {
            if (!en.ContainsKey(id)) en.Add(id, text);
        }
    }
}
