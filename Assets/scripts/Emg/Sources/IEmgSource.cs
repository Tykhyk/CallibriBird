using System;
using System.Collections.Generic;

namespace CallibriEmg
{
    public enum EmgSourceState
    {
        Idle,
        Scanning,
        Connecting,
        Streaming,
        Reconnecting,
        Error,
        Closed,
    }

    public enum ElectrodeContact
    {
        Unknown,
        Normal,
        HighResistance,
        Detached,
    }

    // One Bluetooth packet's worth of samples, already converted to µV.
    public struct EmgChunk
    {
        public uint PackNum;
        public double[] SamplesUv;
    }

    public struct FoundSensor
    {
        public string Name;
        public string Address;
        public short Rssi;
    }

    // The real sensor and the simulated one both implement this, so every
    // screen and the session manager work the same with either of them.
    //
    // All state is polled rather than pushed through events. SDK callbacks
    // arrive on background threads, and polling from Update() keeps every
    // Unity API call on the main thread without extra marshalling code.
    public interface IEmgSource : IDisposable
    {
        string Kind { get; }
        bool IsSimulated { get; }
        EmgSourceState State { get; }
        ElectrodeContact Contact { get; }
        int BatteryPercent { get; }
        int SampleRateHz { get; }
        string SerialNumber { get; }
        string AppliedFilters { get; }
        string LastError { get; }
        IReadOnlyList<FoundSensor> FoundSensors { get; }

        void StartScan();
        void Connect(int sensorIndex);

        // Called once per frame on the main thread.
        void Tick(float unscaledDeltaTime);

        bool TryDequeue(out EmgChunk chunk);
        void Disconnect();
    }
}
