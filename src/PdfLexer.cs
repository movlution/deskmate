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
    class PdfLexer
    {
        readonly byte[] b;
        public int Pos;

        public PdfLexer(byte[] data, int pos) { b = data; Pos = pos; }

        // Names keep every byte, even unescaped high ones
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static bool White(byte c) { return c == 0 || c == 9 || c == 10 || c == 12 || c == 13 || c == 32; }
        public static bool Delim(byte c) { return c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == '/' || c == '%'; }

        public void Skip()
        {
            while (Pos < b.Length)
            {
                if (White(b[Pos])) { Pos++; continue; }
                if (b[Pos] != '%') return;
                while (Pos < b.Length && b[Pos] != '\n' && b[Pos] != '\r') Pos++;
            }
        }

        public bool Peek(string word)
        {
            if (Pos + word.Length > b.Length) return false;
            for (int i = 0; i < word.Length; i++) if (b[Pos + i] != word[i]) return false;
            int end = Pos + word.Length;
            return end >= b.Length || White(b[end]) || Delim(b[end]);
        }

        public void Expect(string word)
        {
            Skip();
            if (!Peek(word)) throw new InvalidDataException(word);
            Pos += word.Length;
        }

        public string Word()
        {
            Skip();
            int start = Pos;
            while (Pos < b.Length && !White(b[Pos]) && !Delim(b[Pos])) Pos++;
            if (Pos == start) throw new InvalidDataException("token");
            return Latin1.GetString(b, start, Pos - start);
        }

        public long Int()
        {
            long v;
            string w = Word();
            if (!long.TryParse(w, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v)) throw new InvalidDataException("int " + w);
            return v;
        }

        public static long Long(object v) { return Long(v, 0); }

        public static long Long(object v, long fallback)
        {
            var r = v as PdfRaw;
            if (r == null) return fallback;
            double d;
            return double.TryParse(r.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (long)d : fallback;
        }

        static bool IsInt(string w)
        {
            if (w.Length == 0) return false;
            for (int i = 0; i < w.Length; i++) if (!char.IsDigit(w[i]) && !(i == 0 && (w[i] == '-' || w[i] == '+'))) return false;
            return true;
        }

        static bool IsNumber(string w)
        {
            double d;
            return w.Length > 0 && (char.IsDigit(w[0]) || w[0] == '-' || w[0] == '+' || w[0] == '.') && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        public object Value()
        {
            Skip();
            if (Pos >= b.Length) throw new InvalidDataException("eof");
            byte c = b[Pos];
            if (c == '/')
            {
                int start = ++Pos;
                while (Pos < b.Length && !White(b[Pos]) && !Delim(b[Pos])) Pos++;
                return new PdfName(Latin1.GetString(b, start, Pos - start));
            }
            if (c == '(') return new PdfRaw(Slice(Pos, LiteralEnd()));
            if (c == '<' && Pos + 1 < b.Length && b[Pos + 1] == '<')
            {
                Pos += 2;
                var d = new PdfDict();
                while (true)
                {
                    Skip();
                    if (Pos + 1 >= b.Length) throw new InvalidDataException("dict");
                    if (b[Pos] == '>' && b[Pos + 1] == '>') { Pos += 2; return d; }
                    var key = Value() as PdfName;
                    if (key == null) throw new InvalidDataException("key");
                    Skip();
                    // A key with nothing after it
                    if (b[Pos] == '>' && Pos + 1 < b.Length && b[Pos + 1] == '>') { Pos += 2; return d; }
                    d.Set(key.Name, Value());
                }
            }
            if (c == '<')
            {
                int start = Pos;
                while (Pos < b.Length && b[Pos] != '>') Pos++;
                Pos++;
                return new PdfRaw(Slice(start, Pos));
            }
            if (c == '[')
            {
                Pos++;
                var list = new List<object>();
                while (true)
                {
                    Skip();
                    if (Pos >= b.Length) throw new InvalidDataException("array");
                    if (b[Pos] == ']') { Pos++; return list; }
                    list.Add(Value());
                }
            }
            if (c == ']' || c == '>' || c == ')' || c == '{' || c == '}') { Pos++; return new PdfWord(((char)c).ToString()); }
            string w = Word();
            if (IsInt(w))
            {
                // "n g R" is a reference
                int save = Pos;
                try
                {
                    Skip();
                    int s2 = Pos;
                    while (Pos < b.Length && char.IsDigit((char)b[Pos])) Pos++;
                    if (Pos > s2)
                    {
                        string g = Encoding.ASCII.GetString(b, s2, Pos - s2);
                        Skip();
                        if (Peek("R"))
                        {
                            Pos++;
                            return new PdfRef(int.Parse(w, CultureInfo.InvariantCulture), int.Parse(g, CultureInfo.InvariantCulture));
                        }
                    }
                }
                catch (Exception) { }
                Pos = save;
                return new PdfRaw(w);
            }
            if (IsNumber(w) || w == "true" || w == "false" || w == "null") return new PdfRaw(w);
            return new PdfWord(w);
        }

        int LiteralEnd()
        {
            int depth = 0;
            for (; Pos < b.Length; Pos++)
            {
                byte c = b[Pos];
                if (c == '\\') { Pos++; continue; }
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) { Pos++; return Pos; }
            }
            return Pos;
        }

        byte[] Slice(int from, int to)
        {
            var r = new byte[Math.Max(0, to - from)];
            Buffer.BlockCopy(b, from, r, 0, r.Length);
            return r;
        }
    }

}
