using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    /// <summary>
    /// CParticleDataFactory: the element readers, FourCC by FourCC in the game's order. Unknown classes give null (the game's
    /// default), so a description with something unread stops cleanly. Loads run with a CRandom16(99) as the current random, as
    /// CreateGPSM does (constant vectors with "initial random" parts are evaluated once here).
    /// </summary>
    public static class MpPartReader
    {
        private static uint Id(MpReader r) { return r.U32(); }
        public static uint ClassId(MpReader r) { return r.U32(); }

        private static readonly uint CNST = MpFourCC.Of("CNST"), NONE = MpFourCC.Of("NONE"), KEYE = MpFourCC.Of("KEYE"), KEYP = MpFourCC.Of("KEYP");

        public static bool Bool(MpReader r) { ClassId(r); return r.Bool(); }

        private static IntEl ZeroI() { return new IECnst { V = 0 }; }
        private static RealEl ZeroR() { return new RECnst { V = 0 }; }
        private static VecEl ZeroV() { return new VEFast { V = V3.Zero }; }

        private static IntEl I(MpReader r) { return Int(r) ?? ZeroI(); }
        private static RealEl R(MpReader r) { return Real(r) ?? ZeroR(); }
        private static VecEl V(MpReader r) { return Vec(r) ?? ZeroV(); }

        private static void Keys(MpReader r, out int percent, out bool loop, out int loopEnd, out int loopStart)
        {
            percent = r.S32();
            r.S32();
            loop = r.Bool();
            r.Bool();
            loopEnd = r.S32();
            loopStart = r.S32();
        }

        public static IntEl Int(MpReader r)
        {
            string c = MpFourCC.Name(ClassId(r));
            switch (c)
            {
                case "CNST": return new IECnst { V = r.S32() };
                case "KEYE":
                case "KEYP":
                {
                    var k = new IEKey(); Keys(r, out k.Percent, out k.Loop, out k.LoopEnd, out k.LoopStart);
                    int n = r.S32(); k.Keys = new int[n]; for (int i = 0; i < n; i++) k.Keys[i] = r.S32(); return k;
                }
                case "TSCL": return new IETscl { A = R(r) };
                case "DETH": return new IEDeath { A = I(r), B = I(r) };
                case "CHAN": return new IEChain { A = I(r), B = I(r), S = I(r) };
                case "ADD_": return new IEAdd { A = I(r), B = I(r) };
                case "MULT": return new IEMul { A = I(r), B = I(r) };
                case "MODU": return new IEMod { A = I(r), B = I(r) };
                case "RAND": return new IERand { A = I(r), B = I(r) };
                case "IMPL": return new IEImpulse { A = I(r) };
                case "ILPT": return new IELtPercent { A = I(r) };
                case "SPAH": { IntEl a = I(r), b = I(r), cc = I(r); return new IESampleHold { Source = cc, Min = a, Max = b }; }
                case "IRND": return new IEInitRand { A = I(r), B = I(r) };
                case "CLMP": return new IEClamp { Min = I(r), Max = I(r), Val = I(r) };
                case "PULS": return new IEPulse { Ad = I(r), Bd = I(r), A = I(r), B = I(r) };
                case "RTOI": return new IERealToInt { A = R(r), B = R(r) };
                case "SUB_": return new IESub { A = I(r), B = I(r) };
                case "GTCP": return new IECumulative();
                case "GAPC": return new IEActive();
                case "GEMT": return new IEEmitterTime();
                default: return null;
            }
        }

        public static RealEl Real(MpReader r)
        {
            string c = MpFourCC.Name(ClassId(r));
            switch (c)
            {
                case "CNST": return new RECnst { V = r.F32() };
                case "KEYE":
                case "KEYP":
                {
                    var k = new REKey(); Keys(r, out k.Percent, out k.Loop, out k.LoopEnd, out k.LoopStart);
                    int n = r.S32(); k.Keys = new float[n]; for (int i = 0; i < n; i++) k.Keys[i] = r.F32(); return k;
                }
                case "SCAL": return new RETscl { A = R(r) };
                case "SINE": { RealEl a = R(r), b = R(r), cc = R(r); return new RESine { Phase = cc, Freq = a, Amp = b }; }
                case "ADD_": return new REAdd { A = R(r), B = R(r) };
                case "MULT": return new REMul { A = R(r), B = R(r) };
                case "DOTP": return new REDot { A = V(r), B = V(r) };
                case "RAND": return new RERand { Min = R(r), Max = R(r) };
                case "IRND": return new REInitRand { Min = R(r), Max = R(r) };
                case "CHAN": return new REChain { A = R(r), B = R(r), S = I(r) };
                case "CLMP": return new REClamp { Min = R(r), Max = R(r), Val = R(r) };
                case "PULS": return new REPulse { Ad = I(r), Bd = I(r), A = R(r), B = R(r) };
                case "RLPT": return new RELtPercent { A = R(r) };
                case "LFTW": return new RELtTween { A = R(r), B = R(r) };
                case "PRLW": return new REPartRota();
                case "PSLL": return new REPartSize();
                case "PAP1": return new REAccess { I = 0 };
                case "PAP2": return new REAccess { I = 1 };
                case "PAP3": return new REAccess { I = 2 };
                case "PAP4": return new REAccess { I = 3 };
                case "PAP5": return new REAccess { I = 4 };
                case "PAP6": return new REAccess { I = 5 };
                case "PAP7": return new REAccess { I = 6 };
                case "PAP8": return new REAccess { I = 7 };
                case "VXTR": return new REVecComp { A = V(r), C = 0 };
                case "VYTR": return new REVecComp { A = V(r), C = 1 };
                case "VZTR": return new REVecComp { A = V(r), C = 2 };
                case "VMAG": return new REVecComp { A = V(r), C = 3 };
                case "ISWT": return new REInitSwitch { A = R(r), B = R(r) };
                case "CLTN": return new RECompLess { A = R(r), B = R(r), C = R(r), D = R(r) };
                case "CEQL": return new RECompEq { A = R(r), B = R(r), C = R(r), D = R(r) };
                case "CRNG": return new RERange { Val = R(r), Min = R(r), Max = R(r), In = R(r), Out = R(r) };
                case "CEXT": return new REExternal { A = I(r) };
                case "ITRL": return new REIntTimesReal { A = I(r), B = R(r) };
                case "SUB_": return new RESub { A = R(r), B = R(r) };
                case "GTCR": return new REColorComp { A = Color(r), C = 0 };
                case "GTCG": return new REColorComp { A = Color(r), C = 1 };
                case "GTCB": return new REColorComp { A = Color(r), C = 2 };
                case "GTCA": return new REColorComp { A = Color(r), C = 3 };
                default: return null;
            }
        }

        public static VecEl Vec(MpReader r)
        {
            string c = MpFourCC.Name(ClassId(r));
            switch (c)
            {
                case "CNST":
                {
                    RealEl x = Real(r), y = Real(r), z = Real(r);
                    if (x != null && y != null && z != null && x.IsConstant && y.IsConstant && z.IsConstant)
                    {
                        float xf = 0, yf = 0, zf = 0; x.Get(0, ref xf); y.Get(0, ref yf); z.Get(0, ref zf);
                        return new VEFast { V = new V3(xf, yf, zf) };
                    }

                    return new VECnst { X = x ?? ZeroR(), Y = y ?? ZeroR(), Z = z ?? ZeroR() };
                }
                case "KEYE":
                case "KEYP":
                {
                    var k = new VEKey(); Keys(r, out k.Percent, out k.Loop, out k.LoopEnd, out k.LoopStart);
                    int n = r.S32(); k.Keys = new V3[n]; for (int i = 0; i < n; i++) k.Keys[i] = new V3(r.F32(), r.F32(), r.F32()); return k;
                }
                case "ANGC": return new VEAngleCone { XC = R(r), YC = R(r), XR = R(r), YR = R(r), Mag = R(r) };
                case "CONE": { var e = new VECone { Dir = V(r), Mag = R(r) }; e.Init(); return e; }
                case "CIRC": { var e = new VECircle { Offset = V(r), Normal = V(r), AngleC = R(r), AngleL = R(r), Radius = R(r) }; e.Init(); return e; }
                case "CCLU":
                {
                    VecEl off = V(r), nrm = V(r); IntEl cyc = I(r); RealEl rf = R(r);
                    var e = new VECluster { Offset = off, RandomFactor = rf }; e.Init(nrm, cyc); return e;
                }
                case "ADD_": return new VEAdd { A = V(r), B = V(r) };
                case "MULT": return new VEMul { A = V(r), B = V(r) };
                case "CHAN": return new VEChain { A = V(r), B = V(r), S = I(r) };
                case "PULS": return new VEPulse { Ad = I(r), Bd = I(r), A = V(r), B = V(r) };
                case "RTOV": return new VERealToVec { A = R(r) };
                case "PLOC": return new VEPartLoc();
                case "PLCO": return new VEPartPrev();
                case "PVEL": return new VEPartVel();
                case "PSOF": return new VESysOrient { Axis = 0 };
                case "PSOU": return new VESysOrient { Axis = 1 };
                case "PSOR": return new VESysOrient { Axis = 2 };
                case "PSTR": return new VESysTrans();
                case "SUB_": return new VESub { A = V(r), B = V(r) };
                case "CTVC": return new VEColorToVec { A = Color(r) };
                default: return null;
            }
        }

        public static EmitterEl Emitter(MpReader r)
        {
            string c = MpFourCC.Name(ClassId(r));
            switch (c)
            {
                case "SETR":
                {
                    uint prop = ClassId(r);
                    if (prop != MpFourCC.Of("ILOC")) return null;
                    VecEl pos = Vec(r);
                    prop = ClassId(r);
                    if (prop != MpFourCC.Of("IVEC")) return null;
                    VecEl vel = Vec(r);
                    return new EESimple { Pos = pos ?? ZeroV(), Vel = vel };
                }
                case "SEMR": { VecEl pos = Vec(r), vel = Vec(r); return new EESimple { Pos = pos ?? ZeroV(), Vel = vel }; }
                case "SPHE": return new EESphere { Origin = V(r), Radius = R(r), VelMag = R(r) };
                case "ASPH": return new EEAngleSphere { Origin = V(r), XB = R(r), YB = R(r), XR = R(r), YR = R(r), Radius = R(r), VelMag = R(r) };
                default: return null;
            }
        }

        public static ModVecEl Mod(MpReader r)
        {
            string c = MpFourCC.Name(ClassId(r));
            switch (c)
            {
                case "CNST":
                {
                    RealEl a = Real(r), b = Real(r), cc = Real(r);
                    if (a != null && b != null && cc != null && a.IsConstant && b.IsConstant && cc.IsConstant)
                    {
                        // the game's own bug: all three components are taken from the first element
                        float af = 0, bf = 0, cf = 0; a.Get(0, ref af); a.Get(0, ref bf); a.Get(0, ref cf);
                        return new MVEFast { V = new V3(af, bf, cf) };
                    }

                    return new MVECnst { X = a ?? ZeroR(), Y = b ?? ZeroR(), Z = cc ?? ZeroR() };
                }
                case "GRAV": return new MVEGrav { A = V(r) };
                case "WIND": return new MVEWind { Vel = V(r), Factor = R(r) };
                case "EXPL": return new MVEExplode { A = R(r), B = R(r) };
                case "CHAN": return new MVEChain { A = Mod(r), B = Mod(r), S = I(r) };
                case "PULS": return new MVEPulse { Ad = I(r), Bd = I(r), A = Mod(r), B = Mod(r) };
                case "IMPL": return new MVEImplosion { Point = V(r), MagScale = R(r), MaxMag = R(r), MinMag = R(r), EnableMin = Bool(r), Kind = 0 };
                case "LMPL": return new MVEImplosion { Point = V(r), MagScale = R(r), MaxMag = R(r), MinMag = R(r), EnableMin = Bool(r), Kind = 1 };
                case "EMPL": return new MVEImplosion { Point = V(r), MagScale = R(r), MaxMag = R(r), MinMag = R(r), EnableMin = Bool(r), Kind = 2 };
                case "SWRL": return new MVESwirl { Helix = V(r), Binormal = V(r), Gain = R(r), Tangential = R(r) };
                case "BNCE": { var e = new MVEBounce { Point = V(r), Normal = V(r), Friction = R(r), Restitution = R(r), Die = Bool(r) }; e.Init(); return e; }
                case "SPOS": return new MVESetPos { A = V(r) };
                default: return null;
            }
        }

        public static ColorEl Color(MpReader r)
        {
            string c = MpFourCC.Name(ClassId(r));
            switch (c)
            {
                case "CNST":
                {
                    RealEl rr = Real(r), g = Real(r), b = Real(r), a = Real(r);
                    if (rr != null && g != null && b != null && a != null && rr.IsConstant && g.IsConstant && b.IsConstant && a.IsConstant)
                    {
                        float rf = 0, gf = 0, bf = 0, af = 0; rr.Get(0, ref rf); g.Get(0, ref gf); b.Get(0, ref bf); a.Get(0, ref af);
                        return new CEFast { V = new C4(Col.Sat(rf), Col.Sat(gf), Col.Sat(bf), Col.Sat(af)) };
                    }

                    return new CECnst { R = rr ?? ZeroR(), G = g ?? ZeroR(), B = b ?? ZeroR(), A = a ?? ZeroR() };
                }
                case "KEYE":
                case "KEYP":
                {
                    var k = new CEKey(); Keys(r, out k.Percent, out k.Loop, out k.LoopEnd, out k.LoopStart);
                    if (k.LoopStart >= k.LoopEnd) k.LoopStart = 0;
                    int n = r.S32(); k.Keys = new C4[n]; for (int i = 0; i < n; i++) k.Keys[i] = new C4(r.F32(), r.F32(), r.F32(), r.F32()); return k;
                }
                case "FADE": return new CEFade { A = Color(r), B = Color(r), End = R(r) };
                case "CFDE": return new CEFadeEnd { A = Color(r), B = Color(r), Start = R(r), End = R(r) };
                case "CHAN": return new CEChain { A = Color(r), B = Color(r), S = I(r) };
                case "PULS": return new CEPulse { Ad = I(r), Bd = I(r), A = Color(r), B = Color(r) };
                case "PCOL": return new CEPartColor();
                default: return null;
            }
        }

        public static UVEl Texture(MpReader r)
        {
            string c = MpFourCC.Name(ClassId(r));
            switch (c)
            {
                case "CNST":
                {
                    uint id = 0;
                    if (ClassId(r) != NONE) id = r.U32();
                    return new UVEl { Texture = id };
                }
                case "ATEX":
                {
                    uint id = 0;
                    if (ClassId(r) != NONE) id = r.U32();
                    var e = new UVEl { Texture = id, Animated = true, TileW = I(r), TileH = I(r), StrideW = I(r), StrideH = I(r), CycleFrames = I(r) };
                    e.Loop = Bool(r);
                    return e;
                }
                default: return null;
            }
        }

        /// <summary>An asset reference ('NONE' or CNST + id); 0 / none = no asset.</summary>
        public static uint Asset(MpReader r)
        {
            if (ClassId(r) == NONE) return 0;
            return r.U32();
        }
    }
}
