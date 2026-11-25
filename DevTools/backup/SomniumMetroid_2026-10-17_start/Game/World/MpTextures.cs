using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// TXTR -> Texture2D, cached by asset id for the session. Decoding (MpTexture.Read) runs on the loader thread; this side only
    /// uploads. Textures are LINEAR (no sRGB conversion): the GameCube combines raw texel values, so the TEV shader does too and
    /// converts its result for a linear frame buffer (MpShaderGlobals). Repeat + trilinear, as the game loads them (kCM_Repeat).
    /// </summary>
    public static class MpTextures
    {
        private static readonly Dictionary<uint, Texture2D> Cache = new Dictionary<uint, Texture2D>();
        private static Texture2D _white;
        public static int Uploaded;
        public static long UploadedBytes;

        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
                    var px = new Color32[16];
                    for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                    _white.SetPixels32(px);
                    _white.Apply(false, true);
                    _white.name = "Mp White";
                }

                return _white;
            }
        }

        public static bool Has(uint id) { return Cache.ContainsKey(id); }

        public static Texture2D Get(uint id)
        {
            Texture2D t;
            return Cache.TryGetValue(id, out t) && t != null ? t : null;
        }

        /// <summary>Uploads a decoded texture (main thread). Returns the cached one if it is already there.</summary>
        public static Texture2D Upload(uint id, MpTexture d)
        {
            Texture2D t;
            if (Cache.TryGetValue(id, out t) && t != null) return t;
            if (d == null) return null;
            try
            {
                t = new Texture2D(d.Width, d.Height, d.Dxt1 ? TextureFormat.DXT1 : TextureFormat.RGBA32, d.Mips, true);
                t.LoadRawTextureData(d.Data);
                t.wrapMode = TextureWrapMode.Repeat;
                t.filterMode = d.Mips > 1 ? FilterMode.Trilinear : FilterMode.Bilinear;
                t.anisoLevel = 4;
                t.name = "TXTR " + id.ToString("X8");
                t.Apply(false, true);
                Uploaded++;
                UploadedBytes += d.Data.Length;
            }
            catch (System.Exception e)
            {
                MpLog.Warn("Textures: TXTR " + id.ToString("X8") + " (" + d.Format + " " + d.Width + "x" + d.Height + " mips " + d.Mips + ") not uploaded: " + e.Message);
                t = null;
            }

            Cache[id] = t;
            return t;
        }

        public static void Clear()
        {
            foreach (var kv in Cache) if (kv.Value != null) Object.Destroy(kv.Value);
            Cache.Clear();
        }
    }
}
