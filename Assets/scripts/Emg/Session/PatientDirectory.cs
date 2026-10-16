using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CallibriEmg
{
    // Where every person's data is kept (Tykhyk's choice, 2026-10-16):
    //
    //   ~/Unity Projects/calibri/Patients' reports/
    //     Jane_Smith/                      one folder per person
    //       patient.json                   who they are (PatientRecord)
    //       Jane_Smith_Calibri_2026-10-16/ one folder per session
    //         report.html, emg_raw.csv, game_events.csv, *.json
    //     _unassigned/                     sessions not yet linked to a person
    //
    // Recording starts on the Connect screen, before anyone is chosen, so a
    // new session begins in _unassigned and moves into the person's folder
    // when the therapist picks or adds them. A session abandoned before that
    // stays in _unassigned.
    public static class DataLocation
    {
        public const string UnassignedFolderName = "_unassigned";

        // Automated play-tests point this at a scratch folder so test
        // sessions never land among real people's data.
        public static string OverrideRoot { get; set; }

        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Unity Projects", "calibri", "Patients' reports");

        // The folder in use. If the chosen folder can't be created (another
        // Mac without that path, no permission), data goes to the app's own
        // data folder instead of being lost.
        public static string Root
        {
            get
            {
                string root = string.IsNullOrEmpty(OverrideRoot) ? DefaultRoot : OverrideRoot;
                try
                {
                    Directory.CreateDirectory(root);
                    return root;
                }
                catch (Exception e)
                {
                    string fallback = Path.Combine(Application.persistentDataPath, "Patients");
                    Debug.LogWarning("[EMG] Can't use " + root + " (" + e.Message + "). Saving to " + fallback + " instead.");
                    Directory.CreateDirectory(fallback);
                    return fallback;
                }
            }
        }

        public static string Unassigned
        {
            get
            {
                string folder = Path.Combine(Root, UnassignedFolderName);
                Directory.CreateDirectory(folder);
                return folder;
            }
        }
    }

    // One person as shown on the "Who is playing?" screen.
    public sealed class PatientEntry
    {
        public PatientRecord Record;
        public string FolderPath = "";
        public int SessionCount;
        public DateTime LastSession = DateTime.MinValue;
    }

    // Reads and writes the person folders. A person is a folder with a
    // patient.json in it, so there is no separate list file that could get
    // out of step with the folders: deleting a person's folder removes them.
    public static class PatientDirectory
    {
        public const string RecordFileName = "patient.json";

        public static List<PatientEntry> LoadAll(string root)
        {
            List<PatientEntry> entries = new List<PatientEntry>();
            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EMG] Could not list people in " + root + ": " + e.Message);
                return entries;
            }

            foreach (string folder in folders)
            {
                string recordPath = Path.Combine(folder, RecordFileName);
                if (!File.Exists(recordPath))
                {
                    continue;
                }
                try
                {
                    PatientRecord record = JsonUtility.FromJson<PatientRecord>(File.ReadAllText(recordPath));
                    if (record == null || string.IsNullOrEmpty(record.patientId))
                    {
                        continue;
                    }
                    PatientEntry entry = new PatientEntry { Record = record, FolderPath = folder };
                    CountSessions(entry);
                    entries.Add(entry);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[EMG] Skipped " + Path.GetFileName(folder) + ": " + e.Message);
                }
            }

            // Most recently seen first: the person the therapist is most
            // likely looking for is at the top.
            entries.Sort((a, b) =>
            {
                int byDate = b.LastSession.CompareTo(a.LastSession);
                return byDate != 0 ? byDate : string.Compare(a.Record.fullName, b.Record.fullName, StringComparison.OrdinalIgnoreCase);
            });
            return entries;
        }

        public static PatientEntry FindSamePerson(IEnumerable<PatientEntry> entries, string fullName, string dateOfBirth)
        {
            foreach (PatientEntry entry in entries)
            {
                if (ProgressHistory.SamePatient(entry.Record.fullName, entry.Record.dateOfBirth, fullName, dateOfBirth))
                {
                    return entry;
                }
            }
            return null;
        }

        // Makes the person's folder, named after them. Two different people
        // with the same name get "Name" and "Name_2".
        public static PatientEntry Create(string root, PatientRecord record)
        {
            string folder = FreeFolderPath(root, SessionNaming.SanitizeName(record.fullName));
            Directory.CreateDirectory(folder);
            PatientEntry entry = new PatientEntry { Record = record, FolderPath = folder };
            Save(entry);
            return entry;
        }

        public static void Save(PatientEntry entry)
        {
            File.WriteAllText(Path.Combine(entry.FolderPath, RecordFileName), JsonUtility.ToJson(entry.Record, true), new UTF8Encoding(false));
        }

        public static string FreeFolderPath(string parent, string folderName)
        {
            string candidate = Path.Combine(parent, folderName);
            for (int copy = 2; Directory.Exists(candidate) || File.Exists(candidate); copy++)
            {
                candidate = Path.Combine(parent, folderName + "_" + copy.ToString(CultureInfo.InvariantCulture));
            }
            return candidate;
        }

        // A session folder is any subfolder with a recording in it. Its date
        // comes from session_results.json, or intake.json for a session that
        // ended early, or the folder itself.
        private static void CountSessions(PatientEntry entry)
        {
            foreach (string session in Directory.GetDirectories(entry.FolderPath))
            {
                if (!File.Exists(Path.Combine(session, SessionRecorder.EmgFileName)))
                {
                    continue;
                }
                entry.SessionCount++;
                DateTime started = SessionStart(session);
                if (started > entry.LastSession)
                {
                    entry.LastSession = started;
                }
            }
        }

        private static DateTime SessionStart(string session)
        {
            try
            {
                string resultsPath = Path.Combine(session, EmgSessionManager.ResultsFileName);
                if (File.Exists(resultsPath))
                {
                    SessionResults results = JsonUtility.FromJson<SessionResults>(File.ReadAllText(resultsPath));
                    if (results != null && DateTime.TryParse(results.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime fromResults))
                    {
                        return fromResults;
                    }
                }
                string intakePath = Path.Combine(session, "intake.json");
                if (File.Exists(intakePath))
                {
                    IntakeData intake = JsonUtility.FromJson<IntakeData>(File.ReadAllText(intakePath));
                    if (intake != null && DateTime.TryParse(intake.submittedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime fromIntake))
                    {
                        return fromIntake;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EMG] Could not read the date of " + Path.GetFileName(session) + ": " + e.Message);
            }
            return Directory.GetCreationTime(session);
        }
    }
}
