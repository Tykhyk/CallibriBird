using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeuroSDK;

namespace CallibriEmg
{
    // Talks to a real Callibri sensor through BrainBit's NeuroSDK2.
    //
    // Threading model:
    // - CreateSensor() and Connect() block for several seconds, so they run
    //   on background tasks.
    // - SDK callbacks arrive on the SDK's own threads. They only write to
    //   thread-safe fields and a ConcurrentQueue; nothing here touches Unity.
    // - sdkLock serialises our calls into the SDK so a reconnect attempt and
    //   a disconnect can never run at the same time.
    public sealed class CallibriEmgSource : IEmgSource
    {
        private const double VoltsToMicrovolts = 1_000_000.0;

        private static readonly SensorFilter[] StandardFilters =
        {
            SensorFilter.FilterHPFBwhLvl2CutoffFreq10Hz,
            SensorFilter.FilterBSFBwhLvl2CutoffFreq55_65Hz,
            SensorFilter.FilterLPFBwhLvl2CutoffFreq400Hz,
        };

        // The strong blink filter swaps the 10 Hz high-pass for 80 Hz. Blinks
        // and eye movements are slow (mostly below 20 Hz) and a forehead
        // sensor picks them up strongly; 80 Hz removes far more of them. It
        // also removes part of the muscle signal, so readings are lower than
        // with the standard set and should only be compared with sessions
        // that used the same filter.
        private static readonly SensorFilter[] StrongBlinkFilters =
        {
            SensorFilter.FilterHPFBwhLvl2CutoffFreq80Hz,
            SensorFilter.FilterBSFBwhLvl2CutoffFreq55_65Hz,
            SensorFilter.FilterLPFBwhLvl2CutoffFreq400Hz,
        };

        private readonly SensorFilter[] desiredFilters;

        private readonly object sdkLock = new object();
        private readonly object foundLock = new object();
        private readonly ConcurrentQueue<EmgChunk> chunks = new ConcurrentQueue<EmgChunk>();

        private Scanner scanner;
        private CallibriSensor sensor;
        private SensorInfo[] found = new SensorInfo[0];
        private Thread reconnectThread;

        private volatile EmgSourceState state = EmgSourceState.Idle;
        private volatile ElectrodeContact contact = ElectrodeContact.Unknown;
        private volatile int battery = -1;
        private volatile int sampleRateHz = EmgConfig.TargetSampleRateHz;
        private volatile string serialNumber = "";
        private volatile string appliedFilters = "";
        private volatile string lastError = "";
        private volatile bool closed;

        public CallibriEmgSource(bool strongBlinkFilter)
        {
            desiredFilters = strongBlinkFilter ? StrongBlinkFilters : StandardFilters;
        }

        public string Kind => "Callibri";
        public bool IsSimulated => false;
        public EmgSourceState State => state;
        public ElectrodeContact Contact => contact;
        public int BatteryPercent => battery;
        public int SampleRateHz => sampleRateHz;
        public string SerialNumber => serialNumber;
        public string AppliedFilters => appliedFilters;
        public string LastError => lastError;

        public IReadOnlyList<FoundSensor> FoundSensors
        {
            get
            {
                lock (foundLock)
                {
                    FoundSensor[] list = new FoundSensor[found.Length];
                    for (int i = 0; i < found.Length; i++)
                    {
                        list[i] = new FoundSensor { Name = found[i].Name, Address = found[i].Address, Rssi = found[i].RSSI };
                    }
                    return list;
                }
            }
        }

        public void StartScan()
        {
            if (closed || state == EmgSourceState.Scanning || state == EmgSourceState.Connecting || state == EmgSourceState.Streaming)
            {
                return;
            }

            state = EmgSourceState.Scanning;
            lastError = "";
            Task.Run(() =>
            {
                try
                {
                    lock (sdkLock)
                    {
                        if (scanner == null)
                        {
                            scanner = new Scanner(SensorFamily.SensorLECallibri, SensorFamily.SensorLEKolibri);
                            scanner.EventSensorsChanged += OnSensorsChanged;
                        }
                        scanner.Start();
                    }
                }
                catch (Exception e)
                {
                    Fail("Bluetooth search failed: " + e.Message);
                }
            });
        }

