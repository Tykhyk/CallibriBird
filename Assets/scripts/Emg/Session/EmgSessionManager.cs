using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CallibriEmg
{
    // The one object that lives for a whole patient session, from Connect to
    // End Session. It owns the sensor, turns samples into an envelope,
    // detects muscle flaps, records everything to disk and keeps the
    // results of each phase.
    //
    // It survives scene loads (DontDestroyOnLoad) because GameMain reloads
    // GameScene on every round, and the sensor must keep streaming through
    // all of that. It runs before every other script each frame
    // (DefaultExecutionOrder) so a flap detected this frame is visible to
    // BirdControl and GameMain in the same frame.
    [DefaultExecutionOrder(-1000)]
    public sealed partial class EmgSessionManager : MonoBehaviour
    {
        public const string ConnectSceneName = "ConnectScene";
        public const string IntakeSceneName = "IntakeScene";
        public const string MeditationSceneName = "MeditationScene";
        public const string AssessmentSceneName = "AssessmentScene";
        public const string CalibrationSceneName = "CalibrationScene";
        public const string GameSceneName = "GameScene";
        public const string SummarySceneName = "SummaryScene";

        private const int MaxPlausiblePacketGap = 1000;
        // Enough for a very long session (every flap is one event) without
        // letting a runaway log fill memory.
        private const int MaxReportEvents = 200000;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly Stopwatch eventClock = new Stopwatch();
        private readonly StringBuilder emgBlock = new StringBuilder(64 * 1024);
        private readonly List<RoundRecord> rounds = new List<RoundRecord>();
        // The report's copy of the session: the envelope averaged into
        // 100 ms buckets (-1 = no signal in that bucket) and the event log.
        // Kept in memory so report.html can be built at the end without
        // re-reading the large CSV files.
        private readonly List<float> reportSeries = new List<float>();
        private readonly List<ReportEvent> reportEvents = new List<ReportEvent>();
        private long reportBucket;
        private double reportBucketSum;
        private int reportBucketCount;
        private bool strongBlinkFilter;

        private SessionRecorder recorder;
        private SampleClock sampleClock;
        private EnvelopeCalculator envelope;
        private int envelopeRateHz;
        private FlapDetector flapDetector;
        private List<float> capture;
        private RoundRecord currentRound;

        private long sampleIndex;
        private long lastPackNum = -1;
        private long packNumStep;
        private EmgSourceState lastState;
        private ElectrodeContact lastContact;

        private int emgTriggers;
        private int emgFlaps;
        private int mouseFlaps;
        private int disconnectCount;
        private long lostSamples;
        private int batteryAtStart = -1;
        private double gameEnvelopeSum;
        private long gameEnvelopeCount;
        private double gameEnvelopePeak;

        public static EmgSessionManager Instance { get; private set; }

        public IEmgSource Source { get; private set; }
        public SessionPhase Phase { get; private set; } = SessionPhase.Connect;
        public bool IsRecording => recorder != null;
        public bool HasEnded { get; private set; }
        public string SessionId { get; private set; } = "";
        public string SessionFolder { get; private set; } = "";
        public IntakeData Intake { get; private set; }
        public PatientEntry Patient { get; private set; }
        public MeditationResult Meditation { get; private set; }
        public CalibrationResult Calibration { get; private set; }
        public MeditationResult RestTest { get; private set; }
        public StrengthTestResult Strength { get; private set; }
        public bool AssessmentSkipped { get; private set; }
        public string ReportPath { get; private set; } = "";

        // Difficulty is the set used by the round being played. The therapist
        // edits PendingDifficulty; it is copied over when the next round's
        // scene loads, so every round runs with one fixed set of values.
        public DifficultySettings Difficulty { get; private set; } = new DifficultySettings();
        public DifficultySettings PendingDifficulty { get; private set; } = new DifficultySettings();

        public int RoundIndex { get; private set; }
        public IReadOnlyList<RoundRecord> Rounds => rounds;
        public bool IsGamePaused { get; private set; }
        public string SummaryText { get; private set; } = "";
        public EnvelopeHistory History { get; } = new EnvelopeHistory(300, 10);
        public double CurrentEnvelopeUv => envelope != null ? envelope.Current : 0.0;
        public long SampleCount => sampleIndex;
        public int SampleRateHz => Source != null ? Source.SampleRateHz : EmgConfig.TargetSampleRateHz;
        // What the sensor really sends per second, measured while recording.
        public double MeasuredSampleRateHz => sampleClock != null ? sampleClock.RateHz : SampleRateHz;
        public float SessionSeconds => (float)eventClock.Elapsed.TotalSeconds;

        public bool IsSignalInterrupted => Source == null || Source.State != EmgSourceState.Streaming || Source.Contact == ElectrodeContact.Detached;

        public static EmgSessionManager GetOrCreate()
        {
            if (Instance != null)
            {
                return Instance;
            }
            GameObject host = new GameObject("EmgSessionManager");
            return host.AddComponent<EmgSessionManager>();
        }

        // Ends the current session (if any) and throws the manager away, so
        // the next ConnectScene starts completely fresh.
        public static void DiscardSession()
        {
            if (Instance == null)
            {
                return;
            }
            Instance.FinishSession();
            // FinishSession only shuts the sensor down when a recording was
            // running; a session abandoned on the Connect screen still has
            // a live Bluetooth connection to close.
            Instance.ShutdownSource();
            Destroy(Instance.gameObject);
            Instance = null;
        }

        public static void ReportRoundStarted()
        {
            if (Instance != null)
            {
                Instance.OnRoundStarted();
            }
        }

        public static void ReportRoundEnded(int score)
        {
            if (Instance != null)
            {
                Instance.EndCurrentRound(score, "death", "death");
            }
        }

        public static void ReportPipePassed(int score)
        {
            if (Instance != null)
            {
                Instance.OnPipePassed(score);
            }
        }

        public static void ReportFlap(FlapSource source)
        {
            if (Instance != null)
            {
                Instance.OnFlap(source);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Recording must not stop when the window loses focus, for
            // example while the therapist checks another app.
            Application.runInBackground = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this)
            {
                FinishSession();
                ShutdownSource();
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            FinishSession();
            ShutdownSource();
        }

        private void Update()
        {
            if (Source == null)
            {
                return;
            }
            Source.Tick(Time.unscaledDeltaTime);
            TrackSourceStatus();
            DrainSamples();
            UpdateGamePause();
            FlushEmgBlock();
        }

        public void UseSource(bool simulated, bool useStrongBlinkFilter = false)
        {
            ShutdownSource();
            strongBlinkFilter = !simulated && useStrongBlinkFilter;
            Source = simulated ? (IEmgSource)new SimulatedEmgSource() : new CallibriEmgSource(strongBlinkFilter);
            envelope = null;
            History.Clear();
            lastPackNum = -1;
            packNumStep = 0;
            lastState = Source.State;
            lastContact = Source.Contact;
        }

        public void SetPhase(SessionPhase phase)
        {
            if (Phase == phase)
            {
                return;
            }
            Phase = phase;
            LogEvent("phase_start", "", PhaseName(phase));
        }

        public void StartRecording()
        {
            if (recorder != null || Source == null)
            {
                return;
            }
            recorder = new SessionRecorder(DataLocation.Unassigned);
            SessionId = recorder.SessionId;
            SessionFolder = recorder.FolderPath;

            // time_ms in both CSV files starts at 0 here, so the EMG clock
            // (sample count) and the event clock (Stopwatch) line up.
            sampleIndex = 0;
            lastPackNum = -1;
            reportSeries.Clear();
            reportEvents.Clear();
            reportBucket = 0;
            reportBucketSum = 0;
            reportBucketCount = 0;
            eventClock.Restart();
            sampleClock = new SampleClock(Source.SampleRateHz);
            sampleClock.Restart(0, 0.0);
            batteryAtStart = Source.BatteryPercent;

            recorder.WriteJson("device.json", new DeviceInfo
            {
                sourceKind = Source.Kind,
                serialNumber = Source.SerialNumber,
                batteryAtStartPercent = Source.BatteryPercent,
                sampleRateHz = Source.SampleRateHz,
                hardwareFilters = Source.AppliedFilters,
                envelopeWindowMs = EmgConfig.EnvelopeWindowMs,
                recordingStartedAt = System.DateTime.Now.ToString("o", Inv),
            });
            LogEvent("recording_start", Source.Kind, Source.SerialNumber);
        }

        // Links this session to a person: the session folder moves into the
        // person's folder (e.g. Jane_Smith/Jane_Smith_Calibri_2026-10-16), and the
        // person's record keeps today's target muscle and indication.
        public void SubmitIntake(IntakeData intake, PatientEntry patient)
        {
            System.DateTime now = System.DateTime.Now;
            intake.submittedAt = now.ToString("o", Inv);
            intake.patientId = patient.Record.patientId;
            Intake = intake;
            Patient = patient;

            patient.Record.targetMuscle = intake.targetMuscle;
            patient.Record.clinicalIndication = intake.clinicalIndication;
            patient.Record.updatedAt = intake.submittedAt;
            try
            {
                PatientDirectory.Save(patient);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError("[EMG] Could not update " + PatientDirectory.RecordFileName + ": " + e.Message);
            }

            if (recorder != null)
            {
                // The session folder keeps Tykhyk's format, name + "Calibri"
                // + date (2026-10-16), inside the person's folder.
                if (recorder.MoveFolder(patient.FolderPath, SessionNaming.FolderNameFor(patient.Record.fullName, recorder.StartedAt)))
                {
                    SessionFolder = recorder.FolderPath;
                    SessionId = recorder.SessionId;
                }
                recorder.WriteJson("intake.json", intake);
            }
            LogEvent("intake_submitted", "", Path.GetFileName(patient.FolderPath) + "/" + Path.GetFileName(SessionFolder));
        }

        public void SetMeditation(MeditationResult result)
        {
            Meditation = result;
            Meditation.ApplyCalibration(Calibration);
            if (recorder != null)
            {
                recorder.WriteJson("meditation.json", Meditation);
            }
        }

        public void SetCalibration(CalibrationResult result)
        {
            Calibration = result;
            flapDetector = CreateFlapDetector(result);
            SaveCalibrationFile();
            LogEvent("calibration_saved", "", string.Format(Inv, "rest={0:0.0} max={1:0.0} on={2:0.0} off={3:0.0}", result.restUv, result.maxUv, result.onUv, result.offUv));

            // Meditation ran first, so its calibration-relative metrics can
            // only be filled in now.
            if (Meditation != null)
            {
                SetMeditation(Meditation);
            }
        }

        // The eyes-closed rest test. Its result also feeds the strength
        // test's "rest as % of strongest", so it is stored before that runs.
        public void SetRestTest(MeditationResult result)
        {
            RestTest = result;
            AssessmentSkipped = false;
            SaveAssessmentFile();
        }

        public void SetStrength(StrengthTestResult result)
        {
            Strength = result;
            AssessmentSkipped = false;
            SaveAssessmentFile();
        }

        public void MarkAssessmentSkipped()
        {
            AssessmentSkipped = RestTest == null && Strength == null;
            SaveAssessmentFile();
            LogEvent("assessment_skipped");
        }

        // A detector for the game, with the same timing rules everywhere:
        // calibration practice uses this too, so the practice light behaves
        // exactly like the bird.
        public FlapDetector CreateFlapDetector(CalibrationResult calibration)
        {
            return new FlapDetector(calibration.onUv, calibration.offUv, RefractorySamples(), (long)EmgConfig.MinSqueezeMs * SampleRateHz / 1000);
        }

        public void SetThresholdFraction(float k)
        {
            if (Calibration == null || !Calibration.valid)
            {
                return;
            }
            Calibration.ApplyThresholdFraction(Mathf.Clamp(k, EmgConfig.MinThresholdFraction, EmgConfig.MaxThresholdFraction));
            if (flapDetector != null)
            {
                flapDetector.SetThresholds(Calibration.onUv, Calibration.offUv);
            }
            SaveCalibrationFile();
            LogEvent("settings_changed", "therapist", string.Format(Inv, "k={0:0.00}", Calibration.thresholdFraction));
        }

        public void NotifyDifficultyChanged()
        {
            SaveCalibrationFile();
            LogEvent("settings_changed", "therapist", "next round: " + PendingDifficulty.Describe());
        }

        public void BeginCapture()
        {
            capture = new List<float>(EmgConfig.TargetSampleRateHz * 35);
        }

        public List<float> EndCapture()
        {
            List<float> captured = capture ?? new List<float>();
            capture = null;
            return captured;
        }

        public void CancelCapture()
        {
            capture = null;
        }

        public void LogEvent(string eventName, string source = "", string detail = "")
        {
            LogEventAt(eventClock.Elapsed.TotalMilliseconds, eventName, source, detail);
        }

        public void EndSession()
        {
            FinishSession();
            SceneManager.LoadScene(SummarySceneName);
        }

        public string DescribeInterruption()
        {
            if (Source == null)
            {
                return "No sensor is connected.";
            }
            switch (Source.State)
            {
                case EmgSourceState.Reconnecting:
                    return "Bluetooth connection lost. Reconnecting to the sensor…";
                case EmgSourceState.Error:
                    return "Sensor error: " + Source.LastError;
                case EmgSourceState.Closed:
                    return "The sensor is disconnected.";
                case EmgSourceState.Streaming:
                    return Source.Contact == ElectrodeContact.Detached ? "An electrode is detached. Please reattach it to the forehead." : "";
                default:
                    return "Waiting for the sensor…";
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (IsGamePaused)
            {
                IsGamePaused = false;
                Time.timeScale = 1f;
            }
            if (scene.name == GameSceneName && Source != null && !HasEnded)
            {
                SetPhase(SessionPhase.Game);
                Difficulty = PendingDifficulty.Clone();
                // A squeeze still held from the previous screen (calibration
                // practice, or the restart squeeze) must not start the round
                // by itself the moment the scene appears.
                flapDetector?.Disarm();
                new GameObject("GameSessionHud").AddComponent<GameSessionHud>();
            }
        }

        private void TrackSourceStatus()
        {
            EmgSourceState state = Source.State;
            if (state != lastState)
            {
                if (state == EmgSourceState.Reconnecting && lastState == EmgSourceState.Streaming)
                {
                    disconnectCount++;
                    LogEvent("disconnect", Source.Kind);
                }
                else if (state == EmgSourceState.Streaming && lastState == EmgSourceState.Reconnecting)
                {
                    ResyncSampleClock();
                    LogEvent("reconnect", Source.Kind);
                }
                else if (state == EmgSourceState.Error)
                {
                    LogEvent("sensor_error", Source.Kind, Source.LastError);
                }
                lastState = state;
            }

            ElectrodeContact contact = Source.Contact;
            if (contact != lastContact)
            {
                LogEvent("electrode_state", Source.Kind, contact.ToString());
                lastContact = contact;
            }
        }

        private void DrainSamples()
        {
            while (Source.TryDequeue(out EmgChunk chunk))
            {
                if (chunk.SamplesUv == null || chunk.SamplesUv.Length == 0)
                {
                    continue;
                }
                EnsureEnvelope();
                HandlePacketGap(chunk);

                string phaseName = PhaseName(Phase);
                bool calibrated = Calibration != null && Calibration.valid;
                bool detectFlaps = calibrated && flapDetector != null && Phase == SessionPhase.Game && !IsGamePaused;

                foreach (double sampleUv in chunk.SamplesUv)
                {
                    double env = envelope.Add(sampleUv);
                    History.Add((float)env);
                    if (capture != null)
                    {
                        capture.Add((float)env);
                    }

                    if (Phase == SessionPhase.Game)
                    {
                        gameEnvelopeSum += env;
                        gameEnvelopeCount++;
                        if (env > gameEnvelopePeak)
                        {
                            gameEnvelopePeak = env;
                        }
                    }

                    if (detectFlaps && flapDetector.Process(env, sampleIndex))
                    {
                        emgTriggers++;
                        FlapInput.RegisterEmgFlap();
                        LogEventAt(SampleTimeMs(), "emg_trigger", "emg", env.ToString("F1", Inv));
                    }

                    if (recorder != null)
                    {
                        string state = !calibrated ? "UNCALIBRATED" : (env >= Calibration.onUv ? "FLEXED" : "RELAXED");
                        AppendEmgRow(sampleUv, env, phaseName, state);
                        AddToReportSeries(env);
                    }
                    sampleIndex++;
                }
                if (recorder != null)
                {
                    sampleClock.Observe(sampleIndex, eventClock.Elapsed.TotalMilliseconds);
                }
            }
        }

        private void EnsureEnvelope()
        {
            int rate = Source.SampleRateHz;
            if (envelope == null || envelopeRateHz != rate)
            {
                envelope = EnvelopeCalculator.ForSampleRate(rate, EmgConfig.EnvelopeWindowMs);
                envelopeRateHz = rate;
                if (Calibration != null && Calibration.valid)
                {
                    flapDetector = CreateFlapDetector(Calibration);
                }
            }
        }

        // Detects Bluetooth packets that never arrived and moves the sample
        // clock forward by the missing amount, so time_ms stays true.
        //
        // The step between consecutive PackNums is learned from the data
        // (the smallest positive jump seen) instead of assumed to be 1,
        // because the SDK docs don't say whether PackNum counts packets or
        // samples. This formula is correct for both.
        private void HandlePacketGap(EmgChunk chunk)
        {
            long current = chunk.PackNum;
            if (lastPackNum >= 0)
            {
                long diff = current - lastPackNum;
                if (diff > 0)
                {
                    if (packNumStep == 0 || diff < packNumStep)
                    {
                        packNumStep = diff;
                    }
                    if (diff > packNumStep && diff < MaxPlausiblePacketGap * packNumStep)
                    {
                        long missing = (diff - packNumStep) * chunk.SamplesUv.Length / packNumStep;
                        sampleIndex += missing;
                        lostSamples += missing;
                        LogEventAt(SampleTimeMs(), "packet_loss", Source.Kind, missing.ToString(Inv) + " samples");
                    }
                }
            }
            lastPackNum = current;
        }

        // After a Bluetooth drop, no samples arrived for a while. Count the
        // samples that would have arrived during the gap as lost and restart
        // the sample clock at the event clock, so EMG rows after the gap
        // still line up with game events.
        private void ResyncSampleClock()
        {
            if (recorder != null && sampleClock != null)
            {
                double nowMs = eventClock.Elapsed.TotalMilliseconds;
                double gapMs = nowMs - sampleClock.TimeMs(sampleIndex);
                if (gapMs > 0)
                {
                    long missing = (long)(gapMs * sampleClock.RateHz / 1000.0);
                    lostSamples += missing;
                    sampleIndex += missing;
                }
                sampleClock.Restart(sampleIndex, nowMs);
            }
            lastPackNum = -1;
            envelope?.Reset();
            flapDetector?.Disarm();
        }

        private void UpdateGamePause()
        {
            bool shouldPause = Phase == SessionPhase.Game && !HasEnded && IsSignalInterrupted && SceneManager.GetActiveScene().name == GameSceneName;
            if (shouldPause == IsGamePaused)
            {
                return;
            }
            IsGamePaused = shouldPause;
            Time.timeScale = shouldPause ? 0f : 1f;
            if (!shouldPause)
            {
                flapDetector?.Disarm();
            }
            LogEvent(shouldPause ? "game_paused" : "game_resumed", Source != null ? Source.Kind : "", DescribeInterruption());
        }

        private void OnRoundStarted()
        {
            RoundIndex++;
            currentRound = new RoundRecord
            {
                round = RoundIndex,
                startSeconds = SessionSeconds,
                difficulty = Difficulty.Clone(),
            };
            // The detector is deliberately left alone here. This runs in the
            // same frame as the squeeze that started the round; re-arming it
            // now would let that same squeeze count as a second flap.
            LogEvent("round_start", "", Difficulty.Describe());
        }

        private void EndCurrentRound(int score, string reason, string eventName)
        {
            if (currentRound == null)
            {
                return;
            }
            currentRound.score = score;
            currentRound.durationSeconds = SessionSeconds - currentRound.startSeconds;
            currentRound.endReason = reason;
            rounds.Add(currentRound);
            currentRound = null;
            LogEvent(eventName, "", "score=" + score.ToString(Inv));
        }

        private void OnPipePassed(int score)
        {
            if (currentRound != null)
            {
                currentRound.score = score;
            }
            LogEvent("pipe_passed", "", "score=" + score.ToString(Inv));
        }

        private void OnFlap(FlapSource source)
        {
            if (source == FlapSource.Emg)
            {
                emgFlaps++;
            }
            else
            {
                mouseFlaps++;
            }
            if (currentRound != null)
            {
                currentRound.flaps++;
            }
            LogEvent("flap", source == FlapSource.Emg ? "emg" : "mouse");
        }

        private void FinishSession()
        {
            if (HasEnded || recorder == null)
            {
                return;
            }
            if (currentRound != null)
            {
                EndCurrentRound(currentRound.score, "session_ended", "round_stopped");
            }
            LogEvent("session_end");
            SetPhase(SessionPhase.Ended);
            HasEnded = true;
            IsGamePaused = false;
            Time.timeScale = 1f;

            CloseReportBucket();
            SessionResults results = BuildResults();
            List<SessionResults> history = LoadHistory(results);
            recorder.WriteJson(ResultsFileName, results);
            // The summary is shown on screen only. report.html is the one
            // document saved (Tykhyk, 2026-10-16: no text file).
            SummaryText = BuildSummary(history);
            FlushEmgBlock();
            WriteReport(history);
            ShutdownSource();
            recorder.Dispose();
            recorder = null;
        }

        private void ShutdownSource()
        {
            if (Source == null)
            {
                return;
            }
            Source.Dispose();
        }

        private void SaveAssessmentFile()
        {
            if (recorder == null)
            {
                return;
            }
            recorder.WriteJson("assessment.json", new AssessmentFile
            {
                skipped = AssessmentSkipped,
                restTest = RestTest,
                strengthTest = Strength,
                savedAt = System.DateTime.Now.ToString("o", Inv),
            });
        }

        // Called for every recorded sample. sampleIndex can jump forward
        // (lost Bluetooth packets, a reconnect), so the buckets skipped over
        // are filled with -1 and the chart shows a gap there instead of a
        // line drawn straight across missing data.
        private void AddToReportSeries(double env)
        {
            long bucket = (long)(SampleTimeMs() / EmgConfig.ReportBucketMs);
            if (bucket != reportBucket)
            {
                CloseReportBucket();
                reportBucket = bucket;
            }
            reportBucketSum += env;
            reportBucketCount++;
        }

        private void CloseReportBucket()
        {
            if (reportBucketCount == 0)
            {
                return;
            }
            while (reportSeries.Count < reportBucket)
            {
                reportSeries.Add(-1f);
            }
            if (reportSeries.Count == reportBucket)
            {
                reportSeries.Add((float)(reportBucketSum / reportBucketCount));
            }
            reportBucketSum = 0;
            reportBucketCount = 0;
        }

        private void SaveCalibrationFile()
        {
            if (recorder == null || Calibration == null)
            {
                return;
            }
            recorder.WriteJson("calibration.json", new CalibrationFile
            {
                calibration = Calibration,
                difficultyForNextRound = PendingDifficulty,
                savedAt = System.DateTime.Now.ToString("o", Inv),
            });
        }

        private void AppendEmgRow(double sampleUv, double env, string phaseName, string state)
        {
            emgBlock.Append(sampleIndex.ToString(Inv)).Append(',')
                .Append(SampleTimeMs().ToString("F1", Inv)).Append(',')
                .Append(sampleUv.ToString("F3", Inv)).Append(',')
                .Append(env.ToString("F3", Inv)).Append(',')
                .Append(phaseName).Append(',')
                .Append(state).Append('\n');
        }

        private void FlushEmgBlock()
        {
            if (emgBlock.Length == 0)
            {
                return;
            }
            if (recorder != null)
            {
                recorder.EnqueueEmgBlock(emgBlock.ToString());
            }
            emgBlock.Clear();
        }

        private void LogEventAt(double timeMs, string eventName, string source, string detail)
        {
            if (recorder == null)
            {
                return;
            }
            if (reportEvents.Count < MaxReportEvents)
            {
                reportEvents.Add(new ReportEvent { TimeMs = timeMs, Phase = PhaseName(Phase), Round = RoundIndex, Name = eventName ?? "", Detail = detail ?? "" });
            }
            string row = timeMs.ToString("F1", Inv) + "," + PhaseName(Phase) + "," + RoundIndex.ToString(Inv) + ","
                + Csv.Escape(eventName) + "," + Csv.Escape(source) + "," + Csv.Escape(detail) + "\n";
            recorder.EnqueueEvent(row);
        }

        private double SampleTimeMs()
        {
            return sampleClock != null ? sampleClock.TimeMs(sampleIndex) : sampleIndex * 1000.0 / SampleRateHz;
        }

        private long RefractorySamples()
        {
            return (long)EmgConfig.RefractoryMs * SampleRateHz / 1000;
        }

        private static string PhaseName(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.Connect: return "connect";
                case SessionPhase.Intake: return "intake";
                case SessionPhase.Meditation: return "meditation";
                case SessionPhase.RestTest: return "rest_test";
                case SessionPhase.StrengthTest: return "strength_test";
                case SessionPhase.Calibration: return "calibration";
                case SessionPhase.Game: return "game";
                default: return "ended";
            }
        }
    }
}
