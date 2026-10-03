using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen HUD: crosshair, the inventory hotbar, telemetry for the selected item, the chamber name
/// and flash messages.
/// </summary>
public class MatterHUD : MonoBehaviour
{
    public static MatterHUD Instance { get; private set; }

    [Serializable]
    public class Slot
    {
        public GameObject root;
        public Image frame;
        public RawImage icon;
        public Text count;
    }

    [Header("Inventory")]
    public Inventory inventory;
    public Slot[] slots = new Slot[0];
    public Text selectedName;
    public Color frameColor = new Color(1f, 1f, 1f, 0.14f);
    public Color selectedFrameColor = new Color(0.25f, 0.8f, 1f, 1f);

    [Header("Readouts")]
    public Text telemetryText;
    public Text messageText;
    public Text objectiveTitle;
    public Text objectiveText;

    Coroutine messageRoutine;

    void Awake()
    {
        Instance = this;
        if (messageText) messageText.text = "";
    }

    void OnEnable()
    {
        if (inventory) inventory.Changed += Refresh;
    }

    void OnDisable()
    {
        if (inventory) inventory.Changed -= Refresh;
    }

    void Start() => Refresh();

    /// <summary>Redraws the hotbar and the telemetry for the selected item.</summary>
    public void Refresh()
    {
        if (!inventory) return;
        for (int i = 0; i < slots.Length; i++)
        {
            Slot slot = slots[i];
            bool used = i < inventory.SlotCount;
            if (slot.root.activeSelf != used) slot.root.SetActive(used);
            if (!used) continue;

            int left = inventory.Count(i);
            slot.icon.texture = ShapeIcon.For(inventory.Item(i).config);
            slot.icon.color = new Color(1f, 1f, 1f, left > 0 ? 1f : 0.25f);
            slot.count.text = Inv($"×{left}");
            slot.count.color = left > 0 ? Color.white : new Color(1f, 0.45f, 0.35f);
            slot.frame.color = i == inventory.Selected ? selectedFrameColor : frameColor;
        }

        InventoryItem selected = inventory.SelectedItem;
        if (selectedName) selectedName.text = selected != null ? selected.name.ToUpperInvariant() : "";
        ShowTelemetry(selected, inventory.Count(inventory.Selected));
    }

    void ShowTelemetry(InventoryItem item, int left)
    {
        if (!telemetryText) return;
        if (item == null)
        {
            telemetryText.text = "<b>MATTER TELEMETRY</b>\nInventory empty.";
            return;
        }
        PhysicalObjectConfig c = item.config;
        Vector3 s = c.Size;
        var sb = new StringBuilder();
        sb.AppendLine("<b>MATTER TELEMETRY</b>");
        sb.AppendLine(Inv($"<color=#9FB4C8><b>{item.name.ToUpperInvariant()}</b>   ·   {left} of {item.count} left</color>"));
        sb.AppendLine(Inv($"MASS   <b>{c.mass:#,0.0} kg</b>"));
        sb.AppendLine(Inv($"BOUNCINESS   <b>{c.bounciness * 100f:0}%</b>"));
        sb.AppendLine(Inv($"FRICTION   <b>{c.dynamicFriction:0.00}</b> dynamic · <b>{c.staticFriction:0.00}</b> static"));
        sb.AppendLine(Inv($"SHAPE   {c.shape}  {s.x:0.##} × {s.y:0.##} × {s.z:0.##} m"));
        sb.Append(Inv($"DENSITY   {c.Density:#,0} kg/m³   ·   METALNESS {c.metalness:0.##}"));
        telemetryText.text = sb.ToString();
    }

    public void SetObjective(string title, string objective)
    {
        if (objectiveTitle) objectiveTitle.text = title;
        if (objectiveText) objectiveText.text = objective;
    }

    public void Flash(string message, float seconds = 3.5f) => Flash(message, new Color(0.85f, 0.95f, 1f), seconds);

    public void Flash(string message, Color color, float seconds = 3.5f)
    {
        if (!messageText) return;
        if (messageRoutine != null) StopCoroutine(messageRoutine);
        messageRoutine = StartCoroutine(FlashRoutine(message, color, seconds));
    }

    IEnumerator FlashRoutine(string message, Color color, float seconds)
    {
        messageText.text = message;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float alpha = Mathf.Min(1f, t / 0.15f) * Mathf.Clamp01((seconds - t) / 0.6f);
            messageText.color = new Color(color.r, color.g, color.b, alpha);
            yield return null;
        }
        messageText.text = "";
        messageRoutine = null;
    }

    static string Inv(FormattableString text) => FormattableString.Invariant(text);
}
