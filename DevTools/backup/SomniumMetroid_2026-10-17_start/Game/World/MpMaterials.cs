using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// A Metroid Prime material (MpMaterial) as a Unity material on the TEV shader (Shaders/MpTev.shader), stage by stage:
    /// texture = the model's texture at the stage's texmap slot, texgen = the material texgen at the stage's texcoord (source,
    /// UV animation or identity, normalize), inputs, output registers, ras channel (COLOR0A0 = lit, anything else = zero), the
    /// stage's konst colour resolved from its colour/alpha selects (GXTevKColorSel / KAlphaSel), the game's blend factors
    /// (alpha-tested materials draw ONE/ZERO, CCubeMaterial::SetupBlendMode) and depth write (flag bit 7).
    /// </summary>
    public static class MpMaterials
    {
        /// <summary>The carrier: a material in the scene that brings the shader into the upload (MpGame._tevMaterial).</summary>
        public static Material Carrier;

        /// <summary>Unity cull mode for the game's front faces (the reflection in the root flips the winding; settled by the lab picture).</summary>
        public static CullMode Cull = CullMode.Back;

        private static readonly float[] KFrac = { 1f, 7f / 8f, 3f / 4f, 5f / 8f, 1f / 2f, 3f / 8f, 1f / 4f, 1f / 8f };
        private static readonly int[] PropCi = Ids("_Ci"), PropAi = Ids("_Ai"), PropOt = Ids("_Ot"), PropTg = Ids("_Tg"), PropTp = Ids("_Tp"), PropK = Ids("_K"), PropTex = Ids("_Tex");
        public static int Created;

        private static int[] Ids(string prefix)
        {
            var a = new int[6];
            for (int i = 0; i < 6; i++) a[i] = Shader.PropertyToID(prefix + i);
            return a;
        }

        public static bool IsOpaque(MpMaterial m)
        {
            if (m.Has(MpMaterial.FlagAlphaTest)) return true;
            return m.BlendSrc == 1 && m.BlendDst == 0;
        }

        /// <summary>Builds the Unity material (main thread). <paramref name="textureIds"/> = the model's texture list.</summary>
        public static Material Make(MpMaterial m, uint[] textureIds, bool actorLighting, string name)
        {
            if (Carrier == null) return null;
            var mat = new Material(Carrier) { name = name };
            int n = Mathf.Min(6, m.Stages.Length);
            mat.SetFloat("_StageCount", Mathf.Max(1, n));
            for (int s = 0; s < 6; s++)
            {
                if (s >= n)
                {
                    mat.SetVector(PropOt[s], Vector4.zero);
                    continue;
                }

                MpMaterial.Stage st = m.Stages[s];
                mat.SetVector(PropCi[s], new Vector4(st.ColorIn(0), st.ColorIn(1), st.ColorIn(2), st.ColorIn(3)));
                mat.SetVector(PropAi[s], new Vector4(st.AlphaIn(0), st.AlphaIn(1), st.AlphaIn(2), st.AlphaIn(3)));
                Texture2D tex = null;
                int map = st.TexMap;
                if (map != 0xFF && map < m.Textures.Length)
                {
                    int ti = m.Textures[map];
                    if (ti >= 0 && ti < textureIds.Length) tex = MpTextures.Get(textureIds[ti]);
                }

                mat.SetTexture(PropTex[s], tex != null ? tex : MpTextures.White);
                int ras = st.RasChannel == 4 ? 1 : 0;
                mat.SetVector(PropOt[s], new Vector4(MpMaterial.Stage.OutReg(st.ColorOps), MpMaterial.Stage.OutReg(st.AlphaOps), ras, tex != null ? 1 : 0));

                Vector4 tg = new Vector4(-1, -1, 0, 0), tp = Vector4.zero;
                int coord = st.TexCoord;
                if (coord != 0xFF && coord < m.TexGens.Length)
                {
                    uint g = m.TexGens[coord];
                    int anim = MpMaterial.TexGenMatrix(g);
                    int mode = -1;
                    if (anim >= 0 && anim < m.Anims.Length)
                    {
                        MpMaterial.UvAnim a = m.Anims[anim];
                        mode = a.Mode;
                        tp = new Vector4(a.P0, a.P1, a.P2, a.P3);
                    }

                    tg = new Vector4(MpMaterial.TexGenSource(g), mode, MpMaterial.TexGenNormalize(g) ? 1 : 0, 0);
                }

                mat.SetVector(PropTg[s], tg);
                mat.SetVector(PropTp[s], tp);
                mat.SetVector(PropK[s], Konst(m, st.KColorSel, st.KAlphaSel));
            }

            bool alphaTest = m.Has(MpMaterial.FlagAlphaTest);
            int src = alphaTest ? 1 : m.BlendSrc, dst = alphaTest ? 0 : m.BlendDst;
            bool opaque = src == 1 && dst == 0;
            mat.SetFloat("_AlphaTest", alphaTest ? 1 : 0);
            mat.SetFloat("_SrcBlend", (float)SrcFactor(src));
            mat.SetFloat("_DstBlend", (float)DstFactor(dst));
            mat.SetFloat("_ZWrite", opaque || m.Has(MpMaterial.FlagDepthWrite) && !m.Has(MpMaterial.FlagDepthSort) ? 1 : 0);
            mat.SetFloat("_Cull", (float)Cull);
            mat.SetFloat("_Lit", actorLighting ? 1 : 0);
            mat.renderQueue = opaque ? (alphaTest ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry) : (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", opaque ? (alphaTest ? "TransparentCutout" : "Opaque") : "Transparent");
            mat.enableInstancing = true;
            Created++;
            return mat;
        }

        private static Vector4 Konst(MpMaterial m, int kc, int ka)
        {
            Vector4 o = Vector4.one;
            if (kc < 8) o.x = o.y = o.z = KFrac[kc];
            else if (kc >= 0x0C && kc <= 0x0F) { Color c = K(m, kc - 0x0C); o.x = c.r; o.y = c.g; o.z = c.b; }
            else if (kc >= 0x10 && kc <= 0x1F) { Color c = K(m, (kc - 0x10) & 3); float v = Comp(c, (kc - 0x10) >> 2); o.x = o.y = o.z = v; }
            if (ka < 8) o.w = KFrac[ka];
            else if (ka >= 0x10 && ka <= 0x1F) o.w = Comp(K(m, (ka - 0x10) & 3), (ka - 0x10) >> 2);
            return o;
        }

        private static float Comp(Color c, int which) { return which == 0 ? c.r : which == 1 ? c.g : which == 2 ? c.b : c.a; }

        private static Color K(MpMaterial m, int i)
        {
            if (i >= m.Konst.Length) return Color.clear;
            uint v = m.Konst[i];
            return new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
        }

        private static BlendMode SrcFactor(int gx)
        {
            switch (gx)
            {
                case 0: return BlendMode.Zero;
                case 2: return BlendMode.DstColor;
                case 3: return BlendMode.OneMinusDstColor;
                case 4: return BlendMode.SrcAlpha;
                case 5: return BlendMode.OneMinusSrcAlpha;
                case 6: return BlendMode.DstAlpha;
                case 7: return BlendMode.OneMinusDstAlpha;
                default: return BlendMode.One;
            }
        }

        private static BlendMode DstFactor(int gx)
        {
            switch (gx)
            {
                case 1: return BlendMode.One;
                case 2: return BlendMode.SrcColor;
                case 3: return BlendMode.OneMinusSrcColor;
                case 4: return BlendMode.SrcAlpha;
                case 5: return BlendMode.OneMinusSrcAlpha;
                case 6: return BlendMode.DstAlpha;
                case 7: return BlendMode.OneMinusDstAlpha;
                default: return BlendMode.Zero;
            }
        }

        /// <summary>Every texture id a material set needs (for the loader thread to decode).</summary>
        public static void TexturesOf(MpMaterialSet set, HashSet<uint> into)
        {
            if (set == null) return;
            foreach (uint id in set.TextureIds) if (MpAssets.Valid(id)) into.Add(id);
        }
    }
}
