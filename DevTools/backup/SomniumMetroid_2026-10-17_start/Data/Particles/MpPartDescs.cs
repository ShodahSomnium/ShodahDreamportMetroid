using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data.Particles
{
    /// <summary>A PART (GPSM) description: CGenDescription, every property the game reads (CParticleDataFactory::CreateGPSM).</summary>
    public sealed class MpGenDesc
    {
        public uint Id;
        public RealEl PSTS, GRTE, SIZE, ROTA, LENG, WIDT, LINT, LFOR, LSLA;
        public IntEl PSLT, PSWT, MBSP, MAXP, LTME, SEED, NCSY, CSSD, NDSY, PISY, SISY, SSSD, SESD, LTYP, LFOT;
        public bool LIT_, ORNT, RSOP, AAPH, ZBUF, SORT, MBLR, VMD1, VMD2, VMD3, VMD4, CIND, PMAB, PMUS, PMOO, LINE, FXLL, OPTS;
        public EmitterEl EMTR;
        public ColorEl COLR, PMCL, LCLR;
        public VecEl POFS, PMOP, PMRT, PMSC, SSPO, SEPO, LOFF, LDIR;
        public ModVecEl VEL1, VEL2, VEL3, VEL4;
        public UVEl TEXR, TIND;
        public uint PMDL, ICTS, IDTS, IITS, SSWH, SELC;
        public RealEl[] ADV = new RealEl[8];
        public List<KeyValuePair<int, uint[]>> KSSM;   // frame -> spawned PART ids
        public int KSSMEnd;
        public bool Complete;                           // every property was understood
        public string Stop;

        public static MpGenDesc Read(byte[] b, uint id)
        {
            var d = new MpGenDesc { Id = id };
            var r = new MpReader(b);
            if (MpFourCC.Name(MpPartReader.ClassId(r)) != "GPSM") { d.Stop = "not GPSM"; return d; }
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = new MpRandom16(99);
            try
            {
                while (r.Remaining >= 4)
                {
                    string c = MpFourCC.Name(MpPartReader.ClassId(r));
                    switch (c)
                    {
                        case "PSIV": MpPartReader.Vec(r); break;
                        case "PSVM": MpPartReader.Mod(r); break;
                        case "PSOV": MpPartReader.Vec(r); break;
                        case "PSTS": d.PSTS = MpPartReader.Real(r); break;
                        case "PSLT": d.PSLT = MpPartReader.Int(r); break;
                        case "PSWT": d.PSWT = MpPartReader.Int(r); break;
                        case "LIT_": d.LIT_ = MpPartReader.Bool(r); break;
                        case "ORNT": d.ORNT = MpPartReader.Bool(r); break;
                        case "RSOP": d.RSOP = MpPartReader.Bool(r); break;
                        case "AAPH": d.AAPH = MpPartReader.Bool(r); break;
                        case "ZBUF": d.ZBUF = MpPartReader.Bool(r); break;
                        case "SORT": d.SORT = MpPartReader.Bool(r); break;
                        case "MBLR": d.MBLR = MpPartReader.Bool(r); break;
                        case "MBSP": d.MBSP = MpPartReader.Int(r); break;
                        case "MAXP": d.MAXP = MpPartReader.Int(r); break;
                        case "GRTE": d.GRTE = MpPartReader.Real(r); break;
                        case "ILOC": MpPartReader.Vec(r); break;
                        case "IVEC": MpPartReader.Vec(r); break;
                        case "EMTR": d.EMTR = MpPartReader.Emitter(r); break;
                        case "SIZE": d.SIZE = MpPartReader.Real(r); break;
                        case "COLR": d.COLR = MpPartReader.Color(r); break;
                        case "POFS": d.POFS = MpPartReader.Vec(r); break;
                        case "VMD1": d.VMD1 = MpPartReader.Bool(r); break;
                        case "VMD2": d.VMD2 = MpPartReader.Bool(r); break;
                        case "VMD3": d.VMD3 = MpPartReader.Bool(r); break;
                        case "VMD4": d.VMD4 = MpPartReader.Bool(r); break;
                        case "VEL1": d.VEL1 = MpPartReader.Mod(r); break;
                        case "VEL2": d.VEL2 = MpPartReader.Mod(r); break;
                        case "VEL3": d.VEL3 = MpPartReader.Mod(r); break;
                        case "VEL4": d.VEL4 = MpPartReader.Mod(r); break;
                        case "LTME": d.LTME = MpPartReader.Int(r); break;
                        case "ROTA": d.ROTA = MpPartReader.Real(r); break;
                        case "LENG": d.LENG = MpPartReader.Real(r); break;
                        case "WIDT": d.WIDT = MpPartReader.Real(r); break;
                        case "TEXR": d.TEXR = MpPartReader.Texture(r); break;
                        case "TIND": d.TIND = MpPartReader.Texture(r); break;
                        case "CIND": d.CIND = MpPartReader.Bool(r); break;
                        case "PMDL": d.PMDL = MpPartReader.Asset(r); break;
                        case "PMOP": d.PMOP = MpPartReader.Vec(r); break;
                        case "PMRT": d.PMRT = MpPartReader.Vec(r); break;
                        case "PMSC": d.PMSC = MpPartReader.Vec(r); break;
                        case "PMCL": d.PMCL = MpPartReader.Color(r); break;
                        case "PMAB": d.PMAB = MpPartReader.Bool(r); break;
                        case "PMUS": d.PMUS = MpPartReader.Bool(r); break;
                        case "PMOO": d.PMOO = MpPartReader.Bool(r); break;
                        case "SEED": d.SEED = MpPartReader.Int(r); break;
                        case "ICTS": d.ICTS = MpPartReader.Asset(r); break;
                        case "NCSY": d.NCSY = MpPartReader.Int(r); break;
                        case "CSSD": d.CSSD = MpPartReader.Int(r); break;
                        case "IDTS": d.IDTS = MpPartReader.Asset(r); break;
                        case "NDSY": d.NDSY = MpPartReader.Int(r); break;
                        case "IITS": d.IITS = MpPartReader.Asset(r); break;
                        case "PISY": d.PISY = MpPartReader.Int(r); break;
                        case "SISY": d.SISY = MpPartReader.Int(r); break;
                        case "SSWH": d.SSWH = MpPartReader.Asset(r); break;
                        case "SSSD": d.SSSD = MpPartReader.Int(r); break;
                        case "SSPO": d.SSPO = MpPartReader.Vec(r); break;
                        case "SELC": d.SELC = MpPartReader.Asset(r); break;
                        case "SESD": d.SESD = MpPartReader.Int(r); break;
                        case "SEPO": d.SEPO = MpPartReader.Vec(r); break;
                        case "KSSM":
                            if (MpFourCC.Name(MpPartReader.ClassId(r)) == "CNST") ReadKssm(r, d);
                            break;
                        case "LINE": d.LINE = MpPartReader.Bool(r); break;
                        case "FXLL": d.FXLL = MpPartReader.Bool(r); break;
                        case "LTYP": d.LTYP = MpPartReader.Int(r); break;
                        case "LCLR": d.LCLR = MpPartReader.Color(r); break;
                        case "LINT": d.LINT = MpPartReader.Real(r); break;
                        case "LOFF": d.LOFF = MpPartReader.Vec(r); break;
                        case "LDIR": d.LDIR = MpPartReader.Vec(r); break;
                        case "LFOT": d.LFOT = MpPartReader.Int(r); break;
                        case "LFOR": d.LFOR = MpPartReader.Real(r); break;
                        case "LSLA": d.LSLA = MpPartReader.Real(r); break;
                        case "OPTS": d.OPTS = MpPartReader.Bool(r); break;
                        case "ADV1": d.ADV[0] = MpPartReader.Real(r); break;
                        case "ADV2": d.ADV[1] = MpPartReader.Real(r); break;
                        case "ADV3": d.ADV[2] = MpPartReader.Real(r); break;
                        case "ADV4": d.ADV[3] = MpPartReader.Real(r); break;
                        case "ADV5": d.ADV[4] = MpPartReader.Real(r); break;
                        case "ADV6": d.ADV[5] = MpPartReader.Real(r); break;
                        case "ADV7": d.ADV[6] = MpPartReader.Real(r); break;
                        case "ADV8": d.ADV[7] = MpPartReader.Real(r); break;
                        case "_END": d.Complete = true; return d;
                        default: d.Stop = c; return d;
                    }
                }
            }
            catch (Exception e) { d.Stop = "exception " + e.Message; }
            finally { MpRandom16.Current = prev; }

            return d;
        }

        private static void ReadKssm(MpReader r, MpGenDesc d)
        {
            r.S32();
            r.S32();
            d.KSSMEnd = r.S32();
            r.S32();
            int frames = r.S32();
            d.KSSM = new List<KeyValuePair<int, uint[]>>();
            for (int i = 0; i < frames; i++)
            {
                int frame = r.S32();
                int n = r.S32();
                var ids = new uint[n];
                for (int j = 0; j < n; j++)
                {
                    // CSpawnSystemKeyframeInfo: id, flags, two unknown words
                    ids[j] = r.U32();
                    r.U32();
                    r.U32();
                    r.U32();
                }

                d.KSSM.Add(new KeyValuePair<int, uint[]>(frame, ids));
            }
        }
    }

    /// <summary>A SWHC description (CSwooshDescription).</summary>
    public sealed class MpSwooshDesc
    {
        public uint Id;
        public IntEl PSLT, LENG, SIDE, SPLN, TSPN;
        public RealEl TIME, LRAD, RRAD, IROT, ROTM;
        public bool LLRD, CROS, SROT, VLS1, VLS2, WIRE, AALP, ZBUF, ORNT, TEXW, CRND;
        public ColorEl COLR;
        public VecEl POFS, IVEL, NPOS;
        public ModVecEl VELM, VLM2;
        public UVEl TEXR;
        public bool Complete;
        public string Stop;

        public static MpSwooshDesc Read(byte[] b, uint id)
        {
            var d = new MpSwooshDesc { Id = id };
            var r = new MpReader(b);
            if (MpFourCC.Name(MpPartReader.ClassId(r)) != "SWSH") { d.Stop = "not SWSH"; return d; }
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = new MpRandom16(99);
            try
            {
                while (r.Remaining >= 4)
                {
                    string c = MpFourCC.Name(MpPartReader.ClassId(r));
                    switch (c)
                    {
                        case "PSLT": d.PSLT = MpPartReader.Int(r); break;
                        case "TIME": d.TIME = MpPartReader.Real(r); break;
                        case "LRAD": d.LRAD = MpPartReader.Real(r); break;
                        case "RRAD": d.RRAD = MpPartReader.Real(r); break;
                        case "LLRD": d.LLRD = MpPartReader.Bool(r); break;
                        case "LENG": d.LENG = MpPartReader.Int(r); break;
                        case "COLR": d.COLR = MpPartReader.Color(r); break;
                        case "SIDE": d.SIDE = MpPartReader.Int(r); break;
                        case "CROS": d.CROS = MpPartReader.Bool(r); break;
                        case "SROT": d.SROT = MpPartReader.Bool(r); break;
                        case "IROT": d.IROT = MpPartReader.Real(r); break;
                        case "ROTM": d.ROTM = MpPartReader.Real(r); break;
                        case "POFS": d.POFS = MpPartReader.Vec(r); break;
                        case "IVEL": d.IVEL = MpPartReader.Vec(r); break;
                        case "NPOS": d.NPOS = MpPartReader.Vec(r); break;
                        case "VELM": d.VELM = MpPartReader.Mod(r); break;
                        case "VLS1": d.VLS1 = MpPartReader.Bool(r); break;
                        case "VLM2": d.VLM2 = MpPartReader.Mod(r); break;
                        case "VLS2": d.VLS2 = MpPartReader.Bool(r); break;
                        case "SPLN": d.SPLN = MpPartReader.Int(r); break;
                        case "WIRE": d.WIRE = MpPartReader.Bool(r); break;
                        case "TEXR": d.TEXR = MpPartReader.Texture(r); break;
                        case "TSPN": d.TSPN = MpPartReader.Int(r); break;
                        case "AALP": d.AALP = MpPartReader.Bool(r); break;
                        case "ZBUF": d.ZBUF = MpPartReader.Bool(r); break;
                        case "ORNT": d.ORNT = MpPartReader.Bool(r); break;
                        case "TEXW": d.TEXW = MpPartReader.Bool(r); break;
                        case "CRND": d.CRND = MpPartReader.Bool(r); break;
                        case "_END": d.Complete = true; return d;
                        default: d.Stop = c; return d;
                    }
                }
            }
            catch (Exception e) { d.Stop = "exception " + e.Message; }
            finally { MpRandom16.Current = prev; }

            return d;
        }
    }

    /// <summary>A WPSC description (CWeaponDescription, CProjectileWeaponDataFactory::CreateWPSM; PAL keys included).</summary>
    public sealed class MpWeaponDescFull
    {
        public uint Id;
        public VecEl IORN, IVEC, PSOV, PSCL, POFS, OFST;
        public ModVecEl PSVM;
        public IntEl PSLT;
        public RealEl TRAT, RNGE, FOFF;
        public ColorEl PCOL;
        public bool VMD2, AP11, AP21, APSO, AS11, AS12, AS13, HOMG, EWTR, LWTR, SWTR, F60H, SVBD, NDTT, SPS1, SPS2, FC60;
        public uint APSM, APS2, ASW1, ASW2, ASW3, OHEF, COLR, PJFX;
        public bool Complete;
        public string Stop;

        public static MpWeaponDescFull Read(byte[] b, uint id)
        {
            var d = new MpWeaponDescFull { Id = id };
            var r = new MpReader(b);
            if (MpFourCC.Name(MpPartReader.ClassId(r)) != "WPSM") { d.Stop = "not WPSM"; return d; }
            MpRandom16 prev = MpRandom16.Current;
            MpRandom16.Current = new MpRandom16(99);
            try
            {
                while (r.Remaining >= 4)
                {
                    string c = MpFourCC.Name(MpPartReader.ClassId(r));
                    switch (c)
                    {
                        case "IORN": d.IORN = MpPartReader.Vec(r); break;
                        case "IVEC": d.IVEC = MpPartReader.Vec(r); break;
                        case "PSOV": d.PSOV = MpPartReader.Vec(r); break;
                        case "PSVM": d.PSVM = MpPartReader.Mod(r); break;
                        case "VMD2": d.VMD2 = MpPartReader.Bool(r); break;
                        case "PSLT": d.PSLT = MpPartReader.Int(r); break;
                        case "APSM": d.APSM = MpPartReader.Asset(r); break;
                        case "AP11": d.AP11 = MpPartReader.Bool(r); break;
                        case "APS2": d.APS2 = MpPartReader.Asset(r); break;
                        case "AP21": d.AP21 = MpPartReader.Bool(r); break;
                        case "APSO": d.APSO = MpPartReader.Bool(r); break;
                        case "ASW1": d.ASW1 = MpPartReader.Asset(r); break;
                        case "AS11": d.AS11 = MpPartReader.Bool(r); break;
                        case "ASW2": d.ASW2 = MpPartReader.Asset(r); break;
                        case "AS12": d.AS12 = MpPartReader.Bool(r); break;
                        case "ASW3": d.ASW3 = MpPartReader.Asset(r); break;
                        case "AS13": d.AS13 = MpPartReader.Bool(r); break;
                        case "OHEF": d.OHEF = MpPartReader.Asset(r); break;
                        case "PSCL": d.PSCL = MpPartReader.Vec(r); break;
                        case "PCOL": d.PCOL = MpPartReader.Color(r); break;
                        case "COLR": d.COLR = MpPartReader.Asset(r); break;
                        case "POFS": d.POFS = MpPartReader.Vec(r); break;
                        case "OFST": d.OFST = MpPartReader.Vec(r); break;
                        case "HOMG": d.HOMG = MpPartReader.Bool(r); break;
                        case "TRAT": d.TRAT = MpPartReader.Real(r); break;
                        case "PJFX": d.PJFX = MpPartReader.Asset(r); break;
                        case "RNGE": d.RNGE = MpPartReader.Real(r); break;
                        case "FOFF": d.FOFF = MpPartReader.Real(r); break;
                        case "EWTR": d.EWTR = MpPartReader.Bool(r); break;
                        case "LWTR": d.LWTR = MpPartReader.Bool(r); break;
                        case "SWTR": d.SWTR = MpPartReader.Bool(r); break;
                        case "F60H": d.F60H = MpPartReader.Bool(r); break;
                        case "SVBD": d.SVBD = MpPartReader.Bool(r); break;
                        case "NDTT": d.NDTT = MpPartReader.Bool(r); break;
                        case "SPS1": d.SPS1 = MpPartReader.Bool(r); break;
                        case "SPS2": d.SPS2 = MpPartReader.Bool(r); break;
                        case "FC60": d.FC60 = MpPartReader.Bool(r); break;
                        case "_END": d.Complete = true; return d;
                        default: d.Stop = c; return d;
                    }
                }
            }
            catch (Exception e) { d.Stop = "exception " + e.Message; }
            finally { MpRandom16.Current = prev; }

            return d;
        }
    }

    /// <summary>A CRSC collision response (CCollisionResponseData): a PART per response type (world material or enemy kind), sounds, decals.</summary>
    public sealed class MpCollisionResponse
    {
        public uint Id;
        public readonly uint[] Particles = new uint[Types.Length];
        public readonly int[] Sounds = new int[SoundTypes.Length];
        public readonly uint[] Decals = new uint[DecalTypes.Length];
        public float AudibleRange = 50f, AudibleFallOff = 0.2f;
        public bool Complete;
        public string Stop;

        /// <summary>The response types in order (EWeaponCollisionResponseTypes; PAL list with MPR entries).</summary>
        public static readonly string[] Types =
        {
            "NODP", "DEFS", "CRTS", "MTLS", "GRAS", "ICEE", "GOOO", "WODS", "WATR", "1MUD", "1LAV",
            "1SAN", "1PRJ", "DCHR", "DCHS", "DCSH", "DENM", "DESP", "DESH", "BTLE", "WASP", "TALP",
            "PTGM", "SPIR", "FPIR", "FFLE", "PARA", "BMON", "BFLR", "PBOS", "IBOS", "1SVA", "1RPR",
            "1MTR", "1PDS", "1FLB", "1DRN", "1MRE", "CHOZ", "JZAP", "1ISE", "1BSE", "1ATB", "1ATA", "1MPR",
            "BTSP", "WWSP", "TASP", "TGSP", "SPSP", "FPSP", "FFSP", "PSSP", "BMSP", "BFSP", "PBSP",
            "IBSP", "2SVA", "2RPR", "2MTR", "2PDS", "2FLB", "2DRN", "2MRE", "CHSP", "JZSP", "3ISE",
            "3BSE", "3ATB", "3ATA", "3MPR",
            "BTSH", "WWSH", "TASH", "TGSH", "SPSH", "FPSH", "FFSH", "PSSH",
            "BMSH", "BFSH", "PBSH", "IBSH", "3SVA", "3RPR", "3MTR", "3PDS", "3FLB", "3DRN", "3MRE",
            "CHSH", "JZSH", "5ISE", "5BSE", "5ATB", "5ATA", "5MPR",
        };

        public static readonly string[] SoundTypes =
        {
            "NSFX", "DSFX", "CSFX", "MSFX", "GRFX", "ICFX", "GOFX", "WSFX", "WTFX", "2MUD", "2LAV",
            "2SAN", "2PRJ", "DCFX", "DSFX", "DSHX", "DEFX", "ESFX", "SHFX", "BEFX", "WWFX", "TAFX",
            "GTFX", "SPFX", "FPFX", "FFFX", "PAFX", "BMFX", "BFFX", "PBFX", "IBFX", "4SVA", "4RPR",
            "4MTR", "4PDS", "4FLB", "4DRN", "4MRE", "CZFX", "JZAS", "2ISE", "2BSE", "2ATB", "2ATA", "2MPR",
            "BSFX", "WSFX", "TSFX", "GSFX", "SSFX", "FSFX", "SFFX", "PSFX", "MSFX", "SBFX", "PBSX",
            "IBSX", "5SVA", "5RPR", "5MTR", "5PDS", "5FLB", "5DRN", "5MRE", "CSFX", "JZPS", "4ISE",
            "4BSE", "4ATB", "4ATA", "4MPR",
            "BHFX", "WHFX", "THFX", "GHFX", "SHFX", "FHFX", "HFFX", "PHFX",
            "MHFX", "HBFX", "PBHX", "IBHX", "6SVA", "6RPR", "6MTR", "6PDS", "6FLB", "6DRN", "6MRE",
            "CHFX", "JZHS", "6ISE", "6BSE", "6ATB", "6ATA", "6MPR",
        };

        public static readonly string[] DecalTypes = { "NDCL", "DDCL", "CODL", "MEDL", "GRDL", "ICDL", "GODL", "WODL", "WTDL", "3MUD", "3LAV", "3SAN", "CHDL", "ENDL" };

        public const int Default = 1, Metal = 3, Grass = 4, Ice = 5, Goo = 6, Wood = 7, Water = 8, Mud = 9, Lava = 10, Sand = 11;

        public static MpCollisionResponse Read(byte[] b, uint id)
        {
            var d = new MpCollisionResponse { Id = id };
            for (int i = 0; i < d.Sounds.Length; i++) d.Sounds[i] = -1;
            var r = new MpReader(b);
            if (MpFourCC.Name(MpPartReader.ClassId(r)) != "CRSM") { d.Stop = "not CRSM"; return d; }
            try
            {
                while (r.Remaining >= 4)
                {
                    string c = MpFourCC.Name(MpPartReader.ClassId(r));
                    int i = Array.IndexOf(Types, c);
                    if (i >= 0) { d.Particles[i] = MpPartReader.Asset(r); continue; }
                    i = Array.IndexOf(SoundTypes, c);
                    if (i >= 0) { if (MpFourCC.Name(MpPartReader.ClassId(r)) != "NONE") d.Sounds[i] = r.S32(); continue; }
                    i = Array.IndexOf(DecalTypes, c);
                    if (i >= 0) { d.Decals[i] = MpPartReader.Asset(r); continue; }
                    if (c == "RNGE") { MpPartReader.ClassId(r); d.AudibleRange = r.F32(); continue; }
                    if (c == "FOFF") { MpPartReader.ClassId(r); d.AudibleFallOff = r.F32(); continue; }
                    if (c == "_END") { d.Complete = true; return d; }
                    d.Stop = c;
                    return d;
                }
            }
            catch (Exception e) { d.Stop = "exception " + e.Message; }
            return d;
        }

        /// <summary>The PART for a response type, falling back to Default (GetParticleDescription for world materials).</summary>
        public uint ParticleFor(int type)
        {
            if (type >= 0 && type < Particles.Length && MpAssets.Valid(Particles[type])) return Particles[type];
            return Particles[Default];
        }

        public int SoundFor(int type)
        {
            if (type >= 0 && type < Sounds.Length && Sounds[type] >= 0) return Sounds[type];
            return Sounds[Default];
        }
    }

    /// <summary>
    /// Loads and keeps particle descriptions by asset id (thread safe: the loader thread prepares, the main thread asks). Children
    /// are not loaded recursively here; <see cref="Prefetch"/> walks a description's references to load everything it can spawn.
    /// </summary>
    public sealed class MpPartLibrary
    {
        private readonly MpAssets _assets;
        private readonly Dictionary<uint, MpGenDesc> _parts = new Dictionary<uint, MpGenDesc>();
        private readonly Dictionary<uint, MpSwooshDesc> _swooshes = new Dictionary<uint, MpSwooshDesc>();
        private readonly Dictionary<uint, MpWeaponDescFull> _weapons = new Dictionary<uint, MpWeaponDescFull>();
        private readonly Dictionary<uint, MpCollisionResponse> _responses = new Dictionary<uint, MpCollisionResponse>();
        public readonly HashSet<uint> Textures = new HashSet<uint>();
        public readonly HashSet<uint> Models = new HashSet<uint>();
        public readonly List<string> Problems = new List<string>();

        public MpPartLibrary(MpAssets assets) { _assets = assets; }

        private byte[] Bytes(uint id, string type)
        {
            if (!MpAssets.Valid(id) || _assets.TypeOf(id) != type) return null;
            try { return _assets.Read(id); }
            catch (Exception) { return null; }
        }

        public MpGenDesc Part(uint id)
        {
            if (!MpAssets.Valid(id)) return null;
            lock (_parts)
            {
                MpGenDesc d;
                if (_parts.TryGetValue(id, out d)) return d;
                byte[] b = Bytes(id, "PART");
                d = b != null ? MpGenDesc.Read(b, id) : null;
                if (d != null && !d.Complete) Problems.Add("PART " + id.ToString("X8") + " stopped at " + d.Stop);
                _parts[id] = d;
                return d;
            }
        }

        public MpSwooshDesc Swoosh(uint id)
        {
            if (!MpAssets.Valid(id)) return null;
            lock (_swooshes)
            {
                MpSwooshDesc d;
                if (_swooshes.TryGetValue(id, out d)) return d;
                byte[] b = Bytes(id, "SWHC");
                d = b != null ? MpSwooshDesc.Read(b, id) : null;
                if (d != null && !d.Complete) Problems.Add("SWHC " + id.ToString("X8") + " stopped at " + d.Stop);
                _swooshes[id] = d;
                return d;
            }
        }

        public MpWeaponDescFull Weapon(uint id)
        {
            if (!MpAssets.Valid(id)) return null;
            lock (_weapons)
            {
                MpWeaponDescFull d;
                if (_weapons.TryGetValue(id, out d)) return d;
                byte[] b = Bytes(id, "WPSC");
                d = b != null ? MpWeaponDescFull.Read(b, id) : null;
                if (d != null && !d.Complete) Problems.Add("WPSC " + id.ToString("X8") + " stopped at " + d.Stop);
                _weapons[id] = d;
                return d;
            }
        }

        public MpCollisionResponse Response(uint id)
        {
            if (!MpAssets.Valid(id)) return null;
            lock (_responses)
            {
                MpCollisionResponse d;
                if (_responses.TryGetValue(id, out d)) return d;
                byte[] b = Bytes(id, "CRSC");
                d = b != null ? MpCollisionResponse.Read(b, id) : null;
                if (d != null && !d.Complete) Problems.Add("CRSC " + id.ToString("X8") + " stopped at " + d.Stop);
                _responses[id] = d;
                return d;
            }
        }

        /// <summary>Loads a PART and everything it can spawn (children, swooshes), collecting the textures and models they use.</summary>
        public void Prefetch(uint part)
        {
            var seen = new HashSet<uint>();
            var todo = new Stack<uint>();
            todo.Push(part);
            while (todo.Count > 0)
            {
                uint id = todo.Pop();
                if (!MpAssets.Valid(id) || !seen.Add(id)) continue;
                MpGenDesc d = Part(id);
                if (d == null) continue;
                if (d.TEXR != null && MpAssets.Valid(d.TEXR.Texture)) Textures.Add(d.TEXR.Texture);
                if (d.TIND != null && MpAssets.Valid(d.TIND.Texture)) Textures.Add(d.TIND.Texture);
                if (MpAssets.Valid(d.PMDL)) Models.Add(d.PMDL);
                todo.Push(d.ICTS); todo.Push(d.IDTS); todo.Push(d.IITS);
                if (d.KSSM != null) foreach (var kv in d.KSSM) foreach (uint k in kv.Value) todo.Push(k);
                if (MpAssets.Valid(d.SSWH))
                {
                    MpSwooshDesc s = Swoosh(d.SSWH);
                    if (s != null && s.TEXR != null && MpAssets.Valid(s.TEXR.Texture)) Textures.Add(s.TEXR.Texture);
                }
            }
        }

        public void PrefetchSwoosh(uint id)
        {
            MpSwooshDesc s = Swoosh(id);
            if (s != null && s.TEXR != null && MpAssets.Valid(s.TEXR.Texture)) Textures.Add(s.TEXR.Texture);
        }

        public MpGenDesc GetPart(uint id) { lock (_parts) { MpGenDesc d; return _parts.TryGetValue(id, out d) ? d : null; } }
        public MpSwooshDesc GetSwoosh(uint id) { lock (_swooshes) { MpSwooshDesc d; return _swooshes.TryGetValue(id, out d) ? d : null; } }
    }
}
