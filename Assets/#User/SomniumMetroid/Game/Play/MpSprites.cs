using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Camera-facing sprites for the shot effects until the game's particle system (PART / SWHC / ELSC) is ported: bolts, trails,
    /// muzzle flashes, charge glow, impacts. Each sprite uses a texture the game's own particle uses (the first TEXR texture of the
    /// PART, read on the loader thread) with additive or alpha blending on the Fx shader. Pooled; one quad mesh for all.
    /// </summary>
    public static class MpSprites
    {
        public sealed class Sprite
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block;
            public Vector3 Pos, Vel;
            public float Size, Grow, Life, Age;
            public Color Color;
            public float FadeIn, Gravity;
            public Sheet Sheet;           // an animated texture: the frame from Clock (60 frames a second, as the game's particles)
            public float Clock;
            public int TexW = 1, TexH = 1;
            public bool Alive;
        }

        private static Mesh _quad;
        private static readonly Dictionary<long, Material> Materials = new Dictionary<long, Material>();
        private static readonly List<Sprite> Pool = new List<Sprite>();
        private static readonly List<Sprite> Live = new List<Sprite>();
        private static Transform _root;
        private static readonly int IdColor = Shader.PropertyToID("_Color"), IdST = Shader.PropertyToID("_MainTex_ST");
        private static Texture2D _glow;

        public static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "Mp sprite quad" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
                return _quad;
            }
        }

        /// <summary>A soft round glow, for effects whose particle texture was not found.</summary>
        public static Texture2D Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int n = 64;
                _glow = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "Mp glow", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        a = a * a;
                        byte v = (byte)(a * 255f);
                        px[y * n + x] = new Color32(v, v, v, v);
                    }

                _glow.SetPixels32(px);
                _glow.Apply(false, true);
                return _glow;
            }
        }

        /// <summary>An animated texture (PART TEXR ATEX, decomp CUVEAnimTexture): tiles of TileW x TileH every StrideW / StrideH texels.</summary>
        public sealed class Sheet
        {
            public int TileW, TileH, StrideW, StrideH, Cycle = 1;
            public bool Loop = true;
        }

        private static readonly Dictionary<uint, Sheet> Sheets = new Dictionary<uint, Sheet>();
        private static readonly Dictionary<Material, Sheet> SheetOfMaterial = new Dictionary<Material, Sheet>();

        /// <summary>
        /// The first texture a PART names (its TEXR element: CNST, or ATEX with its tile layout - decomp CParticleDataFactory::
        /// GetTextureElement) - found by scanning for the TEXR tag, not by parsing the whole description. Loader thread safe.
        /// </summary>
        public static uint TextureOfPart(byte[] part)
        {
            if (part == null) return 0xFFFFFFFF;
            for (int i = 4; i + 16 < part.Length; i++)
            {
                if (part[i] != 'T' || part[i + 1] != 'E' || part[i + 2] != 'X' || part[i + 3] != 'R') continue;
                int k = i + 4;
                string kind = System.Text.Encoding.ASCII.GetString(part, k, 4);
                if (kind != "CNST" && kind != "ATEX") continue;
                k += 4;
                if (System.Text.Encoding.ASCII.GetString(part, k, 4) != "CNST") continue;
                uint tex = MpReader.U32At(part, k + 4);
                k += 8;
                if (kind == "ATEX" && k + 5 * 8 + 5 <= part.Length)
                {
                    var s = new Sheet();
                    int[] v = new int[5];
                    bool ok = true;
                    for (int n = 0; n < 5; n++, k += 8)
                    {
                        if (System.Text.Encoding.ASCII.GetString(part, k, 4) != "CNST") { ok = false; break; }
                        v[n] = (int)MpReader.U32At(part, k + 4);
                    }

                    if (ok && v[2] > 0 && v[3] > 0)
                    {
                        s.TileW = v[0]; s.TileH = v[1]; s.StrideW = v[2]; s.StrideH = v[3]; s.Cycle = Mathf.Max(1, v[4]);
                        s.Loop = System.Text.Encoding.ASCII.GetString(part, k, 4) != "CNST" || part[k + 4] != 0;
                        lock (Sheets) Sheets[tex] = s;
                    }
                }

                return tex;
            }

            return 0xFFFFFFFF;
        }

        /// <summary>A sprite material for a texture id, with its animated sheet when the PART that named it has one.</summary>
        public static Material MaterialFor(uint texId, bool additive)
        {
            Material m = MaterialFor(MpAssets.Valid(texId) ? MpTextures.Get(texId) : null, additive);
            Sheet s;
            bool has;
            lock (Sheets) has = Sheets.TryGetValue(texId, out s);
            if (m != null && has) SheetOfMaterial[m] = s;
            return m;
        }

        public static Material MaterialFor(Texture tex, bool additive)
        {
            Material carrier = MpGame.FxMaterial;
            if (carrier == null) return null;
            if (tex == null) tex = Glow;
            long key = ((long)tex.GetInstanceID() << 1) | (additive ? 1L : 0L);
            Material m;
            if (Materials.TryGetValue(key, out m) && m != null) return m;
            m = new Material(carrier) { name = "Mp sprite " + tex.name + (additive ? " add" : " alpha") };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            m.SetFloat("_Billboard", 1);
            m.renderQueue = 3100;
            Materials[key] = m;
            return m;
        }

        /// <summary>Spawns a sprite at a Unity position: size in metres, life in seconds (fades out over its life), grows by grow per second.</summary>
        public static Sprite Spawn(Material mat, Vector3 pos, Vector3 vel, float size, float grow, float life, Color color)
        {
            if (mat == null) return null;
            if (_root == null) _root = new GameObject("Mp Sprites").transform;
            Sprite s;
            if (Pool.Count > 0)
            {
                s = Pool[Pool.Count - 1];
                Pool.RemoveAt(Pool.Count - 1);
                s.Go.SetActive(true);
            }
            else
            {
                s = new Sprite { Go = new GameObject("sprite"), Block = new MaterialPropertyBlock() };
                s.Go.transform.SetParent(_root, false);
                s.Go.AddComponent<MeshFilter>().sharedMesh = Quad;
                s.Renderer = s.Go.AddComponent<MeshRenderer>();
                s.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s.Renderer.receiveShadows = false;
            }

            s.Renderer.sharedMaterial = mat;
            Sheet sheet;
            s.Sheet = SheetOfMaterial.TryGetValue(mat, out sheet) ? sheet : null;
            Texture mt = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            s.TexW = mt != null ? mt.width : 1;
            s.TexH = mt != null ? mt.height : 1;
            s.Clock = 0f;
            s.Pos = pos;
            s.Vel = vel;
            s.Size = size;
            s.Grow = grow;
            s.Life = life;
            s.Age = 0f;
            s.Color = color;
            s.FadeIn = 0f;
            s.Gravity = 0f;
            s.Alive = true;
            Apply(s, 0f);
            Live.Add(s);
            return s;
        }

        private static void Apply(Sprite s, float t)
        {
            s.Go.transform.position = s.Pos;
            float size = Mathf.Max(0.001f, s.Size + s.Grow * s.Age);
            s.Go.transform.localScale = new Vector3(size, size, size);
            Color c = s.Color;
            float fade = s.Life > 0f ? 1f - t : 1f;
            if (s.FadeIn > 0f && s.Age < s.FadeIn) fade *= s.Age / s.FadeIn;
            c.a *= Mathf.Clamp01(fade);
            s.Block.SetColor(IdColor, c);
            s.Block.SetVector(IdST, FrameST(s));
            s.Renderer.SetPropertyBlock(s.Block);
        }

        /// <summary>
        /// The texture window of the sprite's current frame (decomp CUVEAnimTexture: tiles listed from the last row of the texture's
        /// data up, left to right; frame = particle frame / (cycle frames / tiles), wrapping when it loops).
        /// </summary>
        private static Vector4 FrameST(Sprite s)
        {
            Sheet sh = s.Sheet;
            if (sh == null) return new Vector4(1f, 1f, 0f, 0f);
            int xTiles = Mathf.Max(1, s.TexW / sh.StrideW), yTiles = Mathf.Max(1, s.TexH / sh.StrideH), tiles = xTiles * yTiles;
            int frame = (int)(s.Clock * 60f);
            int tile = (int)(frame / (sh.Cycle / (float)tiles));
            tile = sh.Loop ? (tile % tiles + tiles) % tiles : Mathf.Clamp(tile, 0, tiles - 1);
            int x = tile % xTiles, y = yTiles - 1 - tile / xTiles;
            return new Vector4(sh.TileW / (float)s.TexW, sh.TileH / (float)s.TexH, sh.StrideW * x / (float)s.TexW, sh.StrideH * y / (float)s.TexH);
        }

        public static void Kill(Sprite s)
        {
            if (s == null || !s.Alive) return;
            s.Alive = false;
            s.Life = 0.0001f;
            s.Age = 1f;
        }

        public static void Tick(float dt)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                Sprite s = Live[i];
                s.Age += dt;
                s.Clock += dt;
                if (!s.Alive || (s.Life > 0f && s.Age >= s.Life))
                {
                    s.Alive = false;
                    s.Go.SetActive(false);
                    Live.RemoveAt(i);
                    Pool.Add(s);
                    continue;
                }

                s.Vel += Vector3.down * s.Gravity * dt;
                s.Pos += s.Vel * dt;
                Apply(s, s.Life > 0f ? s.Age / s.Life : 0f);
            }
        }

        /// <summary>Moves a held sprite (a projectile's head, the charge glow) without ageing it.</summary>
        public static void Hold(Sprite s, Vector3 pos, float size, Color color)
        {
            if (s == null || !s.Alive) return;
            s.Pos = pos;
            s.Size = size;
            s.Color = color;
            s.Age = 0f;
            Apply(s, 0f);
        }

        public static void Clear()
        {
            foreach (Sprite s in Live) if (s.Go != null) Object.Destroy(s.Go);
            foreach (Sprite s in Pool) if (s.Go != null) Object.Destroy(s.Go);
            Live.Clear();
            Pool.Clear();
        }
    }
}
