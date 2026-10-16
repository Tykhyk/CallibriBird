using UnityEngine;
using System.Collections;

public class PipeMove : MonoBehaviour {

	public float moveSpeed;

    // A pipe is removed once it is fully past the camera's left edge. The
    // old fixed x = -0.4 was the edge of the original portrait screen; on
    // a wider screen pipes vanished in plain view.
    private const float PipeHalfWidth = 0.26f;
    private float despawnX = -0.4f;

	// Use this for initialization
	void Start () {
		Rigidbody2D body = transform.GetComponent<Rigidbody2D>();
		body.linearVelocity = new Vector2(moveSpeed, 0);

        Camera cam = Camera.main;
        if (cam != null)
        {
            despawnX = Mathf.Min(despawnX, cam.transform.position.x - cam.orthographicSize * cam.aspect - PipeHalfWidth);
        }
	}

	// Update is called once per frame
	void Update () {
		if (transform.position.x <= despawnX)
		{
			Destroy(gameObject);
		}
	}

	public void GameOver()
	{
		Rigidbody2D body = transform.GetComponent<Rigidbody2D>();
		body.linearVelocity = new Vector2(0, 0);
	}
}
