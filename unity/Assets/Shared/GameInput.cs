using UnityEngine;
using UnityEngine.InputSystem;

namespace Shared
{
    /// <summary>
    /// One place for player input: keyboard + mouse through the legacy manager, gamepad through the Input System.
    /// PlayStation naming: Square = hook, Cross = boost/jump, Circle = reel, Triangle / R2 = slash. Xbox: X, A, B, Y / RT.
    /// </summary>
    public static class GameInput
    {
        static Gamepad Pad => Gamepad.current;
        public static Vector2 Move
        {
            get
            {
                var k = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                if (k.sqrMagnitude < 0.01f && Pad != null) k = Pad.leftStick.ReadValue();
                return k;
            }
        }
        /// <summary>Look delta this frame in "mouse units" (gamepad right stick scaled per second).</summary>
        public static Vector2 Look
        {
            get
            {
                var m = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
                if (Pad != null) { var s = Pad.rightStick.ReadValue(); if (s.sqrMagnitude > 0.02f) m += s * (4.5f * Time.unscaledDeltaTime * 60f * 0.1f); }
                return m;
            }
        }
        public static bool Hook => Input.GetMouseButton(1) || (Pad != null && (Pad.buttonWest.isPressed || Pad.leftShoulder.isPressed));
        public static bool Boost => Input.GetKey(KeyCode.Space) || (Pad != null && Pad.buttonSouth.isPressed);
        public static bool Reel => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || (Pad != null && (Pad.buttonEast.isPressed || Pad.leftTrigger.ReadValue() > 0.4f));
        public static bool SlashDown => Input.GetMouseButtonDown(0) || (Pad != null && (Pad.buttonNorth.wasPressedThisFrame || Pad.rightTrigger.wasPressedThisFrame));
        public static bool AnyClick => Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
        public static bool Escape => Input.GetKeyDown(KeyCode.Escape) || (Pad != null && Pad.startButton.wasPressedThisFrame);

        /// <summary>Call every frame from the player: click captures the mouse into the game, Escape releases it.</summary>
        public static void UpdateCursor()
        {
            if (Application.isBatchMode) return;
            if (Cursor.lockState != CursorLockMode.Locked) { if (AnyClick) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; } }
            else if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        public static bool CursorCaptured => Cursor.lockState == CursorLockMode.Locked;
    }
}
