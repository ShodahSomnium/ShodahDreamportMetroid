using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// A posed instance of an <see cref="MpCharacter"/> (decomp CAnimData, simplified): one bone Transform per rig bone (flat under the
    /// model root, which is in game model space: localPosition / localRotation = the bone's model-space offset and rotation) driving
    /// SkinnedMeshRenderers whose bind poses are the reference stance, so Unity's skinning is the game's [R | offset - R * reference].
    /// Playing: an animation number (CAnimPlaybackParms) loops or holds its last frame; a sequence entry plays its steps in turn; a
    /// change cross-fades from the pose it left (the game's transitions are mostly short blends). Animation events (EVNT) fire as
    /// their time passes: sounds through <see cref="SoundEvent"/>, effects through <see cref="ParticleEvent"/>, the rest by name
    /// through <see cref="NamedEvent"/>. The root bone is never posed (its motion is the actor's, CAnimData::RecalcPoseBuilder).
    /// </summary>
    public sealed class MpAnimator
    {
        public readonly MpCharacter Ch;
        public readonly GameObject Go;
        public readonly Transform[] Bones;
        public readonly List<SkinnedMeshRenderer> Renderers = new List<SkinnedMeshRenderer>();
        public readonly MpPose Pose, From;
        private readonly MpPose _work;
        private readonly List<uint> _steps = new List<uint>();
        private int _set;

        public int Anim { get; private set; } = -1;
        public bool Loop { get; private set; }
        public float Time;
        public float Speed = 1f;
        public float Duration { get; private set; }
        public bool Done { get { return !Loop && Time >= Duration; } }
        private float _blend, _blendDur;

        /// <summary>CParticleDatabase: the event effects by their event name (a running one is not started again; its duration ends it).</summary>
        public readonly Dictionary<string, MpEffects.Effect> EventFx = new Dictionary<string, MpEffects.Effect>();
        private readonly Dictionary<MpEffects.Effect, KeyValuePair<Transform, float>> _fxBone = new Dictionary<MpEffects.Effect, KeyValuePair<Transform, float>>();

        /// <summary>The running event effects follow their bones (CParticleDatabase parented modes); called each tick.</summary>
        private void MoveEffects()
        {
            if (_fxBone.Count == 0) return;
            List<MpEffects.Effect> gone = null;
            foreach (var kv in _fxBone)
            {
                if (kv.Key.Dead || kv.Value.Key == null) { if (gone == null) gone = new List<MpEffects.Effect>(); gone.Add(kv.Key); continue; }
                kv.Key.SetGlobal(MpSpace.ToGame(kv.Value.Key.position), MpEffects.GameOrientation(kv.Value.Key.rotation), kv.Value.Value);
            }

            if (gone != null) foreach (MpEffects.Effect e in gone) _fxBone.Remove(e);
        }

        /// <summary>Every running event effect stops emitting (the model goes away).</summary>
        public void StopEffects()
        {
            foreach (MpEffects.Effect e in EventFx.Values) if (e != null) e.Finish();
            EventFx.Clear();
            _fxBone.Clear();
        }

        /// <summary>
        /// CParticleDatabase::AddParticleEffect for an animation event: a new system unless the event's named one still runs; a positive
        /// duration (frames) ends its emission, none keeps it going while the model lives. Placed at <paramref name="gamePos"/>.
        /// </summary>
        public MpEffects.Effect EventEffect(MpAnimEvents.Particle p, Vector3 gamePos, Data.Particles.M3 orient)
        {
            MpEffects.Effect e;
            string key = string.IsNullOrEmpty(p.N.Name) ? p.Id.ToString("X8") : p.N.Name;
            if (EventFx.TryGetValue(key, out e) && e != null && !e.Dead && (e.Persistent || e.FinishAt > 0f))
            {
                if (p.Duration > 0) e.FinishAt = UnityEngine.Time.time + p.Duration / 60f;
                return e;
            }

            e = MpEffects.Spawn(p.Id, Vector3.zero, Data.Particles.M3.Identity, p.Duration <= 0);
            if (e == null) return null;
            e.SetGlobal(gamePos, orient, p.Scale > 0f ? p.Scale : 1f);
            if (p.Duration > 0) e.FinishAt = UnityEngine.Time.time + p.Duration / 60f;
            EventFx[key] = e;
            if (!string.IsNullOrEmpty(p.Bone)) { Transform b = Bone(p.Bone); if (b != null) _fxBone[e] = new KeyValuePair<Transform, float>(b, p.Scale > 0f ? p.Scale : 1f); }
            return e;
        }

        public System.Action<MpAnimEvents.Sound> SoundEvent;
        public System.Action<MpAnimEvents.Particle> ParticleEvent;
        public System.Action<string, int> NamedEvent;

        public MpAnimator(MpCharacter ch, string name, Transform parent, int materialSet = 0)
        {
            Ch = ch;
            Go = new GameObject(name);
            Go.transform.SetParent(parent, false);
            MpRig rig = ch.Rig;
            Bones = new Transform[rig.Count];
            for (int i = 0; i < rig.Count; i++)
            {
                var b = new GameObject(BoneName(i)).transform;
                b.SetParent(Go.transform, false);
                Bones[i] = b;
            }

            Pose = new MpPose(rig);
            From = new MpPose(rig);
            _work = new MpPose(rig);
            MpModel m = ch.Model;
            if (m.Meshes != null)
            {
                Vector3 size = m.Max - m.Min;
                float pad = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                var bounds = new Bounds((m.Min + m.Max) * 0.5f, size + Vector3.one * pad);
                for (int i = 0; i < m.Meshes.Length; i++)
                {
                    if (m.Meshes[i] == null) continue;
                    var go = new GameObject(name + " mesh " + i);
                    go.transform.SetParent(Go.transform, false);
                    var r = go.AddComponent<SkinnedMeshRenderer>();
                    r.sharedMesh = m.Meshes[i];
                    r.bones = Bones;
                    r.rootBone = Go.transform;
                    r.localBounds = bounds;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.skinnedMotionVectors = false;
                    Renderers.Add(r);
                }
            }

            _set = -1;
            SetMaterialSet(materialSet);
            Pose.Reset();
            Apply();
        }

        private string BoneName(int i)
        {
            foreach (var kv in Ch.Rig.ByName) if (kv.Value == i) return kv.Key;
            return "bone " + Ch.Rig.Ids[i];
        }

        /// <summary>The model's material set (CModelFlags::UseShaderSet: the cannon's beam colours, an actor's skin index).</summary>
        public void SetMaterialSet(int set)
        {
            if (set == _set) return;
            _set = set;
            Material[][] mats = Ch.Model.MaterialsOf(set);
            if (mats == null) return;
            int k = 0;
            for (int i = 0; i < Ch.Model.Meshes.Length; i++)
            {
                if (Ch.Model.Meshes[i] == null) continue;
                if (k < Renderers.Count) Renderers[k++].sharedMaterials = mats[i];
            }
        }

        public bool Has(int anim) { return anim >= 0 && Ch.EntryOf(anim) != null; }

        /// <summary>Plays the character's animation <paramref name="anim"/> (cross-fading over <paramref name="blend"/> seconds). False if it has none.</summary>
        public bool Play(int anim, bool loop, float blend = 0.12f, bool restart = true)
        {
            MpAnimSet.Entry e = Ch.EntryOf(anim);
            if (e == null) return false;
            if (!restart && anim == Anim && Loop == loop) return true;
            if (Anim >= 0 && blend > 0f)
            {
                From.CopyLocal(Pose);
                _blend = 0f;
                _blendDur = blend;
            }
            else _blendDur = 0f;

            e.Pick(Random.value, _steps);
            Anim = anim;
            Loop = loop;
            Time = 0f;
            _lastEventT = -1f;
            Duration = 0f;
            foreach (uint id in _steps)
            {
                MpAnimation a = Ch.Anim(id);
                if (a != null) Duration += a.Duration;
            }

            return true;
        }

        public bool Play(string name, bool loop, float blend = 0.12f, bool restart = true)
        {
            return Play(Ch.AnimNumber(name), loop, blend, restart);
        }

        /// <summary>Advances the animation and poses the bones.</summary>
        public void Tick(float dt)
        {
            float before = Time;
            Time += dt * Speed;
            if (Duration > 0f)
            {
                if (Loop && Time >= Duration)
                {
                    FireEvents(before, Duration);
                    Time %= Duration;
                    before = -0.0001f;
                }
                else if (!Loop && Time > Duration) Time = Duration;
            }

            FireEvents(before, Time);
            MoveEffects();
            Sample(Time, Pose);
            if (_blendDur > 0f)
            {
                _blend += dt;
                float w = Mathf.Clamp01(_blend / _blendDur);
                if (w >= 1f) _blendDur = 0f;
                else
                {
                    _work.CopyLocal(From);
                    _work.BlendTo(Pose, w);
                    Pose.CopyLocal(_work);
                }
            }

            Apply();
        }

        /// <summary>The current animation at time <paramref name="t"/> into a pose's local arrays.</summary>
        private void Sample(float t, MpPose into)
        {
            if (_steps.Count == 0) { into.Reset(); return; }
            for (int i = 0; i < _steps.Count; i++)
            {
                MpAnimation a = Ch.Anim(_steps[i]);
                if (a == null) continue;
                if (t <= a.Duration || i == _steps.Count - 1) { into.Sample(a, Mathf.Min(t, a.Duration)); return; }
                t -= a.Duration;
            }

            into.Reset();
        }

        private float _lastEventT = -1f;

        private void FireEvents(float from, float to)
        {
            if (_steps.Count == 0 || to <= from) return;
            float offset = 0f;
            for (int i = 0; i < _steps.Count; i++)
            {
                MpAnimation a = Ch.Anim(_steps[i]);
                float d = a != null ? a.Duration : 0f;
                MpAnimEvents ev;
                if (a != null && Ch.Events.TryGetValue(_steps[i], out ev))
                {
                    if (SoundEvent != null)
                        foreach (MpAnimEvents.Sound s in ev.Sounds)
                            if (Hit(s.N, offset, from, to)) SoundEvent(s);
                    if (ParticleEvent != null)
                        foreach (MpAnimEvents.Particle p in ev.Particles)
                            if (Hit(p.N, offset, from, to)) ParticleEvent(p);
                    if (NamedEvent != null)
                    {
                        foreach (MpAnimEvents.Int32Event n in ev.Ints)
                            if (Hit(n.N, offset, from, to)) NamedEvent(n.N.Name, n.Value);
                        foreach (MpAnimEvents.BoolEvent n in ev.Bools)
                            if (Hit(n.N, offset, from, to)) NamedEvent(n.N.Name, n.Value ? 1 : 0);
                    }
                }

                offset += d;
            }

            _lastEventT = to;
        }

        private bool Hit(MpAnimEvents.Node n, float offset, float from, float to)
        {
            if (n.CharIndex >= 0 && n.CharIndex != Ch.CharIndex) return false;
            float t = n.Time + offset;
            return t > from && t <= to || (from <= 0f && t <= 0f && to > 0f && t >= from);
        }

        /// <summary>Writes the pose (model space) to the bone transforms.</summary>
        public void Apply()
        {
            Pose.Build();
            for (int i = 0; i < Bones.Length; i++)
            {
                int q = i * 4, o = i * 3;
                Bones[i].localPosition = new Vector3(Pose.Off[o], Pose.Off[o + 1], Pose.Off[o + 2]);
                Bones[i].localRotation = new Quaternion(Pose.Rot[q + 1], Pose.Rot[q + 2], Pose.Rot[q + 3], Pose.Rot[q]);
            }
        }

        /// <summary>A named bone / locator (CAnimData::GetLocatorTransform), or null.</summary>
        public Transform Bone(string name)
        {
            int i = Ch.Rig.Find(name);
            return i >= 0 ? Bones[i] : null;
        }

        /// <summary>The first bone whose name contains <paramref name="part"/> (any case), or null.</summary>
        public Transform FindBone(string part)
        {
            for (int i = 0; i < Bones.Length; i++)
                if (Bones[i] != null && Bones[i].name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return Bones[i];
            return null;
        }

        public void SetVisible(bool on)
        {
            foreach (SkinnedMeshRenderer r in Renderers) if (r != null) r.enabled = on;
        }

        public void Destroy()
        {
            StopEffects();
            if (Go != null) Object.Destroy(Go);
        }
    }
}
