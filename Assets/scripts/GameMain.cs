using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using DG.Tweening;
using CallibriEmg;

public class GameMain : MonoBehaviour {

    public GameObject bird;
    public GameObject readyPic;
    public GameObject tipPic;
    public GameObject scoreMgr;
    public GameObject pipeSpawner;
    public GameObject gameOverPic;

    // gameOverPic starts parked above the camera's view; drop it down by
    // this many units to bring it on screen when the run ends.
    public float gameOverDropDistance = 1.44f;
    public float gameOverDropTime = 0.4f;

    // How long to wait after death before a tap is allowed to restart,
    // so the same tap that caused death doesn't instantly restart the game.
    public float restartDelay = 1f;

    private bool gameStarted = false;
    private bool gameOver = false;
    private float gameOverTime = 0f;

    // A patient playing with the sensor restarts with a squeeze, not a tap.
    private const string RestartPrompt = "Squeeze or Tap to Restart";

    private GUIStyle restartTextStyle;
    private GUIStyle restartShadowStyle;

	// Use this for initialization
	void Start () {

	}

	// Update is called once per frame
	void Update () {
        if (!gameStarted && FlapInput.GetFlapDown())
        {
            gameStarted = true;
            StartGame();
            return;
        }

        if (gameStarted && !gameOver && bird.GetComponent<BirdControl>().IsDead)
        {
            gameOver = true;
            gameOverTime = Time.time;
            EmgSessionManager.ReportRoundEnded(scoreMgr.GetComponent<ScoreMgr>().Score);

            gameOverPic.transform.DOMoveY(gameOverPic.transform.position.y - gameOverDropDistance, gameOverDropTime)
                .SetEase(Ease.OutBounce);
        }

        if (gameOver && Time.time - gameOverTime >= restartDelay && FlapInput.GetFlapDown())
        {
            RestartGame();
        }
    }

    private void RestartGame()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);
    }

    void OnGUI()
    {
        // While the sensor is disconnected, the session's pause card is
        // shown instead; IMGUI always draws on top of it otherwise.
        EmgSessionManager session = EmgSessionManager.Instance;
        if (!gameOver || (session != null && session.IsGamePaused))
        {
            return;
        }

        // Lazy-built the first time it's needed: GUIStyle can only be
        // constructed once GUI.skin is available, which isn't guaranteed
        // yet in Start().
        if (restartTextStyle == null)
        {
            restartTextStyle = new GUIStyle();
            restartTextStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.06f);
            restartTextStyle.fontStyle = FontStyle.Bold;
            restartTextStyle.alignment = TextAnchor.MiddleCenter;
            restartTextStyle.normal.textColor = Color.white;

            restartShadowStyle = new GUIStyle(restartTextStyle);
            restartShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        }

        Rect rect = new Rect(0f, Screen.height * 0.35f, Screen.width, restartTextStyle.fontSize + 20);
        Rect shadowRect = new Rect(rect.x + 3f, rect.y + 3f, rect.width, rect.height);

        GUI.Label(shadowRect, RestartPrompt, restartShadowStyle);
        GUI.Label(rect, RestartPrompt, restartTextStyle);
    }

    private void StartGame()
    {
        // Reported before the first JumpUp() so that flap counts toward
        // this round in the session log.
        EmgSessionManager.ReportRoundStarted();

        BirdControl control = bird.GetComponent<BirdControl>();
        control.inGame = true;
        control.JumpUp();

        readyPic.GetComponent<SpriteRenderer>().material.DOFade(0f, 0.2f);
        tipPic.GetComponent<SpriteRenderer>().material.DOFade(0f, 0.2f);

        scoreMgr.GetComponent<ScoreMgr>().SetScore(0);
        pipeSpawner.GetComponent<PipeSpawner>().StartSpawning();
    }
}
