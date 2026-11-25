using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Samus's arm cannon, animated the game's way (CPlayerGun + CGunWeapon, GameCube rules). Every beam is its ANCS character 0: one
    /// shared cannon model (CMDL 19AE626C) skinned over the gun rig, posed by that beam's own animations - its base position is the
    /// beam's shape (the Wave Beam's prongs, the Plasma Beam's fins) - and drawn in the beam's material set (CGunWeapon::mShaderIdx =
    /// the beam). The animation numbers are CGunWeapon::skAnimTypeList: 0 base position, 4 shoot, 1 charge up, 2 charge loop,
    /// 3 charge shoot (a sequence back to base), 5 / 6 missile hatch away / open, 7 missile shoot, 8 missile reload, 9 / 10 from /
    /// to beam, 14 / 15 from / to holster, 16+ fidgets. Samus's forearm (PowerArm / VariaArm, EPlayerSuit) hangs at the cannon's
    /// "elbow" locator and closes the cannon's open back; the muzzle is the "LBEAM" locator.
    ///
    /// Firing follows CPlayerGun: a shot on the press (the beam's cool-down), the charge builds while held (charge-up animation and
    /// sound at 0.25 of the factor, the charge effect and loop animation from 1.0), a release past that fires the charged shot with
    /// the charge-shoot animation. Missiles (CPlayerGun::FireSecondary): the first press fires at once and plays the missile shoot,
    /// then the reload (the next missile waits for both), 7 s without a missile or a press of fire closes the hatch (the fire press
    /// that closes it does not shoot, as on the GameCube). A beam change plays "from beam", swaps through the hologram transition and
    /// plays "to beam" on the new one. The game's idle fidgets are not played. Held on the right controller in VR (the
    /// hand inside the cannon's back, the barrel along the controller's aim, shifted by the player's own fit from the tablet) or by
    /// the eye on a monitor at the PlayerGun tweak's gun position.
    /// </summary>
    public sealed class MpArmCannon
    {
        public readonly MpGunAssets Assets;
        public readonly MpPlayerState State;
        public readonly MpProjectiles Shots;
        public Transform Root;          // Unity: forward = the barrel
        private Transform _model;       // game space (Y forward, Z up), under Root
        private readonly MpAnimator[] _beam = new MpAnimator[4];
        private readonly GameObject[] _static = new GameObject[4];     // fallback: a beam without animations shows its bind pose
        private GameObject _arm;
        private int _armSuit = -1, _armBeam = -1;
        private int _shown = -1;
        private float _cool, _recoil;
        public float Charge;            // 0..1
        private object _chargeSfx;      // the beam's charge loop (keyed off on release)
        public bool Holstered;
        private float _scale = 1f;

        /// <summary>The hand's place in the cannon (model space, game units: the hand sits inside the barrel's back).</summary>
        public static Vector3 HandInModel = new Vector3(0f, 0.16f, -0.01f);

        // CGunWeapon::skAnimTypeList by NWeaponTypes::EGunAnimType, as animation numbers
        public const int AnimBase = 0, AnimChargeUp = 1, AnimChargeLoop = 2, AnimChargeShoot = 3, AnimShoot = 4, AnimFromMissile = 5,
            AnimToMissile = 6, AnimMissileShoot = 7, AnimMissileReload = 8, AnimFromBeam = 9, AnimToBeam = 10, AnimFromHolster = 14,
            AnimToHolster = 15, AnimFidget = 16;

        // CPlayerGun GameCube sound tables (by EBeamId)
        private static readonly int[] ToMissileSfx = { 1823, 1829, 1850, 1852 };
        private static readonly int[] FromMissileSfx = { 1824, 1849, 1851, 1853 };
        private static readonly int[] FromBeamSfx = { -1, 1822, 1828, 1826 };
        private const int MalfunctionSfx = 1781;
        private const float MissileExitTime = 7f, FidgetAfter = 10f;

        /// <summary>Co-op: another player's cannon (MpRemoteCannons) - their fit and forearm, its sounds at the cannon, no haptics here.</summary>
        public bool Remote;
        public float RemoteScale = 1f;
        public bool RemoteSuitArm = true;

        private float CannonScale { get { return Remote ? RemoteScale : MpSettings.CannonScale; } }
        private bool SuitArmOn { get { return Remote ? RemoteSuitArm : MpSettings.SuitArm; } }

        /// <summary>A gun sound: the player's own in their ears (as the game plays them), another player's at their cannon.</summary>
        private void Sfx(int id)
        {
            MpGame g = MpGame.Instance;
            if (g == null) return;
            if (!Remote) { g.PlayUiSfx(id); return; }
            if (id == MpSfx.ChargeStart) id = MpSfx.ChargeLoop[State.Beam];
            g.PlaySfxAt(id, MpSpace.ToGame(Root.position), 1f, false, 40f);
        }

        public MpArmCannon(MpGunAssets assets, MpPlayerState state, MpProjectiles shots)
        {
            Assets = assets;
            State = state;
            Shots = shots;
            Root = new GameObject("Arm Cannon").transform;
            _model = new GameObject("Arm Cannon model").transform;
            _model.SetParent(Root, false);
            Place(1f);
            for (int b = 0; b < 4; b++)
            {
                MpGunAssets.Beam beam = assets.Beams[b];
                if (beam == null) continue;
                if (beam.Char != null && beam.Char.Model != null && beam.Char.Model.Meshes != null)
                {
                    _beam[b] = new MpAnimator(beam.Char, beam.Name + " beam", _model, b);
                    foreach (SkinnedMeshRenderer r in _beam[b].Renderers) Prepare(r);
                    if (b == MpPlayerState.BeamPower) AddCore(_beam[b]);      // the others' open plates showed it as a black cylinder
                    _beam[b].Play(AnimBase, false, 0f);
                    _beam[b].Tick(0f);
                    _beam[b].Go.SetActive(false);
                }
                else if (beam.Model != null)
                {
                    _static[b] = beam.Model.Instantiate(beam.Name + " beam", _model);
                    foreach (MeshRenderer r in _static[b].GetComponentsInChildren<MeshRenderer>()) Prepare(r);
                    _static[b].SetActive(false);
                }
            }

            Show(state.Beam);
            Settle(AnimBase);
        }

        /// <summary>
        /// The cannon's materials take the gun's lighting (the game lights the gun with its own lights; a fixed ambient here) and draw
        /// both faces: the game's cannon and forearm are open shells made for a camera that never sees their back (CPlayerGun draws
        /// them in front of the eye only); in VR the player can look all round, and the inner faces close the shells.
        /// </summary>
        private static void Prepare(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (Material m in r.sharedMaterials)
                if (m != null)
                {
                    m.SetColor("_Ambient", new Color(0.62f, 0.62f, 0.62f, 1f));
                    m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                }
        }

        /// <summary>
        /// A closed dark core inside the cannon's rear body (a capped cylinder along the barrel, skinned to the bone that carries
        /// the rear body), so nothing behind the cannon shows through its open back or the gaps between its plates from any angle.
        /// </summary>
        private static void AddCore(MpAnimator a)
        {
            MpModel m = a.Ch.Model;
            if (m.WeightsOf == null || m.PosOf == null || m.PosOf.Length == 0) return;
            // the bone most of the rear body (model y below 0.2) follows
            var votes = new int[a.Bones.Length];
            for (int k = 0; k < m.PosOf.Length; k++)
                for (int v = 0; v < m.PosOf[k].Length; v++)
                    if (m.PosOf[k][v].y < 0.2f && m.WeightsOf[k][v].boneIndex0 < votes.Length) votes[m.WeightsOf[k][v].boneIndex0]++;
            int bone = 0;
            for (int i = 1; i < votes.Length; i++) if (votes[i] > votes[bone]) bone = i;

            const int Sides = 16;
            const float Radius = 0.068f, Y0 = 0.035f, Y1 = 0.3f, Z = 0.004f;
            var verts = new Vector3[Sides * 2 + 2];
            var tris = new int[Sides * 12];
            for (int s = 0; s < Sides; s++)
            {
                float ang = s * Mathf.PI * 2f / Sides;
                float x = Mathf.Cos(ang) * Radius, z = Mathf.Sin(ang) * Radius + Z;
                verts[s] = new Vector3(x, Y0, z);
                verts[Sides + s] = new Vector3(x, Y1, z);
            }

            int c0 = Sides * 2, c1 = c0 + 1, t = 0;
            verts[c0] = new Vector3(0f, Y0, Z);
            verts[c1] = new Vector3(0f, Y1, Z);
            for (int s = 0; s < Sides; s++)
            {
                int n = (s + 1) % Sides;
                tris[t++] = s; tris[t++] = n; tris[t++] = Sides + s;
                tris[t++] = n; tris[t++] = Sides + n; tris[t++] = Sides + s;
                tris[t++] = c0; tris[t++] = n; tris[t++] = s;
                tris[t++] = c1; tris[t++] = Sides + s; tris[t++] = Sides + n;
            }

            var mesh = new Mesh { name = "Arm cannon core" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            var w = new BoneWeight[verts.Length];
            for (int i = 0; i < w.Length; i++) w[i] = new BoneWeight { boneIndex0 = bone, weight0 = 1f };
            mesh.boneWeights = w;
            mesh.bindposes = m.BindPoses;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Material carrier = MpGame.FxMaterial;
            if (carrier == null) return;
            var mat = new Material(carrier) { name = "Arm cannon core" };
            mat.SetColor("_Color", new Color(0.05f, 0.06f, 0.06f, 1f));
            mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.renderQueue = 2501;      // past 2500: URP depth priming would hide an effects-shader draw in the opaque range
            var go = new GameObject("Arm cannon core");
            go.transform.SetParent(a.Go.transform, false);
            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = mesh;
            r.bones = a.Bones;
            r.rootBone = a.Go.transform;
            r.localBounds = new Bounds(new Vector3(0f, 0.2f, 0f), Vector3.one * 0.8f);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The model node: the hand point (HandInModel) at the root's origin, at the player's chosen size.</summary>
        private void Place(float scale)
        {
            _scale = scale;
            float s = MpSpace.Scale * scale;
            var sc = new Vector3(s, -s, s);
            _model.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            _model.localScale = sc;
            _model.localPosition = -(Quaternion.Euler(-90f, 0f, 0f) * Vector3.Scale(sc, HandInModel));
        }

        private MpAnimator Cur { get { return _shown >= 0 ? _beam[_shown] : null; } }

        private void Show(int beam)
        {
            if (_shown == beam) return;
            for (int b = 0; b < 4; b++)
            {
                if (_beam[b] != null) _beam[b].Go.SetActive(b == beam);
                if (_static[b] != null) _static[b].SetActive(b == beam);
            }

            _shown = beam;
            UpdateArm();
        }

        /// <summary>CGunWeapon::LoadSuitArm / Draw: the suit's forearm at the shown beam's elbow locator (re-hung when the beam or suit changes).</summary>
        private void UpdateArm()
        {
            int suit = State.Suit;
            if (suit < 0 || suit >= Assets.SuitArms.Length || Assets.SuitArms[suit] == null) suit = 0;
            MpAnimator cur = Cur;
            Transform elbow = cur != null ? cur.Bone("elbow") : null;
            if (suit == _armSuit && _shown == _armBeam && _arm != null)
            {
                _arm.SetActive(SuitArmOn);
                return;
            }

            if (_arm != null) Object.Destroy(_arm);
            _arm = null;
            _armSuit = suit;
            _armBeam = _shown;
            MpModel m = Assets.SuitArms[suit];
            if (m == null || m.Meshes == null) return;
            _arm = m.Instantiate("Suit forearm (" + MpGunAssets.SuitArmNames[suit] + ")", elbow != null ? elbow : _model);
            foreach (MeshRenderer r in _arm.GetComponentsInChildren<MeshRenderer>()) Prepare(r);
            _arm.SetActive(SuitArmOn);
        }

        /// <summary>The cannon's tip in Unity space: the shown beam's LBEAM locator (CGunWeapon::skMuzzleLocator).</summary>
        public Vector3 Muzzle
        {
            get
            {
                MpAnimator cur = Cur;
                Transform lbeam = cur != null ? cur.Bone("LBEAM") : null;
                if (lbeam != null) return lbeam.position;
                MpGunAssets.Beam b = Assets.Beams[State.Beam];
                float front = b != null && b.Model != null ? b.Model.Max.y : 0.58f;
                return _model.TransformPoint(new Vector3(0f, front + 0.04f, 0f));
            }
        }

        public Vector3 Forward { get { return Root.forward; } }

        /// <summary>Places the cannon: <paramref name="pos"/> = where the hand is, <paramref name="rot"/> = the aim (forward = barrel).</summary>
        public void Pose(Vector3 pos, Quaternion rot)
        {
            float kick = Cur == null ? _recoil : 0f;     // the shoot animations carry the recoil; the bind-pose fallback kicks by hand
            Vector3 shake = Vector3.zero;
            if (_phase != Phase.NotCharging)
            {
                // CPlayerGun chargeShakeTbl {-0.001, 0, 0.001} x the charge factor, game units at the game's gun scale 2
                float k = MpSpace.Scale * _scale * 2f * _chargeFactor;
                shake = rot * new Vector3(ShakeTbl[Random.Range(0, 3)] * k, ShakeTbl[Random.Range(0, 3)] * k, 0f);
            }

            Root.SetPositionAndRotation(pos - rot * Vector3.forward * (0.035f * kick) + shake, rot * Quaternion.Euler(-6f * kick, 0f, 0f));
        }

        private static readonly float[] ShakeTbl = { -0.001f, 0f, 0.001f };

        public void SetVisible(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        // ------------------------------------------------------------------ animation

        private void PlayAnim(int anim, bool loop, float blend = 0.1f)
        {
            MpAnimator cur = Cur;
            if (cur == null) return;
            if (!cur.Play(anim, loop, blend) && anim != AnimBase) cur.Play(AnimBase, false, blend);
            _idleT = 0f;
        }

        /// <summary>Snaps the shown beam to an animation's first frame (no blend).</summary>
        private void Settle(int anim)
        {
            MpAnimator cur = Cur;
            if (cur == null) return;
            cur.Play(anim, false, 0f);
            cur.Tick(0f);
        }

        private bool AnimOver { get { MpAnimator c = Cur; return c == null || c.Done; } }

        // ------------------------------------------------------------------ beam change (CPlayerGun::HandleBeamChange / ChangeWeapon)

        private enum Next { StatusQuo, ChangeWeapon, ToBeam, MissileReload, MissileShotDone, ExitMissile }
        private Next _next;
        private int _nextBeam = -1;
        private float _wipeT;
        private bool _missileMode, _missileReady = true;
        private float _missileExitT, _idleT;
        private MpEffects.Effect _holo;

        public bool Changing { get { return _next == Next.ChangeWeapon || _next == Next.ToBeam; } }

        /// <summary>A beam change: the old beam plays "from beam", the hologram wipe swaps it, the new one plays "to beam".</summary>
        public void ChangeBeam(int beam)
        {
            if (beam == State.Beam && _next != Next.ChangeWeapon) return;
            if (!State.HasBeam(beam)) return;
            CancelCharge();
            _missileMode = false;
            _missileReady = true;
            _nextBeam = beam;
            _next = Next.ChangeWeapon;
            _wipeT = 0f;
            PlayAnim(AnimFromBeam, false);
            if (FromBeamSfx[State.Beam] >= 0) Sfx(FromBeamSfx[State.Beam]);
            Sfx(MpSfx.BeamChange);
        }

        private float TransformTime { get { return Assets.Tweak != null ? Mathf.Clamp(Assets.Tweak.GunTransformTime, 0.15f, 1f) : 0.3f; } }

        private void TickChange(float dt)
        {
            _wipeT += dt;
            float half = TransformTime * 0.5f;
            if (_next == Next.ChangeWeapon)
            {
                // the old beam folds away (its from-beam animation, a short shrink), then the hologram brings the new one
                float t = Mathf.Clamp01(_wipeT / half);
                Place(CannonScale * (1f - 0.18f * t));
                if (_wipeT < half && !(AnimOver && _wipeT > 0.05f)) return;
                State.Beam = _nextBeam;
                Show(_nextBeam);
                Settle(AnimFromBeam);
                PlayAnim(AnimToBeam, false, 0.05f);
                if (MpSfx.IntoBeam[State.Beam] >= 0) Sfx(MpSfx.IntoBeam[State.Beam]);
                Sfx(MpSfx.BeamChangeDone);
                if (_holo != null) _holo.Kill();
                _holo = MpEffects.Spawn(Assets.HoloPart, MpSpace.ToGame(Root.position), MpEffects.GameOrientation(Root.rotation));
                if (_holo != null) { _holo.Persistent = true; _holo.OwnerUpdates = true; }
                _next = Next.ToBeam;
                _wipeT = 0f;
                return;
            }

            float u = Mathf.Clamp01(_wipeT / half);
            Place(CannonScale * (0.82f + 0.18f * u));
            if (_wipeT < half || !AnimOver) return;
            Place(CannonScale);
            PlayAnim(AnimBase, false);
            _cool = Assets.Tweak != null ? Assets.Tweak.CoolDown[State.Beam] : 0.1f;
            _next = Next.StatusQuo;
        }

        // ------------------------------------------------------------------ the frame

        public void Tick(float dt, bool firePressed, bool fireHeld, bool fireReleased, bool missilePressed, bool canFire)
        {
            _cool = Mathf.Max(0f, _cool - dt);
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt / 0.12f);
            if (!Changing && Mathf.Abs(_scale - CannonScale) > 1e-4f) Place(CannonScale);
            if (_arm != null && _arm.activeSelf != SuitArmOn) _arm.SetActive(SuitArmOn);
            if (_armSuit != State.Suit) UpdateArm();
            MpAnimator cur = Cur;
            if (cur != null) cur.Tick(dt);
            TickHolo(dt);

            if (Changing)
            {
                TickChange(dt);
                UpdateMuzzleFx(dt);
                UpdateSecondFx(dt);
                return;
            }

            Show(State.Beam);
            if (!canFire || Holstered)
            {
                CancelCharge();
                UpdateMuzzleFx(dt);
                UpdateSecondFx(dt);
                return;
            }

            TickMissileStates(dt);
            bool acted = firePressed || missilePressed || fireHeld;
            if (acted) _idleT = 0f;

            // FireSecondary: missiles while the hatch is free (CPlayerGun weapon state 0x4)
            if (missilePressed && _phase == Phase.NotCharging && _next == Next.StatusQuo)
            {
                if (State.Missiles > 0 && State.MissileCapacity > 0)
                {
                    if (_missileReady)
                    {
                        _missileMode = true;
                        State.Missiles--;
                        _recoil = 1.4f;
                        Shots.FireMissile(Muzzle, Forward);
                        _missileExitT = MissileExitTime;
                        PlayAnim(AnimMissileShoot, false, 0.05f);
                        _missileReady = false;
                        _next = State.Missiles > 0 ? Next.MissileReload : Next.MissileShotDone;
                        Haptic(0.7f, 0.08f);
                    }
                }
                else Sfx(MpSfx.MissileEmpty);
            }

            // CPlayerGun::ProcessChargeState: a press fires a normal shot and (charge beam owned) requests the charge; in missile
            // mode the press only closes the hatch (ExitMissile)
            if (firePressed && _phase == Phase.NotCharging)
            {
                if (_missileMode)
                {
                    if (_next != Next.ExitMissile) ExitMissile();
                }
                else if (_cool <= 0f && _next == Next.StatusQuo)
                {
                    float cd = Assets.Tweak != null ? Assets.Tweak.CoolDown[State.Beam] : 0.15f;
                    _cool = Mathf.Max(0.08f, cd);
                    _recoil = 1f;
                    FirePrimary(false);
                    PlayAnim(AnimShoot, false, 0.03f);
                    Haptic(0.35f, 0.05f);
                    if (State.HasCharge) _phase = Phase.ChargeRequested;
                }
            }

            if (fireReleased && _phase != Phase.NotCharging)
            {
                // ResetCharged: a charge past FxGrowing fires the charged shot (damage x the charge factor), an early release just ends it
                if (_phase >= Phase.FxGrowing)
                {
                    _recoil = 1.6f;
                    FirePrimary(true);
                    _cool = 0.25f;
                    PlayAnim(AnimChargeShoot, false, 0.03f);
                    Haptic(0.9f, 0.12f);
                    CancelCharge(false);
                }
                else CancelCharge();
            }
            else if (_phase != Phase.NotCharging && !fireHeld) CancelCharge();

            UpdateChargeState(dt);
            UpdateMuzzleFx(dt);
            UpdateSecondFx(dt);
            // the game's idle fidgets are off (the player: "it looks bad")
        }

        private void Haptic(float strength, float seconds)
        {
            if (Remote) return;
            if (MpXr.Present && MpSettings.Haptics > 0f) MpXr.Pulse(true, strength * MpSettings.Haptics, seconds);
        }

        /// <summary>CPlayerGun::Update's next-state switch: each step waits for the shown animation to end.</summary>
        private void TickMissileStates(float dt)
        {
            if (_missileMode && _next == Next.StatusQuo)
            {
                _missileExitT -= dt;
                if (_missileExitT <= 0f) ExitMissile();
            }

            if (_next == Next.StatusQuo || !AnimOver) return;
            switch (_next)
            {
                case Next.MissileReload:
                    PlayAnim(AnimMissileReload, false, 0.05f);
                    Sfx(MpSfx.MissileLoad);
                    _next = Next.MissileShotDone;
                    return;
                case Next.MissileShotDone:
                    _missileReady = true;
                    _next = Next.StatusQuo;
                    return;
                case Next.ExitMissile:
                    _missileMode = false;
                    _missileReady = true;
                    _cool = Assets.Tweak != null ? Assets.Tweak.CoolDown[State.Beam] : 0.1f;
                    PlayAnim(AnimBase, false, 0.1f);
                    _next = Next.StatusQuo;
                    return;
            }
        }

        /// <summary>CPlayerGun::ExitMissile: the hatch closes ("missile away") with the beam's switch sound.</summary>
        private void ExitMissile()
        {
            _next = Next.ExitMissile;
            PlayAnim(AnimFromMissile, false, 0.05f);
            Sfx(FromMissileSfx[State.Beam]);
        }

        /// <summary>CGunController fidgets, simplified: after a while untouched the cannon plays one of its beam's fidget animations.</summary>
        private void TickIdle(float dt)
        {
            MpAnimator cur = Cur;
            if (cur == null || _phase != Phase.NotCharging || _missileMode || _next != Next.StatusQuo) { _idleT = 0f; return; }
            _idleT += dt;
            if (_idleT < FidgetAfter) return;
            _idleT = -Random.Range(4f, 10f);
            int pick = Random.Range(0, 3);
            int anim = pick == 0 ? AnimFidget : pick == 1 ? AnimFidget + 2 : AnimFidget + 3;
            if (cur.Has(anim)) PlayAnim(anim, false, 0.25f);
        }


        // ------------------------------------------------------------------ the charge (CPlayerGun::UpdateChargeState, GameCube values)

        private enum Phase { NotCharging, ChargeRequested, AnimAndSfx, FxGrowing, FxGrown }
        private Phase _phase;
        private float _chargeFactor;            // mChargeBeamFactor
        private bool _chargeAnimStarted;
        private const float ChargeFactorScale = 1.8f;                       // CPlayerState::GetMissileComboChargeFactor
        private const float KChargeSpeed = 1f / ChargeFactorScale;          // per second
        private const float KChargeStart = 0.025f / ChargeFactorScale;      // the muzzle effect hides
        private const float KTractorBeamFactor = 0.25f / ChargeFactorScale; // charge-up animation and sound
        private const float KChargeFxStart = 1f / ChargeFactorScale;        // the charge effect; a charged shot is ready

        private void UpdateChargeState(float dt)
        {
            switch (_phase)
            {
                case Phase.ChargeRequested:
                    _chargeFactor = 0f;
                    _chargeAnimStarted = false;
                    _phase = Phase.AnimAndSfx;
                    break;
                case Phase.AnimAndSfx:
                    if (!_chargeAnimStarted)
                    {
                        if (_chargeFactor > KChargeStart && _chargeEffectVisible) _chargeEffectVisible = false;
                        if (_chargeFactor > KTractorBeamFactor)
                        {
                            PlayAnim(AnimChargeUp, false, 0.08f);
                            if (_chargeSfx == null) _chargeSfx = MpGame.Instance.PlaySfxAt(MpSfx.ChargeLoop[State.Beam], Remote ? (Vector3?)MpSpace.ToGame(Root.position) : null, 1f, true, Remote ? 40f : 0f);
                            Haptic(0.25f, 0.05f);
                            _chargeAnimStarted = true;
                        }
                    }
                    else if (_chargeFactor >= KChargeFxStart)
                    {
                        _chargeEffectVisible = true;
                        _chargeAnimStarted = false;
                        _phase = Phase.FxGrowing;
                        EnableChargeFx();
                        PlayAnim(AnimChargeLoop, true, 0.08f);
                        Haptic(0.5f, 0.06f);
                    }

                    break;
                case Phase.FxGrowing:
                    if (_chargeFactor >= 1f) _phase = Phase.FxGrown;
                    break;
            }

            if (_phase > Phase.NotCharging && _phase < Phase.FxGrown) _chargeFactor = Mathf.Min(1f, _chargeFactor + KChargeSpeed * dt);
            Charge = _phase != Phase.NotCharging ? _chargeFactor : 0f;      // GetChargePercentage (the reticle's gauge)
            if (_phase >= Phase.FxGrowing && !Remote && MpXr.Present && MpSettings.Haptics > 0f && Time.frameCount % 6 == 0) MpXr.Pulse(true, 0.08f * MpSettings.Haptics, 0.02f);
        }

        /// <summary>FirePrimary: the shot, and whether the muzzle effect shows (not for a normal plasma shot).</summary>
        private void FirePrimary(bool charged)
        {
            _chargeEffectVisible = State.Beam != 3 || _phase != Phase.NotCharging;
            _muzzleVisT = 0.0625f;
            Shots.FireBeam(State.Beam, charged, Muzzle, Forward, charged ? _chargeFactor : 1f);
        }

        /// <summary>CPlayerGun::ResetCharge / CancelFiring: the charge ends, its effects and sound stop (and the cannon returns to its base pose when the charge-up had begun).</summary>
        public void CancelCharge() { CancelCharge(true); }

        private void CancelCharge(bool toBase)
        {
            bool animStarted = _phase == Phase.AnimAndSfx ? _chargeAnimStarted : _phase > Phase.AnimAndSfx;
            if (_phase != Phase.NotCharging) { ActivateCharge(false); EndSecondFx(); }
            _phase = Phase.NotCharging;
            _chargeFactor = 0f;
            _chargeAnimStarted = false;
            Charge = 0f;
            if (_auxFx != null) { _auxFx.Kill(); _auxFx = null; }
            if (_chargeSfx != null && MpGame.Instance != null) { MpGame.Instance.StopSfx(_chargeSfx); _chargeSfx = null; }
            if (toBase && animStarted && !Changing) PlayAnim(AnimBase, false, 0.12f);
        }

        // ------------------------------------------------------------------ the gun's own effects (CGunWeapon muzzle generators, CPlayerGun aux muzzle)

        private readonly MpEffects.Effect[] _muzzleFx = new MpEffects.Effect[2];    // [0] the beam's Muzzle, [1] its Charge
        private int _muzzleIdx, _fxBeam = -1;
        private MpEffects.Effect _auxFx;
        private bool _chargeEffectVisible;
        private float _muzzleVisT;
        // CPlayerGun draws the beam model and its muzzle / aux systems at kScaleVector 2; this cannon draws the model at 1 (it sits on
        // the hand), so the gun's systems are drawn at 1 too - the same size against the cannon as in the game
        public const float MuzzleFxScale = 1f;

        private MpEffects.Effect NewGen(uint part)
        {
            MpEffects.Effect e = MpEffects.Spawn(part, Vector3.zero, Data.Particles.M3.Identity, true);
            if (e != null) { e.OwnerUpdates = true; e.Hidden = true; }
            return e;
        }

        /// <summary>CGunWeapon::LoadMuzzleFx: the beam's muzzle and charge systems, not emitting, stepped once.</summary>
        private void LoadMuzzleFx()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_muzzleFx[i] != null) _muzzleFx[i].Kill();
                _muzzleFx[i] = NewGen(i == 0 ? Assets.MuzzlePart[State.Beam] : Assets.ChargePart[State.Beam]);
                if (_muzzleFx[i] != null) { _muzzleFx[i].Gen.SetParticleEmission(false); _muzzleFx[i].Update(1f / 60f); }
            }

            _muzzleIdx = 0;
            _fxBeam = State.Beam;
        }

        /// <summary>CGunWeapon::ActivateCharge(enable, false): the shown system stops emitting; charging switches to a new charge system.</summary>
        private void ActivateCharge(bool enable)
        {
            MpEffects.Effect cur = _muzzleFx[_muzzleIdx];
            if (cur != null && cur.Gen != null) cur.Gen.SetParticleEmission(false);
            _muzzleIdx = enable ? 1 : 0;
            if (enable)
            {
                if (_muzzleFx[1] != null) _muzzleFx[1].Kill();
                _muzzleFx[1] = NewGen(Assets.ChargePart[State.Beam]);
            }
        }

        /// <summary>EnableChargeFx: the charge system, and a new aux muzzle system emitting.</summary>
        private void EnableChargeFx()
        {
            ActivateCharge(true);
            if (_auxFx != null) _auxFx.Kill();
            _auxFx = NewGen(Assets.AuxMuzzlePart[State.Beam]);
            if (_auxFx != null) _auxFx.Gen.SetParticleEmission(true);
            StartSecondFx();
        }

        /// <summary>
        /// CPlayerGun's per-frame muzzle step: while the charge effect is visible the shown muzzle system and the aux muzzle ride the
        /// barrel (scale 2), emit and step; the muzzle system draws for 1/16 s after a shot or while the charge grows, the aux muzzle
        /// once fully charged.
        /// </summary>
        private void UpdateMuzzleFx(float dt)
        {
            if (_fxBeam != State.Beam) LoadMuzzleFx();
            if (_muzzleVisT > 0f) _muzzleVisT -= dt;
            Vector3 g = MpSpace.ToGame(Muzzle);
            Data.Particles.M3 o = MpEffects.GameOrientation(Root.rotation);
            bool shown = Root.gameObject.activeInHierarchy;
            float scale = MuzzleFxScale * _scale;
            MpEffects.Effect m = _muzzleFx[_muzzleIdx];
            if (_chargeEffectVisible)
            {
                if (m != null)
                {
                    m.SetGlobal(g, o, scale);
                    m.Gen.SetParticleEmission(true);
                    m.Update(dt);
                }

                if (_auxFx != null)
                {
                    _auxFx.SetGlobal(g, o, scale);
                    _auxFx.Gen.SetParticleEmission(true);
                    _auxFx.Update(dt);
                }
            }

            for (int i = 0; i < 2; i++)
                if (_muzzleFx[i] != null) _muzzleFx[i].Hidden = !(shown && i == _muzzleIdx && _chargeEffectVisible && (_muzzleVisT > 0f || _phase > Phase.AnimAndSfx));
            if (_auxFx != null) _auxFx.Hidden = !(shown && _phase >= Phase.FxGrown);
        }


        // ------------------------------------------------------------------ the beams' secondary effects (CPowerBeam / CIceBeam / CWaveBeam / CPlasmaBeam)

        private MpEffects.Effect _second, _iceSmoke;
        private MpElectricFx _secondElec;
        private int _secondBeam = -1;
        private bool _secondEnding;

        /// <summary>EnableSecondaryFx(kSFT_Charge): the beam's own charge effect at the cannon's elbow (the wave's is an electric arc).</summary>
        private void StartSecondFx()
        {
            KillSecondFx();
            _secondBeam = State.Beam;
            _secondEnding = false;
            if (State.Beam == MpPlayerState.BeamWave) _secondElec = MpElectricFx.Spawn(Assets.WaveChargeElsc);
            else
            {
                _second = NewGen(Assets.SecondPart[State.Beam]);
                if (_second != null) _second.Gen.SetParticleEmission(true);
            }
        }

        /// <summary>EnableSecondaryFx(kSFT_CancelCharge): the ice beam swaps to its release effect (Ice2nd_2), the wave to its
        /// cancel arc (Wave2nd_2, 3 s), power and plasma stop emitting and fade out.</summary>
        private void EndSecondFx()
        {
            if (_secondBeam < 0 || _secondEnding) return;
            _secondEnding = true;
            if (_secondBeam == MpPlayerState.BeamIce)
            {
                if (_second != null) _second.Kill();
                _second = NewGen(Assets.IceEndPart);
                if (_second != null) _second.Gen.SetParticleEmission(true);
            }
            else if (_secondBeam == MpPlayerState.BeamWave)
            {
                if (_secondElec != null) _secondElec.Kill();
                _secondElec = MpElectricFx.Spawn(Assets.WaveCancelElsc);
                if (_secondElec != null) _secondElec.StopAfter = 3f;
            }
            else if (_second != null) _second.Gen.SetParticleEmission(false);
        }

        private void KillSecondFx()
        {
            if (_second != null) { _second.Kill(); _second = null; }
            if (_secondElec != null) { _secondElec.Kill(); _secondElec = null; }
            _secondBeam = -1;
        }

        /// <summary>UpdateGunFx: the secondary effect rides the elbow locator; the ice beam's smoke rides the muzzle all the time.</summary>
        private void UpdateSecondFx(float dt)
        {
            bool shown = Root.gameObject.activeInHierarchy;
            MpAnimator cur = Cur;
            Transform elbow = cur != null ? cur.Bone("elbow") : null;
            Vector3 at = MpSpace.ToGame(elbow != null ? elbow.position : Root.position);
            Data.Particles.M3 o = MpEffects.GameOrientation(Root.rotation);
            float scale = MuzzleFxScale * _scale;
            if (_secondBeam >= 0 && _secondBeam != State.Beam) KillSecondFx();
            if (_second != null)
            {
                _second.SetGlobal(at, o, scale);
                _second.Update(dt);
                _second.Hidden = !shown;
                if (_secondEnding && _second.Gen != null && _second.Gen.IsSystemDeletable) { _second.Kill(); _second = null; }
            }

            if (_secondElec != null)
            {
                _secondElec.Place(at, o, scale);
                _secondElec.Hidden = !shown;
                if (_secondElec.Dead) _secondElec = null;
            }

            if (_second == null && _secondElec == null) _secondBeam = -1;

            // CIceBeam: the muzzle smoke (frost) while the ice beam is out
            bool ice = State.Beam == MpPlayerState.BeamIce && shown && !Changing;
            if (ice && _iceSmoke == null)
            {
                _iceSmoke = NewGen(Assets.IceSmokePart);
                if (_iceSmoke != null) _iceSmoke.Gen.SetParticleEmission(true);
            }
            else if (!ice && _iceSmoke != null) { _iceSmoke.Kill(); _iceSmoke = null; }
            if (_iceSmoke != null)
            {
                _iceSmoke.SetGlobal(MpSpace.ToGame(Muzzle), o, scale);
                _iceSmoke.Update(dt);
                _iceSmoke.Hidden = !shown;
            }
        }
        /// <summary>The beam change hologram (GunRes holoTransition) rides the cannon until it is done.</summary>
        private void TickHolo(float dt)
        {
            if (_holo == null) return;
            if (_holo.Dead || _holo.Gen == null) { _holo = null; return; }
            _holo.SetGlobal(MpSpace.ToGame(Root.position + Root.forward * 0.12f * _scale), MpEffects.GameOrientation(Root.rotation), _scale);
            _holo.Update(dt);
            _holo.Hidden = !Root.gameObject.activeInHierarchy;
            if (_holo.Gen.IsSystemDeletable) { _holo.Kill(); _holo = null; }
        }

        public void Destroy()
        {
            CancelCharge(false);
            for (int i = 0; i < 2; i++) if (_muzzleFx[i] != null) { _muzzleFx[i].Kill(); _muzzleFx[i] = null; }
            if (_holo != null) { _holo.Kill(); _holo = null; }
            KillSecondFx();
            if (_iceSmoke != null) { _iceSmoke.Kill(); _iceSmoke = null; }
            if (Root != null) Object.Destroy(Root.gameObject);
        }
    }
}
