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
    public static bool OpenPromptPressed => Keys != null && Keys.tKey.wasPressedThisFrame;
    public static bool SubmitPressed => Keys != null && (Keys.enterKey.wasPressedThisFrame || Keys.numpadEnterKey.wasPressedThisFrame);
    public static bool CancelPressed => Keys != null && Keys.escapeKey.wasPressedThisFrame;
    public static bool RepeatPressed => Keys != null && Keys.gKey.wasPressedThisFrame;
    public static bool DeletePressed => Keys != null && Keys.xKey.wasPressedThisFrame;
    public static bool ClickPressed => Pointer != null && Pointer.leftButton.wasPressedThisFrame;
#else
    public static Vector2 Move => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    /// <summary>Mouse movement this frame, approximately in pixels.</summary>
    public static Vector2 LookDelta => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
    public static bool JumpPressed => Input.GetKeyDown(KeyCode.Space);
    public static bool SprintHeld => Input.GetKey(KeyCode.LeftShift);
    public static bool OpenPromptPressed => Input.GetKeyDown(KeyCode.T);
    public static bool SubmitPressed => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    public static bool CancelPressed => Input.GetKeyDown(KeyCode.Escape);
    public static bool RepeatPressed => Input.GetKeyDown(KeyCode.G);
    public static bool DeletePressed => Input.GetKeyDown(KeyCode.X);
    public static bool ClickPressed => Input.GetMouseButtonDown(0);
#endif
}
