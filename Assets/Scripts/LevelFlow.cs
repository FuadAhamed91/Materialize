using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Restarts the current chamber: R, or the RESTART LEVEL button while the mouse is free. The scene
/// reloads, so every door, plate, circuit, seal and scale is back in its starting state, and the
/// player starts again at that chamber's entrance with its full inventory.
/// </summary>
public class LevelFlow : MonoBehaviour
{
    public FirstPersonController player;
    public Inventory inventory;
    [Tooltip("Chamber inventories in play order; the current chamber is the one whose inventory is loaded.")]
    public ChamberLoadout[] chambers = new ChamberLoadout[0];
    public Transform[] spawns = new Transform[0];
    public string[] titles = new string[0];

    [Header("Restart button")]
    public RectTransform restartButton;
    public Graphic restartFill;
    public Color idleColor = new Color(0.03f, 0.05f, 0.07f, 0.74f);
    public Color hoverColor = new Color(0.1f, 0.42f, 0.58f, 0.92f);

    // survives the scene reload
    static int restartAt = -1;
    bool pointerWasFree;

    /// <summary>Index of the chamber the player is in.</summary>
    public int Current => Mathf.Max(0, Array.IndexOf(chambers, inventory ? inventory.loadout : null));

    void Start()
    {
        if (restartAt < 0 || chambers.Length == 0) return;
        int i = Mathf.Clamp(restartAt, 0, chambers.Length - 1);
        restartAt = -1;
        player.SetCheckpoint(spawns[i]);
        player.Teleport(spawns[i].position, spawns[i].eulerAngles.y);
        inventory.Load(chambers[i]);
        MatterHUD hud = MatterHUD.Instance;
        if (hud)
        {
            hud.SetObjective(titles[i], "");
            hud.Flash(titles[i] + " · RESTARTED", 2.5f);
        }
    }

    void Update()
    {
        bool hover = pointerWasFree && restartButton &&
                     RectTransformUtility.RectangleContainsScreenPoint(restartButton, GameInput.PointerPosition, null);
        if (restartFill) restartFill.color = hover ? hoverColor : idleColor;
        if (GameInput.RestartPressed || (hover && GameInput.ClickPressed)) Restart();
    }

    // a click on a free mouse also captures it this frame, so the button reads last frame's state
    void LateUpdate() => pointerWasFree = Cursor.lockState != CursorLockMode.Locked;

    public void Restart()
    {
        restartAt = Current;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
