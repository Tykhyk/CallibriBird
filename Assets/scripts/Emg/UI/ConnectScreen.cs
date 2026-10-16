using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CallibriEmg
{
    // Step 1 (therapist): find the sensor, connect, check the electrodes.
    // Recording starts when Continue is pressed.
    public sealed class ConnectScreen : MonoBehaviour
    {
        private const int MaxListedSensors = 4;

        private EmgSessionManager manager;
        private bool useSimulated;
        private bool strongBlinkFilter;
        private float scanSeconds;
        private float singleSensorSeconds;
        private float goodContactSeconds;
        private bool connectRequested;

        private Button realButton;
        private Button simButton;
        private Text instructions;
        private Button filterButton;
        private Text filterNote;
        private Button searchButton;
        private Text statusText;
        private readonly List<Button> sensorButtons = new List<Button>();
        private Text detailsText;
        private Text electrodeText;
        private RectTransform graphHolder;
        private Button continueButton;

        private void Start()
        {
            // Coming back here from the summary screen starts a brand-new
            // session; any previous one is closed first.
            if (EmgSessionManager.Instance != null && EmgSessionManager.Instance.HasEnded)
            {
                EmgSessionManager.DiscardSession();
            }
            manager = EmgSessionManager.GetOrCreate();
            manager.SetPhase(SessionPhase.Connect);
            BuildUi();
            RefreshModeButtons();
        }

        private void BuildUi()
        {
            Canvas canvas = UiKit.CreateCanvas("Connect Canvas", 0);
            RectTransform card = UiKit.Column("Card", canvas.transform, 410f, 10, 18, UiKit.Card);

            UiKit.Header(card, "STEP 1 OF 6 · THERAPIST", "Connect Sensor");

            RectTransform modeRow = UiKit.Row(card, 8, 38f);
            realButton = UiKit.Button(modeRow, "Callibri sensor", UiKit.Neutral, () => SelectMode(false), 38f);
            simButton = UiKit.Button(modeRow, "Simulated (demo)", UiKit.Neutral, () => SelectMode(true), 38f);

            instructions = UiKit.Label(card, "", 14, UiKit.Ink, FontStyle.Normal, TextAnchor.UpperLeft);
            filterButton = UiKit.Button(card, "", UiKit.Neutral, ToggleBlinkFilter, 34f, 14);
            filterNote = UiKit.Label(card, "", 12, UiKit.Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            searchButton = UiKit.Button(card, "Search", UiKit.Accent, OnSearchPressed);
            statusText = UiKit.Label(card, "", 15, UiKit.Muted, FontStyle.Italic);

            for (int i = 0; i < MaxListedSensors; i++)
            {
                int index = i;
                Button sensorButton = UiKit.Button(card, "", UiKit.Neutral, () => OnSensorPicked(index), 40f);
                sensorButton.gameObject.SetActive(false);
                sensorButtons.Add(sensorButton);
            }

            detailsText = UiKit.Label(card, "", 13, UiKit.Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            electrodeText = UiKit.Label(card, "", 17, UiKit.Ink, FontStyle.Bold);
            graphHolder = UiKit.Rect("Graph Holder", card);
            VerticalLayoutGroup holderLayout = graphHolder.gameObject.AddComponent<VerticalLayoutGroup>();
            holderLayout.childControlWidth = true;
            holderLayout.childControlHeight = true;
            holderLayout.childForceExpandHeight = false;
            EnvelopeGraph.Create(graphHolder);
            graphHolder.gameObject.SetActive(false);

            continueButton = UiKit.Button(card, "Continue", UiKit.Good, OnContinue);
            continueButton.interactable = false;
            UiKit.Label(card, Credits.CreatedBy, 12, UiKit.Muted, FontStyle.Italic);
        }

        private void SelectMode(bool simulated)
        {
            if (manager.Source != null && manager.Source.State != EmgSourceState.Idle && manager.Source.State != EmgSourceState.Error)
            {
                return;
            }
            useSimulated = simulated;
            RefreshModeButtons();
        }

        // The filter is sent to the sensor when it connects, so it can only
        // be changed before Search.
        private void ToggleBlinkFilter()
        {
            if (manager.Source != null && manager.Source.State != EmgSourceState.Idle && manager.Source.State != EmgSourceState.Error)
            {
                return;
            }
            strongBlinkFilter = !strongBlinkFilter;
            RefreshModeButtons();
        }

        private void RefreshModeButtons()
        {
            realButton.GetComponent<Image>().color = useSimulated ? UiKit.Track : UiKit.Neutral;
            simButton.GetComponent<Image>().color = useSimulated ? UiKit.Neutral : UiKit.Track;
            // White text on the light unselected colour was unreadable.
            realButton.GetComponentInChildren<Text>().color = useSimulated ? UiKit.Ink : Color.white;
            simButton.GetComponentInChildren<Text>().color = useSimulated ? Color.white : UiKit.Ink;
            instructions.text = useSimulated
                ? "Demo mode: no hardware needed.\nHold <b>Space</b> to tense the forehead. <b>F8</b> detaches an electrode, <b>F9</b> drops the Bluetooth connection for 3 s."
                : "1. Turn the sensor on (left light solid green).\n2. Clean the forehead and attach the electrodes.\n3. Keep the laptop close, with nothing blocking it.\n4. Press Search. The first time, macOS asks to let Unity use Bluetooth: click Allow.";

            filterButton.gameObject.SetActive(!useSimulated);
            filterNote.gameObject.SetActive(!useSimulated);
            UiKit.SetButtonText(filterButton, strongBlinkFilter ? "Blink filter: STRONG (80 Hz)" : "Blink filter: standard (10 Hz)");
            filterNote.text = strongBlinkFilter
                ? "Strong: ignores most blinks and eye movement, but readings are lower. Compare only with sessions that used the strong filter too."
                : "Switch to strong if the bird flaps when the patient blinks.";
        }

        private void OnSearchPressed()
        {
            manager.UseSource(useSimulated, strongBlinkFilter);
            manager.Source.StartScan();
            scanSeconds = 0f;
            singleSensorSeconds = 0f;
            goodContactSeconds = 0f;
            connectRequested = false;
            continueButton.interactable = false;
        }

        private void OnSensorPicked(int index)
        {
            if (manager.Source == null || connectRequested)
            {
                return;
            }
            connectRequested = true;
            manager.Source.Connect(index);
        }

        private void OnContinue()
        {
            manager.StartRecording();
            manager.SetPhase(SessionPhase.Intake);
            SceneManager.LoadScene(EmgSessionManager.IntakeSceneName);
        }

        private void Update()
        {
            IEmgSource source = manager.Source;
            bool locked = source != null && source.State != EmgSourceState.Idle && source.State != EmgSourceState.Error;
            realButton.interactable = !locked;
            simButton.interactable = !locked;
            filterButton.interactable = !locked;

            if (source == null)
            {
                statusText.text = "Choose the sensor type, then press Search.";
                return;
            }

            switch (source.State)
            {
                case EmgSourceState.Scanning:
                    UpdateScanning(source);
                    break;
                case EmgSourceState.Connecting:
                    HideSensorList();
                    searchButton.gameObject.SetActive(false);
                    statusText.text = "Connecting… this can take up to 10 seconds.";
                    break;
                case EmgSourceState.Streaming:
                case EmgSourceState.Reconnecting:
                    UpdateConnected(source);
                    break;
                case EmgSourceState.Error:
                    HideSensorList();
                    searchButton.gameObject.SetActive(true);
                    UiKit.SetButtonText(searchButton, "Retry");
                    statusText.text = source.LastError;
                    statusText.color = UiKit.Bad;
                    continueButton.interactable = false;
                    break;
                default:
                    statusText.text = "";
                    break;
            }
        }

        private void UpdateScanning(IEmgSource source)
        {
            searchButton.gameObject.SetActive(false);
            scanSeconds += Time.unscaledDeltaTime;
            IReadOnlyList<FoundSensor> found = source.FoundSensors;

            for (int i = 0; i < sensorButtons.Count; i++)
            {
                bool visible = i < found.Count;
                sensorButtons[i].gameObject.SetActive(visible);
                if (visible)
                {
                    UiKit.SetButtonText(sensorButtons[i], found[i].Name + "   (signal " + found[i].Rssi + " dBm)");
                }
            }

            if (found.Count == 0)
            {
                statusText.color = scanSeconds > EmgConfig.ScanTimeoutSeconds ? UiKit.Bad : UiKit.Muted;
                statusText.text = scanSeconds > EmgConfig.ScanTimeoutSeconds
                    ? "No sensor found yet. Is the green light on? Is Bluetooth allowed for Unity (System Settings > Privacy & Security > Bluetooth)? Still searching…"
                    : "Searching for sensors…";
                singleSensorSeconds = 0f;
                return;
            }

            statusText.color = UiKit.Muted;
            if (found.Count == 1)
            {
                // Auto-connect only after the single result has been stable
                // for a moment, in case a second sensor is about to appear.
                singleSensorSeconds += Time.unscaledDeltaTime;
                statusText.text = "Found 1 sensor. Connecting automatically…";
                if (singleSensorSeconds >= EmgConfig.AutoConnectAfterSeconds)
                {
                    OnSensorPicked(0);
                }
            }
            else
            {
                singleSensorSeconds = 0f;
                statusText.text = "Found " + found.Count + " sensors. Tap the one on the patient.";
            }
        }

        private void UpdateConnected(IEmgSource source)
        {
            HideSensorList();
            searchButton.gameObject.SetActive(false);
            graphHolder.gameObject.SetActive(true);
            statusText.color = UiKit.Good;
            statusText.text = source.State == EmgSourceState.Reconnecting ? "Connection lost. Reconnecting…" : "Connected and streaming.";

            string battery = source.BatteryPercent >= 0 ? source.BatteryPercent + "%" : "unknown";
            string batteryWarning = source.BatteryPercent >= 0 && source.BatteryPercent < EmgConfig.LowBatteryPercent ? " (charge soon)" : "";
            detailsText.text = source.Kind + " · serial " + source.SerialNumber + " · " + source.SampleRateHz + " Hz · battery " + battery + batteryWarning;

            bool streaming = source.State == EmgSourceState.Streaming;
            switch (source.Contact)
            {
                case ElectrodeContact.Normal:
                    goodContactSeconds += Time.unscaledDeltaTime;
                    float remaining = Mathf.Max(0f, EmgConfig.ElectrodeGoodSeconds - goodContactSeconds);
                    electrodeText.color = UiKit.Good;
                    electrodeText.text = remaining > 0f ? "Electrodes OK. Hold still " + Mathf.CeilToInt(remaining) + " s…" : "Electrodes OK";
                    break;
                case ElectrodeContact.HighResistance:
                    goodContactSeconds = 0f;
                    electrodeText.color = UiKit.Warn;
                    electrodeText.text = "Weak contact: press the electrodes down or clean the skin again";
                    break;
                case ElectrodeContact.Detached:
                    goodContactSeconds = 0f;
                    electrodeText.color = UiKit.Bad;
                    electrodeText.text = "Electrode detached: reattach it";
                    break;
                default:
                    goodContactSeconds = 0f;
                    electrodeText.color = UiKit.Muted;
                    electrodeText.text = "Checking electrodes…";
                    break;
            }

            continueButton.interactable = streaming && goodContactSeconds >= EmgConfig.ElectrodeGoodSeconds;
        }

        private void HideSensorList()
        {
            foreach (Button sensorButton in sensorButtons)
            {
                sensorButton.gameObject.SetActive(false);
            }
        }
    }
}
