using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CallibriEmg
{
    // Owns one session folder and writes its files.
    //
    // The two CSV streams are written by a dedicated background thread. At
    // 1000 samples a second, writing on the main thread would make the game
    // stutter whenever the disk is slow, and writing on the Bluetooth
    // callback thread could delay incoming packets. The main thread only
    // queues finished text blocks.
    public sealed class SessionRecorder : IDisposable
    {
        public const string EmgFileName = "emg_raw.csv";
        public const string EventsFileName = "game_events.csv";
        public const string EmgHeader = "sample_index,time_ms,emg_uV,envelope_uV,phase,state";
        public const string EventsHeader = "time_ms,phase,round,event,source,detail";

        private readonly ConcurrentQueue<string> emgQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> eventQueue = new ConcurrentQueue<string>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly StreamWriter emgWriter;
        private readonly StreamWriter eventsWriter;
        private readonly Thread writerThread;
        private volatile bool stopping;
        private volatile string lastError = "";
        private bool disposed;

        public SessionRecorder(string unassignedFolder)
        {
            // Recording starts on the Connect screen, before anyone is
            // chosen, so the folder starts as "Patient_Calibri_<date>" in
            // _unassigned and moves into the person's folder once they are.
            StartedAt = DateTime.Now;
            FolderPath = FreeFolderPath(unassignedFolder, SessionNaming.FolderNameFor(null, StartedAt), null);
            SessionId = Path.GetFileName(FolderPath);
            Directory.CreateDirectory(FolderPath);

            UTF8Encoding utf8NoBom = new UTF8Encoding(false);
            emgWriter = new StreamWriter(Path.Combine(FolderPath, EmgFileName), false, utf8NoBom, 1 << 16);
            eventsWriter = new StreamWriter(Path.Combine(FolderPath, EventsFileName), false, utf8NoBom, 1 << 12);
            emgWriter.WriteLine(EmgHeader);
            eventsWriter.WriteLine(EventsHeader);

            writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "EMG session writer",
            };
            writerThread.Start();
        }

        // The session folder's current name.
        public string SessionId { get; private set; }

        public DateTime StartedAt { get; }

        public string FolderPath { get; private set; }

        public string LastError => lastError;

        public void EnqueueEmgBlock(string csvRows)
        {
            if (!disposed && !string.IsNullOrEmpty(csvRows))
            {
                emgQueue.Enqueue(csvRows);
            }
        }

        public void EnqueueEvent(string csvRow)
        {
            if (!disposed)
            {
                eventQueue.Enqueue(csvRow);
                wake.Set();
            }
        }

        // Moves the session folder into parentFolder under folderName while
        // recording continues. Both folders are on the same disk, so this is
        // a rename, and on macOS an open file keeps working after its folder
        // is renamed: the CSV writers carry on untouched and only later file
        // paths change. If the name is taken (the same person twice on one
        // day), "_2", "_3", ... is added. Returns false (and keeps the old
        // place) if the move fails.
        public bool MoveFolder(string parentFolder, string folderName)
        {
            string target = FreeFolderPath(parentFolder, folderName, FolderPath);
            if (target == FolderPath)
            {
                return true;
            }
            try
            {
                Directory.Move(FolderPath, target);
                FolderPath = target;
                SessionId = Path.GetFileName(target);
                return true;
            }
            catch (IOException e)
            {
                lastError = "Could not move the session folder: " + e.Message;
                UnityEngine.Debug.LogError(lastError);
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                lastError = "No permission to move the session folder: " + e.Message;
                UnityEngine.Debug.LogError(lastError);
                return false;
            }
        }

        // The first free path for folderName inside parent: the name itself,
        // then name_2, name_3, ... A path equal to keep (the folder being
        // renamed) counts as free, so renaming to the same name is a no-op.
        private static string FreeFolderPath(string parent, string folderName, string keep)
        {
            string candidate = Path.Combine(parent, folderName);
            for (int copy = 2; candidate != keep && (Directory.Exists(candidate) || File.Exists(candidate)); copy++)
            {
                candidate = Path.Combine(parent, folderName + "_" + copy.ToString(CultureInfo.InvariantCulture));
            }
            return candidate;
        }

        public void WriteJson(string fileName, object data)
        {
            WriteText(fileName, JsonUtility.ToJson(data, true));
        }

        public void WriteText(string fileName, string text)
        {
            try
            {
                File.WriteAllText(Path.Combine(FolderPath, fileName), text, new UTF8Encoding(false));
            }
            catch (IOException e)
            {
                lastError = "Could not save " + fileName + ": " + e.Message;
                UnityEngine.Debug.LogError(lastError);
            }
            catch (UnauthorizedAccessException e)
            {
                lastError = "No permission to save " + fileName + ": " + e.Message;
                UnityEngine.Debug.LogError(lastError);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            stopping = true;
            wake.Set();
            if (!writerThread.Join(5000))
            {
                UnityEngine.Debug.LogWarning("The EMG writer thread took longer than 5 s to finish; some rows may be missing.");
            }
            emgWriter.Dispose();
            eventsWriter.Dispose();
            wake.Dispose();
        }

        private void WriterLoop()
        {
            Stopwatch sinceFlush = Stopwatch.StartNew();
            while (true)
            {
                wake.WaitOne(200);
                try
                {
                    Drain();
                    if (stopping)
                    {
                        Drain();
                        emgWriter.Flush();
                        eventsWriter.Flush();
                        return;
                    }
                    // Flush about once a second: a crash then loses at most
                    // one second of data, without flushing on every row.
                    if (sinceFlush.Elapsed.TotalSeconds >= EmgConfig.FlushIntervalSeconds)
                    {
                        emgWriter.Flush();
                        eventsWriter.Flush();
                        sinceFlush.Restart();
                    }
                }
                catch (IOException e)
                {
                    lastError = "Writing session data failed: " + e.Message;
                    if (stopping)
                    {
                        return;
                    }
                }
            }
        }

        private void Drain()
        {
            while (emgQueue.TryDequeue(out string rows))
            {
                emgWriter.Write(rows);
            }
            while (eventQueue.TryDequeue(out string row))
            {
                eventsWriter.Write(row);
            }
        }
    }

    public static class Csv
    {
        public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // Quotes a field only when it needs it, so free-text details such as
        // error messages can't break the column layout.
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            bool needsQuotes = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
