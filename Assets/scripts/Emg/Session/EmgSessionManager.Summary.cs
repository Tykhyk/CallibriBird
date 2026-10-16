using System;
using System.Collections.Generic;
using System.Text;

namespace CallibriEmg
{
    // Builds the plain-text summary shown on the summary screen. It is not
    // saved as a file: report.html is the session's document.
    public sealed partial class EmgSessionManager
    {
        private string BuildSummary(List<SessionResults> history)
        {
            StringBuilder s = new StringBuilder(4096);
            TimeSpan duration = eventClock.Elapsed;

            s.AppendLine("CallibriBird — Session Summary");
            s.AppendLine("==============================");
            s.AppendLine("Session ID: " + SessionId);
            s.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv));
            s.AppendLine(string.Format(Inv, "Duration: {0} min {1} s", (int)duration.TotalMinutes, duration.Seconds));
            s.AppendLine();

            s.AppendLine("PATIENT");
            if (Intake != null)
            {
                s.AppendLine("Full name: " + Intake.fullName);
                s.AppendLine("Date of birth: " + Intake.dateOfBirth);
                s.AppendLine("Target muscle: " + Intake.targetMuscle);
                s.AppendLine("Clinical indication: " + (string.IsNullOrWhiteSpace(Intake.clinicalIndication) ? "(none given)" : Intake.clinicalIndication));
            }
            else
            {
                s.AppendLine("(intake form not completed)");
            }
            s.AppendLine();

            s.AppendLine("SENSOR");
            if (Source != null)
            {
                s.AppendLine("Source: " + Source.Kind + (string.IsNullOrEmpty(Source.SerialNumber) ? "" : " (serial " + Source.SerialNumber + ")"));
                s.AppendLine(string.Format(Inv, "Sampling rate: set {0} Hz, measured {1:0.0} Hz | Envelope: {2} ms RMS", Source.SampleRateHz, MeasuredSampleRateHz, EmgConfig.EnvelopeWindowMs));
                s.AppendLine("Hardware filters: " + (string.IsNullOrEmpty(Source.AppliedFilters) ? "(none)" : Source.AppliedFilters));
                s.AppendLine("Battery at start / end: " + FormatBattery(batteryAtStart) + " / " + FormatBattery(Source.BatteryPercent));
            }
            s.AppendLine(string.Format(Inv, "Disconnections: {0} | Lost samples: {1}", disconnectCount, lostSamples));
            s.AppendLine();

            s.AppendLine(string.Format(Inv, "MEDITATION ({0:0} s, guided, eyes open, seconds {1:0}-{0:0} analyzed)", EmgConfig.MeditationSeconds, EmgConfig.MeditationSettleSeconds));
            if (Meditation != null && Meditation.valid)
            {
                AppendRelaxation(s, Meditation);
                if (Meditation.calibrationApplied)
                {
                    s.AppendLine(string.Format(Inv, "Resting tension vs strongest squeeze: {0:0.0}%", Meditation.pct_of_max));
                    s.AppendLine(string.Format(Inv, "Meditation vs calibration rest: {0:0.00}×", Meditation.ratio_to_calib_rest));
                }
            }
            else
            {
                s.AppendLine(Meditation != null ? "(not analyzed: " + Meditation.message + ")" : "(not completed)");
            }
            s.AppendLine();

            s.AppendLine(string.Format(Inv, "REST TEST ({0:0} s, eyes closed, seconds {1:0}-{0:0} analyzed)", EmgConfig.RestTestSeconds, EmgConfig.MeditationSettleSeconds));
            if (RestTest != null && RestTest.valid)
            {
                AppendRelaxation(s, RestTest);
            }
            else
            {
                s.AppendLine(AssessmentSkipped ? "(skipped)" : RestTest != null ? "(not analyzed: " + RestTest.message + ")" : "(not completed)");
            }
            s.AppendLine();

            s.AppendLine(string.Format(Inv, "STRENGTH TEST ({0} maximum squeezes, {1:0} s each, {2:0} s rest between)", EmgConfig.StrengthTrials, EmgConfig.StrengthHoldSeconds, EmgConfig.StrengthRestSeconds));
            if (Strength != null && Strength.valid)
            {
                foreach (StrengthTrialResult trial in Strength.trials)
                {
                    s.AppendLine(trial.valid
                        ? string.Format(Inv, "  Squeeze {0}: steady {1:0.0} µV, change while holding {2:+0.0;-0.0;0.0}%, steady after {3} ms{4}",
                            trial.trial, trial.steadyUv, trial.fadePct, trial.timeToSteadyMs,
                            trial.burstDetected ? string.Format(Inv, " | short burst to {0:0} µV ignored (blink, eye squeeze or electrode movement)", trial.peakUv) : "")
                        : string.Format(Inv, "  Squeeze {0}: {1}", trial.trial, trial.message));
                }
                s.AppendLine(string.Format(Inv, "Strongest squeeze (steady level, seconds 1-4): {0:0.0} µV (squeeze {1}) | Average: {2:0.0} µV", Strength.bestSteadyUv, Strength.bestTrial, Strength.averageSteadyUv));
                if (Strength.restTestAvailable)
                {
                    s.AppendLine(string.Format(Inv, "Resting tension (rest test) as % of strongest squeeze: {0:0.0}%", Strength.restPctOfStrongest));
                }
            }
            else
            {
                s.AppendLine(AssessmentSkipped ? "(skipped)" : Strength != null ? "(not analyzed: " + Strength.message + ")" : "(not completed)");
            }
            s.AppendLine();

