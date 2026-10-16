using UnityEngine;
using UnityEngine.EventSystems;

namespace CallibriEmg
{
    // The single place the game asks "did the player flap this frame?".
    // Muscle flaps and mouse clicks both feed it, so BirdControl and
    // GameMain don't need to know which one the patient is using.
    public static class FlapInput
    {
        private static int emgFlapFrame = -1;

        public static FlapSource LastSource { get; private set; } = FlapSource.None;

        internal static void RegisterEmgFlap()
        {
            emgFlapFrame = Time.frameCount;
        }

        // Same rules as Input.GetButtonDown: true for every caller during the
        // one frame a flap happened. That keeps GameMain and BirdControl
        // working exactly as they did when both polled "Fire1".
        public static bool GetFlapDown()
        {
            EmgSessionManager manager = EmgSessionManager.Instance;
            if (manager != null && manager.IsGamePaused)
            {
                return false;
            }

            if (emgFlapFrame == Time.frameCount)
            {
                LastSource = FlapSource.Emg;
                return true;
            }

            // Clicks on therapist buttons and sliders must not flap the bird.
            if (Input.GetButtonDown("Fire1") && !IsPointerOverUi())
            {
                LastSource = FlapSource.Mouse;
                return true;
            }
            return false;
        }

        private static bool IsPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }
    }
}
