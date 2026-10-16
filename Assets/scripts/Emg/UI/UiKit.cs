using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallibriEmg
{
    // Builds Unity UI (uGUI) elements in code.
    //
    // This project's scenes are saved in binary, so a layout built in the
    // editor could not be read or reviewed as text. Building screens from
    // code keeps every layout readable and diffable, and each scene only
    // needs a camera and one screen component.
    public static class UiKit
    {
        public const float ReferenceWidth = 450f;
        public const float ReferenceHeight = 800f;

        public static readonly Color Ink = new Color32(0x22, 0x2B, 0x38, 0xFF);
        public static readonly Color Muted = new Color32(0x5F, 0x6B, 0x7A, 0xFF);
        public static readonly Color Card = new Color32(0xFF, 0xFB, 0xF0, 0xF2);
        public static readonly Color Accent = new Color32(0xF0, 0x8A, 0x24, 0xFF);
        public static readonly Color Good = new Color32(0x2E, 0x9E, 0x5B, 0xFF);
        public static readonly Color Warn = new Color32(0xD9, 0x8E, 0x0B, 0xFF);
        public static readonly Color Bad = new Color32(0xD1, 0x43, 0x3A, 0xFF);
        public static readonly Color Neutral = new Color32(0x4A, 0x6F, 0x9B, 0xFF);
        public static readonly Color Track = new Color32(0xDD, 0xE2, 0xE8, 0xFF);
        public static readonly Color Sky = new Color32(0x4E, 0xC0, 0xCA, 0xFF);
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);

        private static Font font;
        private static Sprite circle;

        public static Font Font
        {
            get
            {
                if (font == null)
                {
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            GameObject host = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Scale by height only. The app window is portrait (450 x 800),
            // where this changes nothing, but Unity's Game view is usually
            // landscape, and blending in the width made tall cards spill off
            // the top and bottom of the screen there.
            scaler.matchWidthOrHeight = 1f;

            EnsureEventSystem();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }

        // Buttons keep keyboard focus after a click, and the legacy "Submit"
        // input includes Space. Without this, pressing Space for a simulated
        // flex would click whatever button was pressed last.
        public static void ClearSelection()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            GameObject host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            return (RectTransform)host.transform;
        }

        public static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static Image Panel(string name, Transform parent, Color color)
        {
            RectTransform rect = Rect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static LayoutElement SetHeight(Component target, float height)
        {
            LayoutElement element = target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = target.gameObject.AddComponent<LayoutElement>();
            }
            element.preferredHeight = height;
            element.minHeight = height;
            return element;
        }

        // A card in the middle of the screen that stacks its children from
        // the top and grows to fit them.
        public static RectTransform Column(string name, Transform parent, float width, int spacing, int padding, Color background)
        {
            Image card = Panel(name, parent, background);
            RectTransform rect = card.rectTransform;
            Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 0f));
            VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        public static RectTransform Row(Transform parent, int spacing, float height)
        {
            RectTransform rect = Rect("Row", parent);
            HorizontalLayoutGroup layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            SetHeight(rect, height);
            return rect;
        }

        public static RectTransform Spacer(Transform parent, float height)
        {
            RectTransform rect = Rect("Spacer", parent);
            SetHeight(rect, height);
            return rect;
        }

        public static Text Label(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            RectTransform rect = Rect("Label", parent);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.color = color;
            label.fontStyle = style;
            label.alignment = anchor;
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static void Header(Transform parent, string step, string title)
        {
            Label(parent, step, 13, Muted, FontStyle.Bold);
            Label(parent, title, 26, Ink, FontStyle.Bold);
        }

        public static Button Button(Transform parent, string text, Color color, UnityAction onClick, float height = 46f, int fontSize = 18)
        {
            Image background = Panel("Button", parent, color);
            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.55f);
            button.colors = colors;

            Text label = Label(background.transform, text, fontSize, Color.white, FontStyle.Bold);
            Stretch(label.rectTransform, 8f, 8f, 0f, 0f);
            SetHeight(background, height);

            button.onClick.AddListener(() =>
            {
                ClearSelection();
                onClick();
            });
            return button;
        }

        public static void SetButtonText(Button button, string text)
        {
            button.GetComponentInChildren<Text>().text = text;
        }

        public static InputField Input(Transform parent, string placeholder, bool multiline)
        {
            float height = multiline ? 84f : 40f;
            Image background = Panel("Input", parent, Color.white);
            background.gameObject.AddComponent<Outline>().effectColor = Track;
            SetHeight(background, height);

            InputField field = background.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            TextAnchor anchor = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;

            Text text = Label(background.transform, "", 17, Ink, FontStyle.Normal, anchor);
            text.supportRichText = false;
            text.horizontalOverflow = multiline ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(text.rectTransform, 10f, 10f, 6f, 6f);

            Text hint = Label(background.transform, placeholder, 17, Muted, FontStyle.Italic, anchor);
            hint.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(hint.rectTransform, 10f, 10f, 6f, 6f);

            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            return field;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, UnityAction<float> onChanged)
        {
            RectTransform root = Rect("Slider", parent);
            SetHeight(root, 26f);
            Slider slider = root.gameObject.AddComponent<Slider>();

            Image track = Panel("Track", root, Track);
            Stretch(track.rectTransform, 0f, 0f, 9f, 9f);
            RectTransform fillArea = Rect("Fill Area", root);
            Stretch(fillArea, 0f, 0f, 9f, 9f);
            Image fill = Panel("Fill", fillArea, Accent);
            fill.rectTransform.sizeDelta = Vector2.zero;

            RectTransform handleArea = Rect("Handle Area", root);
            Stretch(handleArea, 10f, 10f, 0f, 0f);
            Image handle = Panel("Handle", handleArea, Color.white);
            handle.sprite = CircleSprite();
            handle.rectTransform.sizeDelta = new Vector2(22f, 0f);
            handle.gameObject.AddComponent<Outline>().effectColor = Muted;

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.minValue = min;
            slider.maxValue = max;
            // Value is set before the listener is attached so building the
            // panel doesn't log a fake "settings changed" event.
            slider.value = value;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        public static Text ScrollingText(Transform parent, float height, int fontSize)
        {
            RectTransform content = ScrollList(parent, height, 0);
            Text text = Label(content, "", fontSize, Ink, FontStyle.Normal, TextAnchor.UpperLeft);
            return text;
        }

        // A white box that scrolls vertically. Returns the container to add
        // rows to; rows stack top to bottom and the box scrolls once they
        // are taller than it.
        public static RectTransform ScrollList(Transform parent, float height, int spacing)
        {
            Image background = Panel("Scroll", parent, Color.white);
            LayoutElement element = SetHeight(background, height);
            element.flexibleHeight = 1f;
            ScrollRect scroll = background.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            RectTransform viewport = Rect("Viewport", background.transform);
            Stretch(viewport, 10f, 10f, 10f, 10f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static Image FullScreenOverlay(Transform parent, Color color)
        {
            Image overlay = Panel("Overlay", parent, color);
            Stretch(overlay.rectTransform);
            return overlay;
        }

        // A smooth white circle generated once at runtime, used for the
        // breathing guide, slider handles and status dots. Generating it
        // avoids depending on editor-only built-in UI sprites, which are not
        // available in a player build.
        public static Sprite CircleSprite()
        {
            if (circle != null)
            {
                return circle;
            }
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "GeneratedCircle",
            };
            Color32[] pixels = new Color32[size * size];
            float radius = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            circle = Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return circle;
        }

        // Plays a cue at the camera. AudioSource.PlayClipAtPoint makes a 3D
        // sound, so playing it at the world origin (10 units from the
        // camera) came out at about a tenth of full volume. The rest test
        // is done with the eyes closed, so its chime has to be clearly heard.
        public static void PlayCue(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }
            Camera camera = Camera.main;
            AudioSource.PlayClipAtPoint(clip, camera != null ? camera.transform.position : Vector3.zero);
        }

        public static string FormatClock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }
    }
}
