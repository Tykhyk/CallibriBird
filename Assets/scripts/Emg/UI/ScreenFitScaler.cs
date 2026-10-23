using UnityEngine;
using UnityEngine.UI;

namespace CallibriEmg
{
    // Every screen's cards are laid out for UiKit's 450 x 800 reference
    // screen, and normally scale with the window's height. That keeps tall
    // cards on screen in wide windows. In a window narrower than the
    // reference shape, though, height scaling would make the cards wider
    // than the window and cut off their sides. The app runs in a resizable
    // window, so the choice is made every frame: scale by height in wide
    // windows, by width in narrow ones.
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class ScreenFitScaler : MonoBehaviour
    {
        private CanvasScaler scaler;

        private void Awake()
        {
            scaler = GetComponent<CanvasScaler>();
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            Vector2 reference = scaler.referenceResolution;
            if (Screen.height <= 0 || reference.y <= 0f)
            {
                return;
            }
            float referenceAspect = reference.x / reference.y;
            float aspect = (float)Screen.width / Screen.height;
            scaler.matchWidthOrHeight = aspect < referenceAspect ? 0f : 1f;
        }
    }
}
