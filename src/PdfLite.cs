using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Deskmate
{
    // PDF values: dict, array (List<object>), name, ref, stream, raw token
    class PdfName
    {
        public readonly string Name;
        public PdfName(string name) { Name = name; }
    }

    class PdfRef
    {
        public readonly int Num, Gen;
        public PdfRef(int num, int gen) { Num = num; Gen = gen; }
    }

    // Numbers, strings, true/false/null: written back byte for byte
    class PdfRaw
    {
        public readonly byte[] Bytes;
        public PdfRaw(byte[] bytes) { Bytes = bytes; }
        public PdfRaw(string text) { Bytes = Encoding.ASCII.GetBytes(text); }
        public override string ToString() { return Encoding.ASCII.GetString(Bytes); }
    }

    // A bare keyword met while reading a value
    class PdfWord
    {
        public readonly string Text;
        public PdfWord(string text) { Text = text; }
    }

    class PdfDict
    {
        public readonly List<KeyValuePair<string, object>> Items = new List<KeyValuePair<string, object>>();

        public object Get(string key)
        {
            for (int i = Items.Count - 1; i >= 0; i--) if (Items[i].Key == key) return Items[i].Value;
            return null;
        }

        public bool Has(string key) { return Get(key) != null; }

        public void Set(string key, object value)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Key == key) { Items[i] = new KeyValuePair<string, object>(key, value); return; }
            Items.Add(new KeyValuePair<string, object>(key, value));
        }

        public void Remove(string key) { Items.RemoveAll(kv => kv.Key == key); }
    }

    class PdfStream
    {
        public PdfDict Dict;
        public byte[] Data;
    }

    // Reads one PDF: xref tables or streams, object streams
}
