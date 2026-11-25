using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// One ANCS character ready to animate (decomp CAnimRes / CCharacterInfo / CAnimationManager): its skinned model (CMDL + CSKR over
    /// the CINF rig), its animation set, and every ANIM it can play decoded once. Prepared on the loader thread (<see cref="Load"/>,
    /// cached per ANCS and character); <see cref="Upload"/> on the main thread builds the meshes and materials. Animation numbers are
    /// the character's own (the script objects' and the game code's "anim index": CCharacterInfo maps them to set entries).
    /// </summary>
    public sealed class MpCharacter
    {
        public uint AncsId;
        public int CharIndex;
        public MpAncs Ancs;
        public MpAncs.Character Info;
        public MpAnimSet Set;
        public MpModel Model;
        public MpRig Rig;
        public readonly Dictionary<uint, MpAnimation> Anims = new Dictionary<uint, MpAnimation>();
        public readonly Dictionary<uint, MpAnimEvents> Events = new Dictionary<uint, MpAnimEvents>();
        public readonly Dictionary<string, int> ByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int AnimCount;
        public string Report = "";

        private static readonly Dictionary<ulong, MpCharacter> Cache = new Dictionary<ulong, MpCharacter>();
        private static readonly object Lock = new object();

        public static MpCharacter Get(uint ancs, int character)
        {
            lock (Lock)
            {
                MpCharacter c;
                return Cache.TryGetValue(((ulong)ancs << 8) | (uint)(character & 0xFF), out c) ? c : null;
            }
        }

        /// <summary>Loader thread: the character with its model and animations (parsed once; null when the ANCS or its model cannot be read).</summary>
        public static MpCharacter Load(MpAssets assets, uint ancsId, int character)
        {
            if (!MpAssets.Valid(ancsId)) return null;
            ulong key = ((ulong)ancsId << 8) | (uint)(character & 0xFF);
            lock (Lock)
            {
                MpCharacter c;
                if (Cache.TryGetValue(key, out c)) return c;
            }

            MpCharacter ch = null;
            try { ch = Read(assets, ancsId, character); }
            catch (Exception e) { MpLog.Warn("Characters: ANCS " + ancsId.ToString("X8") + " #" + character + " not read: " + e.Message); }
            lock (Lock) Cache[key] = ch;
            return ch;
        }

        private static MpCharacter Read(MpAssets assets, uint ancsId, int character)
        {
            MpAncs ancs = MpModels.Ancs(assets, ancsId);
            if (ancs == null || ancs.Characters.Count == 0) return null;
            if (character < 0 || character >= ancs.Characters.Count) character = 0;
            MpAncs.Character info = ancs.Characters[character];
            var ch = new MpCharacter { AncsId = ancsId, CharIndex = character, Ancs = ancs, Info = info };
            ch.Model = MpModels.LoadSkinned(assets, info.Cmdl, info.Cskr, info.Cinf);
            if (ch.Model == null) return null;
            ch.Rig = ch.Model.Rig;
            ch.Set = MpAnimSet.Read(assets.Read(ancsId), ancs.AnimSetOffset);
            foreach (var kv in info.AnimNames) if (!ch.ByName.ContainsKey(kv.Value)) ch.ByName[kv.Value] = kv.Key;
            ch.AnimCount = info.AnimIndex.Count > 0 ? info.AnimIndex.Count : ch.Set.Entries.Count;
            int failed = 0;
            foreach (MpAnimSet.Entry e in ch.Set.Entries)
                foreach (uint id in e.Anims)
                {
                    if (ch.Anims.ContainsKey(id) || !MpAssets.Valid(id)) continue;
                    try
                    {
                        byte[] b = assets.Read(id);
                        MpAnimation a = b != null ? MpAnimation.Read(b) : null;
                        ch.Anims[id] = a;
                        if (a != null && MpAssets.Valid(a.Evnt))
                        {
                            try { ch.Events[id] = MpAnimEvents.Read(assets.Read(a.Evnt)); }
                            catch (Exception) { }
                        }
                    }
                    catch (Exception) { ch.Anims[id] = null; failed++; }
                }

            ch.Report = info.Name + ": " + ch.AnimCount + " animations (" + ch.Anims.Count + " ANIMs" + (failed > 0 ? ", " + failed + " unreadable" : "") + "), " + ch.Rig.Count + " bones";
            return ch;
        }

        /// <summary>Main thread: the model's meshes and materials.</summary>
        public void Upload(bool actorLighting = true)
        {
            Model.Build(actorLighting);
        }

        /// <summary>The character's animation number for a name ("powerShoot"), or -1.</summary>
        public int AnimNumber(string name)
        {
            int n;
            return name != null && ByName.TryGetValue(name, out n) ? n : -1;
        }

        /// <summary>The set entry an animation number plays, or null.</summary>
        public MpAnimSet.Entry EntryOf(int anim)
        {
            int e = Set.EntryOf(Info, anim);
            return e >= 0 ? Set.Entries[e] : null;
        }

        public MpAnimation Anim(uint id)
        {
            MpAnimation a;
            return Anims.TryGetValue(id, out a) ? a : null;
        }

        /// <summary>The ANIM ids of the character's animations (and the textures of the model's every material set) for preloading.</summary>
        public void TexturesInto(HashSet<uint> tex) { MpModels.TexturesOf(Model, tex); }
    }
}
