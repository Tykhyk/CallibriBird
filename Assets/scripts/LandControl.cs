using UnityEngine;
using System.Collections.Generic;
using CallibriEmg;

public class LandControl : MonoBehaviour {

    // Units per second the ground scrolls left. Start() overwrites it with
    // the session's pipe speed, so the floor and the pipes always scroll at
    // the same rate instead of drifting relative to each other.
    public float moveSpeed = -0.96f;

    private bool moving = true;
    private float tileWidth = 0.48f;

    // Neighbouring tiles overlap by 2 pixels. The atlas is filtered
    // (bilinear), so each tile's edge blends in a sliver of whatever sits
    // next to it in the sprite sheet, which showed as a thin light line at
    // every seam. The overlap tucks that edge under the next tile.
    private const float SeamOverlap = 0.02f;
    private float tileStep;

    // One tile alone can't stay under the camera for a full scroll cycle:
    // once it has moved more than its own width, its trailing edge has
    // already scrolled past the visible area, leaving open sky (and no
    // collider) under the bird. So this object spawns extra copies of
    // itself at Start and drives all of them as a conveyor belt, recycling
    // whichever tile has scrolled fully off the left edge to the back of
    // the line. The tile count is sized from the actual camera width, so
    // it covers the screen at any aspect ratio, not just the one this was
    // tuned against.
    private readonly List<Transform> tiles = new List<Transform>();

    // Sprite's pivot is at its left edge (confirmed via the BoxCollider2D
    // offset), so tiles chain with a plain +tileWidth step, no half-width
    // math needed.
    void Start () {
        moveSpeed = -DifficultySettings.Current.pipeSpeed;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            tileWidth = sr.bounds.size.x;
        }
        tileStep = tileWidth - SeamOverlap;

        Camera cam = Camera.main;
        float cameraWidth = cam.orthographicSize * 2f * cam.aspect;
        float cameraLeftEdge = cam.transform.position.x - cam.orthographicSize * cam.aspect;

        // Anchor the first tile to the camera's actual left edge rather
        // than trusting the position authored in the scene: at a wider
        // aspect than this was tuned against, the camera sees further
        // left than that authored position, which left a permanent gap
        // of empty sky along the left side of the screen.
        transform.position = new Vector3(cameraLeftEdge, transform.position.y, transform.position.z);
        tiles.Add(transform);

        int tileCount = Mathf.Max(2, Mathf.CeilToInt(cameraWidth / tileStep) + 2);

        for (int i = 1; i < tileCount; i++)
        {
            GameObject clone = Instantiate(gameObject, transform.position + new Vector3(tileStep * i, 0f, 0f), transform.rotation, transform.parent);
            // Keep the exact "land" name: BirdControl's collision check
            // matches on other.name, and Instantiate would otherwise
            // suffix clones with "(Clone)".
            clone.name = gameObject.name;
            // Only one LandControl should run Update/own the tile list.
            // Disable before Destroy so the clone's own Start never fires
            // and spawns a further generation of tiles.
            LandControl cloneControl = clone.GetComponent<LandControl>();
            cloneControl.enabled = false;
            Destroy(cloneControl);
            // Clones don't need to be found individually (GameOver is
            // driven by the manager's own tile list), and leaving the
            // "movable" tag on them would make BroadcastMessage("GameOver")
            // fail since they no longer have a receiver for it.
            clone.tag = "Untagged";

            tiles.Add(clone.transform);
        }
    }

    // Update is called once per frame
    void Update () {
        if (!moving)
        {
            return;
        }

        float delta = moveSpeed * Time.deltaTime;
        float rightmostX = float.NegativeInfinity;
        foreach (Transform tile in tiles)
        {
            tile.position += new Vector3(delta, 0f, 0f);
            if (tile.position.x > rightmostX)
            {
                rightmostX = tile.position.x;
            }
        }

        Camera cam = Camera.main;
        float cameraLeftEdge = cam.transform.position.x - cam.orthographicSize * cam.aspect;

        foreach (Transform tile in tiles)
        {
            if (tile.position.x + tileWidth <= cameraLeftEdge)
            {
                tile.position = new Vector3(rightmostX + tileStep, tile.position.y, tile.position.z);
                rightmostX = tile.position.x;
            }
        }
    }

    public void GameOver()
    {
        moving = false;
    }
}
