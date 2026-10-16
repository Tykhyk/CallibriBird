using UnityEngine;
using System.Collections;
using CallibriEmg;

public class PipeSpawner : MonoBehaviour {

	public float spawnTime = 5f;		// The amount of time between each spawn.
	public float spawnDelay = 3f;		// The amount of time before spawning starts.
	public GameObject pipe;	
	public float[] heights;

    // Local Y of the middle of the opening in the pipe prefab: pipe_down's
    // top edge sits at -0.4 and pipe_up's bottom edge at 1.4.
    private const float GapCentreOffset = 0.5f;

    // How far past the camera's right edge new pipes appear, so they slide
    // in from off-screen at any window width. Half a pipe (0.26) plus a
    // little slack.
    private const float SpawnMargin = 0.4f;

    // At a slow flap pace the bird climbs about one jump (0.5 units) per
    // squeeze, so a big height difference between neighbouring openings
    // would force a burst of rapid squeezes. Each opening is kept within
    // 1.6 jumps of the previous one, starting from the bird's height.
    private const float MaxOpeningStepInJumps = 1.6f;

    private float landTop;
    private float cameraTop;
    private float spawnX;
    private float lastCentre;
	
	void Start ()
	{
		// Start calling the Spawn function repeatedly after a delay .		
	}

    public void StartSpawning()
    {
        // The spacing between pipes is a therapist setting, so it replaces
        // the Inspector value for the round.
        spawnTime = DifficultySettings.Current.spawnInterval;

        LandControl land = FindAnyObjectByType<LandControl>();
        landTop = land != null ? land.GetComponent<BoxCollider2D>().bounds.max.y : 0f;
        Camera cam = Camera.main;
        cameraTop = cam.transform.position.y + cam.orthographicSize;
        // The spawner's position in the scene sits just past the right
        // edge of the original portrait screen. On a wider screen that is
        // mid-view, so pipes would pop into existence in front of the bird.
        spawnX = Mathf.Max(transform.position.x, cam.transform.position.x + cam.orthographicSize * cam.aspect + SpawnMargin);

        BirdControl bird = FindAnyObjectByType<BirdControl>();
        lastCentre = bird != null ? bird.transform.position.y : cam.transform.position.y;

        InvokeRepeating("Spawn", spawnDelay, spawnTime);
    }
	
	
	void Spawn ()
	{
		DifficultySettings difficulty = DifficultySettings.Current;
		int heightIndex = Random.Range(0, heights.Length);
		float maxStep = DifficultySettings.JumpHeight * MaxOpeningStepInJumps;
		float centre = Mathf.Clamp(heights[heightIndex] + GapCentreOffset, lastCentre - maxStep, lastCentre + maxStep);
		float rootY = KeepOpeningOnScreen(centre - GapCentreOffset, difficulty.gapSize);
		lastCentre = rootY + GapCentreOffset;
		Vector2 pos = new Vector2(spawnX, rootY);

		GameObject spawned = Instantiate(pipe, pos, transform.rotation);
        spawned.GetComponent<PipeMove>().moveSpeed = -difficulty.pipeSpeed;
        ApplyGap(spawned.transform, difficulty.gapSize);
	}

    // Moves both pipes apart (or together) around the middle of the
    // opening, and resizes the scoring trigger to match. Children are
    // found by name because BirdControl's collision checks depend on
    // these exact names anyway.
    private static void ApplyGap(Transform spawned, float gap)
    {
        float half = gap / 2f;
        foreach (Transform child in spawned.GetComponentsInChildren<Transform>())
        {
            if (child.name == "pipe_up")
            {
                child.localPosition = new Vector3(child.localPosition.x, GapCentreOffset + half, child.localPosition.z);
            }
            else if (child.name == "pipe_down")
            {
                child.localPosition = new Vector3(child.localPosition.x, GapCentreOffset - half, child.localPosition.z);
            }
            else if (child.name == "pass_trigger")
            {
                BoxCollider2D trigger = child.GetComponent<BoxCollider2D>();
                trigger.size = new Vector2(trigger.size.x, gap);
            }
        }
    }

    // A wide opening could reach into the ground or above the top of the
    // screen, where the bird can't go (flying off the top is a death).
    // The whole opening is kept between the ground and the screen top, so
    // the gap the therapist chose is the gap the patient actually gets.
    private float KeepOpeningOnScreen(float rootY, float gap)
    {
        float half = gap / 2f;
        float centre = rootY + GapCentreOffset;
        float minCentre = landTop + half;
        float maxCentre = Mathf.Max(minCentre, cameraTop - half);
        return Mathf.Clamp(centre, minCentre, maxCentre) - GapCentreOffset;
    }

	public void GameOver()
	{
		CancelInvoke("Spawn");
	}
}
