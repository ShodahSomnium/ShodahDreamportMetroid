using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// FRME GUI frame (decomp src/GuiSys: CGuiFrame::CreateFrame / LoadWidgetsInGame, the PAL factory FGuiWidgetFactoryInGame):
    /// u32 version, 3 u32, u32 widget count, widgets (FourCC type, header: name, parent name, bool anim controller, bool visible,
    /// bool active, bool cull faces, colour RGBA floats, u32 draw flags; type data; base info: bool worker [+ s16 worker id],
    /// float3 translation, 3x3 orientation (rows), float3 unused, u32 unused, s16). Type data: HWIG/BWIG none; CAMR projection (0:
    /// fov, aspect, near, far; 1: left, right, top, bottom, near, far); GRUP s16 default worker, bool; MODL model id, u32, light mask;
    /// SLGP 4 floats; TBGP s16, u16, u32, s16, u16, bool, bool, float, float, bool, float, 4 u16; PANE width, height, scale xyz;
    /// TXPN (PAL) dims xy, float3, font id, word wrap, horizontal, justification, v-justification, font colour, outline colour,
    /// extent xy floats, [version &gt;= 1: JP font id, JP extent xy ints]; LITE type, distance C/L/Q, angle C/L/Q, light id, [spot:
    /// cutoff]; ENRG texture id; METR bool, bool no round up, max capacity, worker count; IMGP 3 u32, coords (u32 count + float3),
    /// uvs (u32 count + float2).
    /// </summary>
    public sealed class MpFrame
    {
        public sealed class Widget
        {
            public string Type, Name, Parent;
            public bool Visible, Active, CullFaces;
            public float R = 1, G = 1, B = 1, A = 1;
            public uint DrawFlags;
            public int WorkerId = -1;
            public float[] Translation = new float[3];
            public float[] Orientation = new float[9];       // rows
            public uint ModelId = 0xFFFFFFFF, FontId = 0xFFFFFFFF, TextureId = 0xFFFFFFFF;
            public float[] Camera;                            // projection 0: fov, aspect, near, far
            public int Projection;
            public float PaneWidth, PaneHeight;
            public float[] TextDims;                          // dimX, dimY, extentX, extentY
            public float[] FontColor, OutlineColor;
            public int Justification, VJustification;
            public int MeterCapacity, MeterWorkers;
            public List<float[]> ImageCoords, ImageUvs;
            public readonly List<Widget> Children = new List<Widget>();
            public Widget ParentWidget;
        }

        public uint Version;
        public readonly List<Widget> Widgets = new List<Widget>();
        public readonly Dictionary<string, Widget> ByName = new Dictionary<string, Widget>();

        public static MpFrame Read(byte[] b)
        {
            var f = new MpFrame();
            var r = new MpReader(b);
            f.Version = r.U32();
            r.U32(); r.U32(); r.U32();
            int count = r.S32();
            for (int i = 0; i < count; i++)
            {
                var w = new Widget { Type = r.FourCC() };
                w.Name = r.CString();
                w.Parent = r.CString();
                r.Bool();
                w.Visible = r.Bool();
                w.Active = r.Bool();
                w.CullFaces = r.Bool();
                w.R = r.F32(); w.G = r.F32(); w.B = r.F32(); w.A = r.F32();
                w.DrawFlags = r.U32();
                switch (w.Type)
                {
                    case "CAMR":
                        w.Projection = r.S32();
                        w.Camera = w.Projection == 0 ? new[] { r.F32(), r.F32(), r.F32(), r.F32() } : new[] { r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32() };
                        break;
                    case "GRUP": r.S16(); r.Bool(); break;
                    case "MODL": w.ModelId = r.U32(); r.U32(); r.U32(); break;
                    case "SLGP": r.Skip(16); break;
                    case "TBGP":
                        r.S16(); r.U16(); r.U32(); r.S16(); r.U16(); r.Bool(); r.Bool(); r.F32(); r.F32(); r.Bool(); r.F32();
                        r.Skip(8);
                        break;
                    case "PANE": w.PaneWidth = r.F32(); w.PaneHeight = r.F32(); r.Skip(12); break;
                    case "TXPN":
                    {
                        float dx = r.F32(), dy = r.F32();
                        r.Skip(12);
                        w.FontId = r.U32();
                        r.Bool(); r.Bool();
                        w.Justification = r.S32();
                        w.VJustification = r.S32();
                        w.FontColor = new[] { r.F32(), r.F32(), r.F32(), r.F32() };
                        w.OutlineColor = new[] { r.F32(), r.F32(), r.F32(), r.F32() };
                        float ex = r.F32(), ey = r.F32();
                        w.TextDims = new[] { dx, dy, ex, ey };
                        if (f.Version >= 1) { r.U32(); r.S32(); r.S32(); }
                        break;
                    }
                    case "LITE":
                    {
                        int type = r.S32();
                        r.Skip(24);
                        r.S32();
                        if (type == 0) r.F32();      // spot: cutoff
                        break;
                    }
                    case "ENRG": w.TextureId = r.U32(); break;
                    case "METR": r.Bool(); r.Bool(); w.MeterCapacity = r.S32(); w.MeterWorkers = r.S32(); break;
                    case "IMGP":
                    {
                        w.TextureId = r.U32(); r.U32(); r.U32();
                        int n = r.S32();
                        w.ImageCoords = new List<float[]>();
                        for (int k = 0; k < n; k++) w.ImageCoords.Add(new[] { r.F32(), r.F32(), r.F32() });
                        n = r.S32();
                        w.ImageUvs = new List<float[]>();
                        for (int k = 0; k < n; k++) w.ImageUvs.Add(new[] { r.F32(), r.F32() });
                        break;
                    }
                }

                bool worker = r.Bool();
                if (worker) w.WorkerId = r.S16();
                for (int k = 0; k < 3; k++) w.Translation[k] = r.F32();
                for (int k = 0; k < 9; k++) w.Orientation[k] = r.F32();
                r.Skip(12);
                r.U32();
                r.S16();
                f.Widgets.Add(w);
                f.ByName[w.Name] = w;
                Widget p;
                if (f.ByName.TryGetValue(w.Parent, out p) && p != w)
                {
                    w.ParentWidget = p;
                    p.Children.Add(w);
                }
            }

            return f;
        }
    }
}
