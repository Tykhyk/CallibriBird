using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CallibriEmg
{
    // Step 3 (patient): 30 seconds of guided relaxation that measures how
    // well the patient can relax the forehead.
    //
    // The screen deliberately shows no muscle reading. Seeing their own
    // tension would change it, and the goal is to measure natural ability.
    public sealed class MeditationScreen : MonoBehaviour
    {
        private const float InhaleSeconds = 4f;
        private const float ExhaleSeconds = 6f;
        private const float SmallCircle = 0.55f;
        private const float RestartDelaySeconds = 2f;

        private enum Stage
        {
            Instructions,
            Running,
            Interrupted,
            Done,
        }

        // Assigned in the scene so the chime ships with the build.
        [SerializeField] private AudioClip chime;

        private EmgSessionManager manager;
        private Stage stage = Stage.Instructions;
        private float elapsed;
        private float restoredFor;

        private GameObject instructionsCard;
        private GameObject runningGroup;
        private GameObject interruptedCard;
        private GameObject doneCard;
        private RectTransform circle;
        private Text breathText;
        private Text countdownText;
        private Text interruptedText;
        private Text doneDetails;

        private void Start()
        {
            manager = EmgSessionManager.Instance;
            if (manager == null || manager.Source == null)
            {
                SceneManager.LoadScene(EmgSessionManager.ConnectSceneName);
                return;
            }
            manager.SetPhase(SessionPhase.Meditation);
            BuildUi();
            ShowStage(Stage.Instructions);
        }

        private void BuildUi()
        {
            Canvas canvas = UiKit.CreateCanvas("Meditation Canvas", 0);

            RectTransform card = UiKit.Column("Instructions", canvas.transform, 400f, 12, 20, UiKit.Card);
            UiKit.Header(card, "STEP 3 OF 6 · PATIENT", "Relaxation (30 seconds)");
            UiKit.Label(card, "Sit comfortably.\nRelax your forehead and jaw.\nBreathe slowly with the circle.\nKeep your eyes soft.\n\nA chime will sound at the end.", 17, UiKit.Ink);
            UiKit.Button(card, "Start", UiKit.Good, StartRun);
            instructionsCard = card.gameObject;

            RectTransform running = UiKit.Rect("Running", canvas.transform);
            UiKit.Stretch(running);
            Image ring = UiKit.Panel("Breathing Circle", running, new Color(1f, 1f, 1f, 0.85f));
            ring.sprite = UiKit.CircleSprite();
            ring.raycastTarget = false;
            circle = ring.rectTransform;
            UiKit.Place(circle, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 260f));
            breathText = UiKit.Label(running, "", 26, UiKit.Ink, FontStyle.Bold);
            UiKit.Place(breathText.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 60f));
            countdownText = UiKit.Label(running, "", 30, Color.white, FontStyle.Bold);
            countdownText.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.5f);
            UiKit.Place(countdownText.rectTransform, new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 50f));
            runningGroup = running.gameObject;

            RectTransform paused = UiKit.Column("Interrupted", canvas.transform, 380f, 10, 20, UiKit.Card);
            UiKit.Label(paused, "Paused", 24, UiKit.Bad, FontStyle.Bold);
            interruptedText = UiKit.Label(paused, "", 16, UiKit.Ink);
            interruptedCard = paused.gameObject;

            RectTransform done = UiKit.Column("Done", canvas.transform, 400f, 12, 20, UiKit.Card);
            UiKit.Label(done, "Well done", 28, UiKit.Good, FontStyle.Bold);
            UiKit.Label(done, "Relaxation complete.", 18, UiKit.Ink);
            doneDetails = UiKit.Label(done, "", 13, UiKit.Muted);
            UiKit.Button(done, "Continue", UiKit.Good, () => SceneManager.LoadScene(EmgSessionManager.AssessmentSceneName));
            doneCard = done.gameObject;

            SessionStatusStrip.Create(canvas.transform);
        }

        private void ShowStage(Stage next)
        {
            stage = next;
            instructionsCard.SetActive(next == Stage.Instructions);
            runningGroup.SetActive(next == Stage.Running);
            interruptedCard.SetActive(next == Stage.Interrupted);
            doneCard.SetActive(next == Stage.Done);
        }

        private void StartRun()
        {
            if (manager.IsSignalInterrupted)
            {
                interruptedText.text = manager.DescribeInterruption() + "\n\nThe 30 seconds will start once the signal is back.";
                restoredFor = 0f;
                ShowStage(Stage.Interrupted);
                return;
            }
            elapsed = 0f;
            manager.BeginCapture();
            manager.LogEvent("meditation_start");
            ShowStage(Stage.Running);
        }

        private void Update()
        {
            switch (stage)
            {
                case Stage.Running:
                    UpdateRunning();
                    break;
                case Stage.Interrupted:
                    UpdateInterrupted();
                    break;
            }
        }

        private void UpdateRunning()
        {
            if (manager.IsSignalInterrupted)
            {
                // Data with a gap in it can't be compared with other sessions,
                // so an interrupted run is thrown away and restarted in full.
                manager.CancelCapture();
                manager.LogEvent("meditation_interrupted", "", manager.DescribeInterruption());
                interruptedText.text = manager.DescribeInterruption() + "\n\nThe 30 seconds will restart once the signal is back.";
                restoredFor = 0f;
                ShowStage(Stage.Interrupted);
                return;
            }

            elapsed += Time.deltaTime;
            float cycle = elapsed % (InhaleSeconds + ExhaleSeconds);
            bool inhaling = cycle < InhaleSeconds;
            float t = inhaling ? cycle / InhaleSeconds : 1f - (cycle - InhaleSeconds) / ExhaleSeconds;
            float scale = Mathf.Lerp(SmallCircle, 1f, Mathf.SmoothStep(0f, 1f, t));
            circle.localScale = new Vector3(scale, scale, 1f);
            breathText.text = inhaling ? "Breathe in" : "Breathe out";
            countdownText.text = Mathf.CeilToInt(Mathf.Max(0f, EmgConfig.MeditationSeconds - elapsed)).ToString();

            if (elapsed >= EmgConfig.MeditationSeconds)
            {
                Finish();
            }
        }

        private void UpdateInterrupted()
        {
            if (manager.IsSignalInterrupted)
            {
                restoredFor = 0f;
                interruptedText.text = manager.DescribeInterruption() + "\n\nThe 30 seconds will restart once the signal is back.";
                return;
            }
            restoredFor += Time.deltaTime;
            interruptedText.text = "Signal is back. Restarting in " + Mathf.CeilToInt(Mathf.Max(0f, RestartDelaySeconds - restoredFor)) + "…";
            if (restoredFor >= RestartDelaySeconds)
            {
                StartRun();
            }
        }

        private void Finish()
        {
            List<float> envelope = manager.EndCapture();
            MeditationResult result = MeditationAnalyzer.Analyze(envelope, manager.SampleRateHz);
            manager.SetMeditation(result);
            manager.LogEvent("meditation_end", "", result.valid ? "mean_uV=" + result.mean_uV.ToString("0.0") : result.message);

            UiKit.PlayCue(chime);
            doneDetails.text = result.valid
                ? "For the therapist: average tension " + result.mean_uV.ToString("0.0") + " µV, change " + result.change_pct.ToString("+0;-0;0") + "%, " + result.tension_spikes + " spikes."
                : "For the therapist: " + result.message;
            ShowStage(Stage.Done);
        }
    }
}
