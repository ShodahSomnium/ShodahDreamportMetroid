using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The game's sound, from the disc: sound effects run their MusyX sound macro (musyx synthmacros.c: keys, random keys, volume
    /// scale, envelopes, ADSR, waits, loops, gosub / goto, child macros, key-off traps, start / stop sample) and play the group's
    /// DSP-ADPCM samples at the macro's pitch; music is the streamed .dsp pair (CScriptStreamedMusic, "xL.dsp|xR.dsp") with its
    /// loop points. Clips are made with AudioClip.Create + SetData (no audio callbacks); a sample loop is an intro clip and a
    /// looping clip scheduled after it, the music loop two sources on one clip handing over at the loop end (PlayScheduled).
    /// Code sound ids (CSfxManager callers, MpSfx) are MusyX FX ids; ids stored in the game's data go through the "sound_lookup"
    /// table first (CSfxManager::TranslateSFXID).
    /// </summary>
    public sealed class MpAudio
    {
        public sealed class Sound
        {
            public readonly List<Voice> Voices = new List<Voice>();
            public Vector3? GamePos;
            public float Volume = 1f, MaxDist;
            public bool Stopped;
            public bool Alive { get { foreach (Voice v in Voices) if (!v.Dead) return true; return false; } }
        }

        public sealed class ClipSet
        {
            public AudioClip Intro, Loop;      // Intro: the whole sample (no loop) or 0..loop start; Loop: the loop part
            public int Rate, IntroSamples;
        }

        public sealed class Voice
        {
            public Sound Owner;
            public MpSoundGroup Group;
            public int Macro, Step;
            public readonly int[] StackMacro = new int[4], StackStep = new int[4];
            public int StackCount;
            public float StartTime, WaitUntil = -1f;
            public bool WaitKeyOff, WaitSampleEnd, MacroDone, Dead, KeyOff;
            public int LoopCount;
            public int OrgNote, CurNote, Detune;
            public float SetFreq = -1f;
            public float Vol, OrgVol;
            public bool EnvOn;
            public float EnvFrom, EnvTo, EnvT0, EnvDur;
            public bool Adsr;
            public float A, D, S = 1f, R;
            public float AdsrGainAtKeyOff = 1f, KeyOffTime;
            public readonly int[] TrapMacro = { -1, -1, -1 }, TrapStep = new int[3];
            public MpSoundSample Sample;
            public ClipSet Clips;
            public GameObject Go;
            public AudioSource Src, SrcLoop;
            public double SampleStart, SampleEnd;   // dsp time; SampleEnd = +inf for a looping sample
            public float SamplePitch = 1f;
            public bool Fading;
            public float FadeT;
        }

        private readonly MpSoundBank _bank;
        private readonly Transform _root;
        private readonly List<Sound> _sounds = new List<Sound>();
        private readonly Dictionary<MpSoundSample, ClipSet> _clips = new Dictionary<MpSoundSample, ClipSet>();
        private readonly Stack<GameObject> _pool = new Stack<GameObject>();
        private readonly System.Random _rand = new System.Random();
        public static float SfxVolume = 1f, MusicVolume = 1f;
        public int Started, Missing;

        public const float TicksPerSecond = 768f;     // musyx synthSetBpm(120): 120 bpm x 384 ticks

        public MpAudio(MpSoundBank bank, Transform parent)
        {
            _bank = bank;
            _root = new GameObject("Metroid Prime sound").transform;
            _root.SetParent(parent, false);
        }

        public int Translate(int dataId) { return _bank != null ? _bank.Translate(dataId) : -1; }

        // ------------------------------------------------------------------ effects

        /// <summary>Starts MusyX FX <paramref name="fx"/> (2D when gamePos is null). Volume 0..1 (the game's 0..127).</summary>
        public Sound Play(int fx, Vector3? gamePos, float volume, bool loop, float maxDist)
        {
            if (_bank == null || fx < 0) return null;
            MpSoundGroup g;
            if (!_bank.FxGroup.TryGetValue(fx, out g)) { Missing++; return null; }
            MpSoundFx f = g.Fx[fx];
            var s = new Sound { GamePos = gamePos, Volume = Mathf.Clamp01(volume), MaxDist = maxDist > 0f ? maxDist : 100f };
            StartId(s, g, f.Macro, f.Key, 127f * s.Volume, 0);
            if (s.Voices.Count == 0) return null;
            _sounds.Add(s);
            Started++;
            for (int i = 0; i < s.Voices.Count; i++) Run(s.Voices[i]);     // a macro may start child voices (PlayMacro)
            return s;
        }

        /// <summary>A macro, keymap (0x4000) or layer (0x8000) id (musyx synthStartSound / StartLayer / StartKeymap).</summary>
        private void StartId(Sound s, MpSoundGroup g, int id, int key, float vol, int depth)
        {
            if (depth > 3) return;
            switch (id & 0xC000)
            {
                case 0:
                    StartMacro(s, g, id, key, vol);
                    break;
                case 0x4000:
                {
                    int o;
                    if (!g.Keymaps.TryGetValue(id, out o) && !g.Keymaps.TryGetValue(id & 0x3FFF, out o)) return;
                    int e = o + (key & 0x7F) * 8;
                    int mid = MpReader.U16At(g.Pool, e);
                    int k = Mathf.Clamp((key & 0x7F) + (sbyte)g.Pool[e + 2], 0, 127);
                    StartId(s, g, mid, k, vol, depth + 1);
                    break;
                }
                case 0x8000:
                {
                    int o;
                    if (!g.Layers.TryGetValue(id, out o) && !g.Layers.TryGetValue(id & 0x3FFF, out o)) return;
                    int n = (int)MpReader.U32At(g.Pool, o);
                    for (int i = 0; i < n && i < 16; i++)
                    {
                        int e = o + 4 + i * 12;
                        int mid = MpReader.U16At(g.Pool, e);
                        if (key < g.Pool[e + 2] || key > g.Pool[e + 3]) continue;
                        int k = Mathf.Clamp(key + (sbyte)g.Pool[e + 4], 0, 127);
                        StartId(s, g, mid, k, vol * g.Pool[e + 5] / 127f, depth + 1);
                    }

                    break;
                }
            }
        }

        private Voice StartMacro(Sound s, MpSoundGroup g, int macro, int key, float vol)
        {
            if (!g.Macros.ContainsKey(macro)) return null;
            var v = new Voice { Owner = s, Group = g, Macro = macro, Step = 0, OrgNote = key & 0x7F, CurNote = key & 0x7F, Vol = vol, OrgVol = vol, StartTime = Time.time };
            s.Voices.Add(v);
            return v;
        }

        /// <summary>Key off (CSfxManager::SfxStop -> sndFXKeyOff): the macro's key-off trap or wait, else the ADSR release.</summary>
        public void Stop(object handle)
        {
            var s = handle as Sound;
            if (s == null || s.Stopped) return;
            s.Stopped = true;
            foreach (Voice v in s.Voices.ToArray()) KeyOffVoice(v, true);
        }

        private void KeyOffVoice(Voice v, bool run)
        {
            if (v.Dead || v.KeyOff) return;
            v.KeyOff = true;
            v.AdsrGainAtKeyOff = AdsrGain(v);
            v.KeyOffTime = Time.time;
            if (v.TrapMacro[0] >= 0)
            {
                v.Macro = v.TrapMacro[0];
                v.Step = v.TrapStep[0];
                v.TrapMacro[0] = -1;
                v.WaitUntil = -1f;
                v.MacroDone = false;
            }

            if (v.MacroDone && !v.Adsr && SamplePlaying(v) && double.IsPositiveInfinity(v.SampleEnd)) StartFade(v);
            if (run) Run(v);
        }

        public void Tick(float dt, Transform head)
        {
            for (int i = _sounds.Count - 1; i >= 0; i--)
            {
                Sound s = _sounds[i];
                for (int k = 0; k < s.Voices.Count; k++)
                {
                    Voice v = s.Voices[k];
                    if (v.Dead) continue;
                    if (!v.MacroDone) Run(v);
                    UpdateVoice(v);
                }

                if (!s.Alive)
                {
                    foreach (Voice v in s.Voices) FreeVoice(v);
                    _sounds.RemoveAt(i);
                }
            }

            TickMusic(dt);
        }

        // ------------------------------------------------------------------ the macro interpreter

        private bool Waiting(Voice v)
        {
            if (v.WaitUntil < 0f) return false;
            if (Time.time >= v.WaitUntil) { v.WaitUntil = -1f; return false; }
            if (v.WaitKeyOff && v.KeyOff) { v.WaitUntil = -1f; return false; }
            if (v.WaitSampleEnd && !SamplePlaying(v)) { v.WaitUntil = -1f; return false; }
            return true;
        }

        private void Run(Voice v)
        {
            if (v.Dead || v.MacroDone) return;
            for (int guard = 0; guard < 96; guard++)
            {
                if (Waiting(v)) return;
                uint p0, p1;
                if (!v.Group.Step(v.Macro, v.Step, out p0, out p1)) { v.MacroDone = true; return; }
                v.Step++;
                if (!Exec(v, p0, p1)) return;
            }
        }

        private int Rand8() { return _rand.Next(256); }

        /// <summary>One macro step. False = the voice stops running this frame (a wait, or the end).</summary>
        private bool Exec(Voice v, uint p0, uint p1)
        {
            int op = (int)(p0 & 0x7F);
            switch (op)
            {
                case 0x00:      // end of macro
                case 0x01:      // stop
                    v.MacroDone = true;
                    return false;
                case 0x02:      // if key
                    if (v.CurNote >= ((p0 >> 8) & 0xFF)) Jump(v, (int)(p0 >> 16), (int)(p1 & 0xFFFF));
                    return true;
                case 0x03:      // if velocity
                    if (v.Vol >= ((p0 >> 8) & 0xFF)) Jump(v, (int)(p0 >> 16), (int)(p1 & 0xFFFF));
                    return true;
                case 0x04:      // wait
                    return !Wait(v, p0, p1, false);
                case 0x05:      // loop
                    Loop(v, p0, p1);
                    return true;
                case 0x06:      // goto
                    if (!Jump(v, (int)(p0 >> 16), (int)(p1 & 0xFFFF))) { v.MacroDone = true; return false; }
                    return true;
                case 0x07:      // wait ms
                    return !Wait(v, p0, p1, true);
                case 0x08:      // play macro (a child voice)
                {
                    int key = Mathf.Clamp(v.OrgNote + (sbyte)((p0 >> 8) & 0xFF), 0, 127);
                    Voice c = StartMacro(v.Owner, v.Group, (int)(p0 >> 16), key, v.Vol);
                    if (c != null) Run(c);
                    return true;
                }
                case 0x0C:      // set ADSR (linear: attack ms, decay ms, sustain /4096, release ms; little-endian)
                    SetAdsr(v, (int)((p0 >> 8) & 0xFFFF), ((p0 >> 24) & 0xFF) != 0);
                    return true;
                case 0x0D:      // scale volume
                {
                    float scale = (p0 >> 8) & 0xFF;
                    v.Vol = (((p1 >> 8) & 0xFF) == 0 ? v.Vol : v.OrgVol) * scale / 127f + ((p0 >> 16) & 0xFF);
                    if (v.Vol > 127f) v.Vol = 127f;
                    v.EnvOn = false;
                    return true;
                }
                case 0x0F:      // envelope (to a volume over a time)
                case 0x14:      // fade in
                {
                    float secs = Seconds(p1 >> 16, ((p1 >> 8) & 1) != 0);
                    float baseVol = CurrentVol(v);
                    float target = baseVol * ((p0 >> 8) & 0xFF) / 128f + ((p0 >> 16) & 0xFF);
                    v.EnvFrom = op == 0x14 ? 0f : baseVol;
                    v.EnvTo = Mathf.Min(127f, target);
                    v.EnvT0 = Time.time;
                    v.EnvDur = Mathf.Max(0.001f, secs);
                    v.EnvOn = true;
                    v.Vol = v.EnvTo;
                    return true;
                }
                case 0x10:      // start sample
                {
                    int smp = (int)((p0 >> 8) & 0xFFFF), mode = (int)(p0 >> 24);
                    int offset = mode == 0 ? (int)p1 : mode == 1 ? (int)((127f - v.Vol) * p1 / 127f) : mode == 2 ? (int)(v.Vol * p1 / 127f) : 0;
                    StartSample(v, smp, offset);
                    return true;
                }
                case 0x11:      // stop sample
                    StopSample(v);
                    return true;
                case 0x12:      // key off (own)
                    KeyOffVoice(v, false);
                    return !v.MacroDone;
                case 0x13:      // if random
                    if (Rand8() >= ((p0 >> 8) & 0xFF)) Jump(v, (int)(p0 >> 16), (int)(p1 & 0xFFFF));
                    return true;
                case 0x17:      // random key
                {
                    int k1 = (int)((p0 >> 8) & 0xFF), k2 = (int)((p0 >> 24) & 0xFF);
                    if (((p1 >> 8) & 0xFF) == 0) { if (k1 > k2) { int t = k1; k1 = k2; k2 = t; } }
                    else { int a = v.CurNote - k1, b = v.CurNote + k2; k1 = Mathf.Clamp(a, 0, 127); k2 = Mathf.Clamp(b, 0, 127); }
                    v.Detune = (p1 & 0xFF) != 0 ? _rand.Next(201) - 100 : (sbyte)((p0 >> 16) & 0xFF);
                    v.CurNote = k1 + _rand.Next(k2 - k1 + 1);
                    v.SetFreq = -1f;
                    ApplyPitch(v);
                    return true;
                }
                case 0x18:      // add key
                case 0x19:      // set key
                case 0x1A:      // last key
                {
                    int d = (sbyte)((p0 >> 8) & 0xFF);
                    if (op == 0x19) v.CurNote = (int)((p0 >> 8) & 0x7F);
                    else if (op == 0x18 && ((p0 >> 24) & 0xFF) == 0) v.CurNote += d;
                    else v.CurNote = v.OrgNote + d;
                    v.CurNote = Mathf.Clamp(v.CurNote, 0, 127);
                    v.Detune = (sbyte)((p0 >> 16) & 0xFF);
                    v.SetFreq = -1f;
                    ApplyPitch(v);
                    return !Wait(v, 4, p1, false);
                }
                case 0x1F:      // set pitch (Hz)
                    v.SetFreq = (p0 >> 8) & 0xFFFFFF;
                    ApplyPitch(v);
                    return true;
                case 0x24:      // return
                    if (v.StackCount > 0) { v.StackCount--; v.Macro = v.StackMacro[v.StackCount]; v.Step = v.StackStep[v.StackCount]; }
                    return true;
                case 0x25:      // gosub
                {
                    int m = v.Macro, st = v.Step;
                    if (!Jump(v, (int)(p0 >> 16), (int)(p1 & 0xFFFF))) { v.MacroDone = true; return false; }
                    if (v.StackCount < 4) { v.StackMacro[v.StackCount] = m; v.StackStep[v.StackCount] = st; v.StackCount++; }
                    return true;
                }
                case 0x28:      // trap event (0 key off, 1 sample end, 2 message)
                {
                    int t = (int)((p0 >> 8) & 0xFF);
                    if (t < 3) { v.TrapMacro[t] = (int)(p0 >> 16); v.TrapStep[t] = (int)(p1 & 0xFFFF); }
                    if (t == 0 && v.KeyOff) KeyOffTrapNow(v);
                    return true;
                }
                case 0x29:      // untrap
                {
                    int t = (int)((p0 >> 8) & 0xFF);
                    if (t < 3) v.TrapMacro[t] = -1;
                    return true;
                }
                default:        // panning, vibrato, priority, age, LFO, variables ... (no effect on a Unity voice)
                    return true;
            }
        }

        private void KeyOffTrapNow(Voice v)
        {
            v.Macro = v.TrapMacro[0];
            v.Step = v.TrapStep[0];
            v.TrapMacro[0] = -1;
        }

        private bool Jump(Voice v, int macro, int step)
        {
            if (!v.Group.Macros.ContainsKey(macro)) return false;
            v.Macro = macro;
            v.Step = step;
            return true;
        }

        private static float Seconds(uint t, bool ms) { return ms ? t / 1000f : t / TicksPerSecond; }

        /// <summary>musyx mcmdWait: wait for a time (0xFFFF = for ever), optionally until key off and / or the sample's end.</summary>
        private bool Wait(Voice v, uint p0, uint p1, bool forceMs)
        {
            uint t = p1 >> 16;
            if (t == 0) return false;
            bool keyFlag = ((p0 >> 8) & 1) != 0, sampleFlag = ((p0 >> 24) & 1) != 0, random = ((p0 >> 16) & 1) != 0;
            if (keyFlag && v.KeyOff) return false;
            if (sampleFlag && !SamplePlaying(v)) return false;
            v.WaitKeyOff = keyFlag;
            v.WaitSampleEnd = sampleFlag;
            if (t == 0xFFFF) { v.WaitUntil = float.PositiveInfinity; return true; }
            if (random) t = (uint)_rand.Next((int)t);
            float secs = Seconds(t, forceMs || ((p1 >> 8) & 1) != 0);
            v.WaitUntil = ((p1 & 1) != 0 ? v.StartTime : Time.time) + secs;
            if (v.WaitUntil <= Time.time) { v.WaitUntil = -1f; return false; }
            return true;
        }

        private void Loop(Voice v, uint p0, uint p1)
        {
            int count = (int)(p1 >> 16);
            if (v.LoopCount == 0)
            {
                if (((p0 >> 16) & 1) != 0 && count > 0) count = _rand.Next(count);
                v.LoopCount = count == 0xFFFF ? -1 : count + 1;
            }

            if (v.LoopCount > 0 && --v.LoopCount == 0) return;
            if (((p0 >> 8) & 1) != 0 && v.KeyOff) { v.LoopCount = 0; return; }
            if (((p0 >> 24) & 1) != 0 && !SamplePlaying(v)) { v.LoopCount = 0; return; }
            v.Step = (int)(p1 & 0xFFFF);
        }

        private void SetAdsr(Voice v, int table, bool dls)
        {
            int o, len;
            if (!v.Group.Tables.TryGetValue(table, out o) || !v.Group.TableBytes.TryGetValue(table, out len)) return;
            byte[] p = v.Group.Pool;
            if (!dls && len >= 8)
            {
                v.A = LE16(p, o) / 1000f;
                v.D = LE16(p, o + 2) / 1000f;
                v.S = Mathf.Clamp01(LE16(p, o + 4) * 8f / 32767f);
                v.R = LE16(p, o + 6) / 1000f;
                v.Adsr = true;
            }
            else if (dls && len >= 12)
            {
                // DLS form: times in the DSP's own units; sustain through the DLS volume table, release in ms
                v.A = 0f;
                v.D = 0f;
                v.S = Mathf.Clamp01((LE16(p, o + 8) >> 5) / 128f);
                v.R = LE16(p, o + 10) / 1000f;
                v.Adsr = true;
            }
        }

        private static int LE16(byte[] b, int o) { return b[o] | (b[o + 1] << 8); }

        // ------------------------------------------------------------------ voices on Unity sources

        private ClipSet ClipsOf(MpSoundGroup g, MpSoundSample s)
        {
            ClipSet c;
            if (_clips.TryGetValue(s, out c)) return c;
            float[] pcm = g.Decode(s);
            c = new ClipSet { Rate = Mathf.Max(1000, s.Rate) };
            int loopStart = Mathf.Clamp(s.LoopStart, 0, pcm.Length), loopEnd = Mathf.Clamp(s.LoopStart + s.LoopLength, 0, pcm.Length);
            if (s.Loops && loopEnd > loopStart + 16)
            {
                if (loopStart > 0)
                {
                    var intro = new float[loopStart];
                    Array.Copy(pcm, intro, loopStart);
                    c.Intro = AudioClip.Create("smp " + s.Id + " intro", loopStart, 1, c.Rate, false);
                    c.Intro.SetData(intro, 0);
                }

                var loop = new float[loopEnd - loopStart];
                Array.Copy(pcm, loopStart, loop, 0, loop.Length);
                c.Loop = AudioClip.Create("smp " + s.Id + " loop", loop.Length, 1, c.Rate, false);
                c.Loop.SetData(loop, 0);
                c.IntroSamples = loopStart;
            }
            else
            {
                c.Intro = AudioClip.Create("smp " + s.Id, pcm.Length, 1, c.Rate, false);
                c.Intro.SetData(pcm, 0);
                c.IntroSamples = pcm.Length;
            }

            _clips[s] = c;
            return c;
        }

        private GameObject Take()
        {
            while (_pool.Count > 0)
            {
                GameObject go = _pool.Pop();
                if (go != null) { go.SetActive(true); return go; }
            }

            var g = new GameObject("voice");
            g.transform.SetParent(_root, false);
            for (int i = 0; i < 2; i++)
            {
                AudioSource a = g.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.dopplerLevel = 0f;
                a.rolloffMode = AudioRolloffMode.Linear;
            }

            return g;
        }

        private void StartSample(Voice v, int id, int offset)
        {
            // the macro's own group first; musyx keeps one sample table, so another loaded group may hold it
            MpSoundGroup owner = v.Group.SampleDir.ContainsKey(id) ? v.Group : FindSampleGroup(id);
            if (owner == null) { Missing++; return; }
            MpSoundSample s = owner.SampleDir[id];
            StopSample(v);
            ClipSet c;
            try { c = ClipsOf(owner, s); }
            catch (Exception e) { MpLog.Warn("Sound: sample " + id + " not decoded: " + e.Message); return; }

            v.Sample = s;
            v.Clips = c;
            if (v.Go == null)
            {
                v.Go = Take();
                AudioSource[] srcs = v.Go.GetComponents<AudioSource>();
                v.Src = srcs[0];
                v.SrcLoop = srcs[1];
            }

            Sound o = v.Owner;
            float blend = o.GamePos.HasValue ? 1f : 0f;
            foreach (AudioSource a in new[] { v.Src, v.SrcLoop })
            {
                a.spatialBlend = blend;
                a.minDistance = 1.5f;
                a.maxDistance = Mathf.Max(2f, o.MaxDist * MpSpace.Scale);
                a.loop = false;
                a.clip = null;
            }

            if (o.GamePos.HasValue) v.Go.transform.position = MpSpace.ToUnity(o.GamePos.Value);
            v.SamplePitch = PitchOf(v);
            double t0 = AudioSettings.dspTime + 0.015;
            v.SampleStart = t0;
            float vol = Gain(v);
            int introLen = c.Intro != null ? c.Intro.samples : 0;
            if (c.Intro != null)
            {
                v.Src.clip = c.Intro;
                v.Src.pitch = v.SamplePitch;
                v.Src.volume = vol;
                v.Src.timeSamples = Mathf.Clamp(offset, 0, Math.Max(0, introLen - 1));
                v.Src.PlayScheduled(t0);
            }

            if (c.Loop != null)
            {
                v.SrcLoop.clip = c.Loop;
                v.SrcLoop.loop = true;
                v.SrcLoop.pitch = v.SamplePitch;
                v.SrcLoop.volume = vol;
                v.SrcLoop.timeSamples = 0;
                double introSecs = Math.Max(0, introLen - (c.Intro != null ? Mathf.Clamp(offset, 0, introLen) : 0)) / (double)c.Rate / v.SamplePitch;
                v.SrcLoop.PlayScheduled(t0 + introSecs);
                v.SampleEnd = double.PositiveInfinity;
            }
            else v.SampleEnd = t0 + Math.Max(0, introLen - offset) / (double)c.Rate / v.SamplePitch;

            v.Fading = false;
        }

        private MpSoundGroup FindSampleGroup(int id)
        {
            foreach (MpSoundGroup g in _bank.Groups) if (g.SampleDir.ContainsKey(id)) return g;
            return null;
        }

        private static bool SamplePlaying(Voice v)
        {
            if (v.Sample == null || v.Src == null) return false;
            return AudioSettings.dspTime < v.SampleEnd;
        }

        private void StopSample(Voice v)
        {
            if (v.Src != null) { v.Src.Stop(); v.SrcLoop.Stop(); }
            v.Sample = null;
            v.SampleEnd = 0;
            v.Fading = false;
        }

        private void StartFade(Voice v)
        {
            v.Fading = true;
            v.FadeT = 0f;
        }

        private float PitchOf(Voice v)
        {
            if (v.Sample == null) return 1f;
            float note = v.CurNote + v.Detune / 100f;
            float ratio;
            if (v.SetFreq > 0f) ratio = v.SetFreq / Mathf.Max(1, v.Sample.Rate);
            else ratio = Mathf.Pow(2f, (note - v.Sample.RootKey) / 12f);
            return Mathf.Clamp(ratio, 0.05f, 3f);
        }

        private void ApplyPitch(Voice v)
        {
            if (v.Sample == null || v.Src == null) return;
            float p = PitchOf(v);
            if (Mathf.Abs(p - v.SamplePitch) < 1e-4f) return;
            // the remaining time of a one-shot stretches with the new pitch
            if (!double.IsPositiveInfinity(v.SampleEnd))
            {
                double left = v.SampleEnd - AudioSettings.dspTime;
                if (left > 0) v.SampleEnd = AudioSettings.dspTime + left * v.SamplePitch / p;
            }

            v.SamplePitch = p;
            v.Src.pitch = p;
            v.SrcLoop.pitch = p;
        }

        private float CurrentVol(Voice v)
        {
            if (!v.EnvOn) return v.Vol;
            float t = (Time.time - v.EnvT0) / v.EnvDur;
            if (t >= 1f) { v.EnvOn = false; return v.EnvTo; }
            return Mathf.Lerp(v.EnvFrom, v.EnvTo, t);
        }

        private float AdsrGain(Voice v)
        {
            if (!v.Adsr || v.Sample == null) return 1f;
            if (v.KeyOff)
            {
                float rt = Time.time - v.KeyOffTime;
                return v.R <= 0f ? 0f : v.AdsrGainAtKeyOff * Mathf.Clamp01(1f - rt / v.R);
            }

            float t = (float)(AudioSettings.dspTime - v.SampleStart);
            if (t < v.A) return v.A > 0f ? t / v.A : 1f;
            t -= v.A;
            if (t < v.D) return Mathf.Lerp(1f, v.S, v.D > 0f ? t / v.D : 1f);
            return v.S;
        }

        /// <summary>The amplitude: the musyx volume curve (about (v/127)^2) x ADSR x the sound's volume x the player's setting.</summary>
        private float Gain(Voice v)
        {
            float vol = CurrentVol(v) / 127f;
            float g = vol * vol * AdsrGain(v) * SfxVolume;
            if (v.Fading) g *= Mathf.Clamp01(1f - v.FadeT / 0.03f);
            return Mathf.Clamp01(g);
        }

        private void UpdateVoice(Voice v)
        {
            if (v.Src != null && v.Sample != null)
            {
                if (v.Fading)
                {
                    v.FadeT += Time.deltaTime;
                    if (v.FadeT >= 0.03f) StopSample(v);
                }

                if (v.Adsr && v.KeyOff && AdsrGain(v) <= 0f) StopSample(v);
                if (v.Sample != null)
                {
                    float g = Gain(v);
                    v.Src.volume = g;
                    v.SrcLoop.volume = g;
                    if (v.Owner.GamePos.HasValue) v.Go.transform.position = MpSpace.ToUnity(v.Owner.GamePos.Value);
                }
            }

            // a sample-end trap
            if (v.TrapMacro[1] >= 0 && v.Sample != null && !SamplePlaying(v))
            {
                v.Macro = v.TrapMacro[1];
                v.Step = v.TrapStep[1];
                v.TrapMacro[1] = -1;
                v.MacroDone = false;
                v.WaitUntil = -1f;
                Run(v);
            }

            if (v.MacroDone && !SamplePlaying(v)) v.Dead = true;
            // a stopped sound whose sample loops on with its macro finished: fade it out
            if (v.MacroDone && v.Owner.Stopped && SamplePlaying(v) && double.IsPositiveInfinity(v.SampleEnd) && !v.Fading && !v.Adsr) StartFade(v);
        }

        private void FreeVoice(Voice v)
        {
            if (v.Go == null) return;
            v.Src.Stop();
            v.SrcLoop.Stop();
            v.Src.clip = null;
            v.SrcLoop.clip = null;
            v.Go.SetActive(false);
            _pool.Push(v.Go);
            v.Go = null;
        }

        /// <summary>Moves a playing 3D sound (an emitter following an object).</summary>
        public static void Move(object handle, Vector3 gamePos)
        {
            var s = handle as Sound;
            if (s != null && s.GamePos.HasValue) s.GamePos = gamePos;
        }

        // ------------------------------------------------------------------ music

        /// <summary>
        /// One of CStreamAudioManager's two software channels: 0 looping music, 1 one-shots (stingers, voice) - CScriptStreamedMusic
        /// plays on IsOneShot(loop). A new file on a busy channel waits for the old one's fade out.
        /// </summary>
        private sealed class Channel
        {
            public Track Playing;
            public string Wanted;
            public float WantedFadeIn, WantedVolume;
            public bool WantedLoop;
        }

        private sealed class Track
        {
            public string File;
            public AudioClip Clip;
            public int Rate, LoopStart, LoopEnd;
            public bool Loop;
            public AudioSource A, B;
            public double Boundary;          // dsp time the active source hands over (or the end of a one-shot)
            public bool BActive, Scheduled;
            public float Volume, Fade = 1f, FadeSpeed;
            public bool Stopping;
        }

        private readonly Channel[] _channels = { new Channel(), new Channel() };
        private MpAssets _assets;
        private MpLoader _loader;
        private readonly Dictionary<string, object[]> _decoded = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _decoding = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void UseDisc(MpAssets assets, MpLoader loader)
        {
            _assets = assets;
            _loader = loader;
        }

        private static int ChannelOf(bool loop) { return loop ? 0 : 1; }

        /// <summary>CStreamAudioManager::PlaySoftwareAudio: the same file again only changes its volume; another queues behind a fade out.</summary>
        public void PlayMusic(string file, float fadeIn, float volume, bool loop)
        {
            if (string.IsNullOrEmpty(file) || _assets == null) return;
            Channel c = _channels[ChannelOf(loop)];
            if (c.Playing != null && string.Equals(c.Playing.File, file, StringComparison.OrdinalIgnoreCase) && !c.Playing.Stopping)
            {
                c.Playing.Volume = volume;
                return;
            }

            c.Wanted = file;
            c.WantedFadeIn = fadeIn;
            c.WantedVolume = volume;
            c.WantedLoop = loop;
            if (c.Playing != null && !c.Playing.Stopping) FadeOut(c.Playing, 0.5f);
            Decode(file);
        }

        /// <summary>CStreamAudioManager::StopSoftwareAudio: the file stops (fading) on whichever channel has it, or leaves the queue; null = everything.</summary>
        public void StopMusic(string file, float fadeOut)
        {
            foreach (Channel c in _channels)
            {
                if (c.Wanted != null && (file == null || string.Equals(c.Wanted, file, StringComparison.OrdinalIgnoreCase))) c.Wanted = null;
                if (c.Playing != null && (file == null || string.Equals(c.Playing.File, file, StringComparison.OrdinalIgnoreCase))) FadeOut(c.Playing, fadeOut);
            }
        }

        public void StopMusic(float fadeOut) { StopMusic(null, fadeOut); }

        private static void FadeOut(Track m, float seconds)
        {
            m.Stopping = true;
            m.FadeSpeed = seconds > 0.01f ? 1f / seconds : 1000f;
        }

        private void Decode(string file)
        {
            if (_decoded.ContainsKey(file) || !_decoding.Add(file)) return;
            MpAssets assets = _assets;
            _loader.Add(() =>
            {
                // the data names files from the disc root ("/audio/x.dsp"); the disc's file table has no leading slash
                string[] parts = file.Split('|');
                string left = parts[0].TrimStart('/'), right = parts.Length > 1 ? parts[1].TrimStart('/') : null;
                if (right == null && left.EndsWith("L.dsp", StringComparison.OrdinalIgnoreCase))
                {
                    string r = left.Substring(0, left.Length - 5) + "R.dsp";
                    if (assets.Disc.Find(r) != null) right = r;
                }

                byte[] lb = assets.Disc.ReadFile(left);
                if (lb == null) throw new Exception("no " + left + " on the disc");
                MpDspAdpcm.Stream ls = MpDspAdpcm.Stream.Read(lb);
                float[] l = ls.DecodeAll();
                float[] rr = null;
                if (right != null)
                {
                    byte[] rb = assets.Disc.ReadFile(right);
                    if (rb != null) rr = MpDspAdpcm.Stream.Read(rb).DecodeAll();
                }

                int n = l.Length;
                var inter = new float[n * 2];
                for (int i = 0; i < n; i++)
                {
                    inter[i * 2] = l[i];
                    inter[i * 2 + 1] = rr != null && i < rr.Length ? rr[i] : l[i];
                }

                return new object[] { inter, ls.Rate, ls.Loop, ls.LoopStart, ls.LoopEnd, rr != null };
            }, (r, e) =>
            {
                _decoding.Remove(file);
                if (e != null)
                {
                    MpLog.Warn("Music: " + file + " not read: " + e.Message);
                    foreach (Channel c in _channels) if (c.Wanted == file) c.Wanted = null;
                    return;
                }

                var d = (object[])r;
                _decoded[file] = d;
                MpLog.Info("Music: " + file + " decoded (" + (((float[])d[0]).Length / 2) + " samples at " + d[1] + " Hz, " + ((bool)d[5] ? "stereo" : "mono") + ((bool)d[2] ? ", loop " + d[3] + ".." + d[4] : "") + ")");
            });
        }

        private void TickMusic(float dt)
        {
            foreach (Channel c in _channels)
            {
                Track m = c.Playing;
                if (m != null)
                {
                    if (m.Stopping)
                    {
                        m.Fade -= dt * m.FadeSpeed;
                        if (m.Fade <= 0f) { DestroyTrack(m); c.Playing = m = null; }
                    }
                    else if (m.Fade < 1f) m.Fade = Mathf.Min(1f, m.Fade + dt * m.FadeSpeed);

                    if (m != null)
                    {
                        float vol = m.Volume * m.Volume * m.Fade * MusicVolume;
                        m.A.volume = vol;
                        m.B.volume = vol;
                        if (m.Loop) ScheduleLoop(m);
                        else if (AudioSettings.dspTime > m.Boundary + 0.2) { DestroyTrack(m); c.Playing = null; }
                    }
                }

                object[] d;
                if (c.Playing == null && c.Wanted != null && _decoded.TryGetValue(c.Wanted, out d)) StartTrack(c, d);
            }
        }

        private void StartTrack(Channel c, object[] d)
        {
            var pcm = (float[])d[0];
            var m = new Track { File = c.Wanted, Rate = (int)d[1], Loop = c.WantedLoop && (bool)d[2], LoopStart = (int)d[3], LoopEnd = (int)d[4], Volume = c.WantedVolume };
            m.Clip = AudioClip.Create("music " + m.File, pcm.Length / 2, 2, Mathf.Max(1000, m.Rate), false);
            m.Clip.SetData(pcm, 0);
            var go = new GameObject("music " + m.File);
            go.transform.SetParent(_root, false);
            m.A = go.AddComponent<AudioSource>();
            m.B = go.AddComponent<AudioSource>();
            foreach (AudioSource a in new[] { m.A, m.B })
            {
                a.playOnAwake = false;
                a.spatialBlend = 0f;
                a.clip = m.Clip;
                a.loop = false;
                a.volume = 0f;
            }

            m.Fade = c.WantedFadeIn > 0.01f ? 0f : 1f;
            m.FadeSpeed = c.WantedFadeIn > 0.01f ? 1f / c.WantedFadeIn : 1000f;
            double t0 = AudioSettings.dspTime + 0.05;
            m.A.timeSamples = 0;
            m.A.PlayScheduled(t0);
            if (m.Loop && m.LoopEnd > m.LoopStart + m.Rate / 4)
            {
                m.Boundary = t0 + m.LoopEnd / (double)m.Rate;
                m.A.SetScheduledEndTime(m.Boundary);
            }
            else
            {
                m.Loop = false;
                m.Boundary = t0 + pcm.Length / 2 / (double)m.Rate;
            }

            c.Playing = m;
            c.Wanted = null;
            MpLog.Info("Music: playing " + m.File + (m.Loop ? " (looping " + m.LoopStart + ".." + m.LoopEnd + ")" : " (once)"));
        }

        /// <summary>Two sources on one clip: the idle one is scheduled at the loop start for the moment the active one reaches the loop end.</summary>
        private static void ScheduleLoop(Track m)
        {
            double now = AudioSettings.dspTime;
            double loopSecs = (m.LoopEnd - m.LoopStart) / (double)m.Rate;
            if (now > m.Boundary)
            {
                m.Boundary += loopSecs;
                m.BActive = !m.BActive;
                m.Scheduled = false;
            }

            if (!m.Scheduled && m.Boundary - now < 2.0)
            {
                AudioSource next = m.BActive ? m.A : m.B;
                next.Stop();
                next.timeSamples = m.LoopStart;
                next.PlayScheduled(m.Boundary);
                next.SetScheduledEndTime(m.Boundary + loopSecs);
                m.Scheduled = true;
            }
        }

        private static void DestroyTrack(Track m)
        {
            if (m.A != null) UnityEngine.Object.Destroy(m.A.gameObject);
            if (m.Clip != null) UnityEngine.Object.Destroy(m.Clip);
        }

        /// <summary>Lab / log: what the two music channels play.</summary>
        public string MusicReport
        {
            get
            {
                string s = "";
                for (int i = 0; i < _channels.Length; i++)
                {
                    Track m = _channels[i].Playing;
                    s += (i == 0 ? "music " : ", one-shot ") + (m != null ? m.File + (m.A.isPlaying || m.B.isPlaying ? " (playing)" : " (scheduled)") : "-");
                }

                return s;
            }
        }

        public void Destroy()
        {
            foreach (Channel c in _channels)
            {
                if (c.Playing != null) DestroyTrack(c.Playing);
                c.Playing = null;
            }

            foreach (ClipSet c in _clips.Values)
            {
                if (c.Intro != null) UnityEngine.Object.Destroy(c.Intro);
                if (c.Loop != null) UnityEngine.Object.Destroy(c.Loop);
            }

            _clips.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
