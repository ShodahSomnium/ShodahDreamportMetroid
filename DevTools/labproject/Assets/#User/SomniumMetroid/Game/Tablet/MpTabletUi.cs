using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumMetroid.Game
{
    /// <summary>
    /// The uGUI kit of the "METROID PRIME" tablet app, in the look of the game's own options and pause screens: near-black navy panels,
    /// thin cyan rules and borders, pale cyan text, orange for what is selected. No textures and nothing of the game: every shape is an
    /// Image colour, every text TextMeshPro with the client's default font (as the BO2 Dreamport's Bo2Ui, proven on the client's tablet).
    /// Layouts are in canvas units with (0, 0) at the parent's top-left and y growing downwards. Everything is made on the UI layer (5),
    /// which the client's XR ray tests per graphic; the link moves it if the client's tablet uses another one.
    /// </summary>
    public static class MpTabletUi
    {
        public const int UiLayer = 5;

        /// <summary>The page's design size in canvas units (scaled to fit the tablet's screen: 0.8 on a Small tablet, 1.26 on a Large one).</summary>
        public const float FrameWidth = 1200f, FrameHeight = 860f;

        public static readonly Color Navy = new Color(0.012f, 0.027f, 0.055f, 1f);
        public static readonly Color Panel = new Color(0.035f, 0.075f, 0.13f, 0.95f);
        public static readonly Color PanelHover = new Color(0.06f, 0.17f, 0.25f, 0.97f);
        public static readonly Color PanelDown = new Color(0.09f, 0.26f, 0.36f, 1f);
        public static readonly Color PanelOn = new Color(0.22f, 0.11f, 0.03f, 0.96f);
        public static readonly Color Cyan = new Color(0.247f, 0.816f, 1f, 1f);       // #3FD0FF
        public static readonly Color CyanDim = new Color(0.13f, 0.42f, 0.55f, 0.85f);
        public static readonly Color CyanFaint = new Color(0.247f, 0.816f, 1f, 0.16f);
        public static readonly Color Orange = new Color(1f, 0.56f, 0.13f, 1f);
        public static readonly Color Ink = new Color(0.84f, 0.95f, 1f, 1f);
        public static readonly Color Grey = new Color(0.46f, 0.56f, 0.64f, 1f);
        public static readonly Color Disabled = new Color(0.28f, 0.33f, 0.38f, 1f);

        public static TMP_FontAsset Font { get { return TMP_Settings.defaultFontAsset; } }

        /// <summary>A child rect at (x, y) from the parent's top-left, w x h canvas units.</summary>
        public static RectTransform Place(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        /// <summary>A child rect filling the parent between two anchor points, inset by the offsets (Unity's offsetMin / offsetMax).</summary>
        public static RectTransform Stretch(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
            return r;
        }

        public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color, bool raycast = false)
        {
            Image img = Place(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Four thin lines along the edges of a w x h rect at (x, y).</summary>
        public static void Border(Transform parent, string name, float x, float y, float w, float h, float thickness, Color color)
        {
            Box(parent, name + " T", x, y, w, thickness, color);
            Box(parent, name + " B", x, y + h - thickness, w, thickness, color);
            Box(parent, name + " L", x, y, thickness, h, color);
            Box(parent, name + " R", x + w - thickness, y, thickness, h, color);
        }

        /// <summary>Four thin lines along the edges of the parent, following its size (for widgets a layout may resize).</summary>
        public static Image[] EdgeLines(Transform parent, float thickness, Color color)
        {
            var lines = new Image[4];
            lines[0] = EdgeLine(parent, "Edge T", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero, color);
            lines[1] = EdgeLine(parent, "Edge B", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness), color);
            lines[2] = EdgeLine(parent, "Edge L", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(thickness, 0f), color);
            lines[3] = EdgeLine(parent, "Edge R", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), Vector2.zero, color);
            return lines;
        }

        private static Image EdgeLine(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color color)
        {
            Image img = Stretch(parent, name, aMin, aMax, oMin, oMax).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h, bool bold = false)
        {
            var t = Place(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            Style(t, text, size, color, align, bold);
            return t;
        }

        public static void Style(TextMeshProUGUI t, string text, float size, Color color, TextAlignmentOptions align, bool bold)
        {
            if (Font != null) t.font = Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = false;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.text = text ?? "";
        }

        public static MpUiButton Button(Transform parent, string label, float x, float y, float w, float h, float textSize, Action onClick)
        {
            RectTransform r = Place(parent, "Button " + label, x, y, w, h);
            var b = r.gameObject.AddComponent<MpUiButton>();
            b.Build(label, textSize, onClick, TextAlignmentOptions.Center);
            return b;
        }

        /// <summary>
        /// A horizontal slider in the game's options look (a thin cyan track, a cyan fill, a pale handle with an orange core). The whole w x h rect
        /// takes the ray (a transparent catcher), so a tap anywhere on the row's track moves the handle there; dragging works with the client's
        /// XR ray and the mouse alike (UnityEngine.UI.Slider).
        /// </summary>
        public static Slider Slider(Transform parent, string name, float x, float y, float w, float h, float min, float max)
        {
            const float HandleW = 26f, TrackH = 8f;
            RectTransform root = Place(parent, name, x, y, w, h);
            var catcher = root.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            Image track = Stretch(root, "Track", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -TrackH * 0.5f), new Vector2(0f, TrackH * 0.5f)).gameObject.AddComponent<Image>();
            track.color = CyanFaint;
            track.raycastTarget = false;
            RectTransform fillArea = Stretch(root, "Fill Area", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(HandleW * 0.5f, -TrackH * 0.5f), new Vector2(-HandleW * 0.5f, TrackH * 0.5f));
            Image fill = Stretch(fillArea, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            fill.color = Cyan;
            fill.raycastTarget = false;

            RectTransform slideArea = Stretch(root, "Handle Slide Area", Vector2.zero, Vector2.one, new Vector2(HandleW * 0.5f, 6f), new Vector2(-HandleW * 0.5f, -6f));
            RectTransform handle = Stretch(slideArea, "Handle", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(-HandleW * 0.5f, 0f), new Vector2(HandleW * 0.5f, 0f));
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Ink;
            handleImage.raycastTarget = false;
            Image core = Stretch(handle, "Core", Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f)).gameObject.AddComponent<Image>();
            core.color = Orange;
            core.raycastTarget = false;

            var slider = root.gameObject.AddComponent<Slider>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = slider.colors;
            colors.normalColor = Ink;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Cyan;
            colors.selectedColor = Ink;
            colors.disabledColor = Disabled;
            slider.colors = colors;
            var nav = new Navigation();
            nav.mode = Navigation.Mode.None;
            slider.navigation = nav;
            return slider;
        }
    }

    /// <summary>
    /// A button of the app: a navy slab with a thin cyan border, lit when pointed at, orange when <see cref="Selected"/> (a chosen tab, a toggle
    /// that is on), dimmed when not <see cref="Interactable"/>. Takes the pointer events of the client's XR ray and its mouse alike; a second
    /// click within 0.15 s is ignored (a ray's double fire).
    /// </summary>
    public sealed class MpUiButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private Image _slab;
        private Image[] _edges;
        private TextMeshProUGUI _label;
        private Action _onClick;
        private bool _hover, _down, _selected, _interactable = true;
        private float _lastClick = -10f;

        public string Label { get { return _label != null ? _label.text : ""; } }
        public TextMeshProUGUI LabelText { get { return _label; } }

        public void Build(string label, float textSize, Action onClick, TextAlignmentOptions align)
        {
            _onClick = onClick;
            _slab = gameObject.AddComponent<Image>();
            _slab.raycastTarget = true;
            _edges = MpTabletUi.EdgeLines(transform, 2f, MpTabletUi.CyanDim);
            _label = MpTabletUi.Stretch(transform, "Label", Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-14f, 0f)).gameObject.AddComponent<TextMeshProUGUI>();
            MpTabletUi.Style(_label, label, textSize, MpTabletUi.Ink, align, true);
            _label.textWrappingMode = TextWrappingModes.NoWrap;

            // A caption wider than its button shrinks (down to 60 %) instead of being cut ("FURTH...").
            _label.enableAutoSizing = true;
            _label.fontSizeMax = textSize;
            _label.fontSizeMin = textSize * 0.6f;
            Refresh();
        }

        public bool Selected
        {
            get { return _selected; }
            set { if (_selected == value) return; _selected = value; Refresh(); }
        }

        public bool Interactable
        {
            get { return _interactable; }
            set { if (_interactable == value) return; _interactable = value; if (!value) { _hover = false; _down = false; } Refresh(); }
        }

        public void SetLabel(string text)
        {
            if (_label != null && _label.text != text) _label.text = text;
        }

        private void Refresh()
        {
            if (_slab == null) return;
            _slab.color = !_interactable ? new Color(0.02f, 0.04f, 0.07f, 0.6f) : _down ? MpTabletUi.PanelDown : _selected ? MpTabletUi.PanelOn : _hover ? MpTabletUi.PanelHover : MpTabletUi.Panel;
            Color edge = !_interactable ? new Color(1f, 1f, 1f, 0.05f) : _selected ? MpTabletUi.Orange : _hover ? MpTabletUi.Cyan : MpTabletUi.CyanDim;
            for (int i = 0; i < _edges.Length; i++) _edges[i].color = edge;
            _label.color = !_interactable ? MpTabletUi.Disabled : _selected ? MpTabletUi.Orange : _hover ? Color.white : MpTabletUi.Ink;
        }

        public void OnPointerEnter(PointerEventData e) { if (_interactable) { _hover = true; Refresh(); } }
        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { if (_interactable) { _down = true; Refresh(); } }
        public void OnPointerUp(PointerEventData e) { _down = false; Refresh(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (!_interactable || Time.unscaledTime - _lastClick < 0.15f) return;
            _lastClick = Time.unscaledTime;
            MpLog.Info("Tablet: '" + Label + "' pressed (" + (e == null ? "code" : e.pointerId < 0 ? "mouse" : "pointer " + e.pointerId) + ").");
            try
            {
                if (_onClick != null) _onClick();
            }
            catch (Exception ex)
            {
                MpLog.Error("Tablet: '" + Label + "' failed: " + ex);
            }
        }

        /// <summary>The click without a pointer (the lab).</summary>
        public void Press()
        {
            OnPointerClick(null);
        }
    }

    /// <summary>The app's home-screen icon, drawn here (nothing of the game): a navy slab with a thin cyan rule, an orange helmet and its green visor.</summary>
    public static class MpTabletIcon
    {
        public static Texture2D Paint(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.name = MpTabletLink.Owned + "IconTexture";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[n * n];
            Color slab = new Color(0.02f, 0.045f, 0.09f, 1f);
            Color cyan = MpTabletUi.Cyan;
            Color helmet = new Color(0.93f, 0.40f, 0.10f, 1f);
            Color rim = new Color(0.55f, 0.18f, 0.05f, 1f);
            Color visor = new Color(0.30f, 0.92f, 0.55f, 1f);
            Color glint = new Color(0.85f, 1f, 0.92f, 1f);
            float centre = (n - 1) * 0.5f;

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x - centre) / n, v = (y - centre) / n;     // -0.5 .. 0.5, v up
                    float outer = RoundBox(u, v, 0.47f, 0.47f, 0.15f);
                    Color c = slab;
                    c = Over(c, cyan, Cover(Mathf.Abs(outer + 0.03f) - 0.007f, n));                                   // the slab's thin inner rule
                    float r = Mathf.Sqrt(u * u + (v + 0.02f) * (v + 0.02f));
                    c = Over(c, rim, Cover(r - 0.30f, n));                                                           // the helmet
                    c = Over(c, helmet, Cover(r - 0.27f, n));
                    float shield = RoundBox(u, v + 0.01f, 0.20f, 0.085f, 0.06f);
                    c = Over(c, new Color(0.05f, 0.12f, 0.10f, 1f), Cover(shield - 0.012f, n));                      // the visor's frame
                    c = Over(c, visor, Cover(shield, n));                                                            // the visor
                    c = Over(c, glint, Cover(RoundBox(u + 0.09f, v - 0.03f, 0.05f, 0.012f, 0.012f), n));              // its glint
                    c.a = Cover(outer, n);
                    pixels[y * n + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Anti-aliased coverage of a distance field value (negative = inside) at n pixels across a unit.</summary>
        private static float Cover(float distance, int n)
        {
            return Mathf.Clamp01(0.5f - distance * n);
        }

        private static Color Over(Color under, Color over, float coverage)
        {
            return Color.Lerp(under, over, coverage);
        }

        private static float RoundBox(float x, float y, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(x) - halfW + radius, qy = Mathf.Abs(y) - halfH + radius;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }
    }
}
