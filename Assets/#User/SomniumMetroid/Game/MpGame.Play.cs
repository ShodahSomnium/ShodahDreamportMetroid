using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The running game. VR: the cannon on the right controller (trigger fires, hold to charge, B fires missiles - the client's tablet
    /// and pop-up menu on that button are held off), left grip + hand movement picks the visor, right grip + hand movement the beam.
    /// Monitor: first person, the cannon by the eye at the PlayerGun tweak's gun position, left click fires (hold to charge), F missile,
    /// 1-4 beams (Power, Wave, Ice, Plasma), Z / X / C / V visors (Combat, Scan, Thermal, X-Ray). Each frame: the rooms stream around
    /// the player, the script entities think, the scan visor looks for scan points, the HUD follows the head, the frame's lights go to
    /// the shaders.
    /// </summary>
    public sealed partial class MpGame
    {
        public Vector3 PlayerGame { get; private set; }
        public static Vector3? LabFocus;
        public MpScripts Scripts { get; private set; }
        public MpPlayerState State { get; private set; }
        public MpArmCannon Cannon { get; private set; }
        public MpProjectiles Shots { get; private set; }
        public MpHud Hud { get; private set; }
        public MpGunAssets GunAssets { get; private set; }
        public MpAudio Audio { get; private set; }
        public MpReticle Reticle { get; private set; }
        private float _aimDistance = 30f;     // Unity metres from the eye to what the view's centre is on (the monitor reticle stands there)

        private bool _placed;
        private float _placeRetry;
        private Vector3 _spawnGame;
        private float _spawnYaw;
        private readonly MpGripSelector _visorSel = new MpGripSelector(false);
        private readonly MpGripSelector _beamSel = new MpGripSelector(true);
        private readonly MpLaserAim _laser = new MpLaserAim();
        /// <summary>The avatar's right forearm (tablet: Cannon on forearm) - the cannon's frame and the hand / forearm hiding.</summary>
        private readonly MpAvatarArm _arm = new MpAvatarArm();
        private bool _hudCine;

        /// <summary>The Bridge hand is the controller's grip pose; until the client's laser is measured the barrel points along this turn of it (the BO2 Dreamport's GripToAim).</summary>
        public static readonly Quaternion GripToAim = Quaternion.Euler(90f, 0f, 0f);
        private bool _desktopFireHeld;
        private float _visorT;              // the visor change (CPlayerVisor's transition): the gun waits
        private int _settingsV = -1;
        /// <summary>A cinematic camera is running (MpCinematics): the player watches, the gun waits.</summary>
        public bool InCinematic;

        // scanning (CPlayer::UpdateScanningState: the scan point nearest the view's centre, held fire downloads it)
        private MpScanPointEntity _scanTarget;
        private float _scanProgress, _scanTotal = 1f;
        private readonly HashSet<uint> _scanned = new HashSet<uint>();
        private readonly Dictionary<uint, ScanInfo> _scanInfo = new Dictionary<uint, ScanInfo>();
        private float[] _scanSpeeds = { 1f, 3f };
        private readonly List<MpSprites.Sprite> _scanDots = new List<MpSprites.Sprite>();
        private bool _scanStarted;

        private sealed class ScanInfo
        {
            public float Seconds = 1f;
            public bool Important;
            public uint Strg = 0xFFFFFFFF;
        }

        private struct Flash { public Vector3 Pos; public Color Color; public float T, Dur, Radius; }
        private readonly List<Flash> _flashes = new List<Flash>();

        /// <summary>Lab: drive the controls (null = real input).</summary>
        public static System.Func<MpGame, bool> LabFire, LabMissile;

        private void PrepareScripts()
        {
            Scripts = new MpScripts(this);
            State = new MpPlayerState();
        }

        private void OnWorldStarted(Vector3 spawnGame, float spawnYawGame, int[] items)
        {
            _spawnGame = spawnGame;
            _spawnYaw = spawnYawGame;
            _introUntil = CurrentPak == StartPak ? Time.unscaledTime + 90f : 0f;      // the intro window (Skip the intro)
            _introSkipped = false;
            State.Give(items);
            MpLog.Info("Samus: energy " + State.Energy + ", missiles " + State.Missiles + "/" + State.MissileCapacity + ", beams " + (State.HasBeam(0) ? "Power " : "") + (State.HasBeam(1) ? "Ice " : "") + (State.HasBeam(2) ? "Wave " : "") + (State.HasBeam(3) ? "Plasma " : "") + (MpPlayerState.UnlockAll ? "(all beams and visors unlocked for testing)" : ""));
            if (GunAssets != null)
            {
                GunAssets.Upload();
                Shots = new MpProjectiles(GunAssets);
                Cannon = new MpArmCannon(GunAssets, State, Shots);
                MpLog.Info("Gun: " + GunAssets.Report);
                MpLog.Info("Gun " + GunAssets.EffectsReport);
            }

            if (Hud != null) Hud.Build(Assets);
            if (Reticle != null) Reticle.Build();
            InstallActions();
            MpTabletApp.Start(transform);
            PlacePlayer();
        }

        private void PlacePlayer()
        {
            string how;
            Vector3 unity = MpSpace.ToUnity(_spawnGame) + Vector3.up * 0.05f;
            _placed = MpPlayer.Teleport(unity, -_spawnYaw, out how);
            MpLog.Info("Start: player " + (_placed ? "placed at " + unity + " (" + how + ")" : "not placed yet (" + how + ")"));
            _placeRetry = Time.unscaledTime + 1f;
        }

        private void TickPlay()
        {
            MpShaderGlobals.ClearLights();
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (!_placed && Time.unscaledTime > _placeRetry) PlacePlayer();
            bool vr = MpXr.Present;
            MpClientKeys.Vr = vr;
            MpClientKeys.Tick();
            Camera cam = MpPlayer.Head();
            Vector3 feet;
            if (LabFocus.HasValue) PlayerGame = LabFocus.Value;
            else if (_settleHoldUntil > Time.unscaledTime) PlayerGame = _settleGame;       // the world just went home: the avatar's teleport is landing
            else if (MpCinemaView.Active) PlayerGame = MpCinemaView.GameFeet;      // a first-person cutscene slides the world: Samus stays put
            else if (MpPlayer.TryFeet(out feet)) PlayerGame = MpSpace.ToGame(feet);
            WatchAreas();
            TickCoop(dt);
            TickBall(dt, vr);
            // Samus's speed, jump and gravity on the avatar (measured against the client's own while the stick / keys are fully held)
            bool fullStick, sprint = false;
            if (vr) fullStick = MpXr.Hand(false).Stick.magnitude >= 0.95f;
            else
            {
                fullStick = SafeKey(() => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D));
                sprint = SafeKey(() => Input.GetKey(KeyCode.LeftShift));
            }

            MpMovement.Tick(vr, fullStick, sprint, Ball.Holding || _warping || LabFocus.HasValue);
            MpFootsteps.Tick(this, Ball.Holding || _warping || LabFocus.HasValue || MpCinematics.InputDisabled || MpCinemaView.Active);
            if (!_warping) WorldView.Tick(PlayerGame);
            Scripts.PlayerFeet = PlayerGame;
            if (_settingsV != MpSettings.Version) { _settingsV = MpSettings.Version; State.UnlockForTesting(); }
            MpAudio.SfxVolume = MpSettings.SfxVolume;
            MpAudio.MusicVolume = MpSettings.MusicVolume;
            TickRide();
            SettleWorld();
            TickDamage(dt);
            TickProgress(dt);
            TickEscape(dt);
            TickBoss();
            TickCinemaInput();
            Scripts.Tick(dt);
            MpCinemaView.Tick(this);
            if (Hud != null && _hudCine != MpCinemaView.Active) { _hudCine = MpCinemaView.Active; Hud.SetVisible(MpSettings.HudShown && !_hudCine); }

            MpAreaView here;
            if (WorldView.Areas.TryGetValue(WorldView.CurrentArea, out here) && here.Payload != null) { MpShaderGlobals.LightFade = here.Payload.LightingLevel; MpShaderGlobals.SetAreaLights(here.Payload.Lights); }

            // controls
            bool firePressed = false, fireHeld = false, fireReleased = false, missile = false;
            int pickVisor = -1, pickBeam = -1;
            Transform head = cam != null ? cam.transform : null;
            if (vr)
            {
                MpHand l = MpXr.Hand(false), r = MpXr.Hand(true);
                firePressed = r.TriggerPressed;
                fireHeld = r.TriggerHeld;
                fireReleased = r.TriggerReleased;
                missile = r.SecondaryPressed;
                pickVisor = _visorSel.Tick(l, MpPlayer.LeftHand, head);
                pickBeam = _beamSel.Tick(r, MpPlayer.RightHand, head);
            }
            else if (LabFire == null)
            {
                bool down = SafeKey(() => Input.GetMouseButton(0));
                firePressed = down && !_desktopFireHeld;
                fireReleased = !down && _desktopFireHeld;
                fireHeld = down;
                _desktopFireHeld = down;
                missile = SafeKey(() => Input.GetKeyDown(KeyCode.F));
                if (SafeKey(() => Input.GetKeyDown(KeyCode.Alpha1))) pickBeam = MpPlayerState.BeamPower;
                if (SafeKey(() => Input.GetKeyDown(KeyCode.Alpha2))) pickBeam = MpPlayerState.BeamWave;
                if (SafeKey(() => Input.GetKeyDown(KeyCode.Alpha3))) pickBeam = MpPlayerState.BeamIce;
                if (SafeKey(() => Input.GetKeyDown(KeyCode.Alpha4))) pickBeam = MpPlayerState.BeamPlasma;
                if (SafeKey(() => Input.GetKeyDown(KeyCode.Z))) pickVisor = MpPlayerState.VisorCombat;
                if (SafeKey(() => Input.GetKeyDown(KeyCode.X))) pickVisor = MpPlayerState.VisorScan;
                if (SafeKey(() => Input.GetKeyDown(KeyCode.C))) pickVisor = MpPlayerState.VisorThermal;
                if (SafeKey(() => Input.GetKeyDown(KeyCode.V))) pickVisor = MpPlayerState.VisorXRay;
            }

            if (LabFire != null) { bool f = LabFire(this); firePressed = f && !_desktopFireHeld; fireReleased = !f && _desktopFireHeld; fireHeld = f; _desktopFireHeld = f; }
            if (LabMissile != null) missile = LabMissile(this);
            // the tablet's page is open (Y / Tab): the hands are on the tablet, not the cannon
            // morphed, fire drops a bomb (CPlayerGun::DropBomb)
            if (Ball.Morphed && firePressed && !MpTabletApp.IsOpen && State.Energy > 0f && !MpCinematics.InputDisabled && Bombs.CanDrop(State)) Bombs.Drop(Ball.Pos, GunAssets);
            Bombs.Tick(dt, this);
            if (MpTabletApp.IsOpen || State.Energy <= 0f || MpCinematics.InputDisabled || Ball.Morphed) { firePressed = fireHeld = missile = fireReleased = false; pickBeam = pickVisor = -1; }

            if (pickVisor >= 0) SetVisor(pickVisor);
            if (pickBeam >= 0 && Cannon != null && State.HasBeam(pickBeam) && pickBeam != State.Beam)
            {
                Cannon.ChangeBeam(pickBeam);
                PlayUiSfx(MpSfx.WeaponSelect);
            }

            // the cannon
            // VR: the avatar's right hand and forearm hidden inside the cannon (wrist or forearm mode); the morph ball hides it all
            _arm.Tick(vr && Cannon != null && !Ball.Morphed, MpSettings.CannonOnForearm);
            _arm.TickBody((Ball.Morphed && Ball.ThirdPerson) || MpCinemaView.Active);
            if (Cannon != null)
            {
                Vector3 gp = Vector3.zero;
                Quaternion gr = Quaternion.identity;
                bool posed = false;
                if (vr && MpPlayer.RightHand != null)
                {
                    // the right controller's pointing direction, as the client's own laser points (measured, not assumed)
                    Transform rh = MpPlayer.RightHand;
                    Quaternion aim;
                    if (!_laser.TryAim(true, rh, out aim)) aim = rh.rotation * GripToAim;
                    Vector3 basePos = rh.position;
                    Vector3 wrist, along;
                    if (MpSettings.CannonOnForearm && _arm.TryForearm(out wrist, out along))
                    {
                        // along the avatar's forearm (elbow to wrist), the controller's up kept square to it, from the wrist
                        Vector3 up = Vector3.ProjectOnPlane(aim * Vector3.up, along);
                        if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.up, along);
                        aim = Quaternion.LookRotation(along, up);
                        basePos = wrist;
                    }
                    // the player's own fit (tablet: Arm Cannon): a tilt up and a shift along the aim, up and right, in centimetres
                    gr = aim * Quaternion.Euler(-MpSettings.CannonPitch, 0f, 0f);
                    gp = basePos + gr * new Vector3(MpSettings.CannonRight, MpSettings.CannonUp, MpSettings.CannonForward) * 0.01f;
                    posed = true;
                }
                else if (head != null)
                {
                    // CTweakPlayerGun gun position by the eye (game units: x right, y forward, z up)
                    float s = MpSpace.Scale;
                    MpTweakGun t = GunAssets != null ? GunAssets.Tweak : null;
                    Vector3 off = t != null ? new Vector3(t.GunPosX, t.GunPosZ, t.GunPosY) : new Vector3(0.25f, -0.35f, 0.3f);
                    gp = head.position + head.rotation * (off * s);
                    // the barrel turns to what is under the reticle (the view's centre), so shots land where the reticle is
                    RaycastHit aimHit;
                    Vector3 aimAt = MpProjectiles.ShotRay(head.position, head.forward, 300f * s, out aimHit) ? aimHit.point : head.position + head.forward * 300f * s;
                    _aimDistance = (aimAt - head.position).magnitude;
                    Vector3 toAim = aimAt - gp;
                    gr = toAim.sqrMagnitude > 0.04f ? Quaternion.LookRotation(toAim, head.up) : head.rotation;
                    posed = true;
                }

                // the scan visor holsters the gun (CPlayer::HolsterGun); in VR the cannon stays on the hand, it only cannot fire
                bool scanning = State.Visor == MpPlayerState.VisorScan;
                Cannon.Holstered = scanning;
                Cannon.SetVisible(posed && (!scanning || vr) && !Ball.Morphed && !MpCinemaView.Active);
                if (posed) Cannon.Pose(gp, gr);
                Cannon.Tick(dt, firePressed && !scanning, fireHeld && !scanning, fireReleased, missile && !scanning, posed && _visorT <= 0f);
                // co-op: the cannon as the others draw it on this avatar (its place by the hand or head, the presses, the hold)
                CoopCannon(posed, gp, gr, vr && MpPlayer.RightHand != null, head, posed && (!scanning || vr) && !Ball.Morphed && !MpCinemaView.Active, scanning,
                    firePressed && !scanning, fireHeld && !scanning, missile && !scanning, posed && _visorT <= 0f);
            }

            if (State.Visor == MpPlayerState.VisorScan) TickScan(dt, head, fireHeld);
            else ClearScan();

            if (Shots != null) Shots.Tick(dt);
            MpEnemyShots.Tick(dt);
            MpEffects.Tick(dt);
            MpSprites.Tick(dt);
            _visorT = Mathf.Max(0f, _visorT - dt);

            if (Hud != null)
            {
                Hud.VisorMenuOpen = _visorSel.Open;
                Hud.VisorHover = _visorSel.HoverItem;
                Hud.BeamMenuOpen = _beamSel.Open;
                Hud.BeamHover = _beamSel.HoverItem;
                Hud.Update(dt);
            }

            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                Flash f = _flashes[i];
                f.T += dt;
                if (f.T >= f.Dur) { _flashes.RemoveAt(i); continue; }
                _flashes[i] = f;
                MpShaderGlobals.AddLight(new MpShaderGlobals.Light { GamePos = f.Pos, Color = f.Color * (1f - f.T / f.Dur), K0 = 1f, K1 = 0f, K2 = 0.03f, Radius = f.Radius });
            }

            if (Audio != null) Audio.Tick(dt, head);
            MpShaderGlobals.Visor = State.Visor;
            MpShaderGlobals.Apply(cam);
        }

        /// <summary>The HUD follows the head at the last moment before the camera draws; the client's clashing keys stay off.</summary>
        private void LateUpdate()
        {
            if (!Running) return;
            MpClientKeys.HoldKeys();
            Camera cam = MpPlayer.Head();
            if (Hud != null && cam != null) Hud.Follow(cam.transform);
            if (Ball.Morphed && Ball.ThirdPerson && Ball.Hold.Taken) Ball.Hold.Write(MpSpace.ToUnity(Ball.ViewPos) - Ball.EyeInRoot);   // again after the client's own updates
            MpCinematics.Render(cam, WorldView);
            WorldView.TickSky(cam);
            MpEffects.Render(cam);
            if (Reticle != null)
            {
                // the monitor's aiming reticle (VR aims with the cannon itself); not while the scan visor holsters the gun
                bool show = cam != null && !MpXr.Present && State.Visor != MpPlayerState.VisorScan && Cannon != null;
                Reticle.SetVisible(show);
                if (show) Reticle.Tick(cam, State.Beam, Cannon.Charge, _aimDistance, Time.deltaTime);
            }
        }

        private static bool SafeKey(System.Func<bool> f)
        {
            try { return f(); }
            catch (System.Exception) { return false; }
        }

        public void SetVisor(int visor)
        {
            if (visor == State.Visor || !State.HasVisor(visor)) return;
            int old = State.Visor;
            State.Visor = visor;
            _visorT = 0.25f;
            WorldView.SetVisor(visor);
            MpActorEntity.Visor = visor;
            foreach (MpEntity e in Scripts.All) { var ae = e as MpActorEntity; if (ae != null) ae.ApplyVisor(); }
            if (Cannon != null) Cannon.CancelCharge();
            if (old == MpPlayerState.VisorScan) PlayUiSfx(MpSfx.ScanOff);
            PlayUiSfx(visor == MpPlayerState.VisorScan ? MpSfx.ScanOn : MpSfx.VisorOn);
            MpLog.Info("Visor: " + new[] { "Combat", "X-Ray", "Scan", "Thermal" }[visor]);
        }

        // ------------------------------------------------------------------ scanning

        private ScanInfo InfoOf(uint scan)
        {
            ScanInfo info;
            if (_scanInfo.TryGetValue(scan, out info)) return info;
            info = new ScanInfo();
            try
            {
                byte[] b = Assets.Read(scan);
                if (b != null)
                {
                    // SCAN (decomp CScannableObjectInfo::Load): version, magic, FRME, STRG, scan speed (index into the GUI tweak's speeds
                    // from version 4, seconds before), category, important (version 5)
                    var r = new MpReader(b);
                    uint version = r.U32();
                    r.U32();
                    r.U32();
                    info.Strg = r.U32();
                    info.Seconds = version < 4 ? r.F32() : _scanSpeeds[Mathf.Clamp(r.S32(), 0, _scanSpeeds.Length - 1)];
                    r.U32();
                    info.Important = version > 4 && r.Bool();
                }
            }
            catch (System.Exception e) { MpLog.Warn("Scan: SCAN " + scan.ToString("X8") + " not read: " + e.Message); }

            _scanInfo[scan] = info;
            return info;
        }

        private void TickScan(float dt, Transform head, bool held)
        {
            if (head == null) return;
            // the scan points in view, marked (orange; red when important; blue once scanned)
            int dots = 0;
            MpScanPointEntity best = null;
            float bestAngle = 12f;
            foreach (MpEntity e in Scripts.All)
            {
                var s = e as MpScanPointEntity;
                if (s == null || !s.Active || !MpAssets.Valid(s.Scan)) continue;
                Vector3 gp = s.Follow != null ? MpSpace.ToGame(s.Follow.position) : s.GamePos;
                Vector3 up = MpSpace.ToUnity(gp);
                Vector3 to = up - head.position;
                float dist = to.magnitude / MpSpace.Scale;
                if (dist > 50f) continue;
                float ang = Vector3.Angle(head.forward, to);
                if (ang > 50f) continue;
                ScanInfo info = InfoOf(s.Scan);
                bool done = _scanned.Contains(s.Scan);
                Color c = done ? new Color(0.4f, 0.55f, 1f, 0.6f) : info.Important ? new Color(1f, 0.25f, 0.2f, 0.9f) : new Color(1f, 0.6f, 0.15f, 0.9f);
                while (_scanDots.Count <= dots) _scanDots.Add(null);
                if (_scanDots[dots] == null || !_scanDots[dots].Alive) _scanDots[dots] = MpSprites.Spawn(GunAssets != null ? GunAssets.ImpactMat : null, up, Vector3.zero, 0.05f, 0f, 0f, c);
                MpSprites.Hold(_scanDots[dots], up, Mathf.Max(0.04f, to.magnitude * 0.018f), c);
                dots++;
                if (ang < bestAngle) { bestAngle = ang; best = s; }
            }

            for (int i = dots; i < _scanDots.Count; i++) if (_scanDots[i] != null) { MpSprites.Kill(_scanDots[i]); _scanDots[i] = null; }

            if (best != _scanTarget)
            {
                _scanTarget = best;
                _scanProgress = 0f;
                _scanStarted = false;
                if (best != null) { _scanTotal = InfoOf(best.Scan).Seconds; PlayUiSfx(MpSfx.LockOn); }
            }

            if (_scanTarget == null || !held) return;
            if (_scanned.Contains(_scanTarget.Scan))
            {
                if (!_scanStarted) { _scanStarted = true; ShowScanText(_scanTarget); }
                return;
            }

            if (!_scanStarted)
            {
                _scanStarted = true;
                SendScanState(_scanTarget, MpScriptMsg.StScanStart);
            }

            _scanProgress += dt / Mathf.Max(0.1f, _scanTotal);
            if (Hud != null) Hud.ShowMessage("Scanning " + Mathf.Clamp(Mathf.RoundToInt(_scanProgress * 100f), 0, 100) + "%", 0.3f);
            if (_scanProgress < 1f) return;
            _scanned.Add(_scanTarget.Scan);
            PlayUiSfx(MpSfx.ScanDone);
            ShowScanText(_scanTarget);
            // CActor::OnScanStateChange(Done): ScanDone to the object's links (POIs open the frigate's doors and holograms this way)
            SendScanState(_scanTarget, MpScriptMsg.StScanDone);
            MpLog.Info("Scan: '" + _scanTarget.Name + "' done");
        }

        public void SendScanState(MpScanPointEntity s, int state)
        {
            s.Send(state);
            MpEntity owner = Scripts.Find(s.Id & 0x7FFFFFFFu);
            if (owner != null && owner != s) owner.Send(state);
        }

        private void ShowScanText(MpScanPointEntity s)
        {
            ScanInfo info = InfoOf(s.Scan);
            if (!MpAssets.Valid(info.Strg)) return;
            try
            {
                MpStrings t = MpStrings.Read(Assets.Read(info.Strg));
                string text = MpStrings.Plain(string.Join("\n", t.Strings.ToArray()));
                MpLog.Info("Scan: text \"" + (text.Length > 120 ? text.Substring(0, 120) + "..." : text).Replace("\n", " / ") + "\"");
                if (Hud != null) Hud.ShowScanText(text);
            }
            catch (System.Exception) { }
        }

        private void ClearScan()
        {
            if (_scanDots.Count == 0 && _scanTarget == null) return;
            foreach (MpSprites.Sprite s in _scanDots) if (s != null) MpSprites.Kill(s);
            _scanDots.Clear();
            _scanTarget = null;
            _scanProgress = 0f;
            _scanStarted = false;
        }

        // ------------------------------------------------------------------ hooks the script entities and shots call

        public void OnDoor(MpDoorEntity door, bool open)
        {
            // the door's own sounds (decomp SFXdor_x_open_00 / SFXdor_x_close_00)
            PlaySfxAt(open ? 1129 : 1128, door.GamePos, 1f, false, 60f);
        }

        public void OnWeaponIneffective(MpEntity target) { }

        public void ShowHudMemo(uint strg, float seconds)
        {
            if (!MpAssets.Valid(strg) || Hud == null) return;
            try
            {
                MpStrings t = MpStrings.Read(Assets.Read(strg));
                Hud.ShowMessage(MpStrings.Plain(t[0]), seconds > 0f ? seconds : 4f);
            }
            catch (System.Exception) { }
        }

        public void FlashLight(Vector3 gamePos, Color c, float seconds, float radius)
        {
            _flashes.Add(new Flash { Pos = gamePos, Color = c, Dur = seconds, Radius = radius });
        }

        public void PlayMusic(string file, float fadeIn, float volume, bool loop) { if (Audio != null) Audio.PlayMusic(file, fadeIn, volume, loop); }
        public void StopMusic(float fadeOut) { if (Audio != null) Audio.StopMusic(fadeOut); }
        public void StopMusic(string file, float fadeOut) { if (Audio != null) Audio.StopMusic(file, fadeOut); }

        /// <summary>A sound by its code id (a MusyX FX id, as CSfxManager callers pass them).</summary>
        public object PlaySfxAt(int id, Vector3? gamePos, float volume, bool loop, float maxDist) { return Audio != null ? Audio.Play(id, gamePos, volume, loop, maxDist) : null; }

        /// <summary>A sound by an id stored in the game's data (script objects, animation events): through the sound lookup table first.</summary>
        public object PlayDataSfxAt(int dataId, Vector3? gamePos, float volume, bool loop, float maxDist) { return Audio != null ? Audio.Play(Audio.Translate(dataId), gamePos, volume, loop, maxDist) : null; }

        public void StopSfx(object handle) { if (Audio != null) Audio.Stop(handle); }

        public void PlayUiSfx(int id)
        {
            if (id == MpSfx.ChargeStart && State != null) id = MpSfx.ChargeLoop[State.Beam];
            if (Audio != null) Audio.Play(id, null, 1f, false, 0f);
        }

        private void OnStopped()
        {
            ClearCoop();
            MpClientKeys.Release();
            MpTabletApp.Shutdown();
            ClearActions();
            Ball.Destroy();
            Bombs.Clear();
            _arm.Destroy();
            MpMovement.Restore();
            if (Cannon != null) Cannon.Destroy();
            if (Hud != null) Hud.Destroy();
            if (Reticle != null) Reticle.Destroy();
            if (Shots != null) Shots.Clear();
            MpEnemyShots.Clear();
            MpCinemaView.End();
            MpCinemaAvatar.Clear();
            MpCinematics.Clear();
            MpEnemy.Live.Clear();
            MpEffects.Clear();
            MpEffects.Lib = null;
            MpSprites.Clear();
            if (Audio != null) Audio.Destroy();
        }
    }
}
