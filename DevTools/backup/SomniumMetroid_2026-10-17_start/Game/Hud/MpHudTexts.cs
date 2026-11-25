using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The game's raster fonts (FONT + its C4 glyph atlas) as Unity textures: one texture per glyph layer, R = fill and G = outline,
    /// built from the atlas's palette INDEX bits the way CTextRenderBuffer::SetFontPalette builds the layer palettes (four layers:
    /// one bit each; two layers + outline: bit 2L fill, 2L+1 outline; two layers: two bits of coverage; one layer (+ outline):
    /// index 1 fill, 2 outline).
    /// </summary>
    public sealed class MpHudFont
    {
        public MpFont Data;
        public Texture2D[] Layers;
        public Material[] Materials;
        public int Width, Height;

        private static readonly Dictionary<uint, MpHudFont> Cache = new Dictionary<uint, MpHudFont>();

        /// <summary>Loader thread data, main thread textures (call on the main thread).</summary>
        public static MpHudFont Get(MpAssets assets, uint id)
        {
            MpHudFont f;
            if (Cache.TryGetValue(id, out f)) return f;
            f = null;
            try
            {
                if (assets.TypeOf(id) == "FONT")
                {
                    f = new MpHudFont { Data = MpFont.Read(assets.Read(id)) };
                    f.Build(assets.Read(f.Data.Texture));
                }
            }
            catch (System.Exception e) { MpLog.Warn("HUD: font " + id.ToString("X8") + " not read: " + e.Message); f = null; }

            Cache[id] = f;
            return f;
        }

        private void Build(byte[] txtr)
        {
            int w, h;
            byte[] idx = MpTexture.RawIndices(txtr, out w, out h);
            Width = w;
            Height = h;
            int mode = Data.Mode;
            int layers = mode == 2 ? 4 : (mode == 3 || mode == 4) ? 2 : 1;
            Layers = new Texture2D[layers];
            Materials = new Material[layers];
            for (int l = 0; l < layers; l++)
            {
                var px = new Color32[w * h];
                for (int i = 0; i < w * h; i++)
                {
                    int v = idx != null ? idx[i] : 0;
                    byte fill = 0, outline = 0;
                    switch (mode)
                    {
                        case 2: fill = (byte)(((v >> l) & 1) != 0 ? 255 : 0); break;
                        case 3:
                            if ((v & (1 << (l * 2))) != 0) fill = 255;
                            else if ((v & (2 << (l * 2))) != 0) outline = 255;
                            break;
                        case 4:
                        {
                            bool first = (v & (1 << (l * 2))) != 0, second = (v & (2 << (l * 2))) != 0;
                            fill = (byte)(first && second ? 255 : second ? 168 : first ? 84 : 0);
                            break;
                        }
                        default:
                            if (v == 1) fill = 255;
                            else if (v == 2) outline = 255;
                            else if (v == 3) fill = 255;
                            break;
                    }

                    // rows stay in the GameCube order (row 0 = v 0): the glyph uvs are the game's
                    px[i] = new Color32(fill, outline, 0, 255);
                }

                var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { name = "Font " + Data.Name + " " + Data.Size + " layer " + l, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels32(px);
                t.Apply(false, true);
                Layers[l] = t;
            }
        }

        public Material MaterialFor(int layer, int queue)
        {
            layer = Mathf.Clamp(layer, 0, Layers.Length - 1);
            if (Materials[layer] != null) return Materials[layer];
            Material carrier = MpGame.FxMaterial;
            if (carrier == null) return null;
            var m = new Material(carrier) { name = Layers[layer].name };
            m.SetTexture("_MainTex", Layers[layer]);
            m.SetFloat("_Text", 1);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.SetFloat("_ZWrite", 0);
            m.renderQueue = queue;
            Materials[layer] = m;
            return m;
        }
    }

    /// <summary>
    /// A text pane (CGuiTextPane): glyph quads laid out in the pane's pixel box (extent) and scaled into its frame-unit size (dims),
    /// the game's A / B / C spacing and kerning, justification left / centre / right, the text colour per vertex, the outline colour on
    /// the material. One child renderer per font layer used.
    /// </summary>
    public sealed class MpHudText
    {
        public GameObject Go;
        private readonly MpHudFont _font;
        private readonly float _dimX, _dimY, _extX, _extY;
        private readonly int _just, _vjust, _queue;
        private string _text;
        private Color _color, _outline;
        private readonly List<MeshFilter> _filters = new List<MeshFilter>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        public MpHudText(Transform parent, MpHudFont font, float[] dims, int justification, int vjust, Color color, Color outline, int queue)
        {
            _font = font;
            _dimX = dims[0];
            _dimY = dims[1];
            _extX = Mathf.Max(1f, dims[2]);
            _extY = Mathf.Max(1f, dims[3]);
            _just = justification;
            _vjust = vjust;
            _color = color;
            _outline = outline;
            _queue = queue;
            Go = new GameObject("text");
            Go.transform.SetParent(parent, false);
        }

        public void SetColor(Color c, Color outline)
        {
            if (c == _color && outline == _outline) return;
            _color = c;
            _outline = outline;
            string t = _text;
            _text = null;
            SetText(t);
        }

        public void SetText(string text)
        {
            if (text == _text) return;
            _text = text;
            if (_font == null) return;
            var verts = new List<Vector3>[_font.Layers.Length];
            var uvs = new List<Vector2>[_font.Layers.Length];
            var cols = new List<Color>[_font.Layers.Length];
            var tris = new List<int>[_font.Layers.Length];
            string[] lines = string.IsNullOrEmpty(text) ? new string[0] : MpStrings.Plain(text).Split('\n');
            float lineH = _font.Data.MonoH + _font.Data.LineMargin;
            float totalH = lines.Length * lineH;
            float y0 = _vjust == 1 ? (_extY - totalH) * 0.5f : _vjust == 2 ? _extY - totalH : 0f;
            float sx = _dimX / _extX, sz = _dimY / _extY;
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float width = Measure(line);
                float x = _just == 1 ? (_extX - width) * 0.5f : _just == 2 ? _extX - width : 0f;
                float baseY = y0 + li * lineH + _font.Data.Baseline;
                char prev = '\0';
                foreach (char ch in line)
                {
                    MpFont.Glyph g;
                    if (!_font.Data.Glyphs.TryGetValue(ch, out g)) { prev = ch; x += _font.Data.MonoW * 0.5f; continue; }
                    if (prev != '\0') x += _font.Data.Kern(prev, ch);
                    x += g.A;
                    int l = Mathf.Clamp(g.Layer, 0, _font.Layers.Length - 1);
                    if (verts[l] == null) { verts[l] = new List<Vector3>(); uvs[l] = new List<Vector2>(); cols[l] = new List<Color>(); tris[l] = new List<int>(); }
                    float top = baseY - g.Baseline, bottom = top + g.CellH;
                    float left = x, right = x + g.CellW;
                    // frame space: x right, z up; the pane spans -dim/2..+dim/2 around the widget
                    Vector3 a = new Vector3(left * sx - _dimX * 0.5f, 0f, _dimY * 0.5f - top * sz);
                    Vector3 b = new Vector3(right * sx - _dimX * 0.5f, 0f, _dimY * 0.5f - top * sz);
                    Vector3 c = new Vector3(right * sx - _dimX * 0.5f, 0f, _dimY * 0.5f - bottom * sz);
                    Vector3 d = new Vector3(left * sx - _dimX * 0.5f, 0f, _dimY * 0.5f - bottom * sz);
                    int n = verts[l].Count;
                    verts[l].Add(a); verts[l].Add(b); verts[l].Add(c); verts[l].Add(d);
                    uvs[l].Add(new Vector2(g.U0, g.V0)); uvs[l].Add(new Vector2(g.U1, g.V0)); uvs[l].Add(new Vector2(g.U1, g.V1)); uvs[l].Add(new Vector2(g.U0, g.V1));
                    for (int k = 0; k < 4; k++) cols[l].Add(_color);
                    tris[l].Add(n); tris[l].Add(n + 1); tris[l].Add(n + 2); tris[l].Add(n); tris[l].Add(n + 2); tris[l].Add(n + 3);
                    x += g.B + g.C;
                    prev = ch;
                }
            }

            for (int l = 0; l < _font.Layers.Length; l++)
            {
                while (_filters.Count <= l)
                {
                    var go = new GameObject("layer " + _filters.Count);
                    go.transform.SetParent(Go.transform, false);
                    _filters.Add(go.AddComponent<MeshFilter>());
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _font.MaterialFor(_filters.Count - 1, _queue);
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _renderers.Add(mr);
                }

                MeshFilter mf = _filters[l];
                if (verts[l] == null) { mf.gameObject.SetActive(false); continue; }
                mf.gameObject.SetActive(true);
                Mesh m = mf.sharedMesh;
                if (m == null) { m = new Mesh { name = "hud text" }; mf.sharedMesh = m; }
                m.Clear();
                m.SetVertices(verts[l]);
                m.SetUVs(0, uvs[l]);
                m.SetColors(cols[l]);
                m.SetTriangles(tris[l], 0, true);
                _block.SetColor("_OutlineColor", _outline);
                _renderers[l].SetPropertyBlock(_block);
            }
        }

        private float Measure(string line)
        {
            float w = 0f;
            char prev = '\0';
            foreach (char ch in line)
            {
                MpFont.Glyph g;
                if (!_font.Data.Glyphs.TryGetValue(ch, out g)) { w += _font.Data.MonoW * 0.5f; prev = ch; continue; }
                if (prev != '\0') w += _font.Data.Kern(prev, ch);
                w += g.A + g.B + g.C;
                prev = ch;
            }

            return w;
        }
    }
}