        public void Connect(int sensorIndex)
        {
            SensorInfo info;
            lock (foundLock)
            {
                if (sensorIndex < 0 || sensorIndex >= found.Length)
                {
                    return;
                }
                info = found[sensorIndex];
            }

            state = EmgSourceState.Connecting;
            lastError = "";
            Task.Run(() =>
            {
                try
                {
                    lock (sdkLock)
                    {
                        if (closed)
                        {
                            return;
                        }

                        scanner.Stop();
                        CallibriSensor created = scanner.CreateSensor(info) as CallibriSensor;
                        if (created == null)
                        {
                            throw new InvalidOperationException("The SDK did not return a Callibri sensor for " + info.Name + ".");
                        }
                        sensor = created;
                        if (sensor.State != SensorState.StateInRange)
                        {
                            sensor.Connect();
                        }

                        sensor.EventSensorStateChanged += OnSensorStateChanged;
                        sensor.EventBatteryChanged += OnBatteryChanged;
                        sensor.EventCallibriElectrodeStateChanged += OnElectrodeStateChanged;
                        sensor.EventCallibriSignalDataRecived += OnSignalData;

                        serialNumber = sensor.SerialNumber ?? "";
                        battery = sensor.BattPower;
                        ConfigureAndStartSignal();
                        contact = Map(sensor.ElectrodeState);
                    }
                    state = EmgSourceState.Streaming;
                }
                catch (Exception e)
                {
                    Fail("Could not connect to the sensor: " + e.Message);
                }
            });
        }

        public void Tick(float unscaledDeltaTime)
        {
            // Everything for the real sensor happens on SDK and task threads.
        }

        public bool TryDequeue(out EmgChunk chunk)
        {
            return chunks.TryDequeue(out chunk);
        }

        public void Disconnect()
        {
            if (closed)
            {
                return;
            }
            // Set first so an in-flight reconnect loop stops trying.
            closed = true;
            state = EmgSourceState.Closed;

            lock (sdkLock)
            {
                if (sensor != null)
                {
                    sensor.EventSensorStateChanged -= OnSensorStateChanged;
                    sensor.EventBatteryChanged -= OnBatteryChanged;
                    sensor.EventCallibriElectrodeStateChanged -= OnElectrodeStateChanged;
                    sensor.EventCallibriSignalDataRecived -= OnSignalData;
                    TryIgnore(() => sensor.ExecCommand(SensorCommand.CommandStopSignal));
                    TryIgnore(() => sensor.Disconnect());
                    TryIgnore(() => sensor.Dispose());
                    sensor = null;
                }
                if (scanner != null)
                {
                    scanner.EventSensorsChanged -= OnSensorsChanged;
                    TryIgnore(() => scanner.Stop());
                    TryIgnore(() => scanner.Dispose());
                    scanner = null;
                }
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        // Must be called while holding sdkLock. The signal type goes first
        // because switching it can reset gain and offset; the sampling rate
        // and filters are applied after so they are not overwritten.
        private void ConfigureAndStartSignal()
        {
            sensor.SignalTypeCallibri = CallibriSignalType.EMG;
            sensor.SamplingFrequency = SensorSamplingFrequency.FrequencyHz1000;

            List<SensorFilter> filters = new List<SensorFilter>();
            foreach (SensorFilter filter in desiredFilters)
            {
                if (sensor.IsSupportedFilter(filter))
                {
                    filters.Add(filter);
                }
            }
            sensor.HardwareFilters = filters;
            appliedFilters = string.Join(" ", filters);

            // Read the rate back instead of assuming 1000 Hz: every timestamp
            // in the CSV is derived from it, so it must be what the device
            // actually uses.
            sampleRateHz = ToHz(sensor.SamplingFrequency);
            sensor.ExecCommand(SensorCommand.CommandStartSignal);
        }

        private void OnSensorsChanged(IScanner source, IReadOnlyList<SensorInfo> sensors)
        {
            SensorInfo[] copy = new SensorInfo[sensors.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = sensors[i];
            }
            lock (foundLock)
            {
                found = copy;
            }
        }

        private void OnSignalData(ISensor source, CallibriSignalData[] data)
        {
            foreach (CallibriSignalData packet in data)
            {
                // The SDK allocates a fresh array for every packet, so it is
                // safe to convert to µV in place instead of copying.
                double[] samples = packet.Samples ?? new double[0];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] *= VoltsToMicrovolts;
                }
                chunks.Enqueue(new EmgChunk { PackNum = packet.PackNum, SamplesUv = samples });
            }
        }

