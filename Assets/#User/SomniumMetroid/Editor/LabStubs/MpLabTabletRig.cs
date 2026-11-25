using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SomniumSpace.Worlds.SomniumMetroid.Game;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumMetroid.LabStubs
{
    /// <summary>
    /// "-mplab tablet" (started by Editor/MpLabTablet.cs): the "METROID PRIME" tablet app in the editor, after the BO2 Dreamport's Bo2LabTablet. The Somnium
    /// tablet cannot exist here, so a stand-in is built from the client's hierarchy (root with TabletControl, Canvases + CanvasGroup, a TabsContainer with the
    /// HomeTab canvas and its grid of buttons, a status bar with Back and Resize, the mesh Home button) and the REAL link and page run against it: discovery by
    /// type name, the injected home button, the app canvas and its raycasters, the page and its tabs, the options written, the Game tab's commands (stand-in
    /// MpGameActions), Back / mesh Home / tab switch / tablet close, a desktop tablet with the mouse, the shutdown and the next visit's sweep.
    /// Pointer input is simulated the way the client's input modules deliver it: a TrackedDeviceEventData ray through the app canvas's TrackedDeviceGraphicRaycaster
    /// (the VR laser) and the EventSystem's raycasters with a screen position (the mouse); the top hit gets enter/down/up/click through ExecuteEvents.
    /// It lives in this assembly (not SomniumMetroid.Editor) because it needs Unity UI, TextMeshPro and XRI, which the editor assembly does not reference.
    /// The player's options (fields and options.txt) and MpGameActions are put back at the end, also after an exception. Every step logs "CHECK ok|FAIL";
    /// any failure ends the scenario with an exception (the lab's exit code 1). Pictures tablet_*.png.
    /// </summary>
    public static class MpLabTabletRig
    {
        private sealed class Fake
        {
            public GameObject Root;
            public TabletControl Control;
            public CanvasGroup Group;
            public Canvas Home;
            public RectTransform HomeRect;
            public GameObject Tabs;
            public Transform Grid;
            public Button Back, Resize;
            public GameObject HomeContent, WorldsContent;
            public XRInteractableButton MeshHome;
            public int ResizePresses;
        }

        private static Action<string> _log;
        private static Action<Camera, string> _shoot;
        private static int _checks, _fails;
        private static Camera _cam;
        private static EventSystem _es;
        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        /// <summary>The scenario; nested waits are stepped here, so the options are restored whatever happens.</summary>
        public static IEnumerator Scenario(Action<string> log, Action<Camera, string> shoot)
        {
            _log = log;
            _shoot = shoot;
            _checks = _fails = 0;

            var fields = new List<KeyValuePair<FieldInfo, object>>();
            foreach (FieldInfo f in typeof(MpSettings).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(float) || f.FieldType == typeof(bool)) fields.Add(new KeyValuePair<FieldInfo, object>(f, f.GetValue(null)));
            var actions = new List<KeyValuePair<FieldInfo, object>>();
            foreach (FieldInfo f in typeof(MpGameActions).GetFields(BindingFlags.Public | BindingFlags.Static)) actions.Add(new KeyValuePair<FieldInfo, object>(f, f.GetValue(null)));
            string file = MpSettings.File;
            bool hadFile = file != null && File.Exists(file);
            string fileText = hadFile ? File.ReadAllText(file) : null;

            var stack = new Stack<IEnumerator>();
            stack.Push(Body());
            Exception error = null;

            while (stack.Count > 0)
            {
                object current;
                try
                {
                    IEnumerator top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    current = top.Current;
                }
                catch (Exception e) { error = e; break; }

                var nested = current as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                yield return current;
            }

            foreach (var kv in fields) kv.Key.SetValue(null, kv.Value);
            foreach (var kv in actions) kv.Key.SetValue(null, kv.Value);
            MpSettings.Touch(false);
            try
            {
                if (hadFile) File.WriteAllText(file, fileText);
                else if (file != null && File.Exists(file)) File.Delete(file);
            }
            catch (Exception e) { Log("could not restore " + file + ": " + e.Message); }

            Log("tablet lab: " + _checks + " checks, " + _fails + " failed; the options and MpGameActions were put back");
            if (error != null) throw new Exception("the tablet lab threw", error);
            if (_fails > 0) throw new Exception("the tablet lab: " + _fails + " of " + _checks + " checks failed");
        }

        private static IEnumerator Body()
        {
            var camGo = new GameObject("Lab Tablet Camera");
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.fieldOfView = 60f;
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = 50f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 1f);
            _cam.transform.position = new Vector3(0f, 3000f, 0f);     // far above the rooms: nothing of the world in the pictures
            _cam.transform.rotation = Quaternion.identity;
            Vector3 at = _cam.transform.position + new Vector3(0f, -0.04f, 0.62f);
            MpPlayer.EnsureEditorEventSystem();
            Es();

            MpTabletApp.Start(MpGame.Instance != null ? MpGame.Instance.transform : null);
            MpTabletLink link = MpTabletApp.Link;
            Check(link != null && MpTabletApp.Page != null, "the app started (MpTabletApp.Start)");
            if (link == null) yield break;
            link.SearchInterval = 0.2f;
            link.NotLinkedLogAfter = 3f;
            yield return Frames(30);
            Check(!link.IsLinked && !MpTabletApp.IsOpen, "no tablet: not linked, IsOpen false");

            // ---- a VR-style tablet appears (a TrackedDeviceGraphicRaycaster on its HomeTab canvas)
            int woke0 = SwitchTabButton.Woke;
            Fake fake = BuildFake(true, at);
            int wokeOriginals = SwitchTabButton.Woke - woke0;
            yield return Until(() => link.IsLinked, 20f, "the link to the stand-in tablet");
            Log("link report: " + link.Report);
            Check(link.Report.Contains("root by type TabletControl") && link.Report.Contains("TabsContainer by type, HomeTab by type, TabletStatusBarView by type") && link.Report.EndsWith("MISSING: Animator", StringComparison.Ordinal),
                  "found by the client's component type names, every part present (the stand-in has no Animator: the open state is read from the CanvasGroup)");
            Check(SwitchTabButton.Woke - woke0 == wokeOriginals, "the cloned home button never woke a client script");
            yield return Frames(60);
            Button ours = OurButton(fake);
            Check(ours != null && ours.transform.GetSiblingIndex() == 0, "the METROID PRIME home button is the first child of the home grid");
            Check(ours != null && ours.GetComponentInChildren<SwitchTabButton>(true) == null, "the clone has no client script left");
            TMP_Text caption = ours != null ? ours.GetComponentInChildren<TMP_Text>(true) : null;
            Check(caption != null && caption.text == "METROID PRIME", "its caption reads METROID PRIME");
            Transform iconT = ours != null ? ours.transform.Find("Icon") : null;
            Image icon = iconT != null ? iconT.GetComponent<Image>() : null;
            Check(icon != null && icon.sprite != null && icon.enabled, "its icon is painted");
            MpTabletPage page = MpTabletApp.Page;
            Check(page != null && page.Root != null && link.Frame != null && page.Root.IsChildOf(link.Frame), "the options page is built into the app's frame");
            Check(link.AppCanvas != null && link.AppCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() != null && link.AppCanvas.GetComponent<GraphicRaycaster>() == null, "the app canvas mirrors the home canvas's raycaster (tracked device)");
            _shoot(_cam, "tablet_1_home_screen.png");
            if (ours == null || page == null || page.Root == null) yield break;

            // ---- open with the VR laser
            Check(ClickOn(fake.Home, ours, true), "the laser's click on the home button was taken");
            yield return Frames(20);
            Check(link.AppOpen && !fake.Tabs.activeSelf && link.AppCanvas.gameObject.activeSelf && MpTabletApp.IsOpen, "the app is open (IsOpen): the client's Tabs hidden, the app's canvas shown");
            Check(fake.ResizePresses == 1 && fake.HomeRect.sizeDelta.x > 1440f && fake.Back.gameObject.activeSelf, "a Small tablet was pressed to Large, Back is shown");
            yield return Frames(10);
            int wrong = 0, graphics = 0;
            foreach (Graphic g in link.Frame.GetComponentsInChildren<Graphic>(true)) { graphics++; if (g.gameObject.layer != link.UiLayer) wrong++; }
            Check(graphics > 150 && wrong == 0, "all " + graphics + " graphics are on the tablet's UI layer (" + wrong + " elsewhere)");
            Check(Mathf.Abs(link.Frame.lossyScale.x / 0.0004f - 1.256f) < 0.01f, "the frame fits the Large screen (" + (link.Frame.lossyScale.x / 0.0004f).ToString("0.000") + " x)");
            _shoot(_cam, "tablet_2_hud.png");
            int cut = 0;
            string cutNames = "";
            foreach (MpUiButton b in page.Root.GetComponentsInChildren<MpUiButton>(false)) if (b.LabelText.isTextTruncated) { cut++; cutNames += " '" + b.Label + "'"; }
            Check(cut == 0, "no shown button caption is cut (" + cut + ":" + cutNames + ")");

            // ---- HUD: the step buttons, a slider, a toggle, a change from elsewhere
            MpSettings.HudDistance = 1.5f;
            MpSettings.Touch(false);
            yield return Frames(3);
            Transform root = page.Root;
            Check(Text(root, "Distance Value") == "1.50 m", "the page follows a change made elsewhere (" + Text(root, "Distance Value") + ")");
            Check(ClickOn(link.AppCanvas, Btn(root, "FURTHER"), true) && Mathf.Abs(MpSettings.HudDistance - 1.6f) < 0.005f && Text(root, "Distance Value") == "1.60 m", "FURTHER moves the HUD 0.1 m away (" + MpSettings.HudDistance + ")");
            yield return Pause();
            Check(ClickOn(link.AppCanvas, Btn(root, "CLOSER"), true) && Mathf.Abs(MpSettings.HudDistance - 1.5f) < 0.005f, "CLOSER brings it back (" + MpSettings.HudDistance + ")");
            int version = MpSettings.Version;
            Slider size = Named<Slider>(root, "Size Slider");
            if (size != null) size.value = 1.234f;
            Check(size != null && Mathf.Abs(MpSettings.HudSize - 1.23f) < 0.001f && MpSettings.Version > version && Text(root, "Size Value") == "1.23x", "the Size slider writes HudSize, quantized (" + MpSettings.HudSize + ")");
            yield return Seconds(0.7f);
            string file = MpSettings.File;
            Check(file == null || (File.Exists(file) && File.ReadAllText(file).Contains("hud_size=1.23")),"options.txt was written after the drag rested (" + (MpSettings.File ?? "no settings folder in the lab") + ")");
            bool shown0 = MpSettings.HudShown;
            MpUiButton hudToggle = Named<MpUiButton>(root, "HUD shown Toggle");
            Check(ClickOn(link.AppCanvas, hudToggle, true) && MpSettings.HudShown != shown0 && hudToggle.Label == (shown0 ? "OFF" : "ON"), "the HUD toggle flips HudShown (" + MpSettings.HudShown + ")");
            yield return Pause();
            Check(ClickOn(link.AppCanvas, hudToggle, true) && MpSettings.HudShown == shown0, "and back");
            GameObject beside = RayHit(link.AppCanvas, link.AppCanvas.transform.TransformPoint(new Vector3(-0.47f * ((RectTransform)link.AppCanvas.transform).rect.width, 0f, 0f)), ~0);
            Check(beside != null && beside.GetComponentInParent<MpUiButton>() == null && beside.transform.IsChildOf(link.AppCanvas.transform), "a ray at the edge ends on the app's own backdrop, never the client's tabs ('" + (beside != null ? beside.name : "nothing") + "')");

            // ---- the other tabs
            Check(ClickOn(link.AppCanvas, Btn(root, "ARM CANNON"), true) && page.Tab == MpTabletPage.TabCannon && page.TabRoot(MpTabletPage.TabCannon).gameObject.activeSelf && !page.TabRoot(MpTabletPage.TabHud).gameObject.activeSelf, "the ARM CANNON tab");
            MpSettings.CannonPitch = 0f;
            MpSettings.Touch(false);
            yield return Frames(5);
            Check(ClickOn(link.AppCanvas, Btn(page.TabRoot(MpTabletPage.TabCannon), "UP"), true) && Mathf.Abs(MpSettings.CannonPitch - 2f) < 0.01f && Text(root, "Pitch Value") == "+2 deg", "the pitch UP step (" + MpSettings.CannonPitch + " deg)");
            _shoot(_cam, "tablet_3_cannon.png");
            Check(ClickOn(link.AppCanvas, Btn(root, "SOUND & CONTROLS"), true) && page.Tab == MpTabletPage.TabSound, "the SOUND & CONTROLS tab");
            yield return Frames(5);
            Check(ClickOn(link.AppCanvas, Btn(root, "HARD"), true) && MpSettings.DamageTaken == 2f && Btn(root, "HARD").Selected && !Btn(root, "NORMAL").Selected, "HARD sets DamageTaken 2");
            _shoot(_cam, "tablet_4_sound.png");
            yield return Pause();
            Check(ClickOn(link.AppCanvas, Btn(root, "RESET TO DEFAULTS"), true) && MpSettings.DamageTaken == 1f && MpSettings.HudSize == 0.9f && Text(root, "Damage taken Value") == "x1", "RESET TO DEFAULTS puts the defaults back and shows them");

            // ---- the Game tab against stand-in MpGameActions
            var rooms = new string[24];
            for (int i = 0; i < rooms.Length; i++) rooms[i] = "Lab room " + (char)('A' + i);
            int warped = -1;
            MpGameActions.SaveGame = () => "Saved (lab).";
            MpGameActions.LoadGame = null;
            MpGameActions.Respawn = () => "Respawned (lab).";
            MpGameActions.SaveInfo = () => "Lab save: Landing Site, 0:12.";
            MpGameActions.Rooms = () => rooms;
            MpGameActions.WarpTo = i => { warped = i; return "Going to " + rooms[i] + " (lab)."; };
            MpGameActions.Status = () => "Lab status: energy 99.";
            MpGameActions.About = () => "Metroid Prime Dreamport lab: the About text.";
            Check(ClickOn(link.AppCanvas, Btn(root, "GAME"), true) && page.Tab == MpTabletPage.TabGame, "the GAME tab");
            yield return Frames(5);
            Transform game = page.TabRoot(MpTabletPage.TabGame);
            Check(Text(game, "Live") == "Lab status: energy 99." && Text(game, "Save Info") == "Lab save: Landing Site, 0:12.", "the status and save lines");
            Check(Btn(game, "SAVE").Interactable && !Btn(game, "LOAD LAST SAVE").Interactable && Btn(game, "RESPAWN / UNSTUCK").Interactable, "a null command is greyed out (LOAD LAST SAVE)");
            Check(ClickOn(link.AppCanvas, Btn(game, "SAVE"), true) && page.StatusText == "Saved (lab).", "SAVE runs and its answer is in the status bar ('" + page.StatusText + "')");
            Transform content = game.Find("Rooms/Viewport/Content");
            int rows = 0;
            if (content != null) foreach (Transform c in content) if (c.gameObject.activeSelf) rows++;
            Check(rows == rooms.Length, "the room list has " + rows + " rows");
            Check(content != null && ClickOn(link.AppCanvas, content.Find("Room 3").GetComponent<MpUiButton>(), true) && warped == 3 && page.StatusText.StartsWith("Going to Lab room D", StringComparison.Ordinal), "a tap on room 3 calls WarpTo(3)");
            ScrollRect scroll = game.Find("Rooms").GetComponent<ScrollRect>();
            Check(ClickOn(link.AppCanvas, Btn(game, "DOWN"), true) && scroll.verticalNormalizedPosition < 0.99f, "DOWN pages the list (" + scroll.verticalNormalizedPosition.ToString("0.00") + ")");
            _shoot(_cam, "tablet_5_game.png");
            MpGameActions.SaveGame = null;
            yield return Seconds(1.2f);
            Check(!Btn(game, "SAVE").Interactable, "SAVE greys out once the game takes it away");
            Check(ClickOn(link.AppCanvas, Btn(root, "ABOUT"), true) && Text(page.TabRoot(MpTabletPage.TabAbout), "About Text") == "Metroid Prime Dreamport lab: the About text.", "the ABOUT tab shows About()");
            yield return Frames(5);
            _shoot(_cam, "tablet_6_about.png");

            // ---- the tablet closes under the app; Back, mesh Home, a client tab switch
            fake.Control.DeActivate();
            yield return Frames(3);
            Check(link.AppOpen && !MpTabletApp.IsOpen, "the client closed its tablet: IsOpen is false (the app stays its page)");
            fake.Control.Activate();
            yield return Frames(3);
            Check(MpTabletApp.IsOpen, "opened again: IsOpen");
            fake.Back.onClick.Invoke();
            yield return Frames(5);
            Check(!link.AppOpen && !MpTabletApp.IsOpen && fake.Tabs.activeSelf && fake.ResizePresses == 2 && fake.HomeRect.sizeDelta.x < 1440f && !fake.Back.gameObject.activeSelf, "Back closes the app, the tablet is Small again, Back hidden");
            yield return Frames(20);
            link.OpenApp();
            yield return Frames(10);
            fake.MeshHome.ButtonPressed.Invoke();
            yield return Frames(5);
            Check(!link.AppOpen && fake.Tabs.activeSelf, "the mesh Home button closes the app");
            yield return Frames(20);
            link.OpenApp();
            yield return Frames(10);
            fake.WorldsContent.SetActive(true);
            fake.HomeContent.SetActive(false);
            yield return Until(() => !link.AppOpen, 5f, "the app to close when the client switches tab");
            fake.WorldsContent.SetActive(false);
            fake.HomeContent.SetActive(true);
            yield return Frames(10);

            // ---- the tablet is destroyed (avatar change); a desktop tablet comes with the mouse
            UnityEngine.Object.Destroy(fake.Root);
            yield return Until(() => !link.IsLinked, 8f, "the unlink");
            Check(!link.IsLinked && page.Root == null, "the tablet is gone: unlinked, the page went with it");
            Fake desk = BuildFake(false, at);
            yield return Until(() => link.IsLinked && page.Root != null, 20f, "the link to the desktop tablet");
            yield return Frames(60);
            GameObject overlay = GameObject.Find("WorldTestingManager(Clone)");      // the SDK's test-mode overlay takes every mouse ray first
            Transform overlayCanvas = overlay != null ? overlay.transform.Find("Canvas") : null;
            bool overlayWas = overlayCanvas != null && overlayCanvas.gameObject.activeSelf;
            if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
            Check(link.AppCanvas.GetComponent<GraphicRaycaster>() != null && link.AppCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null, "the app canvas mirrors the desktop tablet's raycaster (mouse)");
            Check(ClickOn(desk.Home, OurButton(desk), false), "the mouse clicks the home button");
            yield return Frames(20);
            Check(link.AppOpen && MpTabletApp.IsOpen, "the app is open (mouse)");
            Check(ClickOn(link.AppCanvas, Btn(page.Root, "HUD"), false) && page.Tab == MpTabletPage.TabHud, "the mouse clicks the HUD tab");
            yield return Frames(5);      // a tab's graphics are raycast only once the canvas drew them (depth -1 in the frame they were enabled)
            Check(ClickOn(link.AppCanvas, Btn(page.Root, "FURTHER"), false) && Mathf.Abs(MpSettings.HudDistance - 1.6f) < 0.005f, "the mouse clicks FURTHER");
            yield return Frames(5);
            _shoot(_cam, "tablet_7_desktop.png");
            if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(overlayWas);

            // ---- the world shuts down with the app open on an enlarged tablet; the next visit sweeps what a cut visit left
            MpTabletApp.Shutdown();
            yield return Frames(5);
            Check(MpTabletApp.Page == null && MpTabletLink.Instance == null && !MpTabletApp.IsOpen, "the app and the link are gone");
            Check(CountOwned(desk) == 0 && desk.Tabs.activeSelf && desk.HomeRect.sizeDelta.x < 1440f && !desk.Back.gameObject.activeSelf, "nothing named MP.Tablet.* is left; Tabs shown, the tablet Small, Back hidden");
            MakeHomeButton(desk.Grid, "METROID PRIME").name = MpTabletLink.Owned + "Button";
            new GameObject(MpTabletLink.Owned + "App", typeof(RectTransform)).transform.SetParent(desk.Group.transform, false);
            desk.Tabs.SetActive(false);
            MpTabletApp.Start(MpGame.Instance != null ? MpGame.Instance.transform : null);
            MpTabletLink link2 = MpTabletApp.Link;
            link2.SearchInterval = 0.2f;
            yield return Until(() => link2.IsLinked, 20f, "the link of the second visit");
            yield return Frames(60);
            int ownButtons = 0;
            foreach (Transform c in desk.Grid) if (c.name == MpTabletLink.Owned + "Button" && c.gameObject.activeSelf) ownButtons++;
            Check(desk.Tabs.activeSelf && CountOwned(desk) == 2 && ownButtons == 1, "the second visit swept the strays: one button, one app canvas (" + CountOwned(desk) + " owned)");
            MpTabletApp.Shutdown();
            yield return Frames(5);
            Check(CountOwned(desk) == 0, "and its shutdown leaves nothing either");
            UnityEngine.Object.Destroy(desk.Root);
            UnityEngine.Object.Destroy(camGo);
        }

        // ------------------------------------------------------------------ helpers

        private static void Log(string s) { if (_log != null) _log(s); }

        private static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            Log("CHECK " + (ok ? "ok" : "FAIL") + ": " + what);
        }

        private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        private static IEnumerator Seconds(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }

        /// <summary>Past the buttons' double-click guard.</summary>
        private static IEnumerator Pause() { return Seconds(0.25f); }

        private static IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond())
            {
                if (Time.realtimeSinceStartup - t0 > timeout) { Check(false, "TIMEOUT waiting for " + what); yield break; }
                yield return null;
            }
        }

        private static MpUiButton Btn(Transform root, string label)
        {
            if (root == null) return null;
            foreach (MpUiButton b in root.GetComponentsInChildren<MpUiButton>(true)) if (b.Label == label) return b;
            return null;
        }

        private static T Named<T>(Transform root, string name) where T : Component
        {
            if (root == null) return null;
            foreach (T c in root.GetComponentsInChildren<T>(true)) if (c.name == name) return c;
            return null;
        }

        private static string Text(Transform root, string name)
        {
            TextMeshProUGUI t = Named<TextMeshProUGUI>(root, name);
            return t != null ? t.text : null;
        }

        private static Button OurButton(Fake f)
        {
            foreach (Transform c in f.Grid) if (c.name == MpTabletLink.Owned + "Button" && c.gameObject.activeSelf) return c.GetComponent<Button>();
            return null;
        }

        private static int CountOwned(Fake f)
        {
            int n = 0;
            foreach (Transform c in f.Grid) if (c.name.StartsWith(MpTabletLink.Owned, StringComparison.Ordinal)) n++;
            foreach (Transform c in f.Group.transform) if (c.name.StartsWith(MpTabletLink.Owned, StringComparison.Ordinal)) n++;
            return n;
        }

        // ------------------------------------------------------------------ the simulated pointer

        private static EventSystem Es()
        {
            if (_es == null)
            {
                _es = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindFirstObjectByType<EventSystem>();
                if (_es == null) _es = new GameObject("Lab EventSystem").AddComponent<EventSystem>();
            }

            return _es;
        }

        private static Vector3 CenterOf(Component c)
        {
            var rt = (RectTransform)c.transform;
            return rt.TransformPoint(rt.rect.center);
        }

        /// <summary>The VR laser: a ray from a hand beside and below the head through the target, tested by the canvas's own TrackedDeviceGraphicRaycaster.</summary>
        private static GameObject RayHit(Canvas canvas, Vector3 target, int layerMask)
        {
            var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (raycaster == null) return null;
            Vector3 from = _cam.transform.position + _cam.transform.right * 0.18f - _cam.transform.up * 0.28f;
            Vector3 dir = (target - from).normalized;
            var data = new TrackedDeviceEventData(Es()) { rayPoints = new List<Vector3> { from, target + dir * 0.3f }, layerMask = layerMask, pointerId = 7 };
            Hits.Clear();
            raycaster.Raycast(data, Hits);
            return Hits.Count > 0 ? Hits[0].gameObject : null;
        }

        /// <summary>The desktop mouse: the target's screen position through the EventSystem's raycasters.</summary>
        private static GameObject MouseHit(Vector3 target)
        {
            Vector3 screen = _cam.WorldToScreenPoint(target);
            var data = new PointerEventData(Es()) { position = new Vector2(screen.x, screen.y), pointerId = -1 };
            Hits.Clear();
            Es().RaycastAll(data, Hits);
            return Hits.Count > 0 ? Hits[0].gameObject : null;
        }

        /// <summary>Enter, down, up and click on whatever handles them under the hit graphic, as the client's input modules send them. Returns the clicked handler.</summary>
        private static GameObject Deliver(GameObject hit, bool tracked)
        {
            if (hit == null) return null;
            PointerEventData data = tracked ? new TrackedDeviceEventData(Es()) { pointerId = 7 } : new PointerEventData(Es()) { pointerId = -1 };
            data.button = PointerEventData.InputButton.Left;
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerEnterHandler);
            GameObject down = ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerDownHandler);
            data.pointerPress = down;
            data.rawPointerPress = hit;
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerUpHandler);
            GameObject click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            if (click != null && click == down) ExecuteEvents.Execute(click, data, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerExitHandler);
            return click != null && click == down ? click : null;
        }

        /// <summary>One pointer click on a widget of a canvas. True when the widget itself (or its handler parent) took it.</summary>
        private static bool ClickOn(Canvas canvas, Component widget, bool tracked)
        {
            if (widget == null) { Log("pointer: no such widget"); return false; }
            Vector3 target = CenterOf(widget);
            GameObject hit = tracked ? RayHit(canvas, target, ~0) : MouseHit(target);
            GameObject taken = Deliver(hit, tracked);
            Log("pointer " + (tracked ? "ray" : "mouse") + " at '" + widget.name + "': top graphic '" + (hit != null ? hit.name : "none") + "', click taken by '" + (taken != null ? taken.name : "nobody") + "'");
            return taken != null && (taken == widget.gameObject || widget.transform.IsChildOf(taken.transform));
        }

        // ------------------------------------------------------------------ the stand-in tablet

        private static Fake BuildFake(bool tracked, Vector3 at)
        {
            var f = new Fake();
            f.Root = new GameObject(tracked ? "Tablet.XR(Clone)" : "Tablet.NonXR(Clone)");
            f.Root.transform.position = at;
            f.Root.transform.rotation = Quaternion.identity;
            f.Control = f.Root.AddComponent<TabletControl>();

            var canvases = new GameObject("Canvases", typeof(RectTransform));
            canvases.transform.SetParent(f.Root.transform, false);
            f.Group = canvases.AddComponent<CanvasGroup>();
            canvases.transform.localScale = Vector3.one * 0.0004f;
            f.Control.Group = f.Group;
            f.Control.Activate();

            f.Tabs = new GameObject("Tablet.XR.Tabs", typeof(RectTransform));
            f.Tabs.transform.SetParent(canvases.transform, false);
            f.Tabs.AddComponent<TabsContainer>();

            var home = new GameObject("Tablet.XR.HomeTab", typeof(RectTransform));
            home.layer = 9;
            home.transform.SetParent(f.Tabs.transform, false);
            home.AddComponent<HomeTab>();
            f.Home = home.AddComponent<Canvas>();
            f.Home.renderMode = RenderMode.WorldSpace;
            f.Home.worldCamera = _cam;
            if (tracked) home.AddComponent<TrackedDeviceGraphicRaycaster>(); else home.AddComponent<GraphicRaycaster>();
            f.HomeRect = (RectTransform)home.transform;
            f.HomeRect.pivot = new Vector2(0.5f, 0.5f);
            f.HomeRect.sizeDelta = new Vector2(960f, 1080f);      // Small (Large is 1920 x 1080); the scale is on the Canvases, as on the client's tablet
            var back = home.AddComponent<Image>();
            back.color = new Color(0.12f, 0.12f, 0.14f, 1f);

            f.HomeContent = new GameObject("ContentView", typeof(RectTransform));
            f.HomeContent.layer = 5;
            f.HomeContent.transform.SetParent(home.transform, false);
            var contentRect = (RectTransform)f.HomeContent.transform;
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;

            var grid = new GameObject("Grid", typeof(RectTransform));
            grid.layer = 5;
            grid.transform.SetParent(f.HomeContent.transform, false);
            var gridRect = (RectTransform)grid.transform;
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(20f, 20f);
            gridRect.offsetMax = new Vector2(-20f, -20f);
            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(220f, 250f);
            layout.spacing = new Vector2(16f, 16f);
            layout.childAlignment = TextAnchor.UpperLeft;
            grid.AddComponent<RectMask2D>();
            f.Grid = grid.transform;
            foreach (string a in new[] { "WORLDS", "AVATARS", "SOCIAL", "MAP", "CAMERA", "OPTIONS" }) MakeHomeButton(f.Grid, a);

            var worlds = new GameObject("Tab.Worlds", typeof(RectTransform));
            worlds.transform.SetParent(f.Tabs.transform, false);
            f.WorldsContent = new GameObject("ContentView", typeof(RectTransform));
            f.WorldsContent.transform.SetParent(worlds.transform, false);
            f.WorldsContent.SetActive(false);

            var bar = new GameObject("Tablet.XR.StatusBar", typeof(RectTransform));
            bar.transform.SetParent(f.Root.transform, false);
            bar.AddComponent<TabletStatusBarView>();
            f.Back = MakeBarButton(bar.transform, "Tablet.XR.StatusBar.Back");
            f.Back.gameObject.SetActive(false);
            f.Resize = MakeBarButton(bar.transform, "Tablet.XR.StatusBar.Resize");
            f.Resize.onClick.AddListener(() =>
            {
                f.ResizePresses++;
                f.HomeRect.sizeDelta = new Vector2(f.HomeRect.sizeDelta.x > 1440f ? 960f : 1920f, 1080f);
            });

            var meshHome = new GameObject("HomeButton");
            meshHome.transform.SetParent(f.Root.transform, false);
            f.MeshHome = meshHome.AddComponent<XRInteractableButton>();
            return f;
        }

        private static GameObject MakeHomeButton(Transform grid, string label)
        {
            var go = new GameObject("App." + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = 5;
            go.transform.SetParent(grid, false);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.20f, 0.20f, 0.25f, 1f);
            go.GetComponent<Button>().targetGraphic = bg;
            go.AddComponent<SwitchTabButton>();

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.layer = 5;
            icon.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, -16f);
            iconRect.sizeDelta = new Vector2(150f, 150f);
            icon.GetComponent<Image>().color = new Color(0.45f, 0.62f, 0.80f, 1f);

            var cap = new GameObject("Caption", typeof(RectTransform));
            cap.layer = 5;
            cap.transform.SetParent(go.transform, false);
            var capRect = (RectTransform)cap.transform;
            capRect.anchorMin = capRect.anchorMax = new Vector2(0.5f, 0f);
            capRect.pivot = new Vector2(0.5f, 0f);
            capRect.anchoredPosition = new Vector2(0f, 14f);
            capRect.sizeDelta = new Vector2(210f, 60f);
            var tmp = cap.AddComponent<TextMeshProUGUI>();
            if (MpTabletUi.Font != null) tmp.font = MpTabletUi.Font;
            tmp.fontSize = 30f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = label;
            return go;
        }

        private static Button MakeBarButton(Transform bar, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(bar, false);
            return go.GetComponent<Button>();
        }
    }
}