            s.AppendLine("CALIBRATION");
            if (Calibration != null && Calibration.valid)
            {
                s.AppendLine(string.Format(Inv, "Resting baseline (V_rest): {0:0.0} µV (standard deviation {1:0.0} µV)", Calibration.restUv, Calibration.restSdUv));
                s.AppendLine(string.Format(Inv, "Strongest squeeze, 95th percentile (V_max): {0:0.0} µV", Calibration.maxUv));
                s.AppendLine(string.Format(Inv, "Sensitivity k: {0:0.00}, so trigger at {1:0.0} µV{2}, release below {3:0.0} µV", Calibration.thresholdFraction, Calibration.onUv,
                    Calibration.triggerRaisedByFloor ? " (raised to stay above resting noise)" : "", Calibration.offUv));
                s.AppendLine(string.Format(Inv, "A squeeze counts once it stays above the trigger for {0} ms", EmgConfig.MinSqueezeMs));
            }
            else
            {
                s.AppendLine("(not completed)");
            }
            s.AppendLine();

            s.AppendLine("GAME");
            int best = 0;
            int total = 0;
            foreach (RoundRecord round in rounds)
            {
                best = Math.Max(best, round.score);
                total += round.score;
            }
            double average = rounds.Count > 0 ? (double)total / rounds.Count : 0;
            s.AppendLine(string.Format(Inv, "Rounds played: {0} | Best score: {1} | Average score: {2:0.0}", rounds.Count, best, average));
            foreach (RoundRecord round in rounds)
            {
                s.AppendLine(string.Format(Inv, "  Round {0}: score {1}, {2:0.0} s, {3} flaps, ended by {4} | {5}",
                    round.round, round.score, round.durationSeconds, round.flaps, round.endReason, round.difficulty != null ? round.difficulty.Describe() : ""));
            }
            s.AppendLine(string.Format(Inv, "Bird flaps: {0} (muscle {1}, mouse {2}) | Muscle triggers detected: {3}", emgFlaps + mouseFlaps, emgFlaps, mouseFlaps, emgTriggers));
            if (gameEnvelopeCount > 0)
            {
                s.AppendLine(string.Format(Inv, "Forehead tension during play: average {0:0.0} µV, peak {1:0.0} µV", gameEnvelopeSum / gameEnvelopeCount, gameEnvelopePeak));
            }
            s.AppendLine();

            AppendProgress(s, history);
            s.AppendLine();
            s.AppendLine("Report with charts: report.html. Raw data: emg_raw.csv, game_events.csv and the .json files.");
            return s.ToString();
        }

        private static void AppendRelaxation(StringBuilder s, MeditationResult r)
        {
            s.AppendLine(string.Format(Inv, "Average tension: {0:0.0} µV", r.mean_uV));
            s.AppendLine(string.Format(Inv, "Deepest 1-second relaxation: {0:0.0} µV", r.lowest_1s_uV));
            s.AppendLine(string.Format(Inv, "Change from start to end: {0:+0.0;-0.0;0.0}% ({1})", r.change_pct, r.change_pct < 0 ? "relaxed more over time" : "did not relax further"));
            s.AppendLine(string.Format(Inv, "Steadiness (standard deviation): {0:0.0} µV", r.steadiness_sd_uV));
            s.AppendLine(string.Format(Inv, "Tension spikes: {0}", r.tension_spikes));
        }

        private static void AppendProgress(StringBuilder s, List<SessionResults> history)
        {
            s.AppendLine("PROGRESS (sessions with the same name and date of birth)");
            if (history == null || history.Count < 2)
            {
                s.AppendLine("(first session for this patient)");
                return;
            }
            foreach (SessionResults h in history)
            {
                string date = DateTime.TryParse(h.startedAt, Inv, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime started) ? started.ToString("yyyy-MM-dd HH:mm", Inv) : h.startedAt;
                s.AppendLine(string.Format(Inv, "  {0}{1}: rest test {2}, meditation {3}, strongest squeeze {4}, best score {5}{6}",
                    date,
                    h.sessionId == history[history.Count - 1].sessionId ? " (this session)" : "",
                    h.hasRestTest ? h.restTestMeanUv.ToString("0.0", Inv) + " µV" : "-",
                    h.hasMeditation ? h.meditationMeanUv.ToString("0.0", Inv) + " µV" : "-",
                    h.hasStrengthTest ? h.strengthBestUv.ToString("0.0", Inv) + " µV" : h.hasCalibration ? h.calibrationMaxUv.ToString("0.0", Inv) + " µV (calibration)" : "-",
                    h.rounds > 0 ? h.bestScore.ToString(Inv) : "-",
                    string.IsNullOrEmpty(h.blinkFilter) ? "" : " | blink filter " + h.blinkFilter));
            }
        }

        private static string FormatBattery(int percent)
        {
            return percent >= 0 ? percent.ToString(Inv) + "%" : "unknown";
        }
    }
}
