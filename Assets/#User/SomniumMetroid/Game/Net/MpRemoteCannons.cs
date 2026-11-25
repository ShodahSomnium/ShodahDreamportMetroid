using System;
using System.Collections.Generic;
using SomniumSpace.Bridge.Player;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The other players' arm cannons, on their own Somnium avatars. Each player in this world gets a full copy of the cannon
    /// (MpArmCannon in remote mode: the same beams' models, materials, suit forearm and scale as this player's own) posed where its
    /// owner holds it - relative to their avatar's right hand (VR) or head (a monitor), as their slot says, put back on that avatar
    /// as THIS client draws it (so it sits in their hand whatever their world's slide). The cannon runs the game's own state machine
    /// from their replicated inputs: the beam they hold (the beam-change animation and hologram), each fire and missile press (the
    /// shoot animation, muzzle flash and the shot itself - the wave beam's three - from its own muzzle; missiles with the hatch,
    /// shot and reload), the fire hold (charge-up, charge loop, the charge effects and each beam's secondary effects, the charged
    /// shot on release), the scan visor's holster; their sounds play at the cannon. Their shots are ghosts here: their hits are
    /// theirs to report. In VR their avatar's right hand (and forearm in forearm mode) is hidden inside the cannon the way this
    /// player's is (MpAvatarArm on their avatar); morphed in third person, their whole avatar is hidden and the morph ball rolls
    /// where their game has it, placed through their world's origin so it is under their avatar as they see it.
    /// </summary>
    public sealed class MpRemoteCannons
    {
        private sealed class View
        {
            public int PlayerId;
            public string Name;
            public MpPlayerState State;
            public MpArmCannon Cannon;
            public MpAvatarArm Arm;
            public Transform BallRoot;
            public MpAnimator Ball;
            public int LastFire, LastMissile;
            public bool Started, LastHeld, Visible, Logged;
            public Vector3 BallAt, BallPrev;
            public float Seen;
        }

        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private readonly List<int> _drop = new List<int>();
        private readonly MpGame _game;
        private MpProjectiles _shots;

        /// <summary>Lab: the stand-in avatar's anchor for a remote (head when true, else right hand) - the editor has no remote Somnium avatars.</summary>
        public static Func<MpRemote, bool, Transform> LabAnchorOf;
        /// <summary>Lab: hide the stand-in avatar's hand though its owner is on a monitor (to see the hiding work).</summary>
        public static bool LabForceHide;
        public static int LabShown, LabPresses, LabMissiles;
        public static MpArmCannon LabLast;

        public MpRemoteCannons(MpGame game) { _game = game; }

        public bool HasCannon(int playerId)
        {
            View v;
            return _views.TryGetValue(playerId, out v) && v.Cannon != null && v.Visible;
        }

        private static Transform Anchor(MpRemote r, bool onHead)
        {
            if (LabAnchorOf != null) return LabAnchorOf(r, onHead);
            try
            {
                ISomniumPlayer p = r.Player;
                var body = p != null && p.References != null ? p.References.Body : null;
                if (body == null) return null;
                return onHead ? body.Head : body.RightHand;
            }
            catch (Exception) { return null; }
        }

        public void Tick(float dt, IReadOnlyList<MpRemote> remotes)
        {
            float now = Time.unscaledTime;
            int pak = MpNetWire.PakHash(_game.CurrentPak);
            int shown = 0;
            if (_shots == null && _game.GunAssets != null) _shots = new MpProjectiles(_game.GunAssets) { AllGhost = true };
            foreach (MpRemote r in remotes)
            {
                int[] s = r.Ints;
                int flags = s[MpNetWire.PlayerSlot.AtFlags];
                bool here = s[MpNetWire.PlayerSlot.AtStatus] == MpNetWire.PlayerSlot.StatusPlaying && s[MpNetWire.PlayerSlot.AtPak] == pak
                    && (flags & MpNetWire.PlayerSlot.FlagJoined) != 0 && MpSpace.Root != null;
                View v;
                if (!_views.TryGetValue(r.PlayerId, out v))
                {
                    if (!here) continue;
                    v = new View { PlayerId = r.PlayerId, Name = r.Name, State = new MpPlayerState(), Arm = new MpAvatarArm() };
                    _views[r.PlayerId] = v;
                }

                v.Seen = now;
                v.Name = r.Name;
                int cf, fires, missiles;
                float scale;
                Vector3 relPos;
                Quaternion relRot;
                MpNetWire.ReadCannon(s, out cf, out scale, out fires, out missiles, out relPos, out relRot);
                bool onHead = (cf & MpNetWire.PlayerSlot.CannonOnHead) != 0;
                Transform anchor = Anchor(r, onHead);
                Transform head = Anchor(r, true);
                bool cinema = (flags & MpNetWire.PlayerSlot.FlagCinema) != 0;
                bool morphed = (flags & MpNetWire.PlayerSlot.FlagMorphed) != 0;

                // the cannon (made once the gun is ready here)
                if (v.Cannon == null && _game.GunAssets != null && _shots != null)
                {
                    SyncState(v.State, s);
                    v.State.Beam = Mathf.Clamp(s[MpNetWire.PlayerSlot.AtBeam], 0, 3);
                    v.Cannon = new MpArmCannon(_game.GunAssets, v.State, _shots) { Remote = true };
                    v.Cannon.Root.name = "Arm Cannon (" + r.Name + ")";
                    v.Cannon.SetVisible(false);
                }

                if (!v.Started) { v.LastFire = fires; v.LastMissile = missiles; v.Started = true; }
                bool visible = here && !cinema && v.Cannon != null && anchor != null && (cf & MpNetWire.PlayerSlot.CannonPosed) != 0 && (cf & MpNetWire.PlayerSlot.CannonShown) != 0;
                v.Visible = visible;
                if (v.Cannon != null)
                {
                    MpArmCannon c = v.Cannon;
                    SyncState(v.State, s);
                    c.RemoteScale = scale;
                    c.RemoteSuitArm = (cf & MpNetWire.PlayerSlot.CannonSuitArm) != 0;
                    c.Holstered = (cf & MpNetWire.PlayerSlot.CannonHolstered) != 0;
                    int beam = Mathf.Clamp(s[MpNetWire.PlayerSlot.AtBeam], 0, 3);
                    if (beam != v.State.Beam && !c.Changing) { v.State.Items[beam] = 1; c.ChangeBeam(beam); }
                    c.SetVisible(visible);
                    if (anchor != null) c.Pose(anchor.position + anchor.rotation * relPos, anchor.rotation * relRot);
                    // each new press, one a frame (a quick double press arrives as two)
                    bool pressed = false, missile = false;
                    if (fires != v.LastFire) { v.LastFire = (v.LastFire + 1) & 0xFFFF; pressed = here; LabPresses++; }
                    if (missiles != v.LastMissile) { v.LastMissile = (v.LastMissile + 1) & 0xFFFF; missile = here; LabMissiles++; }
                    bool held = here && (cf & MpNetWire.PlayerSlot.CannonFireHeld) != 0;
                    bool released = v.LastHeld && !held;
                    v.LastHeld = held;
                    bool canFire = visible && (cf & MpNetWire.PlayerSlot.CannonCanFire) != 0;
                    c.Tick(dt, pressed && canFire, held && canFire, released, missile && canFire, canFire);
                    if (visible) { shown++; LabLast = c; }
                    if (visible && !v.Logged)
                    {
                        v.Logged = true;
                        MpLog.Info("Co-op: " + r.Name + "'s arm cannon on their avatar (at their " + (onHead ? "head: a monitor" : "right hand: VR") + ")");
                    }
                }

                // their hand inside the cannon (VR), their whole avatar while they roll in third person
                bool vr = (flags & MpNetWire.PlayerSlot.FlagVr) != 0;
                v.Arm.TargetHead = head;
                if (head != null)
                {
                    v.Arm.Tick(visible && (vr || LabForceHide), (cf & MpNetWire.PlayerSlot.CannonForearm) != 0);
                    v.Arm.TickBody(here && morphed && (cf & MpNetWire.PlayerSlot.CannonBodyHidden) != 0);
                }

                TickBall(v, r, here && morphed && !cinema, dt);
            }

            if (_shots != null) _shots.Tick(dt);
            LabShown = shown;
            _drop.Clear();
            foreach (var kv in _views) if (now - kv.Value.Seen > 2f) _drop.Add(kv.Key);
            foreach (int id in _drop) { Drop(_views[id]); _views.Remove(id); }
        }

        /// <summary>Their copy of the cannon's state: the game's unlocks (everyone has the master's), their suit, missiles enough to show their own.</summary>
        private void SyncState(MpPlayerState rs, int[] s)
        {
            MpPlayerState mine = _game.State;
            if (mine != null) Array.Copy(mine.Items, rs.Items, rs.Items.Length);
            rs.Items[MpPlayerState.ItemCharge] = 1;
            for (int b = 0; b < 4; b++) rs.Items[b] = 1;
            rs.Items[MpPlayerState.ItemVaria] = rs.Items[MpPlayerState.ItemGravity] = rs.Items[MpPlayerState.ItemPhazon] = 0;
            switch (s[MpNetWire.PlayerSlot.AtSuit])
            {
                case MpPlayerState.SuitGravity: rs.Items[MpPlayerState.ItemGravity] = 1; break;
                case MpPlayerState.SuitVaria: rs.Items[MpPlayerState.ItemVaria] = 1; break;
                case MpPlayerState.SuitPhazon: rs.Items[MpPlayerState.ItemPhazon] = 1; break;
            }

            rs.MissileCapacity = 99;
            rs.Missiles = 99;
            rs.Energy = 99f;
        }

        /// <summary>Their morph ball where their game has it, through their world's origin (under their avatar as they see it), rolling.</summary>
        private void TickBall(View v, MpRemote r, bool on, float dt)
        {
            if (!on)
            {
                if (v.BallRoot != null && v.BallRoot.gameObject.activeSelf) v.BallRoot.gameObject.SetActive(false);
                return;
            }

            if (v.BallRoot == null)
            {
                v.BallRoot = new GameObject("Co-op morph ball (" + r.Name + ")").transform;
                v.BallRoot.SetParent(MpSpace.Root, false);
                MpCharacter bc = _game.BallAncs != 0 ? MpCharacter.Get(_game.BallAncs, 0) : null;
                if (bc != null && bc.Model != null)
                {
                    bc.Upload(true);
                    v.Ball = new MpAnimator(bc, "Morph Ball model", v.BallRoot);
                    v.Ball.Play(0, true, 0f);
                    v.Ball.Tick(0f);
                }
                else
                {
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    UnityEngine.Object.Destroy(sphere.GetComponent<Collider>());
                    sphere.transform.SetParent(v.BallRoot, false);
                    sphere.transform.localScale = Vector3.one * MpMorphBall.Radius * 2f;
                }
            }

            if (!v.BallRoot.gameObject.activeSelf) { v.BallRoot.gameObject.SetActive(true); v.BallAt = Vector3.zero; }
            int[] s = r.Ints;
            var feet = new Vector3(MpNetWire.Unfix(s[MpNetWire.PlayerSlot.AtX]), MpNetWire.Unfix(s[MpNetWire.PlayerSlot.AtY]), MpNetWire.Unfix(s[MpNetWire.PlayerSlot.AtZ]));
            Vector3 at = MpSpace.ToGame(MpGame.TheirsToUnity(r, feet + new Vector3(0f, 0f, MpMorphBall.Radius)));
            v.BallAt = v.BallAt == Vector3.zero || (at - v.BallAt).sqrMagnitude > 64f ? at : Vector3.Lerp(v.BallAt, at, Mathf.Clamp01(dt * 12f));
            Vector3 step = v.BallAt - v.BallPrev;
            v.BallPrev = v.BallAt;
            v.BallRoot.localPosition = v.BallAt;
            // rolling: about the axis across the motion, the distance over the radius
            Vector3 flat = new Vector3(step.x, step.y, 0f);
            if (flat.sqrMagnitude > 1e-6f)
            {
                Vector3 axis = Vector3.Cross(Vector3.forward, flat.normalized);
                v.BallRoot.localRotation = Quaternion.AngleAxis(flat.magnitude / MpMorphBall.Radius * Mathf.Rad2Deg, axis) * v.BallRoot.localRotation;
            }

            if (v.Ball != null) v.Ball.Tick(dt);
        }

        private static void Drop(View v)
        {
            if (v.Cannon != null) v.Cannon.Destroy();
            if (v.Arm != null) v.Arm.Destroy();
            if (v.Ball != null) v.Ball.Destroy();
            if (v.BallRoot != null) UnityEngine.Object.Destroy(v.BallRoot.gameObject);
        }

        public void Destroy()
        {
            foreach (var kv in _views) Drop(kv.Value);
            _views.Clear();
            if (_shots != null) _shots.Clear();
        }
    }
}
