using System;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    /// <summary>
    /// An ELSC description (CElectricDescription): every property CParticleElectricDataFactory::CreateELSM reads - lifetimes
    /// (LIFE system, SLIF per arc), generation rate (GRAT), arc count (SCNT) and segments (SSEG), the end points (IEMT / FEMT),
    /// the fractal amplitude (AMPL / AMPD), the three line layers (LWD1-3 widths, LCL1-3 colours), the swoosh colour (COLR), the
    /// optional children (SSWH swoosh along the arc, GPSM / EPSM systems at the arc's start / end) and ZERY (flatten Y).
    /// </summary>
    public sealed class MpElectricDesc
    {
        public uint Id;
        public IntEl LIFE, SLIF, SCNT, SSEG;
        public RealEl GRAT, AMPL, AMPD, LWD1, LWD2, LWD3;
        public ColorEl COLR, LCL1, LCL2, LCL3;
        public EmitterEl IEMT, FEMT;
        public uint SSWH = 0xFFFFFFFF, GPSM = 0xFFFFFFFF, EPSM = 0xFFFFFFFF;
        public bool ZERY;
        public bool Complete;
        public string Stop;

        /// <summary>CParticleElectricDataFactory::CreateGeneratorDescription / CreateELSM (read with a CRandom16(99) as the current random).</summary>
        public static MpElectricDesc Read(byte[] b, uint id)
        {
            var d = new MpElectricDesc { Id = id };
            var r = new MpReader(b);
            if (MpFourCC.Name(MpPartReader.ClassId(r)) != "ELSM") { d.Stop = "not ELSM"; return d; }
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = new MpRandom16(99);
            try
            {
                while (r.Remaining >= 4)
                {
                    string c = MpFourCC.Name(MpPartReader.ClassId(r));
                    switch (c)
                    {
                        case "LIFE": d.LIFE = MpPartReader.Int(r); break;
                        case "SLIF": d.SLIF = MpPartReader.Int(r); break;
                        case "GRAT": d.GRAT = MpPartReader.Real(r); break;
                        case "SCNT": d.SCNT = MpPartReader.Int(r); break;
                        case "SSEG": d.SSEG = MpPartReader.Int(r); break;
                        case "COLR": d.COLR = MpPartReader.Color(r); break;
                        case "IEMT": d.IEMT = MpPartReader.Emitter(r); break;
                        case "FEMT": d.FEMT = MpPartReader.Emitter(r); break;
                        case "AMPL": d.AMPL = MpPartReader.Real(r); break;
                        case "AMPD": d.AMPD = MpPartReader.Real(r); break;
                        case "LWD1": d.LWD1 = MpPartReader.Real(r); break;
                        case "LWD2": d.LWD2 = MpPartReader.Real(r); break;
                        case "LWD3": d.LWD3 = MpPartReader.Real(r); break;
                        case "LCL1": d.LCL1 = MpPartReader.Color(r); break;
                        case "LCL2": d.LCL2 = MpPartReader.Color(r); break;
                        case "LCL3": d.LCL3 = MpPartReader.Color(r); break;
                        case "SSWH": d.SSWH = MpPartReader.Asset(r); if (d.SSWH == 0) d.SSWH = 0xFFFFFFFF; break;
                        case "GPSM": d.GPSM = MpPartReader.Asset(r); if (d.GPSM == 0) d.GPSM = 0xFFFFFFFF; break;
                        case "EPSM": d.EPSM = MpPartReader.Asset(r); if (d.EPSM == 0) d.EPSM = 0xFFFFFFFF; break;
                        case "ZERY": d.ZERY = MpPartReader.Bool(r); break;
                        case "_END": d.Complete = true; return d;
                        default: d.Stop = c; return d;
                    }
                }
            }
            catch (Exception e) { d.Stop = "exception " + e.Message; }
            finally { MpRandom16.Current = prev; }

            return d;
        }

        /// <summary>Any of the three line layers (CParticleElectric's mHaveLWD).</summary>
        public bool HasLines { get { return LWD1 != null || LWD2 != null || LWD3 != null; } }
    }
}
