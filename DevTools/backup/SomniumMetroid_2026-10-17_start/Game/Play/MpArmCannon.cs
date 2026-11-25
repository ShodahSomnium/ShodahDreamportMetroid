using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// Samus's arm cannon: the four beam models (each beam ANCS's character 0, the bind pose until the gun animations are played),
    /// held on the right controller in VR (the hand inside the cannon's back, the barrel along the controller's aim) or by the eye on
    /// a monitor at the PlayerGun tweak's gun position (0.25 right, 0.3 forward, 0.35 down, game units). Firing follows CPlayerGun:
    /// a shot on the press (each beam's cool-down from the tweak), the charge builds while the trigger is held (charge beam owned),
    /// a full charge fires the charged shot on release; missiles (B / F) every 0.75 s while any are left; a beam change takes the
    /// tweak's gun transform time with the cannon swapping models.
    /// </summary>
    public sealed class MpArmCannon
    {
        public readonly MpGunAssets Assets;
        public readonly MpPlayerState State;
        public readonly MpProjectiles Shots;
        public Transform Root;          // Unity: forward = the barrel
        private Transform _model;       // game space (Y forward, Z up), under Root
        private readonly GameObject[] _beamGo = new GameObject[4];
        private int _shown = -1;
        private float _cool, _missileCool, _switchT, _recoil;
        public float Charge;            // 0..1
        private object _chargeSfx;      // the beam's charge loop (keyed off on release)
        public bool Holstered;
        public const float MissileReload = 0.75f;

        /// <summary>The hand's place in the cannon (model space, game units: the hand sits inside the barrel's back).</summary>
        public static Vector3 HandInModel = new Vector3(0f, 0.16f, -0.01f);

        public MpArmCannon(MpGunAssets assets, MpPlayerState state, MpProjectiles shots)
        {
            Assets = assets;
            State = state;
            Shots = shots;
            Root = new GameObject("Arm Cannon").transform;
            _model = new GameObject("Arm Cannon model").transform;
            _model.SetParent(Root, false);
            _model.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            _model.localScale = new Vector3(MpSpace.Scale, -MpSpace.Scale, MpSpace.Scale);
            _model.localPosition = -(Quaternion.Euler(-90f, 0f, 0f) * Vector3.Scale(new Vector3(MpSpace.Scale, -MpSpace.Scale, MpSpace.Scale), HandInModel));
            for (int b = 0; b < 4; b++)
            {
                MpGunAssets.Beam beam = assets.Beams[b];
                if (beam == null || beam.Model == null) continue;
                _beamGo[b] = beam.Model.Instantiate(beam.Name + " beam", _model);
                foreach (MeshRenderer r in _beamGo[b].GetComponentsInChildren<MeshRenderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    foreach (Material m in r.sharedMaterials) if (m != null) m.SetColor("_Ambient", new Color(0.62f, 0.62f, 0.62f, 1f));
                }

                _beamGo[b].SetActive(false);
            }

            Show(state.Beam);
        }

        private void Show(int beam)
        {
            if (_shown == beam) return;
            for (int b = 0; b < 4; b++) if (_beamGo[b] != null) _beamGo[b].SetActive(b == beam);
            _shown = beam;
        }

        /// <summary>The cannon's tip in Unity space (the model's front end along its barrel).</summary>
        public Vector3 Muzzle
        {
            get
            {
                MpGunAssets.Beam b = Assets.Beams[State.Beam];
                float front = b != null && b.Model != null ? b.Model.Max.y : 0.58f;
                return _model.TransformPoint(new Vector3(0f, front + 0.04f, 0f));
            }
        }

        public Vector3 Forward { get { return Root.forward; } }

        /// <summary>Places the cannon: <paramref name="pos"/> = where the hand is, <paramref name="rot"/> = the aim (forward = barrel).</summary>
        public void Pose(Vector3 pos, Quaternion rot)
        {
            float kick = _recoil;
            Root.SetPositionAndRotation(pos - rot * Vector3.forward * (0.035f * kick), rot * Quaternion.Euler(-6f * kick, 0f, 0f));
        }

        public void SetVisible(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        /// <summary>A beam change (CPlayerGun::HandleBeamChange): the old beam goes, the new one comes after the gun transform time.</summary>
        public void ChangeBeam(int beam)
        {
            if (beam == State.Beam || !State.HasBeam(beam)) return;
            State.Beam = beam;
            _switchT = Assets.Tweak != null ? Mathf.Max(0.15f, Assets.Tweak.GunTransformTime) : 0.25f;
            CancelCharge();
            MpGame.Instance.PlayUiSfx(MpSfx.BeamChange);
        }

        public void Tick(float dt, bool firePressed, bool fireHeld, bool fireReleased, bool missilePressed, bool canFire)
        {
            _cool -= dt;
            _missileCool -= dt;
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt / 0.12f);
            if (_switchT > 0f)
            {
                _switchT -= dt;
                float t = Mathf.Clamp01(_switchT / 0.25f);
                _model.localScale = new Vector3(MpSpace.Scale, -MpSpace.Scale, MpSpace.Scale) * (1f - 0.25f * Mathf.Sin(t * Mathf.PI));
                if (_switchT <= 0.12f) Show(State.Beam);
                UpdateMuzzleFx(dt);
                return;
            }

            Show(State.Beam);
            if (!canFire || Holstered)
            {
                CancelCharge();
                UpdateMuzzleFx(dt);
                return;
            }

            if (missilePressed && _missileCool <= 0f && _phase == Phase.NotCharging)
            {
                if (State.Missiles > 0)
                {
                    State.Missiles--;
                    _missileCool = MissileReload;
                    _recoil = 1.4f;
                    Shots.FireMissile(Muzzle, Forward);
                }
                else MpGame.Instance.PlayUiSfx(MpSfx.MissileEmpty);
            }

            // CPlayerGun::ProcessChargeState: the press fires a normal shot and (charge beam owned) requests the charge
            if (firePressed && _cool <= 0f && _phase == Phase.NotCharging)
            {
                float cd = Assets.Tweak != null ? Assets.Tweak.CoolDown[State.Beam] : 0.15f;
                _cool = Mathf.Max(0.08f, cd);
                _recoil = 1f;
                FirePrimary(false);
                if (State.HasCharge) _phase = Phase.ChargeRequested;
            }

            if (fireReleased && _phase != Phase.NotCharging)
            {
                // ResetCharged: a charge past FxGrowing fires the charged shot (damage x the charge factor), an early release just ends it
                if (_phase >= Phase.FxGrowing)
                {
                    _recoil = 1.6f;
                    FirePrimary(true);
                    _cool = 0.25f;
                }

                CancelCharge();
            }
            else if (_phase != Phase.NotCharging && !fireHeld) CancelCharge();

            UpdateChargeState(dt);
            UpdateMuzzleFx(dt);
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
                            if (_chargeSfx == null) _chargeSfx = MpGame.Instance.PlaySfxAt(MpSfx.ChargeLoop[State.Beam], null, 1f, true, 0f);
                            if (MpXr.Present) MpXr.Pulse(true, 0.25f, 0.05f);
                            _chargeAnimStarted = true;
                        }
                    }
                    else if (_chargeFactor >= KChargeFxStart)
                    {
                        _chargeEffectVisible = true;
                        _chargeAnimStarted = false;
                        _phase = Phase.FxGrowing;
                        EnableChargeFx();
                    }

                    break;
                case Phase.FxGrowing:
                    if (_chargeFactor >= 1f) _phase = Phase.FxGrown;
                    break;
            }

            if (_phase > Phase.NotCharging && _phase < Phase.FxGrown) _chargeFactor = Mathf.Min(1f, _chargeFactor + KChargeSpeed * dt);
            Charge = _phase != Phase.NotCharging ? _chargeFactor : 0f;      // GetChargePercentage (the reticle's gauge)
        }

        /// <summary>FirePrimary: the shot, and whether the muzzle effect shows (not for a normal plasma shot).</summary>
        private void FirePrimary(bool charged)
        {
            _chargeEffectVisible = State.Beam != 3 || _phase != Phase.NotCharging;
            _muzzleVisT = 0.0625f;
            Shots.FireBeam(State.Beam, charged, Muzzle, Forward, charged ? _chargeFactor : 1f);
        }

        /// <summary>CPlayerGun::ResetCharge / CancelFiring: the charge ends, its effects and sound stop.</summary>
        public void CancelCharge()
        {
            if (_phase != Phase.NotCharging) ActivateCharge(false);
            _phase = Phase.NotCharging;
            _chargeFactor = 0f;
            _chargeAnimStarted = false;
            Charge = 0f;
            if (_auxFx != null) { _auxFx.Kill(); _auxFx = null; }
            if (_chargeSfx != null && MpGame.Instance != null) { MpGame.Instance.StopSfx(_chargeSfx); _chargeSfx = null; }
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
            MpEffects.Effect m = _muzzleFx[_muzzleIdx];
            if (_chargeEffectVisible)
            {
                if (m != null)
                {
                    m.SetGlobal(g, o, MuzzleFxScale);
                    m.Gen.SetParticleEmission(true);
                    m.Update(dt);
                }

                if (_auxFx != null)
                {
                    _auxFx.SetGlobal(g, o, MuzzleFxScale);
                    _auxFx.Gen.SetParticleEmission(true);
                    _auxFx.Update(dt);
                }
            }

            for (int i = 0; i < 2; i++)
                if (_muzzleFx[i] != null) _muzzleFx[i].Hidden = !(shown && i == _muzzleIdx && _chargeEffectVisible && (_muzzleVisT > 0f || _phase > Phase.AnimAndSfx));
            if (_auxFx != null) _auxFx.Hidden = !(shown && _phase >= Phase.FxGrown);
        }

        public void Destroy()
        {
            CancelCharge();
            for (int i = 0; i < 2; i++) if (_muzzleFx[i] != null) { _muzzleFx[i].Kill(); _muzzleFx[i] = null; }
            if (Root != null) Object.Destroy(Root.gameObject);
        }
    }
}
