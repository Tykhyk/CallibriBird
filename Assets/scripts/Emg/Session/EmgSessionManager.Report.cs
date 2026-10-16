using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CallibriEmg
{
    // End-of-session outputs beyond the text summary: session_results.json
    // (this session's key numbers, read back by later sessions), the
    // progress history across sessions, and report.html with the charts.
    public sealed partial class EmgSessionManager
    {
        public const string ResultsFileName = "session_results.json";
        public const string ReportFileName = "report.html";

        private SessionResults BuildResults()
        {
            SessionResults r = new SessionResults
            {
                sessionId = SessionId,
                patientId = Patient != null ? Patient.Record.patientId : "",
                sourceKind = Source != null ? Source.Kind : "",
                measuredSampleRateHz = (float)MeasuredSampleRateHz,
                startedAt = recorder != null ? recorder.StartedAt.ToString("o", Inv) : DateTime.Now.ToString("o", Inv),
                fullName = Intake != null ? Intake.fullName : "",
                dateOfBirth = Intake != null ? Intake.dateOfBirth : "",
                blinkFilter = Source != null ? ProgressHistory.FilterLabel(Source.AppliedFilters) : "",
                emgFlaps = emgFlaps,
                emgTriggers = emgTriggers,
                rounds = rounds.Count,
            };

            if (Meditation != null && Meditation.valid)
            {
                r.hasMeditation = true;
                r.meditationMeanUv = Meditation.mean_uV;
                r.meditationLowestUv = Meditation.lowest_1s_uV;
            }
            if (RestTest != null && RestTest.valid)
            {
                r.hasRestTest = true;
                r.restTestMeanUv = RestTest.mean_uV;
                r.restTestLowestUv = RestTest.lowest_1s_uV;
            }
            if (Strength != null && Strength.valid)
            {
                r.hasStrengthTest = true;
                r.strengthBestUv = Strength.bestSteadyUv;
                if (Strength.restTestAvailable)
                {
                    r.restPctOfStrongest = Strength.restPctOfStrongest;
                }
            }
            if (Calibration != null && Calibration.valid)
            {
                r.hasCalibration = true;
                r.calibrationRestUv = Calibration.restUv;
                r.calibrationMaxUv = Calibration.maxUv;
            }

            int total = 0;
            foreach (RoundRecord round in rounds)
            {
                r.bestScore = Math.Max(r.bestScore, round.score);
                total += round.score;
            }
            r.averageScore = rounds.Count > 0 ? (float)total / rounds.Count : 0f;
            return r;
        }

        // Reads the person's other session folders (they all sit in the
        // person's folder). A folder that can't be read is skipped rather
        // than failing the whole session end: losing one comparison point is
        // better than losing the report.
        private List<SessionResults> LoadHistory(SessionResults current)
        {
            List<SessionResults> others = new List<SessionResults>();
            if (Patient == null || recorder == null)
            {
                return ProgressHistory.Build(others, current);
            }

            string root = Patient.FolderPath;
            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[EMG] Could not list earlier sessions: " + e.Message);
                return ProgressHistory.Build(others, current);
            }

            foreach (string folder in folders)
            {
                if (string.Equals(Path.GetFullPath(folder), Path.GetFullPath(recorder.FolderPath), StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    SessionResults results = ReadResults(folder);
                    if (results != null)
                    {
                        others.Add(results);
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[EMG] Skipped " + Path.GetFileName(folder) + " in the progress history: " + e.Message);
                }
            }
            return ProgressHistory.Build(others, current);
        }

        private static SessionResults ReadResults(string folder)
        {
            string resultsPath = Path.Combine(folder, ResultsFileName);
            if (File.Exists(resultsPath))
            {
                SessionResults saved = JsonUtility.FromJson<SessionResults>(File.ReadAllText(resultsPath));
                if (saved != null && string.IsNullOrEmpty(saved.sessionId))
                {
                    saved.sessionId = Path.GetFileName(folder);
                }
                return saved;
            }
            return RebuildOlderResults(folder);
        }

        // Sessions recorded before session_results.json existed still have
        // their intake, meditation, calibration and device files, which is
        // enough for the meditation and calibration columns.
        private static SessionResults RebuildOlderResults(string folder)
        {
            string intakePath = Path.Combine(folder, "intake.json");
            if (!File.Exists(intakePath))
            {
                return null;
            }
            IntakeData intake = JsonUtility.FromJson<IntakeData>(File.ReadAllText(intakePath));
            if (intake == null)
            {
                return null;
            }

            SessionResults r = new SessionResults
            {
                sessionId = Path.GetFileName(folder),
                patientId = intake.patientId,
                fullName = intake.fullName,
                dateOfBirth = intake.dateOfBirth,
                startedAt = intake.submittedAt,
                fromOlderSession = true,
            };

            string devicePath = Path.Combine(folder, "device.json");
            if (File.Exists(devicePath))
            {
                DeviceInfo device = JsonUtility.FromJson<DeviceInfo>(File.ReadAllText(devicePath));
                if (device != null)
                {
                    if (!string.IsNullOrEmpty(device.recordingStartedAt))
                    {
                        r.startedAt = device.recordingStartedAt;
                    }
                    r.blinkFilter = ProgressHistory.FilterLabel(device.hardwareFilters);
                    r.sourceKind = device.sourceKind;
                }
            }

            string meditationPath = Path.Combine(folder, "meditation.json");
            if (File.Exists(meditationPath))
            {
                MeditationResult meditation = JsonUtility.FromJson<MeditationResult>(File.ReadAllText(meditationPath));
                if (meditation != null && meditation.valid)
                {
                    r.hasMeditation = true;
                    r.meditationMeanUv = meditation.mean_uV;
                    r.meditationLowestUv = meditation.lowest_1s_uV;
                }
            }

            string calibrationPath = Path.Combine(folder, "calibration.json");
            if (File.Exists(calibrationPath))
            {
                CalibrationFile calibration = JsonUtility.FromJson<CalibrationFile>(File.ReadAllText(calibrationPath));
                if (calibration != null && calibration.calibration != null && calibration.calibration.valid)
                {
                    r.hasCalibration = true;
                    r.calibrationRestUv = calibration.calibration.restUv;
                    r.calibrationMaxUv = calibration.calibration.maxUv;
                }
            }
            return r;
        }

        // The report is built last and inside a try/catch: the CSV files and
        // the text summary are already saved, so a problem here can only
        // lose the HTML page, never the session's data.
        private void WriteReport(List<SessionResults> history)
        {
            try
            {
                SessionReportData data = BuildReportData(history);
                recorder.WriteText(ReportFileName, SessionReportBuilder.BuildHtml(data));
                ReportPath = Path.Combine(recorder.FolderPath, ReportFileName);
            }
            catch (Exception e)
            {
                ReportPath = "";
                UnityEngine.Debug.LogError("[EMG] Could not write the HTML report: " + e);
            }
        }

        private SessionReportData BuildReportData(List<SessionResults> history)
        {
            TimeSpan duration = eventClock.Elapsed;
            SessionReportData d = new SessionReportData
            {
                SessionId = SessionId,
                MeasuredRateHz = MeasuredSampleRateHz,
                DateText = (recorder != null ? recorder.StartedAt : DateTime.Now).ToString("MMMM d, yyyy, HH:mm", Inv),
                DurationText = string.Format(Inv, "{0} min {1} s", (int)duration.TotalMinutes, duration.Seconds),
                Intake = Intake,
                Meditation = Meditation,
                RestTest = RestTest,
                Strength = Strength,
                Calibration = Calibration,
                TestsSkipped = AssessmentSkipped,
                EmgFlaps = emgFlaps,
                MouseFlaps = mouseFlaps,
                EmgTriggers = emgTriggers,
                Disconnects = disconnectCount,
                LostSamples = lostSamples,
                GameAverageUv = gameEnvelopeCount > 0 ? (float)(gameEnvelopeSum / gameEnvelopeCount) : -1f,
                GamePeakUv = gameEnvelopeCount > 0 ? (float)gameEnvelopePeak : -1f,
                BucketMs = EmgConfig.ReportBucketMs,
                History = history,
            };
            d.Rounds.AddRange(rounds);
            d.Envelope.AddRange(reportSeries);
            d.Events.AddRange(reportEvents);
            if (Source != null)
            {
                d.Device = new DeviceInfo
                {
                    sourceKind = Source.Kind,
                    serialNumber = Source.SerialNumber,
                    batteryAtStartPercent = batteryAtStart,
                    sampleRateHz = Source.SampleRateHz,
                    hardwareFilters = Source.AppliedFilters,
                    envelopeWindowMs = EmgConfig.EnvelopeWindowMs,
                };
            }
            return d;
        }
    }
}
