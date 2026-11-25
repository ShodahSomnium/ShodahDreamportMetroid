using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumMetroid.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>What the loader thread prepares for the HUD: the frames, their models, the textures, the colour and GUI tweaks, strings.</summary>
    public sealed class MpHudPayload
    {
        public readonly Dictionary<string, MpFrame> Frames = new Dictionary<string, MpFrame>();
        public readonly List<KeyValuePair<uint, MpTexture>> Textures = new List<KeyValuePair<uint, MpTexture>>();
        public float[][] Colors;            // CTweakGuiColors in its order (112 colours)
        public float[][][] VisorColors;     // per HUD type (0 combat, 1 scan, 2 x-ray, 3 thermal, 4 ball): 7 colours
        public float MenuActiveScale = 1.7f, MenuInactiveScale = 1.2f, MenuTranslate = 1.8f;
        public float[] ScanSpeeds = { 1f, 3f };   // CTweakGui scan speeds (SCAN speed index -> seconds)
        public byte[] ThermalPalette, XRayPalette;  // the visor palettes' TLUTs (RGBA)
        public int ThermalCount, XRayCount;
        public MpStrings Main;

        public static readonly string[] FrameNames = { "FRME_BaseHud", "FRME_CombatHud", "FRME_Helmet", "FRME_ScanHud", "FRME_ThermalHud", "FRME_XRayHudNew", "FRME_ScanHudFlat" };

        public static MpHudPayload Prepare(MpAssets assets)
        {
            var p = new MpHudPayload();
            var tex = new HashSet<uint>();
            foreach (string name in FrameNames)
            {
                MpPak.Entry e = assets.Named(name);
                if (e == null) continue;
                try
                {
                    MpFrame f = MpFrame.Read(e.Pak.Read(e));
                    p.Frames[name] = f;
                    foreach (MpFrame.Widget w in f.Widgets)
                    {
                        if (w.Type == "MODL")
                        {
                            MpModel m = MpModels.Load(assets, w.ModelId);
                            if (m != null) MpMaterials.TexturesOf(m.Set, tex);
                        }
                        else if (w.Type == "ENRG" && MpAssets.Valid(w.TextureId)) tex.Add(w.TextureId);
                    }
                }
                catch (Exception ex) { MpLog.Warn("HUD: " + name + " not read: " + ex.Message); }
            }

            foreach (uint id in tex)
            {
                if (MpTextures.Has(id)) continue;
                try { p.Textures.Add(new KeyValuePair<uint, MpTexture>(id, MpTexture.Read(assets.Read(id)))); }
                catch (Exception) { }
            }

            MpPak.Entry colors = assets.Named("GuiColors");
            if (colors != null)
            {
                var r = new MpReader(colors.Pak.Read(colors));
                p.Colors = new float[112][];
                for (int i = 0; i < 112; i++) p.Colors[i] = new[] { r.F32(), r.F32(), r.F32(), r.F32() };
                int n = Math.Min(5, r.S32());
                p.VisorColors = new float[n][][];
                for (int v = 0; v < n; v++)
                {
                    p.VisorColors[v] = new float[7][];
                    for (int k = 0; k < 7; k++) p.VisorColors[v][k] = new[] { r.F32(), r.F32(), r.F32(), r.F32() };
                }
            }

            MpPak.Entry gui = assets.Named("Gui");
            if (gui != null)
            {
                byte[] g = gui.Pak.Read(gui);
                // CTweakGui in its order (decomp CTweakGui.cpp): the visor / beam menu item scales and translate at 182 / 186 / 190
                p.MenuActiveScale = MpReader.F32At(g, 182);
                p.MenuInactiveScale = MpReader.F32At(g, 186);
                p.MenuTranslate = MpReader.F32At(g, 190);
                // the scan speeds: a count at 770, then the seconds (this disc: 2 - 1.0 normal, 3.0 slow)
                int n = (int)MpReader.U32At(g, 770);
                if (n > 0 && n <= 8 && 774 + 4 * n <= g.Length)
                {
                    p.ScanSpeeds = new float[n];
                    for (int i = 0; i < n; i++) p.ScanSpeeds[i] = MpReader.F32At(g, 774 + 4 * i);
                }
            }

            MpPak.Entry thermo = assets.Named("TXTR_ThermoPalette"), xray = assets.Named("TXTR_XRayPalette");
            if (thermo != null) p.ThermalPalette = MpTexture.PaletteRgba(thermo.Pak.Read(thermo), out p.ThermalCount);
            if (xray != null) p.XRayPalette = MpTexture.PaletteRgba(xray.Pak.Read(xray), out p.XRayCount);

            MpPak.Entry main = assets.Named("STRG_Main");
            if (main != null) p.Main = MpStrings.Read(main.Pak.Read(main));
            return p;
        }
    }

    /// <summary>
    /// Samus's helmet HUD, from the game's own frames: FRME_Helmet (the visor's rim and glare), FRME_BaseHud (visor and beam
    /// selectors, radar, the message line), and per visor FRME_CombatHud (energy bar, digits, tanks, missiles, threat) / ScanHud /
    /// ThermalHud / XRayHudNew - head-locked in VR, in front of the eye on a monitor. The logic follows CSamusHud and its interfaces:
    /// energy digits "%02d" of the energy in the current tank (CHudEnergyInterface), the tank meter, the bars (CAuiEnergyBarT01 with
    /// the combat coordinate functions), missiles "%3d" (CHudMissileInterface), the visor / beam menus (CHudVisorBeamMenu: the selected
    /// icon in the middle at the active scale, the others out along their direction, lozenges always out; the menu name fades in on a
    /// change), messages (HUD memos) on the base frame's message line.
    /// </summary>
    public sealed class MpHud
    {
        public Transform Anchor;
        private readonly MpHudPayload _p;
        private MpHudFrame _base, _combat, _helmet, _scan, _thermal, _xray, _scanFlat;
        private readonly MpPlayerState _state;
        private Mesh _energyBar, _missileBar;
        private Material _barMat;
        private float _barEnergy = -1f, _barShadow, _shadowTimer;
        private int _shownVisor = -1, _shownBeam = -1, _lastMissiles = -1;
        private float _visorTitleT, _beamTitleT, _messageT;
        public bool VisorMenuOpen, BeamMenuOpen;
        public int VisorHover = -1, BeamHover = -1;
        public bool Built;

        private static readonly string VisorIdx = "1032", BeamIdx = "3210";

        public MpHud(MpHudPayload payload, MpPlayerState state)
        {
            _p = payload;
            _state = state;
            Anchor = new GameObject("Samus HUD").transform;
        }

        private Color C(int i, Color fallback)
        {
            if (_p.Colors == null || i >= _p.Colors.Length) return fallback;
            float[] c = _p.Colors[i];
            return new Color(c[0], c[1], c[2], c[3]);
        }

        private Color VC(int hud, int i, Color fallback)
        {
            if (_p.VisorColors == null || hud >= _p.VisorColors.Length) return fallback;
            float[] c = _p.VisorColors[hud][i];
            return new Color(c[0], c[1], c[2], c[3]);
        }

        public void Build(MpAssets assets)
        {
            foreach (var kv in _p.Textures) MpTextures.Upload(kv.Key, kv.Value);
            MpFrame f;
            int q = 4000;
            if (_p.Frames.TryGetValue("FRME_Helmet", out f)) { _helmet = new MpHudFrame("helmet", f, Anchor, q); _helmet.Build(assets); q += 300; }
            if (_p.Frames.TryGetValue("FRME_CombatHud", out f)) { _combat = new MpHudFrame("combat", f, Anchor, q); _combat.Build(assets); q += 300; }
            if (_p.Frames.TryGetValue("FRME_ScanHud", out f)) { _scan = new MpHudFrame("scan", f, Anchor, q); _scan.Build(assets); q += 300; }
            if (_p.Frames.TryGetValue("FRME_ThermalHud", out f)) { _thermal = new MpHudFrame("thermal", f, Anchor, q); _thermal.Build(assets); q += 300; }
            if (_p.Frames.TryGetValue("FRME_XRayHudNew", out f)) { _xray = new MpHudFrame("x-ray", f, Anchor, q); _xray.Build(assets); q += 300; }
            if (_p.Frames.TryGetValue("FRME_BaseHud", out f)) { _base = new MpHudFrame("base", f, Anchor, q); _base.Build(assets); }
            if (_p.Frames.TryGetValue("FRME_ScanHudFlat", out f)) { q += 300; _scanFlat = new MpHudFrame("scan text", f, Anchor, q); _scanFlat.Build(assets); _scanFlat.SetVisible(false); Hide(_scanFlat, "textpane_scrollmessage"); }

            if (_combat != null)
            {
                _barMat = MakeBarMaterial(_combat.Find("energybart01_energybar"));
                _energyBar = AttachBar(_combat.Find("energybart01_energybar"));
                _missileBar = AttachBar(_combat.Find("energybart01_missilebar"));
                Hide(_combat, "energybart01_threatbar");
                Hide(_combat, "basewidget_bossenergystuff");
                _bossBar = AttachBar(_combat.Find("energybart01_bossbar"));
                Hide(_combat, "textpane_threatwarning");
                Hide(_combat, "textpane_missilewarning");
                Hide(_combat, "textpane_energyword");       // never filled by the game (CHudEnergyInterface does not use it)
                foreach (MpHudFrame other in new[] { _scan, _thermal, _xray }) if (other != null) Hide(other, "textpane_energyword");
            }

            if (_base != null)
            {
                Hide(_base, "basewidget_message");
                Hide(_base, "textpane_counter");
                SetText(_base, "TextPane_VisorMenu", "");
                SetText(_base, "TextPane_BeamMenu", "");
            }

            Built = true;
            Update(0f);
            MpLog.Info("HUD: built from the game's frames (" + _p.Frames.Count + " frames: " + string.Join(", ", new List<string>(_p.Frames.Keys).ToArray()) + ")");
        }

        private string Str(int i) { return _p.Main != null ? MpStrings.Plain(_p.Main[i]) : ""; }

        /// <summary>A string of the game's main string table (STRG_Main), or "".</summary>
        public string MainString(int i) { try { return Str(i); } catch (System.Exception) { return ""; } }

        private static void Hide(MpHudFrame f, string name)
        {
            MpHudNode n = f.Find(name);
            if (n != null) n.SetVisible(false);
        }

        private static void SetText(MpHudFrame f, string name, string text)
        {
            MpHudNode n = f != null ? f.Find(name) : null;
            if (n != null && n.Text != null) n.Text.SetText(text);
        }

        private Material MakeBarMaterial(MpHudNode n)
        {
            Material carrier = MpGame.FxMaterial;
            if (carrier == null || n == null) return null;
            var m = new Material(carrier) { name = "HUD energy bar" };
            Texture2D t = MpTextures.Get(n.W.TextureId);
            m.SetTexture("_MainTex", t != null ? (Texture)t : Texture2D.whiteTexture);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.SetFloat("_ZWrite", 0);
            m.renderQueue = 4500;
            return m;
        }

        private Mesh AttachBar(MpHudNode n)
        {
            if (n == null || _barMat == null) return null;
            var go = new GameObject("bar");
            go.transform.SetParent(n.T, false);
            var mesh = new Mesh { name = "HUD bar " + n.W.Name };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _barMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mesh;
        }

        /// <summary>CHudEnergyInterface::CombatEnergyCoordFunc.</summary>
        private static Vector3[] EnergyCoord(float t)
        {
            float theta = -0.15882353f + 0.46764705f * t;
            float x = 17f * Mathf.Sin(theta), y = 17f * Mathf.Cos(theta) - 17f;
            return new[] { new Vector3(x, y, 0.4f), new Vector3(x, y, 0f) };
        }

        /// <summary>CHudMissileInterface::CombatMissileBarCoordFunc (range 6.05).</summary>
        private static Vector3[] MissileCoord(float t)
        {
            float z = t * 6.05f;
            return new[] { new Vector3(0f, 0f, z), new Vector3(0.3f, 0f, z) };
        }

        /// <summary>CSamusHud::UpdateEscapeTimer: the countdown on the base HUD's counter pane (null hides it).</summary>
        public void SetCounter(string text)
        {
            if (_base == null) return;
            MpHudNode n = _base.Find("textpane_counter");
            if (n == null) return;
            bool on = text != null;
            if (n.Visible != on) n.SetVisible(on);
            if (on) SetText(_base, "textpane_counter", text);
        }

        private List<string> _scanLines;
        private int _scanPage, _scanPages;
        private float _scanPageT;

        /// <summary>
        /// CScanDisplay: a finished scan's text in the scan visor's own text box (FRME_ScanHudFlat), word-wrapped and paged - the game
        /// turns pages on the A button, here they turn by themselves every few seconds.
        /// </summary>
        public void ShowScanText(string text)
        {
            MpHudNode pane = _scanFlat != null ? _scanFlat.Find("textpane_message") : null;
            if (pane == null || pane.Text == null) { ShowMessage(text, 6f); return; }
            _scanLines = pane.Text.Wrap(text);
            int per = pane.Text.LinesFit;
            _scanPages = Mathf.Max(1, (_scanLines.Count + per - 1) / per);
            _scanPage = -1;
            _scanPageT = 0f;
            _scanFlat.SetVisible(true);
        }

        public void HideScanText() { _scanLines = null; if (_scanFlat != null) _scanFlat.SetVisible(false); }

        private void TickScanText(float dt)
        {
            if (_scanLines == null || _scanFlat == null) return;
            if (_state.Visor != MpPlayerState.VisorScan) { HideScanText(); return; }
            _scanPageT -= dt;
            if (_scanPageT > 0f) return;
            _scanPage++;
            if (_scanPage >= _scanPages) { HideScanText(); return; }
            MpHudNode pane = _scanFlat.Find("textpane_message");
            int per = pane.Text.LinesFit;
            var page = _scanLines.GetRange(_scanPage * per, Mathf.Min(per, _scanLines.Count - _scanPage * per));
            SetText(_scanFlat, "textpane_message", string.Join("\n", page.ToArray()));
            _scanPageT = 3.5f + 0.06f * string.Join(" ", page.ToArray()).Length;
        }

        private Mesh _bossBar;
        private bool _bossShown;
        private float _bossFrac = 1f, _bossShadow = 1f;

        /// <summary>
        /// CHudBossEnergyInterface::SetBossParams: the boss bar under the visor (the game's own bar and name pane), filled to
        /// <paramref name="fraction"/> of the boss's health; hidden with <paramref name="visible"/> false.
        /// </summary>
        public void SetBoss(bool visible, string name, float fraction)
        {
            if (_combat == null) return;
            MpHudNode w = _combat.Find("basewidget_bossenergystuff");
            if (w == null) return;
            if (visible != _bossShown) { w.SetVisible(visible); _bossShown = visible; if (visible) _bossShadow = fraction; }
            if (!visible) return;
            MpHudNode pane = _combat.Find("textpane_boss");
            if (pane != null) pane.SetVisible(true);
            SetText(_combat, "textpane_boss", name ?? "");
            _bossFrac = Mathf.Clamp01(fraction);
        }

        private void TickBoss(float dt)
        {
            if (!_bossShown || _bossBar == null) return;
            _bossShadow = _bossShadow > _bossFrac ? Mathf.MoveTowards(_bossShadow, _bossFrac, dt * 0.25f) : _bossFrac;
            MpHudFrame.BarMesh(_bossBar, BossCoord, 0.2f, _bossFrac, _bossShadow, VC(0, 0, new Color(0.9f, 0.9f, 1f, 1f)), VC(0, 2, new Color(1f, 1f, 1f, 0.6f)), VC(0, 1, new Color(0.3f, 0.3f, 0.4f, 0.3f)));
        }

        /// <summary>CHudBossEnergyInterface::BossEnergyCoordFunc.</summary>
        private static Vector3[] BossCoord(float t)
        {
            float x = 9.25f * t - 4.625f;
            return new[] { new Vector3(x, 0f, 0f), new Vector3(x, 0f, 0.4f) };
        }

        private Renderer _hurtQuad;
        private Material _hurtMat;
        private float _hurtT, _hurtAmp;

        /// <summary>CSamusHud damage: the view flashes red (stronger for bigger hits) and fades over 0.5 s.</summary>
        public void Hurt(float amount)
        {
            if (_hurtQuad == null)
            {
                Material carrier = MpGame.FxMaterial;
                if (carrier == null) return;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.Destroy(q.GetComponent<Collider>());
                q.name = "HUD damage flash";
                q.transform.SetParent(Anchor, false);
                q.transform.localPosition = new Vector3(0f, 0f, 0.3f);
                q.transform.localScale = new Vector3(1.2f, 0.9f, 1f);
                _hurtMat = new Material(carrier) { name = "HUD damage flash" };
                _hurtMat.SetTexture("_MainTex", Texture2D.whiteTexture);
                _hurtMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _hurtMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _hurtMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                _hurtMat.renderQueue = 4990;
                _hurtQuad = q.GetComponent<MeshRenderer>();
                _hurtQuad.sharedMaterial = _hurtMat;
                _hurtQuad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _hurtT = 0.5f;
            _hurtAmp = Mathf.Clamp(0.25f + 0.35f * amount, 0.2f, 0.6f);
        }

        private void TickHurt(float dt)
        {
            if (_hurtQuad == null) return;
            _hurtT = Mathf.Max(0f, _hurtT - dt);
            bool on = _hurtT > 0f;
            if (_hurtQuad.enabled != on) _hurtQuad.enabled = on;
            if (on) _hurtMat.SetColor("_Color", new Color(1f, 0.1f, 0.05f, _hurtAmp * (_hurtT / 0.5f)));
        }

        public void ShowMessage(string text, float seconds)
        {
            if (_base == null) return;
            MpHudNode msg = _base.Find("basewidget_message");
            if (msg == null) return;
            msg.SetVisible(true);
            // CSamusHud::SetMessage shows the widget with its children (kTM_Children): the text pane itself starts hidden in the FRME
            MpHudNode pane = _base.Find("textpane_message");
            if (pane != null) pane.SetVisible(true);
            // one line of the pane: the start of a long memo (wrapped at the pane's width)
            List<string> wrapped = pane != null && pane.Text != null ? pane.Text.Wrap(text) : null;
            SetText(_base, "textpane_message", wrapped != null && wrapped.Count > 0 ? string.Join("\n", wrapped.GetRange(0, Mathf.Min(wrapped.Count, Mathf.Max(1, pane.Text.LinesFit))).ToArray()) : text);
            _messageT = Mathf.Max(1.5f, seconds);
        }

        /// <summary>Head-locked: the anchor sits at the eye with the head's rotation (call just before the camera draws).</summary>
        public void Follow(Transform head)
        {
            if (head == null) return;
            if (_settingsV != MpSettings.Version) ApplySettings();
            // the player's depth (tablet: HUD distance) scales the whole HUD about the eye: the same picture, nearer or further
            Anchor.localScale = Vector3.one * (Mathf.Max(0.1f, MpSettings.HudDistance) / MpHudFrame.Distance);
            Anchor.SetPositionAndRotation(head.position + head.up * MpSettings.HudHeight, head.rotation);
        }

        private int _settingsV = -1;

        /// <summary>The tablet's HUD options: size, opacity, shown.</summary>
        private void ApplySettings()
        {
            _settingsV = MpSettings.Version;
            foreach (MpHudFrame f in new[] { _helmet, _combat, _scan, _thermal, _xray, _base, _scanFlat })
            {
                if (f == null) continue;
                f.Layout(MpSettings.HudSize);
            }

            if (MpHudNode.GlobalAlpha != MpSettings.HudOpacity)
            {
                MpHudNode.GlobalAlpha = MpSettings.HudOpacity;
                foreach (MpHudFrame f in new[] { _helmet, _combat, _scan, _thermal, _xray, _base, _scanFlat })
                    if (f != null) foreach (MpHudNode n in f.Nodes.Values) n.ApplyColor();
            }

            SetVisible(MpSettings.HudShown);
        }

        public void Update(float dt)
        {
            if (!Built) return;
            TickHurt(dt);
            TickBoss(dt);
            TickScanText(dt);
            int visor = _state.Visor;
            if (_helmet != null) _helmet.SetVisible(MpSettings.HelmetShown);      // CSamusHud: the helmet follows the GUI tweak's helmet mode, in every visor (not the morph ball)
            if (_combat != null) _combat.SetVisible(visor == MpPlayerState.VisorCombat);
            if (_scan != null) _scan.SetVisible(visor == MpPlayerState.VisorScan);
            if (_thermal != null) _thermal.SetVisible(visor == MpPlayerState.VisorThermal);
            if (_xray != null) _xray.SetVisible(visor == MpPlayerState.VisorXRay);

            // energy (CHudEnergyInterface)
            float tankCap = 100f;
            float e = Mathf.Max(0f, _state.Energy);
            int filledTanks = Mathf.Clamp(Mathf.FloorToInt((e - 0.01f) / tankCap), 0, _state.EnergyTanks);
            float inTank = e - filledTanks * tankCap;
            if (_barEnergy < 0f) { _barEnergy = inTank; _barShadow = inTank; }
            _barEnergy = Mathf.MoveTowards(_barEnergy, inTank, 99f * dt);
            if (_barShadow > _barEnergy) { _shadowTimer += dt; if (_shadowTimer > 0.7f) _barShadow = Mathf.MoveTowards(_barShadow, _barEnergy, 20f * dt); }
            else { _barShadow = _barEnergy; _shadowTimer = 0f; }
            if (_combat != null)
            {
                SetText(_combat, "textpane_energydigits", ((int)Mathf.Repeat(_barEnergy, tankCap)).ToString("00"));
                bool low = e < 30f;
                SetText(_combat, "textpane_energywarning", low ? Str(9) : "");
                Color filled = low ? C(12, Color.red) : VC(0, 0, new Color(0.9f, 0.9f, 1f, 1f));
                Color shadow = low ? C(13, Color.red) : VC(0, 2, new Color(1f, 1f, 1f, 0.6f));
                Color empty = low ? C(14, Color.red) : VC(0, 1, new Color(0.3f, 0.3f, 0.4f, 0.3f));
                if (_energyBar != null) MpHudFrame.BarMesh(_energyBar, EnergyCoord, 0.2f, _barEnergy / tankCap, _barShadow / tankCap, filled, shadow, empty);
                // tanks (CAuiMeter): a tank shows when owned, filled while full
                MpHudNode meter = _combat.Find("meter_energytanks");
                if (meter != null)
                    for (int i = 0; i < 14; i++)
                    {
                        MpHudNode g = _combat.Find(i == 0 ? "Group_Energytank" : "Group_Energytank" + i);
                        if (g == null) continue;
                        g.SetVisible(i < _state.EnergyTanks);
                        MpHudNode fill = _combat.Find("model_Etankfill0" + i + "b");
                        if (fill != null) fill.SetVisible(i < filledTanks);
                    }

                // missiles (CHudMissileInterface)
                bool hasMissiles = _state.MissileCapacity > 0;
                MpHudNode mStuff = _combat.Find("basewidget_missilestuff");
                if (mStuff != null) mStuff.SetVisible(hasMissiles);
                if (_state.Missiles != _lastMissiles)
                {
                    _lastMissiles = _state.Missiles;
                    SetText(_combat, "textpane_missiledigits", Mathf.Clamp(_state.Missiles, 0, 999).ToString().PadLeft(3));
                    if (_missileBar != null)
                    {
                        float frac = _state.MissileCapacity > 0 ? (float)_state.Missiles / _state.MissileCapacity : 0f;
                        MpHudFrame.BarMesh(_missileBar, MissileCoord, 1f, frac, frac, C(28, Color.white), C(29, Color.white), C(30, new Color(1, 1, 1, 0.3f)));
                    }
                }
            }

            // the visor and beam menus (CHudVisorBeamMenu, steady state + the open menu while a grip is held)
            if (_base != null)
            {
                if (_shownVisor != _state.Visor) { _shownVisor = _state.Visor; _visorTitleT = 1.5f; SetText(_base, "TextPane_VisorMenu", Str(new[] { 0, 2, 1, 3 }[_state.Visor])); }
                if (_shownBeam != _state.Beam) { _shownBeam = _state.Beam; _beamTitleT = 1.5f; SetText(_base, "TextPane_BeamMenu", Str(4 + _state.Beam)); }
                _visorTitleT = Mathf.Max(0f, _visorTitleT - dt);
                _beamTitleT = Mathf.Max(0f, _beamTitleT - dt);
                Menu(true, _state.Visor, VisorMenuOpen ? VisorHover : -1, VisorMenuOpen || _visorTitleT > 0f ? Mathf.Clamp01(_visorTitleT + (VisorMenuOpen ? 1f : 0f)) : 0f);
                Menu(false, _state.Beam, BeamMenuOpen ? BeamHover : -1, BeamMenuOpen || _beamTitleT > 0f ? Mathf.Clamp01(_beamTitleT + (BeamMenuOpen ? 1f : 0f)) : 0f);
                if (_messageT > 0f)
                {
                    _messageT -= dt;
                    if (_messageT <= 0f) Hide(_base, "basewidget_message");
                }
            }
        }

        private void Menu(bool visor, int selected, int hover, float titleAlpha)
        {
            string baseName = visor ? "model_visor" : "model_beam";
            string idx = visor ? VisorIdx : BeamIdx;
            MpHudNode menu = _base.Find(visor ? "BaseWidget_VisorMenu" : "BaseWidget_BeamMenu");
            if (menu == null) return;
            Color active = C(10, Color.white), inactive = C(11, new Color(0.5f, 0.5f, 0.5f, 1f)), loz = C(43, Color.white);
            for (int i = 0; i < 4; i++)
            {
                bool owned = visor ? _state.HasVisor(i) : _state.HasBeam(i);
                MpHudNode icon = _base.Find(baseName + "icon" + idx[i]);
                MpHudNode lz = _base.Find(baseName + "loz" + idx[i]);
                bool sel = i == selected;
                bool hot = i == hover;
                if (icon != null)
                {
                    icon.SetVisible(owned);
                    icon.SetColor(sel || hot ? active : inactive);
                    Place(visor, i, icon, sel ? 0f : 1f, menu);
                }

                if (lz != null)
                {
                    lz.SetColor(owned ? (sel ? loz : hot ? active : inactive) : loz);
                    Place(visor, i, lz, 1f, menu);
                }
            }

            MpHudNode ghost = _base.Find(baseName + "ghost");
            if (ghost != null) { ghost.SetColor(active); Place(visor, selected, ghost, 0f, menu); }
            MpHudNode title = _base.Find(visor ? "basewidget_visormenutitle" : "basewidget_beammenutitle");
            if (title != null)
            {
                title.SetVisible(titleAlpha > 0.01f);
                title.SetColor(new Color(1f, 1f, 1f, titleAlpha));
                foreach (MpHudNode n in _base.Nodes.Values) if (n.Parent == title) n.ApplyColor();
            }
        }

        /// <summary>CHudVisorBeamMenu::UpdateMenuWidgetTransform: translate t x tweak along the item's direction, scale between active and inactive.</summary>
        private void Place(bool visor, int idx, MpHudNode n, float t, MpHudNode menu)
        {
            float translate = t * _p.MenuTranslate;
            float scale = t * _p.MenuInactiveScale + (1f - t) * _p.MenuActiveScale;
            int dir = idx;
            if (visor) { if (dir == 2) dir = 3; else if (dir == 3) dir = 2; }
            else { if (dir == 1) dir = 2; else if (dir == 2) dir = 1; }
            Vector3 off = dir == 0 ? new Vector3(0, 0, translate) : dir == 1 ? new Vector3(translate, 0, 0) : dir == 2 ? new Vector3(0, 0, -translate) : new Vector3(-translate, 0, 0);
            // the widget's world = the menu's world * offset * scale (the game sets the object-to-world transform outright)
            Transform parent = n.T.parent;
            Vector3 menuLocal = parent.InverseTransformPoint(menu.T.TransformPoint(off));
            n.T.localPosition = menuLocal;
            n.T.localRotation = Quaternion.Inverse(parent.rotation) * menu.T.rotation;
            n.T.localScale = Vector3.one * scale;
        }

        public void SetVisible(bool on)
        {
            if (Anchor.gameObject.activeSelf != on) Anchor.gameObject.SetActive(on);
        }

        public void Destroy()
        {
            if (Anchor != null) UnityEngine.Object.Destroy(Anchor.gameObject);
        }
    }
}