        private void OnElectrodeStateChanged(ISensor source, CallibriElectrodeState electrodeState)
        {
            contact = Map(electrodeState);
        }

        private void OnBatteryChanged(ISensor source, int battPower)
        {
            battery = battPower;
        }

        private void OnSensorStateChanged(ISensor source, SensorState sensorState)
        {
            if (closed)
            {
                return;
            }
            if (sensorState == SensorState.StateOutOfRange && state == EmgSourceState.Streaming)
            {
                state = EmgSourceState.Reconnecting;
                contact = ElectrodeContact.Unknown;
                StartReconnectLoop();
            }
        }

        // Runs on its own thread because SDK callbacks must return quickly,
        // and Connect() blocks for seconds.
        private void StartReconnectLoop()
        {
            if (reconnectThread != null && reconnectThread.IsAlive)
            {
                return;
            }
            reconnectThread = new Thread(() =>
            {
                while (!closed && state == EmgSourceState.Reconnecting)
                {
                    Thread.Sleep((int)(EmgConfig.ReconnectIntervalSeconds * 1000));
                    try
                    {
                        lock (sdkLock)
                        {
                            if (closed || sensor == null)
                            {
                                return;
                            }
                            if (sensor.State != SensorState.StateInRange)
                            {
                                sensor.Connect();
                            }
                            ConfigureAndStartSignal();
                            contact = Map(sensor.ElectrodeState);
                        }
                        state = EmgSourceState.Streaming;
                    }
                    catch (Exception e)
                    {
                        lastError = "Reconnect attempt failed: " + e.Message;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Callibri reconnect",
            };
            reconnectThread.Start();
        }

        private void Fail(string message)
        {
            lastError = message;
            if (!closed)
            {
                state = EmgSourceState.Error;
            }
        }

        private static ElectrodeContact Map(CallibriElectrodeState electrodeState)
        {
            switch (electrodeState)
            {
                case CallibriElectrodeState.ElStNormal:
                    return ElectrodeContact.Normal;
                case CallibriElectrodeState.ElStHighResistance:
                    return ElectrodeContact.HighResistance;
                case CallibriElectrodeState.ElStDetached:
                    return ElectrodeContact.Detached;
                default:
                    return ElectrodeContact.Unknown;
            }
        }

        private static int ToHz(SensorSamplingFrequency frequency)
        {
            switch (frequency)
            {
                case SensorSamplingFrequency.FrequencyHz125: return 125;
                case SensorSamplingFrequency.FrequencyHz250: return 250;
                case SensorSamplingFrequency.FrequencyHz500: return 500;
                case SensorSamplingFrequency.FrequencyHz1000: return 1000;
                case SensorSamplingFrequency.FrequencyHz2000: return 2000;
                default:
                    throw new NotSupportedException("The sensor reported an unexpected sampling rate: " + frequency + ".");
            }
        }

        // Best-effort cleanup: a sensor that already dropped off Bluetooth
        // throws on every command, and that must not stop the rest of the
        // shutdown from running.
        private static void TryIgnore(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
            }
        }
    }
}
