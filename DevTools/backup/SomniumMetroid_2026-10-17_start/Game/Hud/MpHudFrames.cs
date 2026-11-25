using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>One widget of a built frame: its node, the renderers of its model, its own colour, its text.</summary>
    public sealed class MpHudNode
    {
        public MpFrame.Widget W;
        public Transform T;
        public MpHudNode Parent;
        public readonly List<MeshRenderer> Renderers = new List<MeshRenderer>();
        public Color Own = Color.white;
        public MpHudText Text;
        public Color TextColor = Color.white, TextOutline = Color.black;
        public Vector3 BasePos;
        public Quaternion BaseRot;
        public Vector3 BaseScale;
        public bool Visible = true;
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        private static readonly int IdMod = Shader.PropertyToID("_ModColor");

        /// <summary>The colour after the parents' (CGuiWidget::GetModifiedColor).</summary>
        public Color Effective
        {
            get
            {
                Color c = Own;
                for (MpHudNode p = Parent; p != null; p = p.Parent) c *= p.Own;
                return c;
            }
        }

        public void ApplyColor()
        {
            Color c = Effective;
            Block.SetColor(IdMod, c);
            foreach (MeshRenderer r in Renderers) if (r != null) r.SetPropertyBlock(Block);
            if (Text != null) Text.SetColor(TextColor * c, TextOutline * new Color(1, 1, 1, c.a));
        }

        public void SetColor(Color c)
        {
            if (c == Own) return;
            Own = c;
            ApplyColor();
        }

        public void SetVisible(bool on)
        {
            Visible = on;
            if (T != null && T.gameObject.activeSelf != on) T.gameObject.SetActive(on);
        }
    }

    /// <summary>
    /// A GUI frame (FRME) built from the game's own widgets: models (their CMDL with the TEV materials, lit by the GUI's white ambient,
    /// coloured by the widget colour and blended by the widget's draw flag as CGuiModel::Draw does), text panes (the game's fonts),
    /// energy bars. Widget transforms are local to their parent. The frame's space (X right, Y away from the camera, Z up) is mapped
    /// so that the HUD camera (CSamusHud: (0, -20, 0), 65 degrees) sits at the anchor's origin looking along +Z, scaled so the frame's
    /// Y = 0 plane is <see cref="Distance"/> metres away.
    /// </summary>
    public sealed class MpHudFrame
    {
        public const float CameraY = -20f;
        public static float Distance = 1.5f;
        /// <summary>Squeezes the HUD towards the middle of the view (1 = the game's own 65-degree layout).</summary>
        public static float Spread = 0.9f;

        public readonly MpFrame Data;
        public readonly Transform Root;
        public readonly Dictionary<string, MpHudNode> Nodes = new Dictionary<string, MpHudNode>();
        private int _queue;

        public MpHudFrame(string name, MpFrame data, Transform anchor, int queueBase)
        {
            Data = data;
            _queue = queueBase;
            var go = new GameObject("HUD " + name);
            Root = go.transform;
            Root.SetParent(anchor, false);
            float k = Distance / -CameraY;
            Root.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Root.localScale = new Vector3(k * Spread, -k, k * Spread);
            Root.localPosition = Root.localRotation * Vector3.Scale(Root.localScale, new Vector3(0f, -CameraY, 0f));
        }

        public MpHudNode Find(string name)
        {
            MpHudNode n;
            return Nodes.TryGetValue(name, out n) ? n : null;
        }

        public void Build(MpAssets assets)
        {
            foreach (MpFrame.Widget w in Data.Widgets)
            {
                if (w.Type == "CAMR" || w.Type == "LITE") continue;
                var n = new MpHudNode { W = w, Own = new Color(w.R, w.G, w.B, w.A) };
                MpHudNode parent;
                Transform parentT = Root;
                if (w.Parent != null && Nodes.TryGetValue(w.Parent, out parent) && parent != n) { n.Parent = parent; parentT = parent.T; }
                var go = new GameObject(w.Type + " " + w.Name);
                n.T = go.transform;
                n.T.SetParent(parentT, false);
                Matrix4x4 m = Matrix4x4.identity;
                m.m00 = w.Orientation[0]; m.m01 = w.Orientation[1]; m.m02 = w.Orientation[2];
                m.m10 = w.Orientation[3]; m.m11 = w.Orientation[4]; m.m12 = w.Orientation[5];
                m.m20 = w.Orientation[6]; m.m21 = w.Orientation[7]; m.m22 = w.Orientation[8];
                n.T.localPosition = new Vector3(w.Translation[0], w.Translation[1], w.Translation[2]);
                Vector3 s = m.lossyScale;
                if (s.x > 1e-4f && s.y > 1e-4f && s.z > 1e-4f)
                {
                    n.T.localRotation = m.rotation;
                    n.T.localScale = s;
                }

                n.BasePos = n.T.localPosition;
                n.BaseRot = n.T.localRotation;
                n.BaseScale = n.T.localScale;
                Nodes[w.Name] = n;
                _queue++;
                switch (w.Type)
                {
                    case "MODL": BuildModel(assets, n); break;
                    case "TXPN":
                    {
                        MpHudFont font = MpHudFont.Get(assets, w.FontId);
                        if (font == null) break;
                        n.TextColor = w.FontColor != null ? new Color(w.FontColor[0], w.FontColor[1], w.FontColor[2], w.FontColor[3]) : Color.white;
                        n.TextOutline = w.OutlineColor != null ? new Color(w.OutlineColor[0], w.OutlineColor[1], w.OutlineColor[2], w.OutlineColor[3]) : Color.black;
                        n.Text = new MpHudText(n.T, font, w.TextDims, w.Justification, w.VJustification, n.TextColor, n.TextOutline, _queue);
                        break;
                    }
                }

                if (!w.Visible) n.SetVisible(false);
            }

            foreach (MpHudNode n in Nodes.Values) n.ApplyColor();
        }

        private void BuildModel(MpAssets assets, MpHudNode n)
        {
            MpModel model = MpModels.Get(n.W.ModelId);
            if (model == null) return;
            model.Build(false);
            if (model.Meshes == null) return;
            for (int i = 0; i < model.Meshes.Length; i++)
            {
                if (model.Meshes[i] == null) continue;
                var go = new GameObject("mesh " + i);
                go.transform.SetParent(n.T, false);
                go.AddComponent<MeshFilter>().sharedMesh = model.Meshes[i];
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var mats = new Material[model.Materials[i].Length];
                for (int k = 0; k < mats.Length; k++)
                {
                    Material src = model.Materials[i][k];
                    if (src == null) continue;
                    var m = new Material(src) { name = src.name + " (HUD " + n.W.Name + ")" };
                    m.SetFloat("_Lit", 2f);
                    m.SetColor("_Ambient", Color.white);
                    m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                    m.SetFloat("_ZWrite", 0f);
                    m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    m.SetFloat("_AlphaTest", 0f);
                    ApplyDrawFlags(m, n.W.DrawFlags, model.Set, i < model.SubMaterialOf.Length ? model.SubMaterialOf[i] : null, k);
                    m.renderQueue = _queue;
                    mats[k] = m;
                }

                mr.sharedMaterials = mats;
                n.Renderers.Add(mr);
            }
        }

        /// <summary>
        /// The blend of CGuiModel::Draw's model flags through CCubeMaterial::SetupBlendMode: shadeless / opaque keep the material's
        /// blend; alpha (trans 5) and additive (7) turn a ONE source into SRCALPHA and a ZERO destination into INVSRCALPHA / ONE;
        /// alpha-additive overdraw ends additive.
        /// </summary>
        private static void ApplyDrawFlags(Material m, uint flags, MpMaterialSet set, int[] subMat, int sub)
        {
            int mi = subMat != null && sub < subMat.Length ? subMat[sub] : -1;
            int src = 1, dst = 0;
            if (set != null && mi >= 0 && mi < set.Materials.Length) { src = set.Materials[mi].BlendSrc; dst = set.Materials[mi].BlendDst; }
            int trans = flags == 2 ? 5 : flags == 3 || flags == 4 ? 7 : 0;
            if (trans > 4 && src == 1)
            {
                src = 4;
                if (dst == 0) dst = trans > 6 ? 1 : 5;
            }

            m.SetFloat("_SrcBlend", (float)Src(src));
            m.SetFloat("_DstBlend", (float)Dst(dst));
        }

        private static UnityEngine.Rendering.BlendMode Src(int gx)
        {
            switch (gx)
            {
                case 0: return UnityEngine.Rendering.BlendMode.Zero;
                case 2: return UnityEngine.Rendering.BlendMode.DstColor;
                case 3: return UnityEngine.Rendering.BlendMode.OneMinusDstColor;
                case 4: return UnityEngine.Rendering.BlendMode.SrcAlpha;
                case 5: return UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                default: return UnityEngine.Rendering.BlendMode.One;
            }
        }

        private static UnityEngine.Rendering.BlendMode Dst(int gx)
        {
            switch (gx)
            {
                case 1: return UnityEngine.Rendering.BlendMode.One;
                case 2: return UnityEngine.Rendering.BlendMode.SrcColor;
                case 3: return UnityEngine.Rendering.BlendMode.OneMinusSrcColor;
                case 4: return UnityEngine.Rendering.BlendMode.SrcAlpha;
                case 5: return UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                default: return UnityEngine.Rendering.BlendMode.Zero;
            }
        }

        public void SetVisible(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        /// <summary>
        /// An energy bar (CAuiEnergyBarT01::Draw): three strips along the bar's coordinate function - filled 0..f, shadow f..s,
        /// empty s..1 - textured along the bar (u = t), tessellated at the game's step, blended SRCALPHA / ONE.
        /// </summary>
        public static void BarMesh(Mesh mesh, System.Func<float, Vector3[]> coord, float tess, float filled, float shadow, Color cf, Color cs, Color ce)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var tri = new List<int>();
            for (int seg = 0; seg < 3; seg++)
            {
                float a = seg == 0 ? 0f : seg == 1 ? filled : shadow;
                float b = seg == 0 ? filled : seg == 1 ? shadow : 1f;
                if (b <= a) continue;
                Color c = seg == 0 ? cf : seg == 1 ? cs : ce;
                int start = v.Count;
                for (float t = a; ; t += tess)
                {
                    float tt = Mathf.Min(t, b);
                    Vector3[] p = coord(tt);
                    v.Add(p[0]); uv.Add(new Vector2(tt, 0f)); col.Add(c);
                    v.Add(p[1]); uv.Add(new Vector2(tt, 1f)); col.Add(c);
                    if (tt >= b) break;
                }

                for (int i = start; i + 3 < v.Count; i += 2)
                {
                    tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
                    tri.Add(i + 2); tri.Add(i + 1); tri.Add(i + 3);
                }
            }

            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(tri, 0, true);
        }
    }
}
