using UnityEngine;

public class ScreenFillTiler : MonoBehaviour {

    // Copies overlap by 2 pixels: the atlas is filtered (bilinear), so a
    // sprite's edge blends in a sliver of its neighbour in the sprite
    // sheet, which showed as a thin light line at every seam.
    private const float SeamOverlap = 0.02f;

    // The art was drawn for a portrait phone screen: the background is only
    // 2.88 units wide. The camera's height is fixed (orthographic size 2.56
    // is exactly the background's height), but its width follows the
    // window, so on a laptop screen the camera sees far past both edges of
    // the picture. This places copies of the sprite to the left and right
    // until the camera's whole width is covered.
    //
    // Copies are children, so they move with the original (the static
    // background and ground in the session scenes never move, and the game
    // scene's scrolling ground already tiles itself in LandControl).
    void Start () {
        SpriteRenderer source = GetComponent<SpriteRenderer>();
        Camera cam = Camera.main;
        if (source == null || source.sprite == null || cam == null)
        {
            return;
        }

        float tileWidth = source.bounds.size.x - SeamOverlap;
        if (tileWidth <= 0f)
        {
            return;
        }

        float halfView = cam.orthographicSize * cam.aspect;
        float viewLeft = cam.transform.position.x - halfView;
        float viewRight = cam.transform.position.x + halfView;
        float spriteLeft = source.bounds.min.x;

        int first = Mathf.FloorToInt((viewLeft - spriteLeft) / tileWidth);
        int last = Mathf.CeilToInt((viewRight - spriteLeft) / tileWidth) - 1;
        float localStep = tileWidth / Mathf.Max(0.0001f, transform.lossyScale.x);

        for (int i = first; i <= last; i++)
        {
            if (i == 0)
            {
                continue;
            }

            GameObject copy = new GameObject(name + " tile " + i);
            copy.transform.SetParent(transform, false);
            // Each copy sits a hair closer to the camera than its left
            // neighbour, so it is drawn on top of that neighbour's right
            // edge, which is the edge that blends with the sprite sheet.
            copy.transform.localPosition = new Vector3(localStep * i, 0f, -0.001f * i);

            SpriteRenderer copyRenderer = copy.AddComponent<SpriteRenderer>();
            copyRenderer.sprite = source.sprite;
            copyRenderer.color = source.color;
            copyRenderer.flipX = source.flipX;
            copyRenderer.flipY = source.flipY;
            copyRenderer.sortingLayerID = source.sortingLayerID;
            copyRenderer.sortingOrder = source.sortingOrder;
            copyRenderer.sharedMaterial = source.sharedMaterial;
        }
    }
}
