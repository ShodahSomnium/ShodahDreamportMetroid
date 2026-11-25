using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// STRG (decomp CStringTable.cpp): u32 magic, u32 version, u32 language count, u32 string count, (FourCC, u32 offset) per
    /// language; at the table end + offset: u32 data length, string offsets (from the data start), UTF-16BE zero-terminated
    /// strings. Languages ENGL GERM FREN SPAN ITAL (PAL) / JAPN. Game text keeps its markup (&amp;push; &amp;font=...) - <see cref="Plain"/> strips it.
    /// </summary>
    public sealed class MpStrings
    {
        public readonly List<string> Strings = new List<string>();

        public static MpStrings Read(byte[] b, string language = "ENGL")
        {
            var s = new MpStrings();
            var r = new MpReader(b);
            r.U32();
            r.U32();
            int langs = r.S32();
            int count = r.S32();
            int pick = -1, first = -1;
            for (int i = 0; i < langs; i++)
            {
                string l = r.FourCC();
                int off = r.S32();
                if (first < 0) first = off;
                if (l == language) pick = off;
            }

            int baseAt = r.Pos;
            r.Pos = baseAt + (pick >= 0 ? pick : first);
            int len = r.S32();
            int data = r.Pos;
            for (int i = 0; i < count; i++)
            {
                int o = data + (int)MpReader.U32At(b, data + i * 4);
                var sb = new StringBuilder();
                while (o + 1 < b.Length && o < data + len)
                {
                    int c = (b[o] << 8) | b[o + 1];
                    o += 2;
                    if (c == 0) break;
                    sb.Append((char)c);
                }

                s.Strings.Add(sb.ToString());
            }

            return s;
        }

        public string this[int i] { get { return i >= 0 && i < Strings.Count ? Strings[i] : ""; } }

        /// <summary>The text without the game's &amp;tag; markup.</summary>
        public static string Plain(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '&')
                {
                    int e = t.IndexOf(';', i);
                    if (e > i && e - i < 64)
                    {
                        string tag = t.Substring(i + 1, e - i - 1);
                        if (tag.Length > 0 && (char.IsLetter(tag[0]) || tag[0] == '#'))
                        {
                            i = e;
                            continue;
                        }
                    }
                }

                sb.Append(t[i]);
            }

            return sb.ToString();
        }
    }
}
