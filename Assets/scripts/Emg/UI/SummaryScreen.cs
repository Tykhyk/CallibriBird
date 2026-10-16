using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CallibriEmg
{
    // Step 6 (therapist): the session report, with shortcuts to the HTML
    // report with charts and to the folder holding every data file.
    public sealed class SummaryScreen : MonoBehaviour
    {
        private string folder = "";
        private string report = "";

        private void Start()
        {
            EmgSessionManager manager = EmgSessionManager.Instance;
            Canvas canvas = UiKit.CreateCanvas("Summary Canvas", 0);
            RectTransform card = UiKit.Column("Card", canvas.transform, 420f, 10, 16, UiKit.Card);
            UiKit.Header(card, "SESSION COMPLETE", "Summary");

            Text text = UiKit.ScrollingText(card, 430f, 13);
            // The summary is plain text that includes what was typed on the
            // intake form. With rich text on, a typed "<b>" or "<color>" tag
            // would change how the summary looks.
            text.supportRichText = false;
            if (manager != null && !string.IsNullOrEmpty(manager.SummaryText))
            {
                text.text = manager.SummaryText;
                folder = manager.SessionFolder;
                report = manager.ReportPath;
            }
            else
            {
                text.text = "No session data is available. Start a new session from the Connect screen.";
            }

            Text folderText = UiKit.Label(card, string.IsNullOrEmpty(folder) ? "" : "Saved in: " + folder, 11, UiKit.Muted, FontStyle.Normal, TextAnchor.MiddleLeft);
            folderText.horizontalOverflow = HorizontalWrapMode.Wrap;

            Button openReport = UiKit.Button(card, "Open report with charts", UiKit.Accent, () => OpenPath(report));
            openReport.interactable = !string.IsNullOrEmpty(report);

            RectTransform buttons = UiKit.Row(card, 8, 46f);
            Button open = UiKit.Button(buttons, "Open data folder", UiKit.Neutral, () => OpenPath(folder));
            open.interactable = !string.IsNullOrEmpty(folder);
            UiKit.Button(buttons, "New session", UiKit.Good, NewSession);
        }

        // A file:// URI with escaped spaces: the data path contains
        // "Unity Projects" and "Patients' reports", which a raw path would
        // break on. A folder opens in Finder, report.html in the default
        // browser.
        private static void OpenPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            Application.OpenURL(new Uri(path).AbsoluteUri);
        }

        private void NewSession()
        {
            EmgSessionManager.DiscardSession();
            SceneManager.LoadScene(EmgSessionManager.ConnectSceneName);
        }
    }
}
