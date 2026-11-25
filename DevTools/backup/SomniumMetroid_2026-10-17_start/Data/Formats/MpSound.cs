using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// A MusyX audio group (AGSC, decomp CAudioGrpSetLoc / musyx hw_pc_assets.c): two names, then four sized sections - pool
    /// (sound macros, tables / ADSR curves, keymaps, layers), project (groups; an SFX group holds the FX table), samples (the
    /// DSP-ADPCM data), sample directory (32-byte entries + ADPCM coefficients). Everything big-endian except the curve
    /// payloads (little-endian, as musyx reads them).
    /// </summary>
    public sealed class MpSoundGroup
    {
        public string BaseDir, Name;
        public byte[] Pool, Project, Samples, Sdir;
        public readonly Dictionary<int, int> Macros = new Dictionary<int, int>();     // id -> payload offset in Pool
        public readonly Dictionary<int, int> MacroSteps = new Dictionary<int, int>();
        public readonly Dictionary<int, int> Tables = new Dictionary<int, int>();
        public readonly Dictionary<int, int> TableBytes = new Dictionary<int, int>();
        public readonly Dictionary<int, int> Keymaps = new Dictionary<int, int>();
        public readonly Dictionary<int, int> Layers = new Dictionary<int, int>();
        public readonly Dictionary<int, MpSoundSample> SampleDir = new Dictionary<int, MpSoundSample>();
        public readonly Dictionary<int, MpSoundFx> Fx = new Dictionary<int, MpSoundFx>();
        public readonly List<int> GroupIds = new List<int>();

        public static MpSoundGroup Read(byte[] b)
        {
            var g = new MpSoundGroup();
            var r = new MpReader(b);
            g.BaseDir = r.CString();
            g.Name = r.CString();
            g.Pool = r.Bytes((int)r.U32());
            g.Project = r.Bytes((int)r.U32());
            g.Samples = r.Bytes((int)r.U32());
            g.Sdir = r.Bytes((int)r.U32());
            g.ReadPool();
            g.ReadProject();
            g.ReadSdir();
            return g;
        }

        private void ReadPool()
        {
            byte[] p = Pool;
            for (int kind = 0; kind < 4; kind++)
            {
                int start = (int)MpReader.U32At(p, kind * 4);
                if (start == 0) continue;
                int limit = p.Length;
                for (int i = 0; i < 4; i++)
                {
                    int s = (int)MpReader.U32At(p, i * 4);
                    if (s > start && s < limit) limit = s;
                }

                int o = start;
                for (int n = 0; n < 65536; n++)
                {
                    if (o + 2 > limit) break;
                    if (kind == 1 && MpReader.U16At(p, o) == 0xFFFF) break;
                    if (o + 4 > limit) break;
                    uint next = MpReader.U32At(p, o);
                    if (next == 0xFFFFFFFF || (kind == 1 && next == 0xFFFF)) break;
                    if (next < 8 || next > limit - o) break;
                    int id = MpReader.U16At(p, o + 4);
                    int payload = o + 8, len = (int)next - 8;
                    switch (kind)
                    {
                        case 0: Macros[id] = payload; MacroSteps[id] = len / 8; break;
                        case 1: Tables[id] = payload; TableBytes[id] = len; break;
                        case 2: Keymaps[id] = payload; break;
                        case 3: Layers[id] = payload; break;
                    }

                    o += (int)next;
                }
            }
        }

        private void ReadProject()
        {
            byte[] p = Project;
            int o = 0;
            for (int n = 0; n < 4096 && o + 32 <= p.Length; n++)
            {
                uint next = MpReader.U32At(p, o);
                if (next == 0xFFFFFFFF) break;
                int gid = MpReader.U16At(p, o + 4);
                int type = MpReader.U16At(p, o + 6);
                GroupIds.Add(gid);
                if (type == 1)
                {
                    int table = (int)MpReader.U32At(p, o + 28);
                    int count = MpReader.U16At(p, table);
                    for (int i = 0; i < count; i++)
                    {
                        int e = table + 4 + i * 10;
                        if (e + 10 > p.Length) break;
                        var fx = new MpSoundFx
                        {
                            Id = MpReader.U16At(p, e), Macro = MpReader.U16At(p, e + 2), MaxVoices = p[e + 4], Priority = p[e + 5],
                            Volume = p[e + 6], Pan = p[e + 7], Key = p[e + 8], VGroup = p[e + 9],
                        };
                        Fx[fx.Id] = fx;
                    }
                }

                if (next <= o) break;
                o = (int)next;
            }
        }

        private void ReadSdir()
        {
            byte[] d = Sdir;
            for (int i = 0; i < 65536 && i * 32 + 32 <= d.Length; i++)
            {
                int o = i * 32;
                int id = MpReader.U16At(d, o);
                if (id == 0xFFFF) break;
                uint info = MpReader.U32At(d, o + 12), length = MpReader.U32At(d, o + 16);
                var s = new MpSoundSample
                {
                    Id = id, Offset = (int)MpReader.U32At(d, o + 4), Rate = (int)(info & 0xFFFFFF), RootKey = (int)(info >> 24),
                    Length = (int)(length & 0xFFFFFF), Type = (int)(length >> 24),
                    LoopStart = (int)MpReader.U32At(d, o + 20), LoopLength = (int)MpReader.U32At(d, o + 24),
                };
                int extra = (int)MpReader.U32At(d, o + 28);
                if (s.Type != 2 && s.Type != 3 && extra > 0 && extra + 40 <= d.Length)
                {
                    s.InitialPs = d[extra + 2];
                    for (int c = 0; c < 16; c++) s.Coefs[c] = (short)MpReader.U16At(d, extra + 8 + c * 2);
                }

                SampleDir[id] = s;
            }
        }

        /// <summary>The sample as floats (-1..1), the whole length (loops are the player's job).</summary>
        public float[] Decode(MpSoundSample s)
        {
            var o = new float[Math.Max(1, s.Length)];
            switch (s.Type)
            {
                case 2:
                    for (int i = 0; i < s.Length && s.Offset + i * 2 + 1 < Samples.Length; i++) o[i] = (short)MpReader.U16At(Samples, s.Offset + i * 2) / 32768f;
                    break;
                case 3:
                    for (int i = 0; i < s.Length && s.Offset + i < Samples.Length; i++) o[i] = (sbyte)Samples[s.Offset + i] / 128f;
                    break;
                default:
                    MpDspAdpcm.Decode(Samples, s.Offset, s.Length, s.Coefs, o, 0);
                    break;
            }

            return o;
        }

        /// <summary>A step of a macro: the two words (musyx MSTEP para[0], para[1]).</summary>
        public bool Step(int macro, int step, out uint p0, out uint p1)
        {
            int off, steps;
            p0 = p1 = 0;
            if (!Macros.TryGetValue(macro, out off) || !MacroSteps.TryGetValue(macro, out steps) || step < 0 || step >= steps) return false;
            p0 = MpReader.U32At(Pool, off + step * 8);
            p1 = MpReader.U32At(Pool, off + step * 8 + 4);
            return true;
        }

        public static readonly string[] OpNames =
        {
            "End", "Stop", "IfKey", "IfVelocity", "Wait", "Loop", "Goto", "WaitMs", "PlayMacro", "SendKeyOff", "IfModulation", "PianoPan",
            "SetADSR", "ScaleVolume", "Panning", "Envelope", "StartSample", "StopSample", "KeyOff", "IfRandom", "FadeIn", "SurroundPan",
            "ADSRFromCtrl", "RandomKey", "AddKey", "SetKey", "LastKey", "Portamento", "Vibrato", "PitchSweep1", "PitchSweep2", "SetPitch",
            "SetPitchADSR", "ScaleVolumeDLS", "Mod2Vibrato", "Tremolo", "Return", "Gosub", "op26", "op27", "TrapEvent", "UntrapEvent",
            "SendMessage", "GetMessage", "GetVID",
        };

        public static string OpName(int op) { return op < OpNames.Length ? OpNames[op] : "op" + op.ToString("X2"); }
    }

    public sealed class MpSoundSample
    {
        public int Id, Offset, Rate, RootKey, Length, Type, LoopStart, LoopLength, InitialPs;
        public readonly short[] Coefs = new short[16];
        public bool Loops { get { return LoopLength > 0; } }
    }

    public struct MpSoundFx
    {
        public int Id, Macro, MaxVoices, Priority, Volume, Pan, Key, VGroup;
    }

    /// <summary>All the game's sound groups and the SFX translation table (ATBL "sound_lookup": game sound id -> MusyX FX id).</summary>
    public sealed class MpSoundBank
    {
        public readonly List<MpSoundGroup> Groups = new List<MpSoundGroup>();
        public readonly Dictionary<int, MpSoundGroup> FxGroup = new Dictionary<int, MpSoundGroup>();
        public short[] Lookup;
        public string Report = "";

        public static MpSoundBank Load(MpAssets assets)
        {
            var bank = new MpSoundBank();
            MpPak.Entry atbl = assets.Named("sound_lookup");
            if (atbl != null)
            {
                var r = new MpReader(atbl.Pak.Read(atbl));
                int n = (int)r.U32();
                bank.Lookup = new short[n];
                for (int i = 0; i < n; i++) bank.Lookup[i] = r.S16();
            }

            var seen = new HashSet<uint>();
            foreach (MpPak pak in assets.Paks)
                foreach (MpPak.Entry e in pak.Entries)
                {
                    if (e.Type != "AGSC" || !seen.Add(e.Id)) continue;
                    try
                    {
                        MpSoundGroup g = MpSoundGroup.Read(pak.Read(e));
                        bank.Groups.Add(g);
                        foreach (int fx in g.Fx.Keys) if (!bank.FxGroup.ContainsKey(fx)) bank.FxGroup[fx] = g;
                    }
                    catch (Exception ex) { MpLog.Warn("Sound: AGSC " + e.Id.ToString("X8") + " not read: " + ex.Message); }
                }

            bank.Report = bank.Groups.Count + " sound groups, " + bank.FxGroup.Count + " effects, lookup " + (bank.Lookup != null ? bank.Lookup.Length.ToString() : "missing");
            return bank;
        }

        /// <summary>The MusyX FX of a game sound id (CSfxManager::TranslateSFXID), or -1.</summary>
        public int Translate(int gameId)
        {
            if (Lookup == null || gameId < 0 || gameId >= Lookup.Length) return -1;
            return Lookup[gameId] < 0 ? -1 : Lookup[gameId];
        }
    }

    /// <summary>GameCube DSP-ADPCM (8-byte frames of 14 samples; musyx hw_pc_decode.c) and the .dsp stream header.</summary>
    public static class MpDspAdpcm
    {
        public static void Decode(byte[] data, int offset, int samples, short[] coefs, float[] dst, int dstOffset)
        {
            int h1 = 0, h2 = 0;
            for (int i = 0; i < samples; i++)
            {
                int frame = offset + (i / 14) * 8;
                if (frame + 8 > data.Length) break;
                int ps = data[frame];
                int within = i % 14;
                int b = data[frame + 1 + within / 2];
                int nib = within % 2 == 0 ? b >> 4 : b & 15;
                if (nib >= 8) nib -= 16;
                int pred = (ps >> 4) & 7;
                long sum = (long)nib * (1 << (ps & 15)) * 2048 + 1024;
                if (coefs != null) sum += (long)coefs[pred * 2] * h1 + (long)coefs[pred * 2 + 1] * h2;
                sum = sum < 0 ? -((-sum + 2047) / 2048) : sum / 2048;
                int v = sum < -32768 ? -32768 : sum > 32767 ? 32767 : (int)sum;
                h2 = h1;
                h1 = v;
                dst[dstOffset + i] = v / 32768f;
            }
        }

        public sealed class Stream
        {
            public int Samples, Rate;
            public bool Loop;
            public int LoopStart, LoopEnd;     // samples
            public readonly short[] Coefs = new short[16];
            public byte[] Data;
            public int DataOffset = 0x60;

            /// <summary>The header (decomp CDSPStreamManager: the loop start / end bytes rounded down to 32).</summary>
            public static Stream Read(byte[] b)
            {
                var s = new Stream { Data = b };
                s.Samples = (int)MpReader.U32At(b, 0);
                s.Rate = (int)MpReader.U32At(b, 8);
                s.Loop = MpReader.U16At(b, 12) != 0;
                uint ls = MpReader.U32At(b, 16), le = MpReader.U32At(b, 20);
                for (int i = 0; i < 16; i++) s.Coefs[i] = (short)MpReader.U16At(b, 28 + i * 2);
                if (s.Loop)
                {
                    int startByte = (int)((ls / 2) & 0x7FFFFFE0), endByte = (int)((le / 2) & 0x7FFFFFE0);
                    s.LoopStart = startByte / 8 * 14;
                    s.LoopEnd = Math.Min(s.Samples, endByte / 8 * 14);
                    if (s.LoopEnd <= s.LoopStart) s.LoopEnd = s.Samples;
                }
                else s.LoopEnd = s.Samples;

                return s;
            }

            public float[] DecodeAll()
            {
                var o = new float[Math.Max(1, Samples)];
                Decode(Data, DataOffset, Samples, Coefs, o, 0);
                return o;
            }
        }
    }
}
