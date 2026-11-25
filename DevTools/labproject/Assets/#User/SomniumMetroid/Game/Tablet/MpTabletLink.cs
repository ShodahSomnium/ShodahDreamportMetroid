using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The Somnium client's own tablet, borrowed for the "METROID PRIME" app: a port of the BO2 Dreamport's Bo2TabletLink (itself the Euro Truck Sim
    /// Dreamport's EtsTabletLink and the WoW Dreamport's WowMapTabletLink), which found every part below in the real client, by type: Player.log
    /// 2026-09-23 "linked - 'Tablet.XR(Clone)' under 'XR.Body' in scene 'MasterScene', XR, root by type TabletControl; TabsContainer by type, HomeTab by
    /// type, TabletStatusBarView by type; ... all parts found". Only the BO2 match rules are left out.
    ///
    /// There is no tablet API for worlds (the Bridge has no tablet member). The client instantiates Tablet.XR / Tablet.NonXR into "MasterScene" and
    /// parents it under the local player when "tablet follows player" is on. So it is found by searching, by the client's component TYPE names first
    /// (TabletControl, TabsContainer, HomeTab, TabletStatusBarView: VRTablet/Tablet/TabletControl.cs, Tabs/Scripts/TabsContainer.cs, Tabs/HomeTab/HomeTab.cs,
    /// Views/TabletStatusBarView.cs) with object names as the fallback; the client is Mono, so nothing renames them. The client's own apps are never touched:
    /// "METROID PRIME" is one more button on the home screen.
    ///
    /// What it adds (everything named "MP.Tablet.*", swept at every link and at shutdown):
    ///  - ONE home-screen button: a copy of one client button made under an INACTIVE holder (no client script wakes on it), stripped to Unity UI and
    ///    TextMeshPro, its persistent onClick replaced, put FIRST in the home grid (kept first, and its measured cell logged once);
    ///  - the app's canvas, a sibling of the client's tab canvases under "Canvases" (so it fades and stops taking the ray with the tablet), on the HomeTab
    ///    canvas's layer, its sorting, camera and raycasters mirrored (TrackedDeviceGraphicRaycaster for the VR ray and poke, GraphicRaycaster for the mouse),
    ///    following the HomeTab rect every LateUpdate so the player's own Resize is followed; the app draws into <see cref="Frame"/>.
    /// What it changes while the app is open: the client's Tabs container is hidden, its Back button shown, and on a Small tablet its Resize pressed (and
    /// pressed back on close); a client tab switch closes the app. Everything is put back when the app closes and when the world goes (the tablet outlives
    /// the world, so <see cref="OnDestroy"/> takes it all off and shows a Tabs container an earlier visit left hidden).
    /// Degrades to nothing: with no tablet (or a client whose tablet looks different) this only searches, throttled (every 0.5 s for the first minute, then 5 s),
    /// and the game plays on with the options it read from options.txt.
    /// Log lines (all "Tablet: ..."): found/linked/not linked after N s/injection failed/removed N objects/app opened/app closed/unlinked, see MpTabletApp.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class MpTabletLink : MonoBehaviour
    {
        /// <summary>The prefix of everything this puts on the client's tablet; swept on every link.</summary>
        public const string Owned = "MP.Tablet.";

        /// <summary>HomeTab widths: Small 960, Large 1920 (Tablet.XR.HomeTab.prefab:424-434); Large = anything wider than this.</summary>
        public const float LargeAbove = 1440f;

        public static MpTabletLink Instance { get; private set; }

        [Tooltip("Discovery and one report line only; nothing is added to the client's tablet.")]
        public bool LogOnly;

        [Tooltip("Seconds between searches for the tablet while it has not been found (5 s after the first minute).")]
        public float SearchInterval = 0.5f;

        [Tooltip("Seconds without a tablet before the one 'not linked' line is written.")]
        public float NotLinkedLogAfter = 20f;

        [Tooltip("Caption under the app's icon on the tablet's home screen.")]
        public string AppTitle = "METROID PRIME";

        [Tooltip("Press the tablet's Resize button when the app opens on a Small tablet, and again when it closes: the options page is a wide page.")]
        public bool ForceLarge = true;

        /// <summary>The size of the rectangle the app lays itself out in (scaled to fit the HomeTab rect); set before the link is made.</summary>
        public float FrameWidth = MpTabletUi.FrameWidth, FrameHeight = MpTabletUi.FrameHeight;

        /// <summary>The home-screen icon (MpTabletApp paints it); the sprite is made from it once linked.</summary>
        public Texture2D AppIcon;

        public event Action<bool> AppShown;
        public event Action LinkChanged;

        public bool IsLinked { get { return !LogOnly && _t != null && _t.Root != null; } }
        public bool IsDesktopTablet { get { return _t != null && _t.Desktop; } }
        public bool TabletOpen { get; private set; }
        public bool AppOpen { get; private set; }

        /// <summary>The rectangle the app lays itself out in, inside the tablet's screen; null unless linked.</summary>
        public RectTransform Frame { get { return IsLinked ? _frame : null; } }

        /// <summary>The app's canvas (the lab reads its raycasters); null unless linked.</summary>
        public Canvas AppCanvas { get { return IsLinked ? _appCanvas : null; } }

        /// <summary>The layer the client's tablet graphics carry (5, UI), which the XR ray tests per graphic: what the app's widgets must be on.</summary>
        public int UiLayer { get { return IsLinked ? _t.UiLayer : MpTabletUi.UiLayer; } }

        /// <summary>The last discovery result, as logged.</summary>
        public string Report { get; private set; } = "not searched yet";

        /// <summary>The home-screen button exists and is shown (the app is available).</summary>
        public bool ButtonVisible { get { return _button != null && _button.activeSelf; } }

        /// <summary>False hides the home-screen button (and closes the app); the client's own tablet works as always.</summary>
        public bool AppAvailable
        {
            get { return _available; }
            set
            {
                if (_available == value) return;
                _available = value;
                if (!value && AppOpen) Close(true, "not available now");
                ApplyAvailability();
            }
        }

        private sealed class Parts
        {
            public Transform Root;
            public Component Control;
            public MethodInfo Deactivate;
            public bool Desktop;
            public Animator Animator;
            public bool AnimatorHasIsOpen;
            public GameObject GrabCollider;
            public Transform Canvases;
            public CanvasGroup CanvasesGroup;
            public Transform Tabs;
            public Canvas HomeCanvas;
            public RectTransform Home;
            public GridLayoutGroup HomeGrid;
            public Button ReferenceButton;
            public RectTransform StatusBar;
            public Button Back;
            public Button Resize;
            public int GridShown;
            public int UiLayer = 5;
            public readonly List<GameObject> TabContents = new List<GameObject>();
            public readonly List<UnityEvent> MeshHome = new List<UnityEvent>();
        }

        private static readonly int IsOpenHash = Animator.StringToHash("IsOpen");
        private static readonly List<MonoBehaviour> Buffer = new List<MonoBehaviour>();
        private static readonly List<GameObject> RootsBuffer = new List<GameObject>();
        private static readonly Stack<Transform> WalkStack = new Stack<Transform>();
        private static readonly Vector3[] Corners = new Vector3[4];

        private Parts _t;
        private GameObject _button;
        private Image _buttonIcon;
        private GameObject _app;
        private Canvas _appCanvas;
        private RectTransform _frame;
        private float _nextSlow;
        private bool[] _tabSnapshot;
        private bool _backWasActive;
        private bool _enlarged;
        private float _resizeSettleUntil;
        private Sprite _iconSprite;
        private bool _available = true;

        private float _nextSearch;
        private int _searches;
        private double _slowestMs;
        private float _firstSearchAt = -1f;
        private bool _notLinkedReported;
        private bool _ending;
        private float _linkedAt;
        private bool _placeReported;
        private bool _reorderReported;
        private bool _closeFailedReported;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            // Statics survive a world re-entry in the same client session: the previous visit's instance is replaced, never trusted.
            Instance = this;
        }

        private void OnDestroy()
        {
            // The world is unloading: leave the client's tablet as it was. No events from here.
            _ending = true;
            Unlink(true, "world unloading");

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Takes everything off the client's tablet now (MpTabletApp.Shutdown) and stops searching.</summary>
        public void Shutdown()
        {
            _ending = true;
            enabled = false;
            Unlink(true, "world unloading");
        }

        private void Update()
        {
            if (!IsLinked)
            {
                if (_t != null)
                {
                    Unlink(false, "the tablet was destroyed (avatar or mode change)");
                }

                if (Time.unscaledTime >= _nextSearch)
                {
                    if (_firstSearchAt < 0f)
                    {
                        _firstSearchAt = Time.unscaledTime;
                    }

                    // Every SearchInterval for the first minute (the tablet comes with the local player), then every 5 s.
                    float interval = Time.unscaledTime - _firstSearchAt < 60f ? Mathf.Max(0.1f, SearchInterval) : 5f;
                    _nextSearch = Time.unscaledTime + interval;
                    TryLink();
                }

                return;
            }

            TabletOpen = ReadTabletOpen();

            if (Time.unscaledTime < _nextSlow)
            {
                return;
            }

            _nextSlow = Time.unscaledTime + 0.25f;

            if (_appCanvas != null)
            {
                Camera eventCamera = _t.HomeCanvas != null ? _t.HomeCanvas.worldCamera : null;
                Camera wanted = eventCamera != null ? eventCamera : Camera.main;

                if (_appCanvas.worldCamera != wanted)
                {
                    _appCanvas.worldCamera = wanted;
                }
            }

            if (_buttonIcon != null && _buttonIcon.sprite == null)
            {
                SetIcon(); // the icon was painted after the link
            }

            ApplyAvailability();
            KeepButtonFirst();

            if (AppOpen)
            {
                if (TabsChangedSinceOpen())
                {
                    Close(false, "client switched tab"); // mesh Map/Chat/..., a deep link, the exit dialog
                    return;
                }

                // The widgets are built on the UI layer; only a tablet whose graphics carry another one needs them moved (rows added later too).
                if (_t.UiLayer != MpTabletUi.UiLayer)
                {
                    SetLayer(_app.transform, _t.UiLayer);
                }
            }
        }

        private void LateUpdate()
        {
            if (IsLinked)
            {
                FollowHomeRect();
            }
        }

        // ------------------------------------------------------------------ discovery

        private void TryLink()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Parts found;
            bool complete;
            string report;

            try
            {
                found = Discover(OwnWorldRoot(), out report, out complete);
            }
            catch (Exception e)
            {
                found = null;
                complete = false;
                report = "the last search threw " + e.GetType().Name + ": " + e.Message;
            }

            watch.Stop();
            _searches++;
            _slowestMs = Math.Max(_slowestMs, watch.Elapsed.TotalMilliseconds);
            Report = report;

            if (!complete || LogOnly)
            {
                if (complete && LogOnly && !_notLinkedReported)
                {
                    _notLinkedReported = true;
                    MpLog.Info("Tablet (log only): found - " + report + "; " + _searches + " searches, slowest " + _slowestMs.ToString("0.0") + " ms.");
                    enabled = false;
                    return;
                }

                // One line, once, so a log shows why the app is not on the tablet.
                if (!_notLinkedReported && Time.unscaledTime - _firstSearchAt > NotLinkedLogAfter)
                {
                    _notLinkedReported = true;
                    MpLog.Info("Tablet: not linked after " + (Time.unscaledTime - _firstSearchAt).ToString("0") + " s - " + report +
                               "; still searching every " + (Time.unscaledTime - _firstSearchAt < 60f ? SearchInterval.ToString("0.0") : "5") + " s; " +
                               _searches + " searches, slowest " + _slowestMs.ToString("0.0") + " ms. The game plays with the options of options.txt.");
                }

                return;
            }

            _t = found;
            RemoveLeftovers(_t);

            try
            {
                Inject();
            }
            catch (Exception e)
            {
                // Half an injection is worse than none: take off whatever went on, and look again later.
                MpLog.Warn("Tablet: injection failed (" + e.GetType().Name + ": " + e.Message + ") - " + report);
                Unlink(true, "injection failed");
                _nextSearch = Time.unscaledTime + 5f;
                return;
            }

            MpLog.Info("Tablet: linked - " + report + "; " + _searches + " searches, slowest " + _slowestMs.ToString("0.0") + " ms.");
            _linkedAt = Time.unscaledTime;
            _placeReported = false;
            _notLinkedReported = false;

            if (LinkChanged != null)
            {
                LinkChanged();
            }
        }

        /// <summary>The root of the world's own hierarchy this lives in (the game's rooms, models, HUD): never searched, unless it is in MasterScene.</summary>
        private Transform OwnWorldRoot()
        {
            Transform root = transform.root;
            return root.gameObject.scene.name == "MasterScene" ? null : root;
        }

        /// <summary>
        /// Four times a second while linked: the "METROID PRIME" button stays the grid's first child, and once (0.5 s after the link, grid active and laid out)
        /// its measured cell is logged against the grid's rect - the in-client proof that it shows:
        ///   Tablet: the METROID PRIME button is cell 1 of 15 shown in the home grid (19 client buttons + METROID PRIME), at 10..245 x 0..250 of the 960 x 1000 view, inside the mask: yes.
        /// </summary>
        private void KeepButtonFirst()
        {
            if (_button == null || _t.HomeGrid == null)
            {
                return;
            }

            Transform button = _button.transform;

            if (button.parent == _t.HomeGrid.transform && button.GetSiblingIndex() != 0)
            {
                button.SetSiblingIndex(0);

                if (!_reorderReported)
                {
                    _reorderReported = true;
                    MpLog.Info("Tablet: the client re-ordered its home grid; the " + AppTitle + " button is first again.");
                }
            }

            if (_placeReported || Time.unscaledTime - _linkedAt < 0.5f || !_button.activeSelf || !_t.HomeGrid.gameObject.activeInHierarchy)
            {
                return;
            }

            _placeReported = true;
            var grid = (RectTransform)_t.HomeGrid.transform;
            int shown = 0, index = -1, buttons = 0;

            for (int i = 0; i < grid.childCount; i++)
            {
                Transform child = grid.GetChild(i);
                buttons += child != button && child.GetComponent<Button>() != null ? 1 : 0;

                if (!child.gameObject.activeSelf)
                {
                    continue; // not laid out
                }

                if (child == button)
                {
                    index = shown;
                }

                shown++;
            }

            Rect view = grid.rect;
            ((RectTransform)button).GetWorldCorners(Corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < 4; i++)
            {
                Vector3 p = grid.InverseTransformPoint(Corners[i]);
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            bool inside = minX >= view.xMin - 0.5f && maxX <= view.xMax + 0.5f && minY >= view.yMin - 0.5f && maxY <= view.yMax + 0.5f;
            bool masked = grid.GetComponent<Mask>() != null || grid.GetComponent<RectMask2D>() != null;
            MpLog.Info("Tablet: the " + AppTitle + " button is cell " + (index + 1) + " of " + shown + " shown in the home grid (" + buttons + " client buttons + " + AppTitle + "), at " +
                       (minX - view.xMin).ToString("0") + ".." + (maxX - view.xMin).ToString("0") + " x " + (view.yMax - maxY).ToString("0") + ".." + (view.yMax - minY).ToString("0") +
                       " of the " + view.width.ToString("0") + " x " + view.height.ToString("0") + " view, " + (masked ? "inside the mask: " : "no mask on the grid, inside its rect: ") + (inside ? "yes." : "NO."));
        }

        /// <summary>
        /// One search. Returns the parts when a tablet root was found (complete or not) and null otherwise; complete when Tabs, the HomeTab canvas, its grid
        /// and a reference button are all there. <paramref name="skip"/> (the world's own hierarchy) is not entered.
        /// </summary>
        private static Parts Discover(Transform skip, out string report, out bool complete)
        {
            complete = false;
            Scene master = SceneManager.GetSceneByName("MasterScene"); // WorldsHelper.MASTER_SCENE_NAME
            bool masterLoaded = master.IsValid() && master.isLoaded;
            Transform root = null;
            Component control = null;
            bool rootByType = false;

            if (masterLoaded)
            {
                RootsBuffer.Clear();
                master.GetRootGameObjects(RootsBuffer);
                root = SearchRoots(RootsBuffer, skip, out rootByType, out control);
            }

            int others = 0;

            for (int i = 0; root == null && i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded || scene == master)
                {
                    continue;
                }

                others++;
                RootsBuffer.Clear();
                scene.GetRootGameObjects(RootsBuffer);
                root = SearchRoots(RootsBuffer, skip, out rootByType, out control);
            }

            RootsBuffer.Clear();

            if (root == null)
            {
                report = "no transform with a TabletControl component, and none named 'Tablet.XR(...' or 'Tablet.NonXR(...', in MasterScene (" +
                         (masterLoaded ? "loaded" : "NOT loaded") + ") or " + others + " other loaded scene(s); 'Simulator' objects (remote tablets) and the world's own subtrees skipped";
                return null;
            }

            var t = new Parts { Root = root, Control = control };

            if (control != null)
            {
                t.Deactivate = control.GetType().GetMethod("DeActivate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            }

            Transform tabs = FindTyped(root, "TabsContainer");
            string tabsHow = tabs != null ? "type" : null;

            if (tabs == null)
            {
                tabs = FindNamed(root, "Tablet.XR.Tabs", 3);
                tabsHow = tabs != null ? "name" : "-";
            }

            Transform home = FindTyped(root, "HomeTab");
            string homeHow = home != null ? "type" : null;

            if (home == null)
            {
                home = FindNamed(root, "Tablet.XR.HomeTab", 4);
                homeHow = home != null ? "name" : "-";
            }

            Transform bar = FindTyped(root, "TabletStatusBarView");
            string barHow = bar != null ? "type" : null;

            if (bar == null)
            {
                bar = FindNamed(root, "Tablet.XR.StatusBar", 3);
                barHow = bar != null ? "name" : "-";
            }

            t.Tabs = tabs;
            t.Home = home as RectTransform;
            t.StatusBar = bar as RectTransform;
            t.Canvases = tabs != null ? tabs.parent : FindNamed(root, "Canvases", 1);
            t.CanvasesGroup = t.Canvases != null ? t.Canvases.GetComponent<CanvasGroup>() : null;
            Transform grabCollider = FindNamed(root, "GrabCollider", 1);
            t.GrabCollider = grabCollider != null ? grabCollider.gameObject : null;

            Canvas homeCanvas = home != null ? home.GetComponent<Canvas>() : null;
            GridLayoutGroup grid = home != null ? home.GetComponentInChildren<GridLayoutGroup>(true) : null;
            t.HomeCanvas = homeCanvas;
            t.HomeGrid = grid;
            Button reference = null;
            int uiLayer = 5;
            int gridButtons = 0;
            int gridShown = 0;

            if (grid != null)
            {
                foreach (Transform child in grid.transform)
                {
                    var b = child.GetComponent<Button>();

                    if (b == null)
                    {
                        continue;
                    }

                    bool foreign = IsOtherDreamportsClone(child.name);
                    gridButtons++;
                    gridShown += child.gameObject.activeSelf && !foreign ? 1 : 0;

                    // Never a clone an earlier visit (or another Dreamport) left: a copy of a copy would carry no client look at all.
                    if (reference == null && !foreign && child.GetComponentInChildren<TMP_Text>(true) != null)
                    {
                        reference = b;
                        var graphic = child.GetComponent<Graphic>();
                        uiLayer = graphic != null ? graphic.gameObject.layer : child.gameObject.layer;
                    }
                }
            }

            t.ReferenceButton = reference;
            t.UiLayer = uiLayer;
            t.GridShown = gridShown;
            bool back = false;
            bool resize = false;

            if (bar != null)
            {
                foreach (Button b in bar.GetComponentsInChildren<Button>(true))
                {
                    bool isBack = b.name.IndexOf("Back", StringComparison.Ordinal) >= 0;
                    bool isResize = b.name.IndexOf("Resize", StringComparison.Ordinal) >= 0;
                    back |= isBack;
                    resize |= isResize;
                    if (isBack && t.Back == null) t.Back = b;
                    if (isResize && t.Resize == null) t.Resize = b;
                }
            }

            int tabCount = 0;
            int contentViews = 0;

            if (tabs != null)
            {
                foreach (Transform tab in tabs)
                {
                    tabCount++;
                    Transform content = tab.Find("ContentView");
                    contentViews += content != null ? 1 : 0;
                    t.TabContents.Add(content != null ? content.gameObject : null);
                }
            }

            var animator = root.GetComponent<Animator>();
            bool isOpenParameter = false;

            if (animator != null && animator.isActiveAndEnabled)
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    isOpenParameter |= p.type == AnimatorControllerParameterType.Bool && p.name == "IsOpen";
                }
            }

            t.Animator = animator;
            t.AnimatorHasIsOpen = isOpenParameter;
            bool meshHome = FindMeshHome(root, t.MeshHome);
            bool desktop = root.name.StartsWith("Tablet.NonXR", StringComparison.Ordinal) || !MpPlayer.XrPresent;
            t.Desktop = desktop;
            var homeRect = home as RectTransform;
            string size = (homeRect == null ? "home size unknown" :
                          "home " + homeRect.rect.width.ToString("0") + "x" + homeRect.rect.height.ToString("0") + (homeRect.rect.width > LargeAbove ? " (Large)" : " (Small)")) +
                          ", " + gridShown + " of the grid's buttons shown";

            string missing = (tabs == null ? " Tabs" : "") + (homeCanvas == null ? " HomeCanvas" : "") + (grid == null ? " HomeGrid" : "") +
                             (reference == null ? " ReferenceButton" : "") + (!back ? " Back" : "") + (!resize ? " Resize" : "") +
                             (animator == null ? " Animator" : isOpenParameter ? "" : " IsOpen") + (t.Deactivate == null ? " DeActivate" : "");

            report = "'" + root.name + "' under '" + (root.parent != null ? root.parent.name : "(scene root)") + "' in scene '" + root.gameObject.scene.name + "', " +
                     (desktop ? "desktop" : "XR") + ", root by " + (rootByType ? "type TabletControl" : "name") +
                     "; TabsContainer by " + tabsHow + ", HomeTab by " + homeHow + ", TabletStatusBarView by " + barHow +
                     "; " + tabCount + " tabs (" + contentViews + " with a ContentView), home grid " + gridButtons + " buttons, " + size +
                     ", UI layer " + uiLayer + ", canvas layer " + (homeCanvas != null ? homeCanvas.gameObject.layer.ToString() : "-") +
                     ", raycaster " + Raycaster(homeCanvas) + ", mesh Home " + (meshHome ? "yes" : "no") +
                     (missing.Length > 0 ? "; MISSING:" + missing : "; all parts found");

            complete = tabs != null && homeCanvas != null && grid != null && reference != null;
            return t;
        }

        /// <summary>A home-grid button an earlier visit of this world, or another Dreamport's tablet app, cloned (their names): never the reference for ours.</summary>
        private static bool IsOtherDreamportsClone(string name)
        {
            return name.StartsWith(Owned, StringComparison.Ordinal) || name.StartsWith("BO2.Tablet.", StringComparison.Ordinal) ||
                   name.StartsWith("ETS.Tablet.", StringComparison.Ordinal) || name.StartsWith("WoW.Tablet.", StringComparison.Ordinal);
        }

        /// <summary>
        /// The first transform carrying a TabletControl, else the first named "Tablet.XR(" / "Tablet.NonXR(". Remote players' tablets ("Simulator"), the
        /// world's own hierarchy (<paramref name="skip"/>) and its "Metroid ..." / "Mp ..." / "Samus ..." objects (rooms, models, HUD, sound) are not entered.
        /// </summary>
        private static Transform SearchRoots(List<GameObject> roots, Transform skip, out bool byType, out Component control)
        {
            byType = false;
            control = null;
            WalkStack.Clear();

            foreach (GameObject go in roots)
            {
                if (go != null)
                {
                    WalkStack.Push(go.transform);
                }
            }

            Transform named = null;

            while (WalkStack.Count > 0)
            {
                Transform t = WalkStack.Pop();
                string name = t.name;

                if (t == skip || name.StartsWith("Metroid ", StringComparison.Ordinal) || name.StartsWith("Mp ", StringComparison.Ordinal) ||
                    name.StartsWith("Samus ", StringComparison.Ordinal) || name.IndexOf("Simulator", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                MonoBehaviour tabletControl = FindOn(t, "TabletControl");

                if (tabletControl != null)
                {
                    byType = true;
                    control = tabletControl;
                    WalkStack.Clear();
                    return t; // the client's own component: the surest mark
                }

                if (named == null && (name.StartsWith("Tablet.XR(", StringComparison.Ordinal) || name.StartsWith("Tablet.NonXR(", StringComparison.Ordinal)))
                {
                    named = t;
                }

                for (int i = 0; i < t.childCount; i++)
                {
                    WalkStack.Push(t.GetChild(i));
                }
            }

            return named;
        }

        private static MonoBehaviour FindOn(Transform t, string typeName)
        {
            t.GetComponents(Buffer);
            MonoBehaviour found = null;

            for (int i = 0; i < Buffer.Count; i++)
            {
                MonoBehaviour mb = Buffer[i];

                if (mb != null && mb.GetType().Name == typeName)
                {
                    found = mb;
                    break;
                }
            }

            Buffer.Clear();
            return found;
        }

        private static Transform FindTyped(Transform root, string typeName)
        {
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Name == typeName)
                {
                    return mb.transform;
                }
            }

            return null;
        }

        private static Transform FindNamed(Transform root, string name, int maxDepth)
        {
            if (maxDepth < 0)
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);

                if (child.name == name)
                {
                    return child;
                }
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform deeper = FindNamed(root.GetChild(i), name, maxDepth - 1);

                if (deeper != null)
                {
                    return deeper;
                }
            }

            return null;
        }

        /// <summary>
        /// The tablet's mesh Home button fires XRInteractableButton.ButtonPressed on a poke; the event is a public UnityEvent field, reached by reflection
        /// (optional: without it only Back closes the app).
        /// </summary>
        private static bool FindMeshHome(Transform root, List<UnityEvent> into)
        {
            Transform meshHome = FindNamed(root, "HomeButton", 8);

            if (meshHome == null)
            {
                return false;
            }

            foreach (MonoBehaviour mb in meshHome.GetComponents<MonoBehaviour>())
            {
                FieldInfo field = mb != null ? mb.GetType().GetField("ButtonPressed", BindingFlags.Public | BindingFlags.Instance) : null;
                var pressed = field != null ? field.GetValue(mb) as UnityEvent : null;

                if (pressed != null)
                {
                    into.Add(pressed);
                }
            }

            return into.Count > 0;
        }

        /// <summary>Which raycaster the home canvas carries: the app's canvas mirrors it.</summary>
        private static string Raycaster(Canvas canvas)
        {
            if (canvas == null)
            {
                return "-";
            }

            string found = "";

            foreach (MonoBehaviour mb in canvas.GetComponents<MonoBehaviour>())
            {
                string type = mb != null ? mb.GetType().Name : "";

                if (type.EndsWith("Raycaster", StringComparison.Ordinal))
                {
                    found += (found.Length > 0 ? "+" : "") + type;
                }
            }

            return found.Length > 0 ? found : "none";
        }

        // ------------------------------------------------------------------ injection

        /// <summary>What an earlier visit left on this tablet (MP.Tablet.*) goes, and a Tabs container it left hidden is shown.</summary>
        private static void RemoveLeftovers(Parts t)
        {
            var doomed = new List<GameObject>();

            foreach (Transform child in t.HomeGrid.transform)
            {
                if (child.name.StartsWith(Owned, StringComparison.Ordinal)) doomed.Add(child.gameObject);
            }

            if (t.Canvases != null)
            {
                foreach (Transform child in t.Canvases)
                {
                    if (child.name.StartsWith(Owned, StringComparison.Ordinal)) doomed.Add(child.gameObject);
                }
            }

            foreach (GameObject go in doomed)
            {
                // Out of the grid at once (Destroy is deferred to the end of the frame, and the grid lays out before).
                go.SetActive(false);
                Destroy(go);
            }

            bool tabsShown = false;

            if (t.Tabs != null && !t.Tabs.gameObject.activeSelf)
            {
                t.Tabs.gameObject.SetActive(true);
                tabsShown = true;
            }

            if (doomed.Count > 0 || tabsShown)
            {
                MpLog.Info("Tablet: removed " + doomed.Count + " objects left by an earlier visit" + (tabsShown ? "; its hidden Tabs container shown again" : "") + ".");
            }
        }

        private void Inject()
        {
            // The home-screen button: a copy of ONE client button, made under an inactive holder so that no client script on it ever wakes (Instantiate under
            // an inactive parent runs no Awake/OnEnable), stripped to Unity UI + TextMeshPro, and only then moved into the grid - first.
            var staging = new GameObject(Owned + "Staging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);

            try
            {
                _button = Instantiate(_t.ReferenceButton.gameObject, staging.transform, false);
                _button.name = Owned + "Button";
                StripToUnityUi(_button);
                _button.transform.SetParent(_t.HomeGrid.transform, false);
                _button.transform.SetSiblingIndex(0);
            }
            finally
            {
                Destroy(staging);
            }

            var button = _button.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent(); // drops the persistent call into the destroyed client script
            button.onClick.AddListener(OnAppButton);
            button.interactable = true;

            foreach (TMP_Text caption in _button.GetComponentsInChildren<TMP_Text>(true))
            {
                caption.text = AppTitle;

                // "METROID PRIME" is longer than the client's own captions: one line, shrunk to the cell instead of wrapped or cut.
                caption.textWrappingMode = TextWrappingModes.NoWrap;
                caption.enableAutoSizing = true;
                caption.fontSizeMax = Mathf.Max(caption.fontSize, 8f);
                caption.fontSizeMin = Mathf.Max(8f, caption.fontSizeMax * 0.45f);
            }

            Transform iconTransform = _button.transform.Find("Icon");
            _buttonIcon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            SetIcon();
            _button.SetActive(_available);

            // The app's own canvas, a sibling of the client's tab canvases under "Canvases" (never inside the Tabs container, which is hidden while the app
            // shows), so the tablet's close fade hides it too.
            _app = new GameObject(Owned + "App", typeof(RectTransform));
            _app.SetActive(false);
            _app.layer = _t.HomeCanvas.gameObject.layer;
            _app.transform.SetParent(_t.Canvases != null ? _t.Canvases : _t.Tabs.parent, false);
            _appCanvas = _app.AddComponent<Canvas>();
            _appCanvas.renderMode = RenderMode.WorldSpace;
            _appCanvas.sortingLayerID = _t.HomeCanvas.sortingLayerID;
            _appCanvas.sortingOrder = _t.HomeCanvas.sortingOrder;
            _appCanvas.additionalShaderChannels = _t.HomeCanvas.additionalShaderChannels;
            _appCanvas.worldCamera = _t.HomeCanvas.worldCamera;

            // Nested canvases need their own raycaster; mirror what HomeTab really has (the NonXR variant has GraphicRaycasters), both if neither.
            bool tracked = _t.HomeCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() != null;
            bool mouse = _t.HomeCanvas.GetComponent<GraphicRaycaster>() != null;

            if (tracked || !mouse)
            {
                _app.AddComponent<TrackedDeviceGraphicRaycaster>();
            }

            if (mouse || !tracked)
            {
                _app.AddComponent<GraphicRaycaster>();
            }

            // RectMask2D clips to the tablet's screen and filters raycasts to it.
            _app.AddComponent<RectMask2D>();
            var backdrop = _app.AddComponent<Image>();
            backdrop.color = MpTabletUi.Navy;
            backdrop.raycastTarget = true; // a miss on the app never falls through to the hidden tabs

            _frame = new GameObject(Owned + "Frame", typeof(RectTransform)).GetComponent<RectTransform>();
            _frame.SetParent(_app.transform, false);
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = new Vector2(FrameWidth, FrameHeight);
            _frame.gameObject.layer = _t.UiLayer;
            FollowHomeRect();

            foreach (UnityEvent pressed in _t.MeshHome)
            {
                pressed.AddListener(OnMeshHome);
            }
        }

        /// <summary>The icon the app painted (MpTabletApp), as the home button's picture.</summary>
        private void SetIcon()
        {
            if (_buttonIcon == null)
            {
                return;
            }

            if (AppIcon != null)
            {
                if (_iconSprite == null || _iconSprite.texture != AppIcon)
                {
                    _iconSprite = Sprite.Create(AppIcon, new Rect(0f, 0f, AppIcon.width, AppIcon.height), new Vector2(0.5f, 0.5f), 100f);
                    _iconSprite.name = Owned + "Icon";
                }

                _buttonIcon.sprite = _iconSprite;
                _buttonIcon.color = Color.white;
                _buttonIcon.preserveAspect = true;
                _buttonIcon.enabled = true;
            }
            else
            {
                // Not the client's picture under our caption: no picture until the icon is there.
                _buttonIcon.sprite = null;
                _buttonIcon.enabled = false;
            }
        }

        /// <summary>Every MonoBehaviour outside Unity UI and TextMeshPro goes, last-added first, in up to four passes.</summary>
        private static void StripToUnityUi(GameObject root)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                int foreign = 0;
                MonoBehaviour[] all = root.GetComponentsInChildren<MonoBehaviour>(true);

                for (int i = all.Length - 1; i >= 0; i--)
                {
                    MonoBehaviour mb = all[i];

                    if (mb == null)
                    {
                        continue;
                    }

                    string assembly = mb.GetType().Assembly.GetName().Name;

                    if (assembly == "UnityEngine.UI" || assembly == "Unity.TextMeshPro")
                    {
                        continue;
                    }

                    foreign++;
                    DestroyImmediate(mb);
                }

                if (foreign == 0)
                {
                    return;
                }
            }
        }

        private void Unlink(bool restore, string why)
        {
            if (AppOpen)
            {
                Close(restore, why);
            }

            if (_t != null)
            {
                foreach (UnityEvent pressed in _t.MeshHome)
                {
                    pressed.RemoveListener(OnMeshHome);
                }
            }

            if (_button != null)
            {
                _button.SetActive(false);
                Destroy(_button);
            }

            if (_app != null)
            {
                _app.SetActive(false);
                Destroy(_app);
            }

            if (_iconSprite != null)
            {
                Destroy(_iconSprite);
            }

            bool was = _t != null;
            _button = null;
            _buttonIcon = null;
            _app = null;
            _appCanvas = null;
            _frame = null;
            _iconSprite = null;
            _t = null;
            TabletOpen = false;

            if (was)
            {
                MpLog.Info("Tablet: unlinked (" + why + ").");

                if (!_ending && LinkChanged != null)
                {
                    LinkChanged();
                }
            }
        }

        // ------------------------------------------------------------------ showing and hiding

        private void ApplyAvailability()
        {
            if (_button != null && _button.activeSelf != _available)
            {
                _button.SetActive(_available);
            }
        }

        private void OnAppButton()
        {
            Open("app button");
        }

        private void OnBack()
        {
            Close(true, "Back");
        }

        private void OnMeshHome()
        {
            Close(true, "mesh Home");
        }

        public void OpenApp()
        {
            Open("world");
        }

        public void CloseApp()
        {
            Close(true, "world");
        }

        /// <summary>
        /// Closes the app, then the client's tablet itself (TabletControl.DeActivate, found by reflection: a public method of the client's component).
        /// True when the tablet was open and its close was called; false when it was closed already, is held in a hand (the client ignores the call then)
        /// or the client's component has no such method (the tablet is then left open on its home screen).
        /// </summary>
        public bool CloseTablet(string why)
        {
            if (!IsLinked)
            {
                return false;
            }

            if (AppOpen)
            {
                Close(true, why);
            }

            if (!ReadTabletOpen())
            {
                return false;
            }

            try
            {
                if (_t.Control == null || _t.Deactivate == null)
                {
                    if (!_closeFailedReported)
                    {
                        _closeFailedReported = true;
                        MpLog.Info("Tablet: the client's tablet has no DeActivate to call - it stays open on its home screen (" + why + ").");
                    }

                    return false;
                }

                _t.Deactivate.Invoke(_t.Control, null);
                MpLog.Info("Tablet: the client's tablet closed (" + why + ").");
                return true;
            }
            catch (Exception e)
            {
                if (!_closeFailedReported)
                {
                    _closeFailedReported = true;
                    MpLog.Warn("Tablet: closing the client's tablet failed (" + e.GetType().Name + ": " + e.Message + ") - it stays open on its home screen.");
                }

                return false;
            }
        }

        private void Open(string why)
        {
            if (!IsLinked || AppOpen || _app == null || !_available)
            {
                return;
            }

            _tabSnapshot = SnapshotTabs();
            _t.Tabs.gameObject.SetActive(false);
            _app.SetActive(true);
            SetLayer(_app.transform, _t.UiLayer);
            _app.layer = _t.HomeCanvas.gameObject.layer;

            if (_t.Back != null)
            {
                _backWasActive = _t.Back.gameObject.activeSelf;
                _t.Back.gameObject.SetActive(true); // TabletStatusBarView hides it on HOME
                _t.Back.onClick.AddListener(OnBack);
            }

            float width = _t.Home != null ? _t.Home.rect.width : 0f;
            string size = width > LargeAbove ? "Large" : "Small";

            if (ForceLarge && _t.Resize != null && width <= LargeAbove && Time.unscaledTime >= _resizeSettleUntil)
            {
                _t.Resize.onClick.Invoke(); // TabletResize toggles Small <-> Large
                _enlarged = true;
                _resizeSettleUntil = Time.unscaledTime + 0.35f; // its tween runs 0.2 s
                size = "Small->Large";
            }

            AppOpen = true;
            MpLog.Info("Tablet: " + AppTitle + " opened (" + why + ", size " + size + ").");

            if (AppShown != null)
            {
                AppShown(true);
            }
        }

        private void Close(bool restoreSize, string why)
        {
            if (!AppOpen)
            {
                return;
            }

            AppOpen = false;

            if (_app != null) _app.SetActive(false);
            if (_t != null && _t.Tabs != null) _t.Tabs.gameObject.SetActive(true);

            if (_t != null && _t.Back != null)
            {
                _t.Back.onClick.RemoveListener(OnBack);
                _t.Back.gameObject.SetActive(_backWasActive);
            }

            bool shrunk = false;

            if (restoreSize && _enlarged && _t != null && _t.Resize != null && IsLargeNow())
            {
                _t.Resize.onClick.Invoke();
                _resizeSettleUntil = Time.unscaledTime + 0.35f;
                shrunk = true;
            }

            _enlarged = false;
            MpLog.Info("Tablet: " + AppTitle + " closed (" + why + (shrunk ? ", size Large->Small" : "") + ").");

            if (!_ending && AppShown != null)
            {
                AppShown(false);
            }
        }

        private bool IsLargeNow()
        {
            if (Time.unscaledTime < _resizeSettleUntil)
            {
                return true; // our own press is still tweening
            }

            RectTransform measured = _t.Home != null ? _t.Home : _t.StatusBar;
            return measured != null && measured.rect.width > LargeAbove;
        }

        private bool ReadTabletOpen()
        {
            if (_t.Animator != null && _t.AnimatorHasIsOpen)
            {
                return _t.Animator.GetBool(IsOpenHash);
            }

            if (_t.CanvasesGroup != null)
            {
                // The close animation fades the Canvases CanvasGroup to alpha 0, not interactable.
                return _t.CanvasesGroup.alpha > 0.5f && _t.CanvasesGroup.blocksRaycasts;
            }

            return _t.GrabCollider == null || _t.GrabCollider.activeSelf;
        }

        private bool[] SnapshotTabs()
        {
            var shot = new bool[_t.TabContents.Count];

            for (int i = 0; i < shot.Length; i++)
            {
                GameObject content = _t.TabContents[i];
                shot[i] = content != null && content.activeSelf;
            }

            return shot;
        }

        private bool TabsChangedSinceOpen()
        {
            if (_tabSnapshot == null || _tabSnapshot.Length != _t.TabContents.Count)
            {
                return false;
            }

            for (int i = 0; i < _tabSnapshot.Length; i++)
            {
                GameObject content = _t.TabContents[i];

                if ((content != null && content.activeSelf) != _tabSnapshot[i])
                {
                    return true;
                }
            }

            return false;
        }

        private void FollowHomeRect()
        {
            if (_app == null || _t.Home == null)
            {
                return;
            }

            // A home canvas caught mid close-tween (scale towards 0) is not copied.
            var rect = (RectTransform)_app.transform;

            if (rect.parent == null || _t.Home.lossyScale.x < rect.parent.lossyScale.x * 0.5f)
            {
                return;
            }

            Vector2 size = _t.Home.rect.size;
            rect.localScale = Vector3.one;
            rect.pivot = _t.Home.pivot;
            rect.sizeDelta = size;
            rect.SetPositionAndRotation(_t.Home.position, _t.Home.rotation);

            if (_frame != null)
            {
                float fit = Mathf.Min(size.x / FrameWidth, size.y / FrameHeight); // 1200 x 860 frame: Small 960 x 1080 0.80, Large 1920 x 1080 1.26
                _frame.localScale = new Vector3(fit, fit, 1f);
            }
        }

        /// <summary>
        /// Puts every graphic of the app on the layer the client's own tablet graphics carry (5, UI), which the XR ray tests per graphic
        /// (TrackedDeviceGraphicRaycaster.ShouldTestGraphic); the canvas root keeps the HomeTab canvas's layer. Called when the app opens
        /// and whenever the page was built (or rows were added) into the frame.
        /// </summary>
        public void ApplyLayers()
        {
            if (IsLinked && _app != null)
            {
                SetLayer(_app.transform, _t.UiLayer);
            }
        }

        private static void SetLayer(Transform t, int layer)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);

                if (child.gameObject.layer != layer)
                {
                    child.gameObject.layer = layer;
                }

                SetLayer(child, layer);
            }
        }
    }
}
