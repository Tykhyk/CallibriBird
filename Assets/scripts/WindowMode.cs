using UnityEngine;

// The app runs in a normal, resizable window with the system's own title
// bar (close, minimize, maximize), like other desktop apps (Tykhyk,
// 2026-10-23). Earlier builds opened borderless full screen, which has no
// title bar and no close button.
//
// Unity remembers the last window mode per computer and reuses it on the
// next launch, ignoring the project's default. So on the first launch of
// this version the window is switched to windowed mode once; after that,
// whatever the user chooses (resize, maximize, full screen) is remembered.
//
// F11 toggles full screen on both Mac and Windows. Windows also has
// Alt+Enter built in, and the Mac title bar's green button works as usual.
public class WindowMode : MonoBehaviour {

    private const string PrefKey = "WindowModeVersion";
    private const int CurrentVersion = 1;
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 800;
    // Many Windows laptops have 1366 x 768 screens: a 1280 x 800 window
    // would be taller than the screen and could push the title bar (and
    // its close button) off the top. The window never takes more than this
    // share of the screen.
    private const float MaxScreenShare = 0.9f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install () {
        if (Application.isEditor)
        {
            return;
        }
        if (PlayerPrefs.GetInt(PrefKey, 0) < CurrentVersion)
        {
            SetWindowed();
            PlayerPrefs.SetInt(PrefKey, CurrentVersion);
            PlayerPrefs.Save();
        }
        GameObject host = new GameObject("WindowMode");
        DontDestroyOnLoad(host);
        host.AddComponent<WindowMode>();
    }

    void Update () {
        if (!Input.GetKeyDown(KeyCode.F11))
        {
            return;
        }
        if (Screen.fullScreenMode == FullScreenMode.Windowed)
        {
            Resolution native = Screen.currentResolution;
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            SetWindowed();
        }
    }

    // The default size, shrunk to fit the screen if needed and keeping its
    // 16:10 shape.
    static void SetWindowed () {
        float fit = Mathf.Min(1f,
            Display.main.systemWidth * MaxScreenShare / DefaultWidth,
            Display.main.systemHeight * MaxScreenShare / DefaultHeight);
        Screen.SetResolution(Mathf.RoundToInt(DefaultWidth * fit), Mathf.RoundToInt(DefaultHeight * fit), FullScreenMode.Windowed);
    }
}
