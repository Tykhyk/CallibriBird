using UnityEngine;
using System.Collections;
using DG.Tweening;
using CallibriEmg;

public class BirdControl : MonoBehaviour {

	public float upSpeed = 10;

    // Approximately how long (seconds) the nose-down pivot takes once the
    // bird starts falling, via SmoothDampAngle. Unlike a plain
    // velocity*rate formula, this eases continuously instead of
    // hard-clamping at the bottom angle, so there's no sudden kink where
    // the motion stops dead.
    public float rotateSmoothTime = 0.5f;

    // Separate, much faster smoothing for the nose-up pivot on jump. It
    // needs its own value rather than sharing rotateSmoothTime: the bird
    // only keeps rising for a brief window (upSpeed/gravity-limited, not
    // tunable here) before gravity turns it over, so if this eased as
    // slowly as the fall does, it would still be mid-ease when the target
    // flips back to nose-down and would never visibly reach the full
    // up-angle.
    public float upRotateSmoothTime = 0.08f;
    private float rotateAngularVelocity = 0f;

    // How far the bird pitches up (degrees) right after a jump.
    public float upRotationAngle = 30f;
    public GameObject scoreMgr;

    public AudioClip jumpUp;
    public AudioClip hit;
    public AudioClip score;

    public bool inGame = false;

	private bool dead = false;
	private bool landed = false;

    public bool IsDead
    {
        get { return dead; }
    }

    private Sequence birdSequence;

    // GameMain.StartGame() calls JumpUp() for the flap that starts a round,
    // and this script's own Update() can see that same flap in the same
    // frame. Remembering the frame stops one flap from counting twice.
    private int lastJumpFrame = -1;

    private float flightGravityScale;
    private bool flightStarted = false;

    // Use this for initialization
    void Start () {
        // The therapist's flap pace (how many seconds one squeeze keeps the
        // bird up) sets both gravity and jump strength, so the jump stays the
        // same height at any pace; it replaces the Inspector's upSpeed.
        //
        // Gravity is only switched on once the round starts. While the bird
        // hovers on the "Get Ready" screen the tween holds it in place, but
        // gravity would keep building up falling speed underneath. After
        // 15-20 s of waiting, one physics step then carries it into the
        // ground trigger, which switches gravity off for good, and the
        // round starts with a bird that floats away. Patients often wait
        // that long.
        DifficultySettings difficulty = DifficultySettings.Current;
        flightGravityScale = difficulty.GravityScale;
        upSpeed = difficulty.JumpVelocity;
        GetComponent<Rigidbody2D>().gravityScale = 0f;

        float birdOffset = 0.05f;
        float birdTime = 0.3f;
        float birdStartY = transform.position.y;

        birdSequence = DOTween.Sequence();

        birdSequence.Append(transform.DOMoveY(birdStartY + birdOffset, birdTime).SetEase(Ease.Linear))
            .Append(transform.DOMoveY(birdStartY - 2 * birdOffset, 2 * birdTime).SetEase(Ease.Linear))
            .Append(transform.DOMoveY(birdStartY, birdTime).SetEase(Ease.Linear))
            .SetLoops(-1);
    }
	
	// Update is called once per frame
	void Update () {
        if (!inGame)
        {
            return;
        }
        birdSequence.Kill();

        if (!flightStarted)
        {
            flightStarted = true;
            GetComponent<Rigidbody2D>().gravityScale = flightGravityScale;
        }

		if (!dead)
		{
			if (FlapInput.GetFlapDown())
			{
                JumpUp();
			}

            CheckOutOfBounds();
		}

		if (!landed)
		{
			float v = transform.GetComponent<Rigidbody2D>().linearVelocity.y;
			float target = v >= 0f ? upRotationAngle : -90f;
			float smoothTime = v >= 0f ? upRotateSmoothTime : rotateSmoothTime;

			float current = transform.eulerAngles.z;
			if (current > 180f)
			{
				current -= 360f;
			}

			float smoothed = Mathf.SmoothDampAngle(current, target, ref rotateAngularVelocity, smoothTime);
			transform.rotation = Quaternion.Euler(0f, 0f, smoothed);
		}
		else
		{
			transform.GetComponent<Rigidbody2D>().rotation = -90;
		}
	}

	void OnTriggerEnter2D (Collider2D other)
	{
		if (other.name == "land" || other.name == "pipe_up" || other.name == "pipe_down")
		{
            Die();

			if (other.name == "land")
			{
				transform.GetComponent<Rigidbody2D>().gravityScale = 0;
				transform.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(0, 0);

				landed = true;
			}
		}

        if (other.name == "pass_trigger")
        {
            ScoreMgr scoreKeeper = scoreMgr.GetComponent<ScoreMgr>();
            scoreKeeper.AddScore();
            EmgSessionManager.ReportPipePassed(scoreKeeper.Score);
            AudioSource.PlayClipAtPoint(score, Vector3.zero);
        }


	}

    // Nothing ever stopped the bird from flying off the top of the screen,
    // and a gap in the scrolling ground could let it fall through the
    // bottom without hitting a collider. Both ends of the camera's view
    // are a hard boundary regardless of what the bird collided with.
    private void CheckOutOfBounds()
    {
        Camera cam = Camera.main;
        float top = cam.transform.position.y + cam.orthographicSize;
        float bottom = cam.transform.position.y - cam.orthographicSize;

        if (transform.position.y > top || transform.position.y < bottom)
        {
            Die();
        }
    }

    private void Die()
    {
        if (dead)
        {
            return;
        }

        GameObject[] objs = GameObject.FindGameObjectsWithTag("movable");
        foreach (GameObject g in objs)
        {
            g.BroadcastMessage("GameOver");
        }

        GetComponent<Animator>().SetTrigger("die");
        AudioSource.PlayClipAtPoint(hit, Vector3.zero);
    }

    public void JumpUp()
    {
        if (lastJumpFrame == Time.frameCount)
        {
            return;
        }
        lastJumpFrame = Time.frameCount;
        EmgSessionManager.ReportFlap(FlapInput.LastSource);

        transform.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(0, upSpeed);
        rotateAngularVelocity = 0f;
        AudioSource.PlayClipAtPoint(jumpUp, Vector3.zero);
    }
	
	public void GameOver()
	{
		dead = true;
	}

    void OnDestroy()
    {
        if (birdSequence != null)
        {
            birdSequence.Kill();
        }
    }
}
