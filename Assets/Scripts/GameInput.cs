using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Keyboard / mouse shim so gameplay code runs on either input backend.</summary>
public static class GameInput
{
#if ENABLE_INPUT_SYSTEM
    static Keyboard Keys => Keyboard.current;
    static Mouse Pointer => Mouse.current;

    public static Vector2 Move
    {
        get
        {
            Keyboard k = Keys;
            if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
        }
    }

    /// <summary>Mouse movement this frame, in pixels.</summary>
    public static Vector2 LookDelta => Pointer != null ? Pointer.delta.ReadValue() : Vector2.zero;
    public static bool JumpPressed => Keys != null && Keys.spaceKey.wasPressedThisFrame;
    public static bool SprintHeld => Keys != null && Keys.leftShiftKey.isPressed;
    public static bool CancelPressed => Keys != null && Keys.escapeKey.wasPressedThisFrame;
    public static bool ClickPressed => Pointer != null && Pointer.leftButton.wasPressedThisFrame;
    /// <summary>Place the selected item: left click or F.</summary>
    public static bool PlacePressed => ClickPressed || (Keys != null && Keys.fKey.wasPressedThisFrame);
    /// <summary>Recycle the matter under the crosshair: right click or X.</summary>
    public static bool RecyclePressed => (Pointer != null && Pointer.rightButton.wasPressedThisFrame) || (Keys != null && Keys.xKey.wasPressedThisFrame);
    /// <summary>Restart the current chamber.</summary>
    public static bool RestartPressed => Keys != null && Keys.rKey.wasPressedThisFrame;
    /// <summary>Mouse wheel this frame; only the sign matters.</summary>
    public static float Scroll => Pointer != null ? Pointer.scroll.ReadValue().y : 0f;
    /// <summary>Mouse position in screen pixels, origin bottom left.</summary>
    public static Vector2 PointerPosition => Pointer != null ? Pointer.position.ReadValue() : Vector2.zero;

    /// <summary>Index 0-8 of a number key 1-9 pressed this frame, or -1.</summary>
    public static int SlotPressed
    {
        get
        {
            Keyboard k = Keys;
            if (k == null) return -1;
            for (int i = 0; i < 9; i++)
                if (k[Key.Digit1 + i].wasPressedThisFrame) return i;
            return -1;
        }
    }
#else
    public static Vector2 Move => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    /// <summary>Mouse movement this frame, approximately in pixels.</summary>
    public static Vector2 LookDelta => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
    public static bool JumpPressed => Input.GetKeyDown(KeyCode.Space);
    public static bool SprintHeld => Input.GetKey(KeyCode.LeftShift);
    public static bool CancelPressed => Input.GetKeyDown(KeyCode.Escape);
    public static bool ClickPressed => Input.GetMouseButtonDown(0);
    public static bool PlacePressed => ClickPressed || Input.GetKeyDown(KeyCode.F);
    public static bool RecyclePressed => Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.X);
    public static bool RestartPressed => Input.GetKeyDown(KeyCode.R);
    public static float Scroll => Input.mouseScrollDelta.y;
    public static Vector2 PointerPosition => Input.mousePosition;

    public static int SlotPressed
    {
        get
        {
            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
            return -1;
        }
    }
#endif
}
