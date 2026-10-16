using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CallibriEmg
{
    // Step 2: who is playing? The therapist picks a person from the list of
    // everyone in "Patients' reports", or adds a new one (Tykhyk, 2026-10-16).
    // Picking an existing person links the session to their folder and
    // history, so it never depends on the name being typed the same way
    // again. The sensor keeps recording in the background the whole time.
    public sealed class IntakeScreen : MonoBehaviour
    {
        private const float ListHeight = 300f;

        private EmgSessionManager manager;
        private List<PatientEntry> people = new List<PatientEntry>();
        private readonly List<KeyValuePair<PatientEntry, GameObject>> rows = new List<KeyValuePair<PatientEntry, GameObject>>();
        private PatientEntry chosen;

        private GameObject chooseCard;
        private InputField searchField;
        private RectTransform listContent;
        private Text emptyText;

        private GameObject formCard;
        private Text formStep;
        private Text formTitle;
        private GameObject identityGroup;
        private Text identityText;
        private InputField nameField;
        private InputField dobField;
        private InputField muscleField;
        private InputField indicationField;
        private Text errorText;
        private Button backButton;
        private readonly List<InputField> tabOrder = new List<InputField>();

        private void Start()
        {
            manager = EmgSessionManager.Instance;
            if (manager == null || manager.Source == null)
            {
                // Pressing Play directly in this scene has no sensor session;
                // every session has to begin on the Connect screen.
                SceneManager.LoadScene(EmgSessionManager.ConnectSceneName);
                return;
            }
            manager.SetPhase(SessionPhase.Intake);
            people = PatientDirectory.LoadAll(DataLocation.Root);
            BuildUi();
            if (people.Count == 0)
            {
                // Nobody to choose from yet: go straight to the form.
                ShowForm(null);
            }
            else
            {
                ShowChooser();
            }
        }

        private void BuildUi()
        {
            Canvas canvas = UiKit.CreateCanvas("Intake Canvas", 0);
            BuildChooser(canvas.transform);
            BuildForm(canvas.transform);
            SessionStatusStrip.Create(canvas.transform);
        }

        private void BuildChooser(Transform canvas)
        {
            RectTransform card = UiKit.Column("Choose Person", canvas, 410f, 8, 18, UiKit.Card);
            UiKit.Header(card, "STEP 2 OF 6 · THERAPIST", "Who is playing?");
            searchField = UiKit.Input(card, "Search by name", false);
            searchField.onValueChanged.AddListener(FilterList);

            listContent = UiKit.ScrollList(card, ListHeight, 6);
            emptyText = UiKit.Label(listContent, "No one matches.", 14, UiKit.Muted, FontStyle.Italic);
            UiKit.SetHeight(emptyText, 40f);
            foreach (PatientEntry entry in people)
            {
                PatientEntry person = entry;
                Button row = UiKit.Button(listContent, DescribeRow(person), UiKit.Neutral, () => ShowForm(person), 56f, 15);
                // Names are typed by people: show them as plain text, so a
                // typed "<b>" can't change the formatting.
                row.GetComponentInChildren<Text>().supportRichText = false;
                rows.Add(new KeyValuePair<PatientEntry, GameObject>(person, row.gameObject));
            }

            UiKit.Button(card, "+ New person", UiKit.Good, () => ShowForm(null));
            UiKit.Label(card, "The sensor is recording while you choose.", 12, UiKit.Muted, FontStyle.Italic);
            chooseCard = card.gameObject;
        }

        private void BuildForm(Transform canvas)
        {
            RectTransform card = UiKit.Column("Person Details", canvas, 410f, 8, 18, UiKit.Card);
            formStep = UiKit.Label(card, "STEP 2 OF 6 · PATIENT", 13, UiKit.Muted, FontStyle.Bold);
            formTitle = UiKit.Label(card, "", 26, UiKit.Ink, FontStyle.Bold);
            formTitle.supportRichText = false;

            // Name and date of birth identify the person, so they are typed
            // only once, for a new person. An existing person's are shown
            // instead of editable, so a slip of the keyboard can't change
            // who the history belongs to.
            RectTransform identity = UiKit.Rect("Identity", card);
            VerticalLayoutGroup identityLayout = identity.gameObject.AddComponent<VerticalLayoutGroup>();
            identityLayout.spacing = 8;
            identityLayout.childControlWidth = true;
            identityLayout.childControlHeight = true;
            identityLayout.childForceExpandHeight = false;
            nameField = AddField(identity, "Full Name", "e.g. Jane Smith", false);
            dobField = AddField(identity, "Date of Birth (MM/DD/YYYY)", "e.g. 04/23/1985", false);
            identityGroup = identity.gameObject;
            identityText = UiKit.Label(card, "", 15, UiKit.Muted, FontStyle.Normal);
            identityText.supportRichText = false;

            muscleField = AddField(card, "Target Muscle", "", false);
            indicationField = AddField(card, "Clinical Indication", "Reason for the session", true);

            errorText = UiKit.Label(card, "", 14, UiKit.Bad, FontStyle.Bold);
            RectTransform buttons = UiKit.Row(card, 8, 46f);
            backButton = UiKit.Button(buttons, "Back", UiKit.Neutral, ShowChooser);
            UiKit.Button(buttons, "Continue", UiKit.Good, OnContinue);
            UiKit.Label(card, "The sensor is recording while you fill this in.", 12, UiKit.Muted, FontStyle.Italic);
            formCard = card.gameObject;
        }

        private InputField AddField(Transform parent, string label, string placeholder, bool multiline)
        {
            UiKit.Label(parent, label, 14, UiKit.Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            InputField field = UiKit.Input(parent, placeholder, multiline);
            tabOrder.Add(field);
            return field;
        }

        private static string DescribeRow(PatientEntry entry)
        {
            string sessions = entry.SessionCount == 1 ? "1 session" : entry.SessionCount.ToString(CultureInfo.InvariantCulture) + " sessions";
            string last = entry.SessionCount > 0 ? " · last " + entry.LastSession.ToString("MMM d", CultureInfo.InvariantCulture) : "";
            return entry.Record.fullName + "\nborn " + entry.Record.dateOfBirth + " · " + sessions + last;
        }

        private void FilterList(string query)
        {
            string wanted = ProgressHistory.NormalizeName(query);
            int shown = 0;
            foreach (KeyValuePair<PatientEntry, GameObject> row in rows)
            {
                bool match = wanted.Length == 0 || ProgressHistory.NormalizeName(row.Key.Record.fullName).Contains(wanted);
                row.Value.SetActive(match);
                if (match)
                {
                    shown++;
                }
            }
            emptyText.gameObject.SetActive(shown == 0);
        }

        private void ShowChooser()
        {
            chosen = null;
            chooseCard.SetActive(true);
            formCard.SetActive(false);
            FilterList(searchField.text);
        }

        // person == null: a new person; otherwise their details, ready to
        // confirm today's target muscle and indication.
        private void ShowForm(PatientEntry person)
        {
            chosen = person;
            chooseCard.SetActive(false);
            formCard.SetActive(true);
            errorText.text = "";

            bool isNew = person == null;
            // With nobody in the list yet there is nothing to go back to.
            backButton.gameObject.SetActive(people.Count > 0);
            identityGroup.SetActive(isNew);
            identityText.gameObject.SetActive(!isNew);
            formStep.text = isNew ? "STEP 2 OF 6 · NEW PERSON" : "STEP 2 OF 6 · CONFIRM";
            if (isNew)
            {
                formTitle.text = "New person";
                nameField.text = "";
                dobField.text = "";
                muscleField.text = "Frontalis";
                indicationField.text = "";
                nameField.ActivateInputField();
            }
            else
            {
                formTitle.text = person.Record.fullName;
                identityText.text = "Born " + person.Record.dateOfBirth + " · " + (person.SessionCount == 1 ? "1 earlier session" : person.SessionCount + " earlier sessions");
                muscleField.text = string.IsNullOrWhiteSpace(person.Record.targetMuscle) ? "Frontalis" : person.Record.targetMuscle;
                indicationField.text = person.Record.clinicalIndication;
                indicationField.ActivateInputField();
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Tab) || !formCard.activeSelf)
            {
                return;
            }
            List<InputField> visible = tabOrder.FindAll(field => field.gameObject.activeInHierarchy);
            if (visible.Count == 0)
            {
                return;
            }
            int focused = visible.FindIndex(field => field.isFocused);
            int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
            int next = focused < 0 ? 0 : (focused + step + visible.Count) % visible.Count;
            visible[next].ActivateInputField();
        }

        private void OnContinue()
        {
            IntakeData intake = new IntakeData
            {
                fullName = chosen != null ? chosen.Record.fullName : nameField.text.Trim(),
                dateOfBirth = chosen != null ? chosen.Record.dateOfBirth : dobField.text.Trim(),
                targetMuscle = muscleField.text.Trim(),
                clinicalIndication = indicationField.text.Trim(),
            };

            string problem = intake.Validate(DateTime.Today);
            if (problem.Length > 0)
            {
                errorText.text = problem;
                return;
            }

            PatientEntry person = chosen;
            if (person == null)
            {
                // Someone already in the list, typed in again as new: use the
                // existing person so their history stays in one place.
                person = PatientDirectory.FindSamePerson(people, intake.fullName, intake.dateOfBirth);
            }
            if (person == null)
            {
                try
                {
                    person = PatientDirectory.Create(DataLocation.Root, PatientRecord.FromIntake(intake, DateTime.Now));
                }
                catch (Exception e)
                {
                    errorText.text = "Could not create the person's folder: " + e.Message;
                    Debug.LogError("[EMG] " + errorText.text);
                    return;
                }
            }

            manager.SubmitIntake(intake, person);
            SceneManager.LoadScene(EmgSessionManager.MeditationSceneName);
        }
    }
}
