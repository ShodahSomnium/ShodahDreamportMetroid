using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The "METROID PRIME" app on the Somnium client's own tablet (like the BO2, ETS 2 and WoW Dreamports' apps): the player's options
    /// (<see cref="MpSettings"/>) and the game's few commands (<see cref="MpGameActions"/>) on one page with five tabs - HUD, Arm Cannon,
    /// Sound &amp; Controls, Game, About. <see cref="MpTabletLink"/> finds the tablet and puts the app's button on its home screen; the
    /// tablet opens on a tap of Y (left controller) in VR and on Tab on a monitor, both the client's own bindings.
    ///
    /// The game's entry points (MpGame calls them):
    ///  - <see cref="Start"/>(parent): makes the app (a "Metroid Tablet App" object under <c>parent</c> with the link and the page); a second
    ///    call returns the running one. Never throws: without a tablet (the editor, a client whose tablet looks different) it only searches.
    ///  - <see cref="Shutdown"/>(): takes everything off the client's tablet at once (the tablet outlives the world) and removes the app;
    ///    safe to call twice and without an app. MpGame.OnDestroy should call it.
    ///  - <see cref="IsOpen"/>: true while the app's page is showing on an open tablet - the game holds fire input then.
    /// </summary>
    public static class MpTabletApp
    {
        /// <summary>The running page (null before <see cref="Start"/> and after <see cref="Shutdown"/>).</summary>
        public static MpTabletPage Page { get { return MpTabletPage.Instance; } }

        public static MpTabletLink Link
        {
            get
            {
                MpTabletPage page = MpTabletPage.Instance;
                return page != null ? page.Link : null;
            }
        }

        /// <summary>The app's page is showing on an open tablet (the player is pointing at the options, not playing).</summary>
        public static bool IsOpen
        {
            get
            {
                MpTabletPage page = MpTabletPage.Instance;
                return page != null && page.Showing;
            }
        }

        /// <summary>The client's tablet is open (on any of its pages) - known only once it was found.</summary>
        public static bool TabletOpen
        {
            get
            {
                MpTabletLink link = Link;
                return link != null && link.IsLinked && link.TabletOpen;
            }
        }

        public static MpTabletPage Start(Transform parent)
        {
            MpTabletPage running = MpTabletPage.Instance;

            if (running != null)
            {
                return running;
            }

            GameObject host = null;

            try
            {
                host = new GameObject("Metroid Tablet App");
                if (parent != null) host.transform.SetParent(parent, false);
                MpTabletLink link = host.AddComponent<MpTabletLink>();
                MpTabletPage page = host.AddComponent<MpTabletPage>();
                page.Setup(link);
                return page;
            }
            catch (Exception e)
            {
                MpLog.Warn("Tablet: the app could not start (" + e.GetType().Name + ": " + e.Message + ") - the game plays with the options of options.txt.");
                if (host != null) UnityEngine.Object.Destroy(host);
                return null;
            }
        }

        public static void Shutdown()
        {
            MpTabletPage page = MpTabletPage.Instance;

            if (page != null)
            {
                page.Teardown(true);
            }

            // A link without its page (should not happen): still nothing may stay on the client's tablet.
            MpTabletLink stray = MpTabletLink.Instance;

            if (stray != null)
            {
                stray.Shutdown();
            }
        }
    }

    /// <summary>
    /// The page of the "METROID PRIME" app, built into the link's frame once the tablet is found (and again after a new tablet was found).
    /// Every control writes its <see cref="MpSettings"/> field and calls <see cref="MpSettings.Touch"/> (the game re-applies; the file is
    /// written - while a slider is dragged it is written 0.5 s after the last move). The values shown are read again from MpSettings when
    /// the page opens and whenever its <see cref="MpSettings.Version"/> moved under it. The Game tab greys out what the game has not
    /// offered yet (a null <see cref="MpGameActions"/> member) and shows every command's answer in the status bar at the bottom.
    /// Cost while the app is closed: a few field reads a frame; nothing allocates.
    /// </summary>
    [DefaultExecutionOrder(1001)]
    public sealed class MpTabletPage : MonoBehaviour
    {
        public const int TabHud = 0, TabCannon = 1, TabSound = 2, TabGame = 3, TabAbout = 4;
        public static readonly string[] TabNames = { "HUD", "ARM CANNON", "SOUND & CONTROLS", "GAME", "ABOUT" };

        private const float W = MpTabletUi.FrameWidth, H = MpTabletUi.FrameHeight;
        private const float ContentTop = 172f, ContentHeight = 616f, StatusTop = 800f;
        private const float RowH = 60f, RowPitch = 76f, FirstRow = 12f;
        private const float RoomRowH = 46f, RoomRowPitch = 50f;

        public static MpTabletPage Instance { get; private set; }

        public MpTabletLink Link { get { return _link; } }

        /// <summary>The page is showing: the app is open on an open tablet.</summary>
        public bool Showing { get { return _link != null && _link.IsLinked && _link.AppOpen && _link.TabletOpen; } }

        /// <summary>The page's root in the link's frame (null until built).</summary>
        public RectTransform Root { get { return _root; } }

        public int Tab { get { return _tab; } }
        public string StatusText { get { return _statusText; } }

        public RectTransform TabRoot(int tab)
        {
            return _root != null && tab >= 0 && tab < _tabRoots.Length ? _tabRoots[tab] : null;
        }

        private sealed class SliderRow
        {
            public Slider Slider;
            public TextMeshProUGUI Value;
            public Func<float> Get;
            public Action<float> Set;
            public Func<float, string> Format;
            public float Step, Quantum;
        }

        private sealed class ToggleRow
        {
            public MpUiButton Button;
            public Func<bool> Get;
            public Action<bool> Set;
        }

        private static readonly float[] DamageLevels = { 0.5f, 1f, 2f };
        private static readonly string[] DamageNames = { "EASY", "NORMAL", "HARD" };

        private MpTabletLink _link;
        private Texture2D _icon;
        private RectTransform _root;
        private readonly RectTransform[] _tabRoots = new RectTransform[5];
        private readonly MpUiButton[] _tabButtons = new MpUiButton[5];
        private int _tab;
        private TextMeshProUGUI _status;
        private string _statusText = "";
        private readonly List<SliderRow> _sliders = new List<SliderRow>();
        private readonly List<ToggleRow> _toggles = new List<ToggleRow>();
        private readonly MpUiButton[] _damage = new MpUiButton[3];
        private TextMeshProUGUI _damageValue;
        private int _seenVersion = -1;
        private bool _savePending;
        private float _saveAt;

        // the Game tab
        private TextMeshProUGUI _live, _saveInfo, _roomsNote;
        private MpUiButton _saveButton, _loadButton, _respawnButton, _upButton, _downButton;
        private ScrollRect _scroll;
        private RectTransform _roomContent;
        private readonly List<MpUiButton> _roomRows = new List<MpUiButton>();
        private string[] _roomsShown;
        private float _nextLive;

        // the About tab
        private TextMeshProUGUI _about;

        private bool _tornDown, _buildFailed;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;
        }

        internal void Setup(MpTabletLink link)
        {
            _link = link;
            _icon = MpTabletIcon.Paint(128);
            _link.AppIcon = _icon;
            _link.AppShown += OnAppShown;
            _link.LinkChanged += OnLinkChanged;
            MpLog.Info("Tablet: searching for the Somnium tablet (the \"" + _link.AppTitle + "\" app goes on its home screen; VR: tap Y, monitor: Tab).");
        }

        private void OnDestroy()
        {
            Teardown(false);
        }

        /// <summary>Takes the app off the client's tablet (synchronous) and removes it; the host object goes too when <paramref name="destroyHost"/>.</summary>
        internal void Teardown(bool destroyHost)
        {
            if (_tornDown)
            {
                return;
            }

            _tornDown = true;
            enabled = false;

            try
            {
                FlushSave();

                if (_link != null)
                {
                    _link.AppShown -= OnAppShown;
                    _link.LinkChanged -= OnLinkChanged;
                    _link.Shutdown();               // the button, the canvas, the Tabs container, the tablet's size
                }

                if (_icon != null)
                {
                    Destroy(_icon);
                }
            }
            catch (Exception e)
            {
                MpLog.Warn("Tablet: shutting the app down failed: " + e.GetType().Name + ": " + e.Message);
            }

            _link = null;
            _icon = null;
            _root = null;

            if (Instance == this)
            {
                Instance = null;
            }

            if (destroyHost && this != null)
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            if (_link == null)
            {
                return;
            }

            if (_root == null)
            {
                if (_link.Frame == null || _buildFailed)
                {
                    return;
                }

                Build();

                if (_root == null)
                {
                    return;
                }
            }

            if (!_link.AppOpen)
            {
                FlushSave();
                return;
            }

            if (_savePending && Time.unscaledTime >= _saveAt)
            {
                FlushSave();
            }

            if (MpSettings.Version != _seenVersion)
            {
                RefreshSettings(); // something else changed the options under the open page
            }

            if (_tab == TabGame && Time.unscaledTime >= _nextLive)
            {
                _nextLive = Time.unscaledTime + 1f;
                RefreshGame(false);
            }
        }

        private void OnAppShown(bool shown)
        {
            if (shown)
            {
                if (_root == null && !_buildFailed && _link != null && _link.Frame != null)
                {
                    Build();
                }

                RefreshAll();
            }
            else
            {
                FlushSave();
            }
        }

        private void OnLinkChanged()
        {
            if (_link == null || !_link.IsLinked)
            {
                _root = null; // its objects went with the app's canvas
            }

            _buildFailed = false; // a new tablet: one more try
        }

        /// <summary>Builds the page; a failure is logged once and leaves the app's page empty until the next link (never an exception a frame).</summary>
        private void Build()
        {
            try
            {
                BuildPage();
            }
            catch (Exception e)
            {
                _buildFailed = true;
                MpLog.Warn("Tablet: building the options page failed (" + e.GetType().Name + ": " + e.Message + ") - the app's page stays empty.");
                if (_root != null) Destroy(_root.gameObject);
                _root = null;
            }
        }

        // ------------------------------------------------------------------ the page

        private void BuildPage()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _sliders.Clear();
            _toggles.Clear();
            _roomRows.Clear();
            _roomsShown = null;

            _root = MpTabletUi.Place(_link.Frame, "Metroid Options", 0f, 0f, W, H);
            MpTabletUi.Box(_root, "Backdrop", 0f, 0f, W, H, MpTabletUi.Navy, true);
            MpTabletUi.Border(_root, "Frame", 6f, 6f, W - 12f, H - 12f, 2f, new Color(0.247f, 0.816f, 1f, 0.35f));
            Corners(_root);

            // the header: the game's name, the version, how the tablet opens
            TextMeshProUGUI title = MpTabletUi.Text(_root, "Title", "METROID PRIME", 46f, MpTabletUi.Ink, TextAlignmentOptions.Left, 32f, 16f, 640f, 54f, true);
            title.characterSpacing = 6f;
            TextMeshProUGUI sub = MpTabletUi.Text(_root, "Subtitle", "DREAMPORT OPTIONS", 19f, MpTabletUi.Orange, TextAlignmentOptions.Left, 34f, 68f, 640f, 26f, true);
            sub.characterSpacing = 10f;
            MpTabletUi.Text(_root, "Version", MpVersion.Label, 18f, MpTabletUi.Grey, TextAlignmentOptions.Right, 560f, 22f, 616f, 28f);
            MpTabletUi.Text(_root, "How", "VR: tap Y for the tablet  -  monitor: Tab", 18f, MpTabletUi.Grey, TextAlignmentOptions.Right, 560f, 50f, 616f, 28f);
            MpTabletUi.Box(_root, "Rule", 24f, 98f, W - 48f, 2f, MpTabletUi.Cyan);
            MpTabletUi.Box(_root, "Rule Accent", 24f, 97f, 120f, 4f, MpTabletUi.Orange);

            float[] widths = { 150f, 230f, 320f, 150f, 180f };
            float x = 24f;

            for (int i = 0; i < TabNames.Length; i++)
            {
                int tab = i;
                _tabButtons[i] = MpTabletUi.Button(_root, TabNames[i], x, 110f, widths[i], 50f, 22f, () => SetTab(tab, true));
                x += widths[i] + 12f;
                _tabRoots[i] = MpTabletUi.Place(_root, TabNames[i] + " Tab", 0f, ContentTop, W, ContentHeight);
            }

            BuildHud(_tabRoots[TabHud]);
            BuildCannon(_tabRoots[TabCannon]);
            BuildSound(_tabRoots[TabSound]);
            BuildGame(_tabRoots[TabGame]);
            BuildAbout(_tabRoots[TabAbout]);

            // the status bar: every command's answer
            MpTabletUi.Box(_root, "Status Bar", 24f, StatusTop, W - 48f, 44f, MpTabletUi.Panel);
            MpTabletUi.Box(_root, "Status Mark", 24f, StatusTop, 6f, 44f, MpTabletUi.Cyan);
            _status = MpTabletUi.Text(_root, "Status", "", 21f, MpTabletUi.Ink, TextAlignmentOptions.MidlineLeft, 42f, StatusTop, W - 84f, 44f);
            _status.textWrappingMode = TextWrappingModes.NoWrap;
            SetStatus(_statusText.Length > 0 ? _statusText : "Changes apply at once and are kept on this computer.");

            SetTab(_tab, false);
            RefreshAll();
            _link.ApplyLayers();
            MpLog.Info("Tablet: the options page was built into the app frame in " + watch.ElapsedMilliseconds + " ms (" + _root.GetComponentsInChildren<Graphic>(true).Length + " graphics).");
        }

        /// <summary>Orange corner brackets, the game's frame marks.</summary>
        private static void Corners(Transform root)
        {
            const float L = 36f, T = 4f, M = 6f;
            MpTabletUi.Box(root, "Corner TL h", M, M, L, T, MpTabletUi.Orange);
            MpTabletUi.Box(root, "Corner TL v", M, M, T, L, MpTabletUi.Orange);
            MpTabletUi.Box(root, "Corner TR h", W - M - L, M, L, T, MpTabletUi.Orange);
            MpTabletUi.Box(root, "Corner TR v", W - M - T, M, T, L, MpTabletUi.Orange);
            MpTabletUi.Box(root, "Corner BL h", M, H - M - T, L, T, MpTabletUi.Orange);
            MpTabletUi.Box(root, "Corner BL v", M, H - M - L, T, L, MpTabletUi.Orange);
            MpTabletUi.Box(root, "Corner BR h", W - M - L, H - M - T, L, T, MpTabletUi.Orange);
            MpTabletUi.Box(root, "Corner BR v", W - M - T, H - M - L, T, L, MpTabletUi.Orange);
        }

        public void SetTab(int tab, bool byPlayer)
        {
            if (tab < 0 || tab >= TabNames.Length) return;
            bool changed = tab != _tab;
            _tab = tab;

            for (int i = 0; i < _tabRoots.Length; i++)
            {
                if (_tabRoots[i] != null) _tabRoots[i].gameObject.SetActive(i == tab);
                if (_tabButtons[i] != null) _tabButtons[i].Selected = i == tab;
            }

            if (tab == TabGame)
            {
                _nextLive = Time.unscaledTime + 1f;
                RefreshGame(true);
            }
            else if (tab == TabAbout)
            {
                RefreshAbout();
            }

            if (byPlayer)
            {
                MpLog.Info("Tablet: tab " + TabNames[tab] + (changed ? "" : " (already shown)") + ".");
            }
        }

        public void SetStatus(string text)
        {
            _statusText = text ?? "";
            if (_status != null) _status.text = _statusText;
        }

        private void RefreshAll()
        {
            if (_root == null) return;
            RefreshSettings();
            RefreshGame(true);
            RefreshAbout();
        }

        // ------------------------------------------------------------------ the HUD, Arm Cannon and Sound & Controls tabs

        private void BuildHud(Transform tab)
        {
            AddSlider(tab, 0, "Distance", 0.3f, 4f, 0.1f, 0.01f, () => MpSettings.HudDistance, v => MpSettings.HudDistance = v, v => Fixed(v, "0.00") + " m", "CLOSER", "FURTHER");
            AddSlider(tab, 1, "Size", 0.5f, 1.5f, 0.05f, 0.01f, () => MpSettings.HudSize, v => MpSettings.HudSize = v, v => Fixed(v, "0.00") + "x", "-", "+");
            AddSlider(tab, 2, "Height", -0.5f, 0.5f, 0.05f, 0.01f, () => MpSettings.HudHeight, v => MpSettings.HudHeight = v, v => Signed(v, "0.00") + " m", "LOWER", "HIGHER");
            AddSlider(tab, 3, "Opacity", 0.1f, 1f, 0.05f, 0.01f, () => MpSettings.HudOpacity, v => MpSettings.HudOpacity = v, Percent, "-", "+");
            AddToggle(tab, 4, "HUD shown", "The helmet's combat HUD: energy, missiles, beams, visors.", () => MpSettings.HudShown, v => MpSettings.HudShown = v);
            AddToggle(tab, 5, "Helmet rim", "The helmet's frame and glare around the view.", () => MpSettings.HelmetShown, v => MpSettings.HelmetShown = v);
            AddToggle(tab, 6, "Cutscenes: you are the camera", "Stand inside the cutscene and look around (the world moves around you); off: on the cinema screen.", () => MpSettings.CinemaFirstPerson, v => MpSettings.CinemaFirstPerson = v);
            AddToggle(tab, 7, "Cutscenes: avatar plays Samus", "The instance master's avatar (yours when alone) takes Samus's place in cutscenes.", () => MpSettings.CinemaAvatar, v => MpSettings.CinemaAvatar = v);
        }

        private void BuildCannon(Transform tab)
        {
            AddSlider(tab, 0, "Forward", -30f, 30f, 1f, 0.5f, () => MpSettings.CannonForward, v => MpSettings.CannonForward = v, v => Signed(v, "0.0") + " cm", "-", "+");
            AddSlider(tab, 1, "Up", -30f, 30f, 1f, 0.5f, () => MpSettings.CannonUp, v => MpSettings.CannonUp = v, v => Signed(v, "0.0") + " cm", "-", "+");
            AddSlider(tab, 2, "Right", -30f, 30f, 1f, 0.5f, () => MpSettings.CannonRight, v => MpSettings.CannonRight = v, v => Signed(v, "0.0") + " cm", "-", "+");
            AddSlider(tab, 3, "Pitch", -45f, 45f, 2f, 1f, () => MpSettings.CannonPitch, v => MpSettings.CannonPitch = v, v => Signed(v, "0") + " deg", "DOWN", "UP");
            AddSlider(tab, 4, "Scale", 0.5f, 1.6f, 0.05f, 0.01f, () => MpSettings.CannonScale, v => MpSettings.CannonScale = v, v => Fixed(v, "0.00") + "x", "-", "+");
            AddToggle(tab, 5, "Suit forearm", "Samus's suit arm behind the cannon.", () => MpSettings.SuitArm, v => MpSettings.SuitArm = v);
            AddToggle(tab, 6, "Gun sway on monitor", "The cannon's sway and idle motions (monitor only).", () => MpSettings.GunMotion, v => MpSettings.GunMotion = v);
            AddToggle(tab, 7, "Cannon on forearm", "VR: aim with your forearm, not the wrist (your avatar's hand is hidden either way).", () => MpSettings.CannonOnForearm, v => MpSettings.CannonOnForearm = v);
        }

        private void BuildSound(Transform tab)
        {
            AddSlider(tab, 0, "Music volume", 0f, 1f, 0.05f, 0.01f, () => MpSettings.MusicVolume, v => MpSettings.MusicVolume = v, Percent, "-", "+");
            AddSlider(tab, 1, "Effects volume", 0f, 1f, 0.05f, 0.01f, () => MpSettings.SfxVolume, v => MpSettings.SfxVolume = v, Percent, "-", "+");
            AddSlider(tab, 2, "Haptics", 0f, 1f, 0.1f, 0.01f, () => MpSettings.Haptics, v => MpSettings.Haptics = v, Percent, "-", "+");

            float y = RowY(3);
            MpTabletUi.Text(tab, "Damage taken", "Damage taken", 25f, MpTabletUi.Ink, TextAlignmentOptions.MidlineLeft, 30f, y, 262f, RowH, true);

            for (int i = 0; i < DamageLevels.Length; i++)
            {
                float level = DamageLevels[i];
                _damage[i] = MpTabletUi.Button(tab, DamageNames[i], 446f + i * 214f, y + 6f, 200f, RowH - 12f, 22f, () => SetDamage(level));
            }

            _damageValue = MpTabletUi.Text(tab, "Damage taken Value", "", 25f, MpTabletUi.Cyan, TextAlignmentOptions.MidlineRight, 1090f, y, 86f, RowH, true);
            AddToggle(tab, 4, "All beams & visors (testing)", "Every beam and visor from the start.", () => MpSettings.UnlockAll, v => MpSettings.UnlockAll = v);
            AddToggle(tab, 5, "Morph ball: 3rd person", "See the ball from behind with your own eyes (your avatar hidden); off: on the cinema screen.", () => MpSettings.BallThirdPerson, v => MpSettings.BallThirdPerson = v);
            AddToggle(tab, 6, "Samus speed & jump", "Your avatar moves, jumps and falls as fast as Samus (measured and matched as you move); off: Somnium's own.", () => MpSettings.SamusMovement, v => MpSettings.SamusMovement = v);

            AddToggle(tab, 7, "Skip the intro", "A new game starts on the frigate's deck, past the opening cutscene (the master's choice in co-op).", () => MpSettings.SkipIntro, v => MpSettings.SkipIntro = v);
        }

        private static float RowY(int row)
        {
            return FirstRow + row * RowPitch;
        }

        private void AddSlider(Transform tab, int row, string label, float min, float max, float step, float quantum,
                               Func<float> get, Action<float> set, Func<float, string> format, string minus, string plus)
        {
            float y = RowY(row);
            var r = new SliderRow { Get = get, Set = set, Format = format, Step = step, Quantum = quantum };
            MpTabletUi.Text(tab, label, label, 25f, MpTabletUi.Ink, TextAlignmentOptions.MidlineLeft, 30f, y, 250f, RowH, true);
            MpTabletUi.Button(tab, minus, 288f, y + 6f, 150f, RowH - 12f, minus.Length == 1 ? 34f : 20f, () => StepSlider(r, -1f));
            r.Slider = MpTabletUi.Slider(tab, label + " Slider", 452f, y, 420f, RowH, min, max);
            MpTabletUi.Button(tab, plus, 886f, y + 6f, 150f, RowH - 12f, plus.Length == 1 ? 34f : 20f, () => StepSlider(r, 1f));
            r.Value = MpTabletUi.Text(tab, label + " Value", "", 25f, MpTabletUi.Cyan, TextAlignmentOptions.MidlineRight, 1046f, y, 130f, RowH, true);
            r.Value.textWrappingMode = TextWrappingModes.NoWrap;
            r.Slider.onValueChanged.AddListener(v => OnSlider(r, v));
            _sliders.Add(r);
        }

        private void AddToggle(Transform tab, int row, string label, string hint, Func<bool> get, Action<bool> set)
        {
            float y = RowY(row);
            var t = new ToggleRow { Get = get, Set = set };
            MpTabletUi.Text(tab, label, label, 25f, MpTabletUi.Ink, TextAlignmentOptions.MidlineLeft, 30f, y, 410f, RowH, true);
            t.Button = MpTabletUi.Button(tab, "OFF", 446f, y + 6f, 160f, RowH - 12f, 22f, () => FlipToggle(t));
            t.Button.name = label + " Toggle";
            MpTabletUi.Text(tab, label + " Hint", hint, 20f, MpTabletUi.Grey, TextAlignmentOptions.MidlineLeft, 626f, y, 550f, RowH);
            _toggles.Add(t);
        }

        private void OnSlider(SliderRow r, float v)
        {
            float q = Quantize(v, r.Quantum, r.Slider.minValue, r.Slider.maxValue);
            if (q != v) r.Slider.SetValueWithoutNotify(q);
            r.Set(q);
            r.Value.text = r.Format(q);

            // Re-applied at once; the file is written once the hand rests (a drag moves the value every frame).
            MpSettings.Touch(false);
            _seenVersion = MpSettings.Version;
            _savePending = true;
            _saveAt = Time.unscaledTime + 0.5f;
        }

        private void StepSlider(SliderRow r, float direction)
        {
            float v = Quantize(r.Get() + direction * r.Step, r.Quantum, r.Slider.minValue, r.Slider.maxValue);
            r.Set(v);
            r.Slider.SetValueWithoutNotify(v);
            r.Value.text = r.Format(v);
            Touched();
        }

        private void FlipToggle(ToggleRow t)
        {
            t.Set(!t.Get());
            ShowToggle(t);
            Touched();
        }

        private void SetDamage(float level)
        {
            MpSettings.DamageTaken = level;
            ShowDamage();
            Touched();
        }

        private void OnResetDefaults()
        {
            _savePending = false;
            MpSettings.Defaults();          // touches and saves
            RefreshSettings();
            SetStatus("Options back to their start values.");
        }

        /// <summary>A discrete change: re-applied and written now.</summary>
        private void Touched()
        {
            _savePending = false;
            MpSettings.Touch();
            _seenVersion = MpSettings.Version;
        }

        private void FlushSave()
        {
            if (!_savePending) return;
            _savePending = false;
            MpSettings.Save();
        }

        private void RefreshSettings()
        {
            for (int i = 0; i < _sliders.Count; i++)
            {
                SliderRow r = _sliders[i];
                float v = r.Get();
                r.Slider.SetValueWithoutNotify(v);
                r.Value.text = r.Format(v);
            }

            for (int i = 0; i < _toggles.Count; i++)
            {
                ShowToggle(_toggles[i]);
            }

            ShowDamage();
            _seenVersion = MpSettings.Version;
        }

        private static void ShowToggle(ToggleRow t)
        {
            bool on = t.Get();
            t.Button.SetLabel(on ? "ON" : "OFF");
            t.Button.Selected = on;
        }

        private void ShowDamage()
        {
            float d = MpSettings.DamageTaken;

            for (int i = 0; i < _damage.Length; i++)
            {
                if (_damage[i] != null) _damage[i].Selected = Mathf.Abs(d - DamageLevels[i]) < 0.01f;
            }

            if (_damageValue != null) _damageValue.text = "x" + d.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static float Quantize(float v, float quantum, float min, float max)
        {
            float q = Mathf.Clamp(Mathf.Round(v / quantum) * quantum, min, max);
            return Mathf.Abs(q) < 1e-4f ? 0f : q;      // never "-0"
        }

        private static string Fixed(float v, string format)
        {
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string Signed(float v, string format)
        {
            return (v > 0f ? "+" : "") + v.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string Percent(float v)
        {
            return Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        }

        // ------------------------------------------------------------------ the Game tab

        private void BuildGame(Transform tab)
        {
            MpTabletUi.Box(tab, "Live Panel", 24f, 4f, W - 48f, 56f, MpTabletUi.Panel);
            MpTabletUi.Box(tab, "Live Mark", 24f, 4f, 6f, 56f, MpTabletUi.Orange);
            _live = MpTabletUi.Text(tab, "Live", "", 23f, MpTabletUi.Ink, TextAlignmentOptions.MidlineLeft, 42f, 4f, W - 84f, 56f);
            _live.textWrappingMode = TextWrappingModes.NoWrap;

            _saveButton = MpTabletUi.Button(tab, "SAVE", 24f, 74f, 300f, 60f, 23f, () => RunAction(MpGameActions.SaveGame, "Save"));
            _loadButton = MpTabletUi.Button(tab, "LOAD LAST SAVE", 336f, 74f, 360f, 60f, 23f, () => RunAction(MpGameActions.LoadGame, "Load"));
            _respawnButton = MpTabletUi.Button(tab, "RESPAWN / UNSTUCK", 708f, 74f, W - 732f, 60f, 23f, () => RunAction(MpGameActions.Respawn, "Respawn"));
            _saveInfo = MpTabletUi.Text(tab, "Save Info", "", 21f, MpTabletUi.Grey, TextAlignmentOptions.MidlineLeft, 30f, 140f, W - 440f, 40f);
            MpTabletUi.Button(tab, "RESET ALL OPTIONS", W - 384f, 140f, 360f, 44f, 21f, OnResetDefaults);      // every option on every tab back to its start value

            MpTabletUi.Text(tab, "Rooms Title", "GO TO ROOM (TESTING)", 22f, MpTabletUi.Orange, TextAlignmentOptions.MidlineLeft, 30f, 190f, 560f, 34f, true);
            _roomsNote = MpTabletUi.Text(tab, "Rooms Note", "", 19f, MpTabletUi.Grey, TextAlignmentOptions.MidlineRight, 600f, 190f, 576f, 34f);

            RectTransform list = MpTabletUi.Place(tab, "Rooms", 24f, 230f, 1010f, 376f);
            var listBack = list.gameObject.AddComponent<Image>();
            listBack.color = new Color(0.02f, 0.045f, 0.08f, 0.95f);
            listBack.raycastTarget = true;      // a drag anywhere on the list scrolls it
            MpTabletUi.EdgeLines(list, 2f, MpTabletUi.CyanDim);
            RectTransform viewport = MpTabletUi.Stretch(list, "Viewport", Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            viewport.gameObject.AddComponent<RectMask2D>();
            _roomContent = MpTabletUi.Stretch(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _roomContent.pivot = new Vector2(0.5f, 1f);
            _roomContent.sizeDelta = new Vector2(0f, 0f);
            _scroll = list.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _roomContent;
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = true;
            _scroll.scrollSensitivity = 40f;

            // VR drags are fiddly: the list also pages with two big buttons.
            _upButton = MpTabletUi.Button(tab, "UP", 1046f, 230f, 130f, 182f, 24f, () => ScrollRooms(1f));
            _downButton = MpTabletUi.Button(tab, "DOWN", 1046f, 424f, 130f, 182f, 24f, () => ScrollRooms(-1f));
        }

        private void RunAction(Func<string> action, string what)
        {
            if (action == null)
            {
                SetStatus(what + ": not available yet.");
                return;
            }

            string answer;

            try
            {
                answer = action();
            }
            catch (Exception e)
            {
                answer = what + " failed: " + e.Message;
                MpLog.Warn("Tablet: " + what + " threw " + e);
            }

            SetStatus(string.IsNullOrEmpty(answer) ? what + ": done." : answer);
            RefreshGame(false);
        }

        private void WarpTo(int index, string name)
        {
            Func<int, string> warp = MpGameActions.WarpTo;

            if (warp == null)
            {
                SetStatus("Go to room: not available yet.");
                return;
            }

            string answer;

            try
            {
                answer = warp(index);
            }
            catch (Exception e)
            {
                answer = "Go to " + name + " failed: " + e.Message;
                MpLog.Warn("Tablet: WarpTo(" + index + ") threw " + e);
            }

            SetStatus(string.IsNullOrEmpty(answer) ? "Going to " + name + "." : answer);
            RefreshGame(false);
        }

        /// <summary>The Game tab's lines and which commands are there; the room list too when <paramref name="rooms"/> (it is rebuilt only when the rooms changed).</summary>
        private void RefreshGame(bool rooms)
        {
            if (_live == null) return;
            _saveButton.Interactable = MpGameActions.SaveGame != null;
            _loadButton.Interactable = MpGameActions.LoadGame != null;
            _respawnButton.Interactable = MpGameActions.Respawn != null;
            _live.text = Ask(MpGameActions.Status, "The game is not running yet.");
            _saveInfo.text = Ask(MpGameActions.SaveInfo, "No save information yet.");

            if (rooms || (_roomsShown == null && MpGameActions.Rooms != null))
            {
                string[] list = null;
                Func<string[]> source = MpGameActions.Rooms;

                if (source != null)
                {
                    try { list = source(); }
                    catch (Exception e) { MpLog.Warn("Tablet: Rooms() threw " + e.GetType().Name + ": " + e.Message); }
                }

                if (!SameRooms(list, _roomsShown))
                {
                    FillRooms(list);
                }
            }

            bool warp = MpGameActions.WarpTo != null;

            for (int i = 0; i < _roomRows.Count; i++)
            {
                _roomRows[i].Interactable = warp;
            }

            bool scrolls = _roomsShown != null && _roomsShown.Length > 0;
            _upButton.Interactable = scrolls;
            _downButton.Interactable = scrolls;
            _roomsNote.text = _roomsShown == null ? "Room list not available yet." : !warp ? _roomsShown.Length + " rooms - going there is not available yet." : _roomsShown.Length + " rooms - tap one to go there.";
        }

        private static string Ask(Func<string> f, string fallback)
        {
            if (f == null) return fallback;

            try
            {
                string s = f();
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch (Exception e)
            {
                return "(" + e.GetType().Name + ": " + e.Message + ")";
            }
        }

        private static bool SameRooms(string[] a, string[] b)
        {
            if (a == b) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private void FillRooms(string[] rooms)
        {
            for (int i = _roomContent.childCount - 1; i >= 0; i--)
            {
                GameObject old = _roomContent.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            _roomRows.Clear();
            _roomsShown = rooms;
            int n = rooms != null ? rooms.Length : 0;

            for (int i = 0; i < n; i++)
            {
                int index = i;
                string name = string.IsNullOrEmpty(rooms[i]) ? "Room " + i : rooms[i];
                float top = 6f + i * RoomRowPitch;
                RectTransform row = MpTabletUi.Stretch(_roomContent, "Room " + i, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(6f, -top - RoomRowH), new Vector2(-6f, -top));
                var button = row.gameObject.AddComponent<MpUiButton>();
                button.Build(i.ToString("00", CultureInfo.InvariantCulture) + "   " + name, 21f, () => WarpTo(index, name), TextAlignmentOptions.MidlineLeft);
                _roomRows.Add(button);
            }

            _roomContent.sizeDelta = new Vector2(0f, n > 0 ? 12f + n * RoomRowPitch : 0f);
            _scroll.verticalNormalizedPosition = 1f;
            if (_link != null) _link.ApplyLayers();
        }

        private void ScrollRooms(float direction)
        {
            float view = ((RectTransform)_scroll.viewport).rect.height;
            float content = _roomContent.rect.height;
            if (content <= view + 1f) return;
            _scroll.StopMovement();
            float page = (view * 0.8f) / (content - view);
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + direction * page);
        }

        // ------------------------------------------------------------------ the About tab

        private void BuildAbout(Transform tab)
        {
            _about = MpTabletUi.Text(tab, "About Text", "", 21f, MpTabletUi.Ink, TextAlignmentOptions.TopLeft, 30f, 4f, W - 60f, 112f);
            MpTabletUi.Box(tab, "About Rule", 24f, 124f, W - 48f, 2f, MpTabletUi.CyanFaint);
            MpTabletUi.Text(tab, "Controls Title", "CONTROLS", 24f, MpTabletUi.Orange, TextAlignmentOptions.MidlineLeft, 30f, 134f, 400f, 34f, true);

            string[] vr =
            {
                "Right trigger", "Fire (hold to charge)",
                "B (right)", "Missile",
                "Right grip + move hand", "Beam: up Power, right Wave, down Ice, left Plasma",
                "Left grip + move hand", "Visor: up Combat, right X-Ray, down Thermal, left Scan",
                "X (left)", "Morph Ball on / off (left stick rolls it, trigger drops a bomb)",
                "Both grips", "Skip a cutscene",
                "Y (left)", "Opens the Somnium tablet",
            };
            string[] monitor =
            {
                "Left mouse", "Fire (hold to charge)",
                "F", "Missile",
                "1  2  3  4", "Beams: Power, Wave, Ice, Plasma",
                "Z  X  C  V", "Visors: Combat, Scan, Thermal, X-Ray",
                "Q", "Morph Ball on / off (WASD rolls it, left mouse drops a bomb)",
                "Enter", "Skip a cutscene",
                "Tab", "Opens the Somnium tablet",
            };
            ControlsColumn(tab, "VR (HEADSET)", vr, 30f);
            ControlsColumn(tab, "MONITOR", monitor, 610f);
            MpTabletUi.Text(tab, "Footer", MpVersion.Label + "  -  the options are kept in options.txt on this computer.", 18f, MpTabletUi.Grey, TextAlignmentOptions.MidlineLeft, 30f, 572f, W - 60f, 32f);
        }

        private static void ControlsColumn(Transform tab, string title, string[] pairs, float x)
        {
            MpTabletUi.Text(tab, title, title, 22f, MpTabletUi.Cyan, TextAlignmentOptions.MidlineLeft, x, 172f, 560f, 32f, true);

            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                float y = 210f + (i / 2) * 64f;
                MpTabletUi.Box(tab, title + " Row " + i, x - 6f, y, 566f, 58f, new Color(0.035f, 0.075f, 0.13f, 0.6f));
                MpTabletUi.Text(tab, title + " Key " + i, pairs[i], 20f, MpTabletUi.Cyan, TextAlignmentOptions.MidlineLeft, x + 4f, y, 196f, 58f, true);
                MpTabletUi.Text(tab, title + " Does " + i, pairs[i + 1], 20f, MpTabletUi.Ink, TextAlignmentOptions.MidlineLeft, x + 206f, y, 350f, 58f);
            }
        }

        private void RefreshAbout()
        {
            if (_about != null) _about.text = Ask(MpGameActions.About, "Metroid Prime, played from your own disc image - nothing of the game is in this world.");
        }
    }
}
