using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace CallibriEmg
{
    // A fake sensor that produces EMG-like data, for development and demos
    // without the hardware.
    //
    // The signal is Gaussian noise whose strength follows a "muscle
    // activation" level. That is how real surface EMG looks: random-looking
    // swings whose size grows with effort. Keys:
    //   Space (hold) = tense the forehead
    //   F8           = toggle a detached electrode
    //   F9           = simulate a 3-second Bluetooth drop
    public sealed class SimulatedEmgSource : IEmgSource
    {
        public const KeyCode FlexKey = KeyCode.Space;
        public const KeyCode DetachKey = KeyCode.F8;
        public const KeyCode DropKey = KeyCode.F9;

        private const double RestFloorUv = 3.0;
        private const double RestExtraUv = 5.0;
        private const double RelaxTimeConstantSeconds = 20.0;
        private const double DriftUv = 0.6;
        private const double DriftPeriodSeconds = 7.0;
        private const double FlexUv = 120.0;
        private const double SpikeUv = 25.0;
        private const double SpikeSeconds = 0.2;
        private const double AttackSeconds = 0.04;
        private const double ReleaseSeconds = 0.08;
        private const double DetachedNoiseUv = 400.0;
        private const float ScanDelaySeconds = 0.8f;
        private const float ConnectDelaySeconds = 1.5f;
        private const float DropSeconds = 3f;

        private static readonly FoundSensor[] FakeSensorList =
        {
            new FoundSensor { Name = "Callibri (simulated)", Address = "SIM-0001", Rssi = -48 },
        };

        private readonly ConcurrentQueue<EmgChunk> chunks = new ConcurrentQueue<EmgChunk>();
        private readonly System.Random random = new System.Random();
        // Samples are generated against a real clock, not Unity's frame time:
        // Unity caps the length of one frame, so a long scene-load stall
        // would otherwise silently lose time and the EMG file would fall
        // behind the event log.
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        private EmgSourceState state = EmgSourceState.Idle;
        private float stateTimer;
        private bool sensorVisible;
        private bool detached;
        private double activation;
        private double streamSeconds;
        private long generatedSamples;
        private double spikeRemaining;
        private double nextSpikeAt;
        private uint packNum;

        public string Kind => "Simulated";
        public bool IsSimulated => true;
        public EmgSourceState State => state;
        public ElectrodeContact Contact => state == EmgSourceState.Streaming ? (detached ? ElectrodeContact.Detached : ElectrodeContact.Normal) : ElectrodeContact.Unknown;
        public int BatteryPercent => state == EmgSourceState.Streaming || state == EmgSourceState.Reconnecting ? 100 : -1;
        public int SampleRateHz => EmgConfig.TargetSampleRateHz;
        public string SerialNumber => "SIM-0001";
        public string AppliedFilters => "simulated (no hardware filters)";
        public string LastError => "";
        public IReadOnlyList<FoundSensor> FoundSensors => sensorVisible ? FakeSensorList : Array.Empty<FoundSensor>();

        // Holds the simulated squeeze without the keyboard, for automated
        // play-tests and for demos run by someone else at the controls.
        public bool ForceFlex { get; set; }

        public void ToggleDetached()
        {
            detached = !detached;
        }

        public void SimulateDrop()
        {
            if (state == EmgSourceState.Streaming)
            {
                state = EmgSourceState.Reconnecting;
                stateTimer = DropSeconds;
            }
        }

        public void StartScan()
        {
            if (state != EmgSourceState.Idle && state != EmgSourceState.Error)
            {
                return;
            }
            state = EmgSourceState.Scanning;
            stateTimer = ScanDelaySeconds;
            sensorVisible = false;
        }

        public void Connect(int sensorIndex)
        {
            if (!sensorVisible || sensorIndex != 0 || state != EmgSourceState.Scanning)
            {
                return;
            }
            state = EmgSourceState.Connecting;
            stateTimer = ConnectDelaySeconds;
        }

        public void Tick(float unscaledDeltaTime)
        {
            switch (state)
            {
                case EmgSourceState.Scanning:
                    stateTimer -= unscaledDeltaTime;
                    if (stateTimer <= 0f)
                    {
                        sensorVisible = true;
                    }
                    break;
                case EmgSourceState.Connecting:
                    stateTimer -= unscaledDeltaTime;
                    if (stateTimer <= 0f)
                    {
                        state = EmgSourceState.Streaming;
                        nextSpikeAt = NextSpikeGap();
                        clock.Restart();
                        generatedSamples = 0;
                    }
                    break;
                case EmgSourceState.Reconnecting:
                    stateTimer -= unscaledDeltaTime;
                    if (stateTimer <= 0f)
                    {
                        state = EmgSourceState.Streaming;
                        // Like a real Bluetooth drop, nothing recorded during
                        // the gap is delivered afterwards.
                        generatedSamples = SamplesDueNow();
                    }
                    break;
                case EmgSourceState.Streaming:
                    HandleTestKeys();
                    if (state == EmgSourceState.Streaming)
                    {
                        Generate();
                    }
                    break;
            }
        }

        public bool TryDequeue(out EmgChunk chunk)
        {
            return chunks.TryDequeue(out chunk);
        }

        public void Disconnect()
        {
            state = EmgSourceState.Closed;
        }

        public void Dispose()
        {
            Disconnect();
        }

        private void HandleTestKeys()
        {
            if (Input.GetKeyDown(DetachKey))
            {
                ToggleDetached();
            }
            if (Input.GetKeyDown(DropKey))
            {
                SimulateDrop();
            }
        }

        private long SamplesDueNow()
        {
            return (long)(clock.Elapsed.TotalSeconds * SampleRateHz);
        }

        private void Generate()
        {
            long due = SamplesDueNow();
            // Only an extreme stall (over 10 s, e.g. a paused debugger) is
            // cut short, so one frame never produces a huge burst.
            int count = (int)Math.Min(due - generatedSamples, SampleRateHz * 10L);
            generatedSamples = due;
            if (count <= 0)
            {
                return;
            }

            bool flexing = Input.GetKey(FlexKey) || ForceFlex;
            double step = 1.0 / SampleRateHz;
            double[] samples = new double[count];

            for (int i = 0; i < count; i++)
            {
                double target = flexing ? 1.0 : 0.0;
                double timeConstant = target > activation ? AttackSeconds : ReleaseSeconds;
                activation += (target - activation) * (step / timeConstant);
                streamSeconds += step;

                // Resting tension starts a little high and eases down over
                // time, like a patient settling in, plus a slow drift.
                double restUv = RestFloorUv
                    + RestExtraUv * Math.Exp(-streamSeconds / RelaxTimeConstantSeconds)
                    + DriftUv * Math.Sin(2.0 * Math.PI * streamSeconds / DriftPeriodSeconds);

                if (streamSeconds >= nextSpikeAt)
                {
                    spikeRemaining = SpikeSeconds;
                    nextSpikeAt = streamSeconds + NextSpikeGap();
                }
                double spikeUv = spikeRemaining > 0 ? SpikeUv : 0.0;
                spikeRemaining -= step;

                double rmsUv = restUv + activation * (FlexUv - restUv) + spikeUv;
                samples[i] = detached ? Gaussian() * DetachedNoiseUv : Gaussian() * rmsUv;
            }

            chunks.Enqueue(new EmgChunk { PackNum = packNum++, SamplesUv = samples });
        }

        // Occasional involuntary tension bursts every 6-14 s, so meditation
        // metrics such as tension_spikes have something to count.
        private double NextSpikeGap()
        {
            return 6.0 + random.NextDouble() * 8.0;
        }

        // Box-Muller transform: turns two uniform random numbers into one
        // normally distributed number with mean 0 and standard deviation 1.
        private double Gaussian()
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
