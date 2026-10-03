using UnityEditor;
using UnityEngine;

/// <summary>
/// Stores LLM API keys in this machine's EditorPrefs so they never land in a scene, prefab or commit.
/// Menu: Materialize > Set LLM API Keys...
/// </summary>
public class MaterializeApiKeyWindow : EditorWindow
{
    string anthropicKey;
    string groqKey;
    string openAIKey;

    [MenuItem("Materialize/Set LLM API Keys...", priority = 20)]
    static void Open()
    {
        var window = GetWindow<MaterializeApiKeyWindow>(true, "Materialize · LLM API Keys");
        window.minSize = new Vector2(500f, 220f);
        window.anthropicKey = EditorPrefs.GetString(PhysicsPromptService.AnthropicKeyPref, "");
        window.groqKey = EditorPrefs.GetString(PhysicsPromptService.GroqKeyPref, "");
        window.openAIKey = EditorPrefs.GetString(PhysicsPromptService.OpenAIKeyPref, "");
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Paste a key into one box and Save. With the provider on Auto, the Matter Gun uses the first key it finds " +
            "(Anthropic, then Groq, then OpenAI); with no key it uses its offline physics compiler. Keys are stored in " +
            "this machine's EditorPrefs only, never in the scene or the repository. Builds read ANTHROPIC_API_KEY, " +
            "GROQ_API_KEY or OPENAI_API_KEY from the environment instead.",
            MessageType.Info);
        anthropicKey = EditorGUILayout.PasswordField("Anthropic API key", anthropicKey);
        groqKey = EditorGUILayout.PasswordField("Groq API key", groqKey);
        openAIKey = EditorGUILayout.PasswordField("OpenAI API key", openAIKey);
        GUILayout.FlexibleSpace();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear all"))
            {
                anthropicKey = groqKey = openAIKey = "";
                EditorPrefs.DeleteKey(PhysicsPromptService.AnthropicKeyPref);
                EditorPrefs.DeleteKey(PhysicsPromptService.GroqKeyPref);
                EditorPrefs.DeleteKey(PhysicsPromptService.OpenAIKeyPref);
            }
            if (GUILayout.Button("Save"))
            {
                Store(PhysicsPromptService.AnthropicKeyPref, anthropicKey);
                Store(PhysicsPromptService.GroqKeyPref, groqKey);
                Store(PhysicsPromptService.OpenAIKeyPref, openAIKey);
                Close();
            }
        }
    }

    static void Store(string pref, string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) EditorPrefs.DeleteKey(pref);
        else EditorPrefs.SetString(pref, value);
    }
}
