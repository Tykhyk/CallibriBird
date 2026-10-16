using UnityEngine;
using UnityEngine.UI;

namespace CallibriEmg
{
    // A horizontal bar showing the current muscle tension, with optional
    // markers for the trigger and release levels.
    public sealed class LevelMeter : MonoBehaviour
    {
        private RectTransform fill;
        private Image fillImage;
        private RectTransform onMarker;
        private RectTransform offMarker;

        public static LevelMeter Create(Transform parent, float height)
        {
            Image background = UiKit.Panel("Level Meter", parent, UiKit.Track);
            UiKit.SetHeight(background, height);
            LevelMeter meter = background.gameObject.AddComponent<LevelMeter>();

            meter.fillImage = UiKit.Panel("Fill", background.transform, UiKit.Accent);
            meter.fill = meter.fillImage.rectTransform;
            meter.fill.anchorMin = Vector2.zero;
            meter.fill.anchorMax = new Vector2(0f, 1f);
            meter.fill.offsetMin = Vector2.zero;
            meter.fill.offsetMax = Vector2.zero;

            meter.offMarker = CreateMarker(background.transform, UiKit.Neutral);
            meter.onMarker = CreateMarker(background.transform, UiKit.Ink);
            return meter;
        }

        // scaleMax is the value that fills the whole bar. Pass a negative
        // marker value to hide that marker.
        public void SetValues(float level, float scaleMax, float onLevel, float offLevel)
        {
            float max = Mathf.Max(scaleMax, 1f);
            fill.anchorMax = new Vector2(Mathf.Clamp01(level / max), 1f);
            fillImage.color = onLevel >= 0f && level >= onLevel ? UiKit.Good : UiKit.Accent;
            PlaceMarker(onMarker, onLevel, max);
            PlaceMarker(offMarker, offLevel, max);
        }

        private static RectTransform CreateMarker(Transform parent, Color color)
        {
            RectTransform marker = UiKit.Panel("Marker", parent, color).rectTransform;
            marker.anchorMin = new Vector2(0f, 0f);
            marker.anchorMax = new Vector2(0f, 1f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(3f, 6f);
            marker.gameObject.SetActive(false);
            return marker;
        }

        private static void PlaceMarker(RectTransform marker, float value, float max)
        {
            bool visible = value >= 0f;
            marker.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }
            float x = Mathf.Clamp01(value / max);
            marker.anchorMin = new Vector2(x, 0f);
            marker.anchorMax = new Vector2(x, 1f);
            marker.anchoredPosition = Vector2.zero;
        }
    }

    // A scrolling bar graph of the last 3 seconds of muscle tension, so the
    // therapist can see the signal is alive while checking the electrodes.
    public sealed class EnvelopeGraph : MonoBehaviour
    {
        private const int Height = 90;
        private const float MinScaleUv = 20f;

        private static readonly Color32 Background = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Color32 Bar = new Color32(0xF0, 0x8A, 0x24, 0xFF);
        private static readonly Color32 Grid = new Color32(0xE6, 0xEA, 0xEF, 0xFF);

        private Texture2D texture;
        private Color32[] pixels;
        private Text scaleLabel;
        private float scale = MinScaleUv;

        public static EnvelopeGraph Create(Transform parent)
        {
            RectTransform root = UiKit.Rect("Envelope Graph", parent);
            UiKit.SetHeight(root, Height + 18f);
            VerticalLayoutGroup layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            EnvelopeGraph graph = root.gameObject.AddComponent<EnvelopeGraph>();
            RawImage image = UiKit.Rect("Plot", root).gameObject.AddComponent<RawImage>();
            UiKit.SetHeight(image, Height);
            graph.scaleLabel = UiKit.Label(root, "", 12, UiKit.Muted, FontStyle.Normal, TextAnchor.MiddleRight);
            UiKit.SetHeight(graph.scaleLabel, 18f);

            EnvelopeHistory history = EmgSessionManager.Instance != null ? EmgSessionManager.Instance.History : null;
            int width = history != null ? history.Capacity : 300;
            graph.texture = new Texture2D(width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "EnvelopeGraph",
            };
            graph.pixels = new Color32[width * Height];
            image.texture = graph.texture;
            return graph;
        }

