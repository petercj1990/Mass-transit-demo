using System;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Thin wrapper over the legacy Input Manager. If the project is set to the new Input System
    /// only, the legacy API throws; we catch that once and tell the player how to fix it instead
    /// of spamming exceptions every frame.
    /// </summary>
    public static class Keys
    {
        public static bool Broken { get; private set; }
        public static string BrokenReason { get; private set; }

        private static bool Guard(Func<bool> f)
        {
            if (Broken) return false;
            try
            {
                return f();
            }
            catch (InvalidOperationException e)
            {
                Broken = true;
                BrokenReason = e.Message;
                Debug.LogError("[Kinforge] Legacy input is disabled. Set Edit > Project Settings > Player > Active Input Handling to 'Both' or 'Input Manager (Old)'.\n" + e.Message);
                return false;
            }
        }

        public static bool Down(KeyCode k) => Guard(() => Input.GetKeyDown(k));
        public static bool Held(KeyCode k) => Guard(() => Input.GetKey(k));
        public static bool MouseDown(int b) => Guard(() => Input.GetMouseButtonDown(b));
        public static bool MouseUp(int b) => Guard(() => Input.GetMouseButtonUp(b));
        public static bool MouseHeld(int b) => Guard(() => Input.GetMouseButton(b));

        public static Vector2 Mouse
        {
            get
            {
                if (Broken) return Vector2.zero;
                try
                {
                    return Input.mousePosition;
                }
                catch (InvalidOperationException)
                {
                    Broken = true;
                    return Vector2.zero;
                }
            }
        }

        public static float Scroll
        {
            get
            {
                if (Broken) return 0f;
                try
                {
                    return Input.mouseScrollDelta.y;
                }
                catch (InvalidOperationException)
                {
                    Broken = true;
                    return 0f;
                }
            }
        }

        public static bool Shift => Held(KeyCode.LeftShift) || Held(KeyCode.RightShift);
        public static bool Ctrl => Held(KeyCode.LeftControl) || Held(KeyCode.RightControl);
    }
}
