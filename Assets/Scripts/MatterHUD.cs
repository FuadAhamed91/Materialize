using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Screen HUD: crosshair, the hidden synthesis prompt bar (T), the matter telemetry readout,
/// a status line while the LLM compiles, the chamber objective and flash messages.
/// </summary>
public class MatterHUD : MonoBehaviour
{
    public static MatterHUD Instance { get; private set; }

    [Header("Prompt")]
    public GameObject promptBar;
    public InputField promptField;

    [Header("Readouts")]
    public Text telemetryText;
    public Text statusText;
    public Text messageText;
    public Text objectiveTitle;
    public Text objectiveText;

    int synthesizing;
    string synthesizingLabel = "";
    Coroutine messageRoutine;

    public string PromptText => promptField ? promptField.text : "";

    void Awake()
    {
        Instance = this;
        if (promptBar) promptBar.SetActive(false);
        if (messageText) messageText.text = "";
        if (statusText) statusText.text = "";
    }

    void Update()
    {
        if (synthesizing <= 0 || !statusText) return;
        int dots = 1 + (int)(Time.unscaledTime * 3f) % 3;
        statusText.text = $"SYNTHESIZING{new string('.', dots)}   <color=#8FA3B8>\"{synthesizingLabel}\"</color>";
    }

    public void ShowPrompt(bool show)
    {
        if (!promptBar) return;
        promptBar.SetActive(show);
        if (show)
        {
            StartCoroutine(FocusPrompt());
        }
        else
        {
            if (promptField) promptField.DeactivateInputField();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
    }

    IEnumerator FocusPrompt()
    {
        promptField.text = "";
        promptField.Select();
        promptField.ActivateInputField();
        yield return null;
        yield return null;
        // the T that opened the prompt can leak into the field on some platforms
        if (promptField.text == "t" || promptField.text == "T") promptField.text = "";
    }

    public void BeginSynthesis(string prompt)
    {
        synthesizing++;
        synthesizingLabel = Shorten(prompt, 60);
    }

    public void EndSynthesis()
    {
        synthesizing = Mathf.Max(0, synthesizing - 1);
        if (synthesizing == 0 && statusText) statusText.text = "";
    }

    public void ShowTelemetry(PhysicalObjectConfig c, string source)
    {
        if (!telemetryText) return;
        Vector3 s = c.Size;
        var sb = new StringBuilder();
        sb.AppendLine("<b>MATTER TELEMETRY</b>");
        sb.AppendLine(Inv($"<color=#9FB4C8><i>\"{Shorten(c.prompt, 52)}\"</i></color>"));
        sb.AppendLine(Inv($"MASS   <b>{c.mass:#,0.0} kg</b>"));
        sb.AppendLine(Inv($"BOUNCINESS   <b>{c.bounciness * 100f:0}%</b>"));
        sb.AppendLine(Inv($"FRICTION   <b>{c.dynamicFriction:0.00}</b> dynamic · <b>{c.staticFriction:0.00}</b> static"));
        sb.AppendLine(Inv($"SHAPE   {c.shape}  {s.x:0.##} × {s.y:0.##} × {s.z:0.##} m"));
        sb.AppendLine(Inv($"DENSITY   {c.Density:#,0} kg/m³   ·   METALNESS {c.metalness:0.##}"));
        sb.Append(Inv($"<size=17><color=#7E93A8>{source}</color></size>"));
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

    static string Shorten(string text, int max)
    {
        text = (text ?? "").Replace('\n', ' ');
        return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }
}
