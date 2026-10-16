using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CallibriEmg
{
    // Step 4 (patient): learn how strong this patient's squeeze is, so the
    // game can tell a deliberate squeeze from resting.
    //
    // Sequence: rest 5 s, then 3 × (squeeze 2 s, rest 3 s). Only the rest
    // and squeeze windows are captured; the rest periods between squeezes
    // just give the muscle time to let go.
    public sealed class CalibrationScreen : MonoBehaviour
    {
        private enum Stage
        {
            Instructions,
            Running,
            Interrupted,
            Result,
        }

        private enum StepKind
        {
            OpeningRest,
            Squeeze,
            Recover,
        }

        private struct Step
        {
            public StepKind Kind;
            public float Seconds;
        }

        private EmgSessionManager manager;
        private readonly List<Step> steps = new List<Step>();
        private readonly List<float> restSamples = new List<float>();
        private readonly List<float> squeezeSamples = new List<float>();
        private Stage stage;
        private int stepIndex;
        private float stepElapsed;
        private bool capturing;
        private CalibrationResult result;
        private FlapDetector practiceDetector;
        private float flashTimer;

        private GameObject instructionsCard;
        private GameObject runningCard;
        private GameObject interruptedCard;
        private GameObject resultCard;
        private Text promptText;
        private Text stepCountdown;
        private Image progressFill;
        private LevelMeter runningMeter;
        private Text interruptedText;
        private Text resultTitle;
        private Text resultDetails;
        private GameObject practiceGroup;
        private LevelMeter practiceMeter;
        private Image flapLight;
        private Text flapLightText;
        private Button startGameButton;

        private void Start()
        {
            manager = EmgSessionManager.Instance;
            if (manager == null || manager.Source == null)
            {
                SceneManager.LoadScene(EmgSessionManager.ConnectSceneName);
                return;
            }
            manager.SetPhase(SessionPhase.Calibration);

            steps.Add(new Step { Kind = StepKind.OpeningRest, Seconds = EmgConfig.CalibrationRestSeconds });
            for (int i = 0; i < EmgConfig.CalibrationFlexCount; i++)
            {
                steps.Add(new Step { Kind = StepKind.Squeeze, Seconds = EmgConfig.CalibrationFlexSeconds });
                steps.Add(new Step { Kind = StepKind.Recover, Seconds = EmgConfig.CalibrationBetweenRestSeconds });
            }

            BuildUi();
            ShowStage(Stage.Instructions);
        }

        private void BuildUi()
        {
            Canvas canvas = UiKit.CreateCanvas("Calibration Canvas", 0);

            RectTransform intro = UiKit.Column("Instructions", canvas.transform, 400f, 12, 20, UiKit.Card);
            UiKit.Header(intro, "STEP 5 OF 6 · PATIENT", "Calibration (20 seconds)");
            UiKit.Label(intro, "The game needs to learn how strong your squeeze is.\n\nWhen the screen says <b>SQUEEZE YOUR MUSCLES</b>, squeeze firmly and hold.\nWhen it says <b>RELAX</b>, let your muscles go loose.", 16, UiKit.Ink);
            UiKit.Button(intro, "Start", UiKit.Good, StartRun);
            instructionsCard = intro.gameObject;

            RectTransform running = UiKit.Column("Running", canvas.transform, 400f, 12, 20, UiKit.Card);
            promptText = UiKit.Label(running, "", 32, UiKit.Good, FontStyle.Bold);
            stepCountdown = UiKit.Label(running, "", 18, UiKit.Muted);
            Image progressTrack = UiKit.Panel("Progress", running, UiKit.Track);
            UiKit.SetHeight(progressTrack, 8f);
            progressFill = UiKit.Panel("Progress Fill", progressTrack.transform, UiKit.Neutral);
            progressFill.rectTransform.anchorMin = Vector2.zero;
            progressFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            progressFill.rectTransform.offsetMin = Vector2.zero;
            progressFill.rectTransform.offsetMax = Vector2.zero;
            UiKit.Label(running, "Muscle tension", 12, UiKit.Muted);
            runningMeter = LevelMeter.Create(running, 22f);
            runningCard = running.gameObject;

            RectTransform paused = UiKit.Column("Interrupted", canvas.transform, 380f, 12, 20, UiKit.Card);
            UiKit.Label(paused, "Calibration stopped", 24, UiKit.Bad, FontStyle.Bold);
            interruptedText = UiKit.Label(paused, "", 16, UiKit.Ink);
            UiKit.Button(paused, "Try again", UiKit.Accent, StartRun);
            interruptedCard = paused.gameObject;

            RectTransform done = UiKit.Column("Result", canvas.transform, 400f, 10, 20, UiKit.Card);
            resultTitle = UiKit.Label(done, "", 26, UiKit.Good, FontStyle.Bold);
            resultDetails = UiKit.Label(done, "", 14, UiKit.Ink);

            RectTransform practice = UiKit.Rect("Practice", done);
            VerticalLayoutGroup practiceLayout = practice.gameObject.AddComponent<VerticalLayoutGroup>();
            practiceLayout.spacing = 8;
            practiceLayout.childControlWidth = true;
            practiceLayout.childControlHeight = true;
            practiceLayout.childForceExpandHeight = false;
            UiKit.Label(practice, "Try a few squeezes. The light should flash once per squeeze.", 14, UiKit.Muted, FontStyle.Italic);
            practiceMeter = LevelMeter.Create(practice, 22f);
            flapLight = UiKit.Panel("Flap Light", practice, UiKit.Track);
            UiKit.SetHeight(flapLight, 40f);
            flapLightText = UiKit.Label(flapLight.transform, "FLAP", 20, Color.white, FontStyle.Bold);
            UiKit.Stretch(flapLightText.rectTransform);
            practiceGroup = practice.gameObject;

            RectTransform buttons = UiKit.Row(done, 8, 46f);
            UiKit.Button(buttons, "Redo calibration", UiKit.Neutral, StartRun);
            startGameButton = UiKit.Button(buttons, "Start game", UiKit.Good, StartGame);
            resultCard = done.gameObject;

            SessionStatusStrip.Create(canvas.transform);
        }

        private void ShowStage(Stage next)
        {
            stage = next;
            instructionsCard.SetActive(next == Stage.Instructions);
            runningCard.SetActive(next == Stage.Running);
            interruptedCard.SetActive(next == Stage.Interrupted);
            resultCard.SetActive(next == Stage.Result);
        }

        private void StartRun()
        {
            restSamples.Clear();
            squeezeSamples.Clear();
            capturing = false;
            if (manager.IsSignalInterrupted)
            {
                interruptedText.text = manager.DescribeInterruption();
                ShowStage(Stage.Interrupted);
                return;
            }
            stepIndex = 0;
            stepElapsed = 0f;
            manager.LogEvent("calibration_start");
            ShowStage(Stage.Running);
        }

        private void Update()
        {
            if (stage == Stage.Running)
            {
                UpdateRunning();
            }
            else if (stage == Stage.Result && practiceDetector != null)
            {
                UpdatePractice();
            }
        }

        private void UpdateRunning()
        {
            if (manager.IsSignalInterrupted)
            {
                if (capturing)
                {
                    manager.CancelCapture();
                    capturing = false;
                }
                manager.LogEvent("calibration_interrupted", "", manager.DescribeInterruption());
                interruptedText.text = manager.DescribeInterruption() + "\n\nFix the sensor, then press Try again.";
                ShowStage(Stage.Interrupted);
                return;
            }

            Step step = steps[stepIndex];
            stepElapsed += Time.deltaTime;

            // The opening rest skips its first second: the patient has only
            // just pressed Start and is still settling.
            float captureFrom = step.Kind == StepKind.OpeningRest ? EmgConfig.CalibrationRestSkipSeconds : 0f;
            if (!capturing && step.Kind != StepKind.Recover && stepElapsed >= captureFrom)
            {
                manager.BeginCapture();
                capturing = true;
            }

            bool squeeze = step.Kind == StepKind.Squeeze;
            promptText.text = squeeze ? "SQUEEZE YOUR MUSCLES" : "RELAX";
            promptText.color = squeeze ? UiKit.Accent : UiKit.Good;
            stepCountdown.text = Mathf.CeilToInt(Mathf.Max(0f, step.Seconds - stepElapsed)) + " s";

            float total = 0f;
            float done = 0f;
            for (int i = 0; i < steps.Count; i++)
            {
                total += steps[i].Seconds;
                if (i < stepIndex)
                {
                    done += steps[i].Seconds;
                }
            }
            done += Mathf.Min(stepElapsed, step.Seconds);
            progressFill.rectTransform.anchorMax = new Vector2(done / total, 1f);

            float level = (float)manager.CurrentEnvelopeUv;
            runningMeter.SetValues(level, Mathf.Max(60f, level * 1.3f), -1f, -1f);

            if (stepElapsed < step.Seconds)
            {
                return;
            }

            if (capturing)
            {
                List<float> captured = manager.EndCapture();
                capturing = false;
                (step.Kind == StepKind.OpeningRest ? restSamples : squeezeSamples).AddRange(captured);
            }

            stepIndex++;
            stepElapsed = 0f;
            if (stepIndex >= steps.Count)
            {
                Finish();
            }
        }

        private void Finish()
        {
            float k = manager.Calibration != null && manager.Calibration.valid ? manager.Calibration.thresholdFraction : EmgConfig.DefaultThresholdFraction;
            result = CalibrationCalculator.Compute(restSamples, squeezeSamples, k);
            manager.LogEvent("calibration_end", "", result.valid ? "valid" : result.message);

            if (!result.valid)
            {
                resultTitle.text = "Let's try that again";
                resultTitle.color = UiKit.Bad;
                resultDetails.text = result.message;
                practiceGroup.SetActive(false);
                startGameButton.interactable = manager.Calibration != null && manager.Calibration.valid;
                practiceDetector = null;
                ShowStage(Stage.Result);
                return;
            }

            manager.SetCalibration(result);
            resultTitle.text = "Calibration complete";
            resultTitle.color = UiKit.Good;
            resultDetails.text = string.Format("Resting: {0:0.0} µV   ·   Strongest squeeze: {1:0.0} µV\nThe bird flaps when a squeeze stays above {2:0.0} µV for {3} ms.", result.restUv, result.maxUv, result.onUv, EmgConfig.MinSqueezeMs)
                + (result.triggerRaisedByFloor ? "\n(Raised to stay above the resting signal's normal wobble.)" : "");
            practiceGroup.SetActive(true);
            startGameButton.interactable = true;
            practiceDetector = manager.CreateFlapDetector(result);
            ShowStage(Stage.Result);
        }

        // Practice uses its own detector, fed once per frame, so trying it
        // here doesn't produce game flaps or affect the real detector.
        private void UpdatePractice()
        {
            float level = (float)manager.CurrentEnvelopeUv;
            practiceMeter.SetValues(level, result.maxUv * 1.3f, result.onUv, result.offUv);
            if (practiceDetector.Process(level, manager.SampleCount))
            {
                flashTimer = 0.3f;
            }
            flashTimer -= Time.deltaTime;
            flapLight.color = flashTimer > 0f ? UiKit.Good : UiKit.Track;
            flapLightText.color = flashTimer > 0f ? Color.white : UiKit.Muted;
        }

        private void StartGame()
        {
            if (manager.Calibration == null || !manager.Calibration.valid)
            {
                return;
            }
            manager.SetPhase(SessionPhase.Game);
            SceneManager.LoadScene(EmgSessionManager.GameSceneName);
        }
    }
}
