using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace CallibriEmg
{
    // The overlay on top of GameScene during a session: status strip, a live
    // tension meter, therapist settings, End Session, and the "paused"
    // screen shown when the sensor signal is lost.
    //
    // The session manager creates it each time GameScene loads, so
    // GameScene itself needed no changes in the editor.
    public sealed class GameSessionHud : MonoBehaviour
    {
        private EmgSessionManager manager;
        private LevelMeter meter;
        private GameObject settingsPanel;
        private GameObject confirmPanel;
        private GameObject pausedOverlay;
        private Text pausedText;
        private Text roundText;
        private Text sensitivityLabel;
        private Text paceLabel;
        private Text speedLabel;
        private Text gapLabel;
        private Text spacingLabel;
        private Slider sensitivitySlider;
        private Slider paceSlider;
        private Slider speedSlider;
        private Slider gapSlider;
        private Slider spacingSlider;

        private void Start()
        {
            manager = EmgSessionManager.Instance;
            if (manager == null)
            {
                Destroy(gameObject);
                return;
            }

            Canvas canvas = UiKit.CreateCanvas("Session HUD", 10);
            canvas.transform.SetParent(transform, false);
            SessionStatusStrip.Create(canvas.transform);
            BuildTopButtons(canvas.transform);
            BuildMeter(canvas.transform);
            BuildSettings(canvas.transform);
            BuildConfirm(canvas.transform);
            BuildPaused(canvas.transform);
        }

        private void BuildTopButtons(Transform canvas)
        {
            RectTransform row = UiKit.Rect("Top Buttons", canvas);
            UiKit.Place(row, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -34f), new Vector2(240f, 30f));
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            UiKit.Button(row, "Settings", UiKit.Neutral, ToggleSettings, 30f, 14);
            UiKit.Button(row, "End Session", UiKit.Bad, () => confirmPanel.SetActive(true), 30f, 14);

            roundText = UiKit.Label(canvas, "", 14, Color.white, FontStyle.Bold, TextAnchor.UpperLeft);
            roundText.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            UiKit.Place(roundText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -36f), new Vector2(180f, 24f));
        }

        private void BuildMeter(Transform canvas)
        {
            Image card = UiKit.Panel("Meter Card", canvas, new Color(1f, 1f, 1f, 0.82f));
            card.raycastTarget = false;
            UiKit.Place(card.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(300f, 48f));
            VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.spacing = 4;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            UiKit.Label(card.transform, "Forehead tension (bird flaps at the dark line)", 11, UiKit.Muted);
            meter = LevelMeter.Create(card.transform, 16f);
        }

        private void BuildSettings(Transform canvas)
        {
            RectTransform card = UiKit.Column("Settings", canvas, 400f, 6, 16, UiKit.Card);
            UiKit.Label(card, "Therapist Settings", 22, UiKit.Ink, FontStyle.Bold);

            DifficultySettings pending = manager.PendingDifficulty;
            float k = manager.Calibration != null && manager.Calibration.valid ? manager.Calibration.thresholdFraction : EmgConfig.DefaultThresholdFraction;

            sensitivityLabel = AddSliderLabel(card);
            sensitivitySlider = UiKit.Slider(card, EmgConfig.MinThresholdFraction, EmgConfig.MaxThresholdFraction, k, value =>
            {
                manager.SetThresholdFraction(value);
                RefreshLabels();
            });

            UiKit.Label(card, "Changes below apply from the next round.", 12, UiKit.Muted, FontStyle.Italic);
            paceLabel = AddSliderLabel(card);
            paceSlider = UiKit.Slider(card, DifficultySettings.MinFlapSeconds, DifficultySettings.MaxFlapSeconds, pending.flapSeconds, value => OnDifficulty(() => pending.flapSeconds = value));
            speedLabel = AddSliderLabel(card);
            speedSlider = UiKit.Slider(card, DifficultySettings.MinPipeSpeed, DifficultySettings.MaxPipeSpeed, pending.pipeSpeed, value => OnDifficulty(() => pending.pipeSpeed = value));
            gapLabel = AddSliderLabel(card);
            gapSlider = UiKit.Slider(card, DifficultySettings.MinGapSize, DifficultySettings.MaxGapSize, pending.gapSize, value => OnDifficulty(() => pending.gapSize = value));
            spacingLabel = AddSliderLabel(card);
            spacingSlider = UiKit.Slider(card, DifficultySettings.MinSpawnInterval, DifficultySettings.MaxSpawnInterval, pending.spawnInterval, value => OnDifficulty(() => pending.spawnInterval = value));

            RectTransform buttons = UiKit.Row(card, 8, 40f);
            UiKit.Button(buttons, "Reset to Normal", UiKit.Neutral, ResetDifficulty, 40f);
            UiKit.Button(buttons, "Close", UiKit.Good, ToggleSettings, 40f);

            settingsPanel = card.gameObject;
            settingsPanel.SetActive(false);
            RefreshLabels();
        }

        private void BuildConfirm(Transform canvas)
        {
            Image dim = UiKit.FullScreenOverlay(canvas, UiKit.Dim);
            RectTransform card = UiKit.Column("Confirm", dim.transform, 360f, 12, 20, UiKit.Card);
            UiKit.Label(card, "End the session?", 22, UiKit.Ink, FontStyle.Bold);
            UiKit.Label(card, "Recording stops and the summary is saved.", 15, UiKit.Muted);
            RectTransform buttons = UiKit.Row(card, 8, 44f);
            UiKit.Button(buttons, "Keep playing", UiKit.Neutral, () => confirmPanel.SetActive(false), 44f);
            UiKit.Button(buttons, "End Session", UiKit.Bad, () => manager.EndSession(), 44f);
            confirmPanel = dim.gameObject;
            confirmPanel.SetActive(false);
        }

        private void BuildPaused(Transform canvas)
        {
            Image dim = UiKit.FullScreenOverlay(canvas, UiKit.Dim);
            dim.raycastTarget = false;
            RectTransform card = UiKit.Column("Paused", dim.transform, 360f, 10, 20, UiKit.Card);
            UiKit.Label(card, "Game paused", 24, UiKit.Bad, FontStyle.Bold);
            pausedText = UiKit.Label(card, "", 16, UiKit.Ink);
            UiKit.Label(card, "The game continues automatically when the signal is back.", 13, UiKit.Muted, FontStyle.Italic);
            pausedOverlay = dim.gameObject;
            pausedOverlay.SetActive(false);
            // Keep the overlay below the confirm dialog, so End Session still
            // works while the sensor is disconnected.
            pausedOverlay.transform.SetSiblingIndex(confirmPanel.transform.GetSiblingIndex());
        }

        private Text AddSliderLabel(Transform parent)
        {
            return UiKit.Label(parent, "", 14, UiKit.Ink, FontStyle.Normal, TextAnchor.MiddleLeft);
        }

        private void OnDifficulty(System.Action change)
        {
            change();
            manager.NotifyDifficultyChanged();
            RefreshLabels();
        }

        private void ResetDifficulty()
        {
            manager.PendingDifficulty.ResetToNormal();
            DifficultySettings pending = manager.PendingDifficulty;
            paceSlider.SetValueWithoutNotify(pending.flapSeconds);
            speedSlider.SetValueWithoutNotify(pending.pipeSpeed);
            gapSlider.SetValueWithoutNotify(pending.gapSize);
            spacingSlider.SetValueWithoutNotify(pending.spawnInterval);
            manager.NotifyDifficultyChanged();
            RefreshLabels();
        }

        private void ToggleSettings()
        {
            settingsPanel.SetActive(!settingsPanel.activeSelf);
            if (settingsPanel.activeSelf && manager.Calibration != null)
            {
                sensitivitySlider.SetValueWithoutNotify(manager.Calibration.thresholdFraction);
                RefreshLabels();
            }
        }

        private void RefreshLabels()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            DifficultySettings pending = manager.PendingDifficulty;
            if (manager.Calibration != null && manager.Calibration.valid)
            {
                // When the resting-noise floor lifted the trigger, the slider
                // value is not what is in effect, so the label says so.
                sensitivityLabel.text = manager.Calibration.triggerRaisedByFloor
                    ? string.Format(inv, "Squeeze needed: <b>{0:0}%</b> of strongest, raised to <b>{1:0.0} µV</b> to stay above resting noise. Applies now.", manager.Calibration.thresholdFraction * 100f, manager.Calibration.onUv)
                    : string.Format(inv, "Squeeze needed: <b>{0:0}%</b> of strongest ({1:0.0} µV). Applies now.", manager.Calibration.thresholdFraction * 100f, manager.Calibration.onUv);
            }
            else
            {
                sensitivityLabel.text = "Squeeze needed: calibrate first";
            }
            paceLabel.text = string.Format(inv, "Flap pace: one squeeze keeps the bird up <b>{0:0.0} s</b> (about {1:0} squeezes a minute)", pending.flapSeconds, pending.SqueezesPerMinute);
            speedLabel.text = string.Format(inv, "Pipe speed: <b>{0:0.00}</b> (normal {1:0.00})", pending.pipeSpeed, DifficultySettings.NormalPipeSpeed);
            gapLabel.text = string.Format(inv, "Gap between pipes: <b>{0:0.00}</b> (normal {1:0.00})", pending.gapSize, DifficultySettings.NormalGapSize);
            spacingLabel.text = string.Format(inv, "New pipe every: <b>{0:0.0} s</b> (normal {1:0.0})", pending.spawnInterval, DifficultySettings.NormalSpawnInterval);
        }

        private void Update()
        {
            if (manager == null)
            {
                return;
            }

            CalibrationResult calibration = manager.Calibration;
            float level = (float)manager.CurrentEnvelopeUv;
            if (calibration != null && calibration.valid)
            {
                meter.SetValues(level, calibration.maxUv * 1.3f, calibration.onUv, calibration.offUv);
            }
            else
            {
                meter.SetValues(level, 60f, -1f, -1f);
            }

            roundText.text = "Round " + Mathf.Max(1, manager.RoundIndex + (manager.Phase == SessionPhase.Game && !RoundRunning() ? 1 : 0));

            pausedOverlay.SetActive(manager.IsGamePaused);
            if (manager.IsGamePaused)
            {
                pausedText.text = manager.DescribeInterruption();
            }
        }

        // RoundIndex is incremented when a round starts, so before the first
        // flap of a new round the label must show the round that's about to
        // begin.
        private bool RoundRunning()
        {
            int completed = manager.Rounds.Count;
            return manager.RoundIndex > completed;
        }
    }
}