        private void Update()
        {
            EmgSessionManager manager = EmgSessionManager.Instance;
            if (manager == null)
            {
                return;
            }
            EnvelopeHistory history = manager.History;
            int width = texture.width;

            // The vertical scale follows the recent peak but changes slowly,
            // so the graph doesn't jump every time a spike enters or leaves.
            float target = Mathf.Max(MinScaleUv, history.Max() * 1.15f);
            scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f));

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Background;
            }
            for (int x = 0; x < width; x++)
            {
                pixels[(Height / 2) * width + x] = Grid;
            }
            for (int x = 0; x < width && x < history.Capacity; x++)
            {
                int barHeight = Mathf.Clamp(Mathf.RoundToInt(history.Get(x) / scale * Height), 0, Height);
                for (int y = 0; y < barHeight; y++)
                {
                    pixels[y * width + x] = Bar;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            scaleLabel.text = "top of graph: " + scale.ToString("0") + " µV · last 3 s";
        }

        private void OnDestroy()
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }

    // The thin bar across the top of every patient screen: recording time,
    // electrode contact and battery, so problems are visible at a glance.
    public sealed class SessionStatusStrip : MonoBehaviour
    {
        private Text leftText;
        private Text centreText;
        private Text rightText;
        private Image dot;
        private float refreshTimer;

        public static SessionStatusStrip Create(Transform canvas)
        {
            Image bar = UiKit.Panel("Status Strip", canvas, new Color(0.13f, 0.17f, 0.22f, 0.88f));
            bar.raycastTarget = false;
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 28f);
            rect.anchoredPosition = Vector2.zero;

            SessionStatusStrip strip = bar.gameObject.AddComponent<SessionStatusStrip>();
            strip.dot = UiKit.Panel("Rec Dot", bar.transform, UiKit.Bad);
            strip.dot.sprite = UiKit.CircleSprite();
            strip.dot.raycastTarget = false;
            UiKit.Place(strip.dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(10f, 10f));

            strip.leftText = UiKit.Label(bar.transform, "", 13, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
            UiKit.Stretch(strip.leftText.rectTransform, 26f, 0f, 0f, 0f);
            strip.centreText = UiKit.Label(bar.transform, "", 13, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
            UiKit.Stretch(strip.centreText.rectTransform, 0f, 0f, 0f, 0f);
            strip.rightText = UiKit.Label(bar.transform, "", 13, Color.white, FontStyle.Normal, TextAnchor.MiddleRight);
            UiKit.Stretch(strip.rightText.rectTransform, 0f, 10f, 0f, 0f);
            strip.Refresh();
            return strip;
        }

        private void Update()
        {
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 0.25f;
                Refresh();
            }
        }

        private void Refresh()
        {
            EmgSessionManager manager = EmgSessionManager.Instance;
            if (manager == null || manager.Source == null)
            {
                leftText.text = "No sensor";
                centreText.text = "";
                rightText.text = "";
                dot.enabled = false;
                return;
            }

            IEmgSource source = manager.Source;
            bool blinkOn = Mathf.FloorToInt(Time.unscaledTime * 2f) % 2 == 0;
            dot.enabled = manager.IsRecording && blinkOn;
            leftText.text = manager.IsRecording ? "REC " + UiKit.FormatClock(manager.SessionSeconds) : "Not recording";

            if (manager.IsSignalInterrupted)
            {
                centreText.text = source.State == EmgSourceState.Streaming ? "Electrode detached" : "Signal lost";
                centreText.color = UiKit.Bad;
            }
            else if (source.Contact == ElectrodeContact.HighResistance)
            {
                centreText.text = "Weak electrode contact";
                centreText.color = UiKit.Warn;
            }
            else
            {
                centreText.text = source.IsSimulated ? "Simulated sensor" : "Electrodes OK";
                centreText.color = Color.white;
            }

            int battery = source.BatteryPercent;
            rightText.text = battery >= 0 ? "Battery " + battery + "%" : "";
            rightText.color = battery >= 0 && battery < EmgConfig.LowBatteryPercent ? UiKit.Bad : Color.white;
        }
    }
}
