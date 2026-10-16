using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CallibriEmg
{
    // Step 4 (patient): two standard tests that answer the therapist's two
    // questions about the muscle after an injury.
    //
    // 1. Rest test, 30 s with the eyes closed: how far the muscle relaxes.
    //    Closed eyes remove most blinking and eye-movement signal, which a
    //    forehead sensor picks up strongly, so this is the cleanest resting
    //    number of the session. Like the meditation it shows no live
    //    reading, because seeing their own tension would change it.
    // 2. Strength test, 3 × 5 s maximum squeezes with 30 s rest between:
    //    how hard the muscle can squeeze (the maximum voluntary contraction,
    //    MVC, a standard EMG measurement). 30 s of rest lets the muscle
    //    recover so the second and third squeeze aren't weakened by the
    //    first. Here a live bar IS shown: it helps the patient give full
    //    effort, and it is shown the same way every session.
    //
    // Both run before calibration so the strength test isn't affected by
    // the calibration squeezes, and in the same order every session so the
    // numbers can be compared over time.
    public sealed class AssessmentScreen : MonoBehaviour
    {
        private const float GetReadySeconds = 3f;
        private const float RestartDelaySeconds = 2f;

        private enum Stage
        {
            Intro,
            RestRunning,
            RestDone,
            StrengthRunning,
            Interrupted,
            Done,
        }

        private enum StrengthStep
        {
            Recover,
            GetReady,
            Squeeze,
        }

        // Assigned in the scene so the clips ship with the build: the chime
        // ends each test, the cue marks the start and end of every squeeze.
        [SerializeField] private AudioClip chime;
        [SerializeField] private AudioClip cue;

        private EmgSessionManager manager;
        private Stage stage = Stage.Intro;
        private float elapsed;
        private float restoredFor;
        private bool resumeRestTest;

        private readonly List<StrengthTrialResult> trials = new List<StrengthTrialResult>();
        private int trialIndex;
        private StrengthStep step;
        private float stepElapsed;
        private float bestPeakSoFar;

        private GameObject introCard;
        private GameObject restCard;
        private GameObject restDoneCard;
        private GameObject strengthCard;
        private GameObject interruptedCard;
        private GameObject doneCard;
        private Text restCountdown;
        private Text restDoneTitle;
        private Text restDoneDetails;
        private Text trialText;
        private Text promptText;
        private Text stepCountdown;
        private LevelMeter meter;
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
            manager.SetPhase(SessionPhase.RestTest);
            BuildUi();
            ShowStage(Stage.Intro);
        }

        private void BuildUi()
        {
            Canvas canvas = UiKit.CreateCanvas("Assessment Canvas", 0);

            RectTransform intro = UiKit.Column("Intro", canvas.transform, 410f, 12, 20, UiKit.Card);
            UiKit.Header(intro, "STEP 4 OF 6 · PATIENT", "Muscle tests (about 2 minutes)");
            UiKit.Label(intro, "Two short tests: how well your muscles relax, and how hard they can squeeze.\n\n"
                + "<b>1. Rest:</b> close your eyes and let your face go completely loose for 30 seconds. A chime tells you when to open them.\n\n"
                + "<b>2. Strength:</b> squeeze as hard as you can for 5 seconds, 3 times, with 30 seconds of rest between.", 15, UiKit.Ink, FontStyle.Normal, TextAnchor.UpperLeft);
            UiKit.Button(intro, "Start rest test", UiKit.Good, StartRestTest);
            UiKit.Button(intro, "Skip tests (therapist)", UiKit.Neutral, SkipAll, 36f, 14);
            introCard = intro.gameObject;

            RectTransform rest = UiKit.Column("Rest Test", canvas.transform, 400f, 12, 24, UiKit.Card);
            UiKit.Label(rest, "Close your eyes", 30, UiKit.Ink, FontStyle.Bold);
            UiKit.Label(rest, "Let your forehead, eyes and jaw go completely loose.\nBreathe normally. A chime will sound at the end.", 16, UiKit.Ink);
            restCountdown = UiKit.Label(rest, "", 14, UiKit.Muted);
            restCard = rest.gameObject;

            RectTransform restDone = UiKit.Column("Rest Done", canvas.transform, 410f, 10, 20, UiKit.Card);
            restDoneTitle = UiKit.Label(restDone, "", 24, UiKit.Good, FontStyle.Bold);
            restDoneDetails = UiKit.Label(restDone, "", 13, UiKit.Muted);
            UiKit.Label(restDone, "<b>Next: strength test.</b>\nWhen the screen says <b>SQUEEZE AS HARD AS YOU CAN</b>, squeeze your muscles with all your strength and keep squeezing until it says <b>RELAX</b>.", 15, UiKit.Ink, FontStyle.Normal, TextAnchor.UpperLeft);
            RectTransform restDoneButtons = UiKit.Row(restDone, 8, 46f);
            UiKit.Button(restDoneButtons, "Redo rest test", UiKit.Neutral, StartRestTest, 46f, 16);
            UiKit.Button(restDoneButtons, "Start strength test", UiKit.Good, StartStrengthTest, 46f, 16);
            UiKit.Button(restDone, "Skip strength test (therapist)", UiKit.Neutral, SkipStrength, 36f, 14);
            restDoneCard = restDone.gameObject;

            RectTransform strength = UiKit.Column("Strength Test", canvas.transform, 400f, 12, 20, UiKit.Card);
            trialText = UiKit.Label(strength, "", 14, UiKit.Muted, FontStyle.Bold);
            promptText = UiKit.Label(strength, "", 30, UiKit.Accent, FontStyle.Bold);
            stepCountdown = UiKit.Label(strength, "", 18, UiKit.Muted);
            UiKit.Label(strength, "Muscle tension", 12, UiKit.Muted);
            meter = LevelMeter.Create(strength, 26f);
            strengthCard = strength.gameObject;

            RectTransform paused = UiKit.Column("Interrupted", canvas.transform, 380f, 10, 20, UiKit.Card);
            UiKit.Label(paused, "Paused", 24, UiKit.Bad, FontStyle.Bold);
            interruptedText = UiKit.Label(paused, "", 16, UiKit.Ink);
            interruptedCard = paused.gameObject;

            RectTransform done = UiKit.Column("Done", canvas.transform, 410f, 12, 20, UiKit.Card);
            UiKit.Label(done, "Tests complete", 28, UiKit.Good, FontStyle.Bold);
            UiKit.Label(done, "Well done. Next, a short calibration sets up the game.", 16, UiKit.Ink);
            doneDetails = UiKit.Label(done, "", 13, UiKit.Muted);
            RectTransform doneButtons = UiKit.Row(done, 8, 46f);
            UiKit.Button(doneButtons, "Redo strength test", UiKit.Neutral, StartStrengthTest, 46f, 16);
            UiKit.Button(doneButtons, "Continue", UiKit.Good, GoToCalibration, 46f, 16);
            doneCard = done.gameObject;

            SessionStatusStrip.Create(canvas.transform);
        }

        private void ShowStage(Stage next)
        {
            stage = next;
            introCard.SetActive(next == Stage.Intro);
            restCard.SetActive(next == Stage.RestRunning);
            restDoneCard.SetActive(next == Stage.RestDone);
            strengthCard.SetActive(next == Stage.StrengthRunning);
            interruptedCard.SetActive(next == Stage.Interrupted);
            doneCard.SetActive(next == Stage.Done);
        }

        private void Update()
        {
            switch (stage)
            {
                case Stage.RestRunning:
                    UpdateRestTest();
                    break;
                case Stage.StrengthRunning:
                    UpdateStrengthTest();
                    break;
                case Stage.Interrupted:
                    UpdateInterrupted();
                    break;
            }
        }

        // ---- Rest test ----

        private void StartRestTest()
        {
            manager.SetPhase(SessionPhase.RestTest);
            if (manager.IsSignalInterrupted)
            {
                ShowInterrupted(true, "The rest test will start once the signal is back.");
                return;
            }
            elapsed = 0f;
            manager.BeginCapture();
            manager.LogEvent("rest_test_start");
            ShowStage(Stage.RestRunning);
        }

        private void UpdateRestTest()
        {
            if (manager.IsSignalInterrupted)
            {
                // A run with a gap can't be compared with other sessions, so
                // it is thrown away and restarted in full, like the
                // meditation.
                manager.CancelCapture();
                manager.LogEvent("rest_test_interrupted", "", manager.DescribeInterruption());
                ShowInterrupted(true, "The 30 seconds will restart once the signal is back.");
                return;
            }

            elapsed += Time.deltaTime;
            restCountdown.text = "For the therapist: " + Mathf.CeilToInt(Mathf.Max(0f, EmgConfig.RestTestSeconds - elapsed)) + " s left";
            if (elapsed >= EmgConfig.RestTestSeconds)
            {
                FinishRestTest();
            }
        }

        private void FinishRestTest()
        {
            List<float> envelope = manager.EndCapture();
            MeditationResult result = MeditationAnalyzer.Analyze(envelope, manager.SampleRateHz);
            manager.SetRestTest(result);
            manager.LogEvent("rest_test_end", "", result.valid ? "mean_uV=" + result.mean_uV.ToString("0.0") : result.message);
            UiKit.PlayCue(chime);

            restDoneTitle.text = "Done. Open your eyes.";
            restDoneTitle.color = result.valid ? UiKit.Good : UiKit.Bad;
            restDoneDetails.text = result.valid
                ? "For the therapist: resting tension " + result.mean_uV.ToString("0.0") + " µV, deepest " + result.lowest_1s_uV.ToString("0.0") + " µV, " + result.tension_spikes + " spikes."
                : "For the therapist: " + result.message + " Redo the rest test.";
            ShowStage(Stage.RestDone);
        }

        // ---- Strength test ----

        private void StartStrengthTest()
        {
            trials.Clear();
            trialIndex = 0;
            bestPeakSoFar = 0f;
            manager.SetPhase(SessionPhase.StrengthTest);
            manager.LogEvent("strength_test_start");
            BeginTrial();
        }

        // Every squeeze starts with a short "get ready" countdown, including
        // a squeeze repeated after a signal drop.
        private void BeginTrial()
        {
            if (manager.IsSignalInterrupted)
            {
                ShowInterrupted(false, "The strength test will continue once the signal is back.");
                return;
            }
            step = StrengthStep.GetReady;
            stepElapsed = 0f;
            ShowStage(Stage.StrengthRunning);
        }

        private void UpdateStrengthTest()
        {
            if (manager.IsSignalInterrupted)
            {
                // A squeeze cut short by a drop isn't a true maximum, so it is
                // discarded and repeated. A drop during the rest between
                // squeezes loses nothing.
                if (step == StrengthStep.Squeeze)
                {
                    manager.CancelCapture();
                    manager.LogEvent("strength_trial_interrupted", "", (trialIndex + 1).ToString());
                }
                ShowInterrupted(false, "Squeeze " + (trialIndex + 1) + " will start again once the signal is back.");
                return;
            }

            stepElapsed += Time.deltaTime;
            float level = (float)manager.CurrentEnvelopeUv;
            meter.SetValues(level, Mathf.Max(60f, bestPeakSoFar * 1.3f, level * 1.1f), -1f, -1f);
            trialText.text = "SQUEEZE " + (trialIndex + 1) + " OF " + EmgConfig.StrengthTrials;

            switch (step)
            {
                case StrengthStep.Recover:
                    float recoverSeconds = EmgConfig.StrengthRestSeconds - GetReadySeconds;
                    SetPrompt("RELAX", UiKit.Good, "Rest. Next squeeze in " + Mathf.CeilToInt(Mathf.Max(0f, EmgConfig.StrengthRestSeconds - stepElapsed)) + " s");
                    if (stepElapsed >= recoverSeconds)
                    {
                        step = StrengthStep.GetReady;
                        stepElapsed = 0f;
                    }
                    break;

                case StrengthStep.GetReady:
                    SetPrompt("GET READY", UiKit.Neutral, "Squeeze in " + Mathf.CeilToInt(Mathf.Max(0f, GetReadySeconds - stepElapsed)) + "…");
                    if (stepElapsed >= GetReadySeconds)
                    {
                        step = StrengthStep.Squeeze;
                        stepElapsed = 0f;
                        manager.BeginCapture();
                        manager.LogEvent("strength_trial_start", "", (trialIndex + 1).ToString());
                        UiKit.PlayCue(cue);
                    }
                    break;

                case StrengthStep.Squeeze:
                    SetPrompt("SQUEEZE AS HARD AS YOU CAN", UiKit.Accent, "Keep going! " + Mathf.CeilToInt(Mathf.Max(0f, EmgConfig.StrengthHoldSeconds - stepElapsed)) + " s");
                    if (stepElapsed >= EmgConfig.StrengthHoldSeconds)
                    {
                        FinishTrial();
                    }
                    break;
            }
        }

        private void FinishTrial()
        {
            List<float> envelope = manager.EndCapture();
            StrengthTrialResult result = StrengthAnalyzer.AnalyzeTrial(envelope, manager.SampleRateHz, trialIndex + 1);
            trials.Add(result);
            if (result.valid)
            {
                bestPeakSoFar = Mathf.Max(bestPeakSoFar, result.peakUv);
            }
            manager.LogEvent("strength_trial_end", "", (trialIndex + 1).ToString());
            UiKit.PlayCue(cue);

            trialIndex++;
            if (trialIndex >= EmgConfig.StrengthTrials)
            {
                FinishStrengthTest();
                return;
            }
            step = StrengthStep.Recover;
            stepElapsed = 0f;
        }

        private void FinishStrengthTest()
        {
            StrengthTestResult result = StrengthAnalyzer.Summarize(trials, manager.RestTest);
            manager.SetStrength(result);
            manager.LogEvent("strength_test_end", "", result.valid ? "best_steady_uV=" + result.bestSteadyUv.ToString("0.0") : result.message);
            UiKit.PlayCue(chime);

            if (!result.valid)
            {
                doneDetails.text = "For the therapist: " + result.message + " Check the electrodes and redo the strength test, or continue without it.";
            }
            else
            {
                string restPart = result.restTestAvailable ? ", resting tension is " + result.restPctOfStrongest.ToString("0.0") + "% of it" : "";
                doneDetails.text = "For the therapist: strongest steady squeeze " + result.bestSteadyUv.ToString("0.0") + " µV (squeeze " + result.bestTrial + ")" + restPart + "."
                    + (result.burstTrials > 0 ? "\n" + result.burstTrials + " squeeze(s) had a short burst (blink, eye squeeze or electrode movement); it was not counted as strength." : "");
            }
            ShowStage(Stage.Done);
        }

        private void SetPrompt(string prompt, Color color, string countdown)
        {
            promptText.text = prompt;
            promptText.color = color;
            stepCountdown.text = countdown;
        }

        // ---- Interruptions and navigation ----

        private void ShowInterrupted(bool restTest, string whatHappensNext)
        {
            resumeRestTest = restTest;
            restoredFor = 0f;
            interruptedText.text = manager.DescribeInterruption() + "\n\n" + whatHappensNext;
            ShowStage(Stage.Interrupted);
        }

        private void UpdateInterrupted()
        {
            if (manager.IsSignalInterrupted)
            {
                restoredFor = 0f;
                return;
            }
            restoredFor += Time.deltaTime;
            interruptedText.text = "Signal is back. Continuing in " + Mathf.CeilToInt(Mathf.Max(0f, RestartDelaySeconds - restoredFor)) + "…";
            if (restoredFor < RestartDelaySeconds)
            {
                return;
            }
            if (resumeRestTest)
            {
                StartRestTest();
            }
            else
            {
                BeginTrial();
            }
        }

        private void SkipAll()
        {
            manager.MarkAssessmentSkipped();
            GoToCalibration();
        }

        private void SkipStrength()
        {
            manager.LogEvent("strength_test_skipped");
            GoToCalibration();
        }

        private void GoToCalibration()
        {
            manager.CancelCapture();
            SceneManager.LoadScene(EmgSessionManager.CalibrationSceneName);
        }
    }
}
