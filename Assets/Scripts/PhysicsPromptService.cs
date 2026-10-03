using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public enum MatterCompilerProvider
{
    /// <summary>First provider that has a key: Anthropic, then Groq, then OpenAI. Offline otherwise.</summary>
    Auto,
    Anthropic,
    Groq,
    OpenAI,
    CustomRelay,
    OfflineOnly,
}

/// <summary>
/// Compiles a free-text matter description into a PhysicalObjectConfig.
/// Calls an LLM (Anthropic, Groq, OpenAI or a custom relay) through UnityWebRequest, and falls back
/// to a keyword compiler whenever there is no key, no network, a timeout or an unreadable reply,
/// so a live demo never stalls.
/// </summary>
public class PhysicsPromptService : MonoBehaviour
{
    public const string SystemPrompt =
        "You are a strict real-world physics compiler for a video game. Given an object description, " +
        "return ONLY valid JSON matching PhysicalObjectConfig schema. Accurately calculate realistic " +
        "real-world densities, friction coefficients, and restitution values. Never return conversational " +
        "text or markdown blocks.\n\n" +
        "PhysicalObjectConfig schema:\n" +
        "{\"shape\": \"cube\" | \"sphere\" | \"cylinder\" | \"wedge\", \"dimensions\": [x, y, z] (metres), " +
        "\"mass\": kg, \"bounciness\": 0-1, \"dynamicFriction\": 0-1, \"staticFriction\": 0-1, " +
        "\"hexColor\": \"#RRGGBB\", \"roughness\": 0-1, \"metalness\": 0-1, \"prompt\": \"<the description>\"}\n" +
        "Conventions: x = width, y = height, z = length pointing away from the player. Beams, planks, rods " +
        "and bridges put their long side on z. A wedge is a ramp that rises towards +z. Spheres use equal " +
        "dimensions (the diameter). mass = realistic density x volume unless the description states a mass, " +
        "in which case that mass wins. bounciness is the coefficient of restitution. Metals use metalness 1.";

    public const string AnthropicKeyPref = "Materialize.ApiKey.Anthropic";
    public const string GroqKeyPref = "Materialize.ApiKey.Groq";
    public const string OpenAIKeyPref = "Materialize.ApiKey.OpenAI";

    const string AnthropicUrl = "https://api.anthropic.com/v1/messages";
    const string GroqUrl = "https://api.groq.com/openai/v1/chat/completions";
    const string OpenAIUrl = "https://api.openai.com/v1/chat/completions";

    static readonly MatterCompilerProvider[] AutoOrder =
        { MatterCompilerProvider.Anthropic, MatterCompilerProvider.Groq, MatterCompilerProvider.OpenAI };

    [Header("Provider")]
    [Tooltip("Auto uses the first provider that has a key (Anthropic, then Groq, then OpenAI), otherwise the offline compiler.")]
    public MatterCompilerProvider provider = MatterCompilerProvider.Auto;
    public string anthropicModel = "claude-sonnet-5-5";
    [Tooltip("Tried in order: if a model is retired, rate-limited or rejects the request, the next one answers.")]
    public string[] groqModels = { "openai/gpt-oss-120b", "openai/gpt-oss-20b" };
    [Tooltip("Reasoning depth for Groq's gpt-oss models (low | medium | high). Low keeps synthesis snappy.")]
    public string groqReasoningEffort = "low";
    public string openAIModel = "gpt-4.1-mini";
    [Tooltip("POST {\"prompt\", \"system\"} here. The relay answers with the config JSON, or text that contains it.")]
    public string relayUrl = "http://localhost:8787/materialize";
    [Tooltip("Web builds can't keep an API key secret, so in Auto mode they ask this same-origin relay " +
             "(the Vercel function in web/api) and fall back to the offline compiler if it fails.")]
    public string webRelayUrl = "/api/materialize";

    [Header("Keys (read at runtime, never saved in the scene)")]
    public string anthropicKeyVariable = "ANTHROPIC_API_KEY";
    public string groqKeyVariable = "GROQ_API_KEY";
    public string openAIKeyVariable = "OPENAI_API_KEY";
    public string relayTokenVariable = "MATERIALIZE_RELAY_TOKEN";

    [Header("Behaviour")]
    [Range(2f, 30f)] public float timeoutSeconds = 12f;
    [Tooltip("A mass written in the prompt (\"1000kg\") overrides the model's estimate.")]
    public bool explicitMassWins = true;

    public int InFlight { get; private set; }

    int preferredGroqModel;

    public void Compile(string description, Action<PhysicalObjectConfig, string> onCompiled)
    {
        StartCoroutine(CompileRoutine(description ?? "", onCompiled));
    }

    IEnumerator CompileRoutine(string description, Action<PhysicalObjectConfig, string> onCompiled)
    {
        InFlight++;
        PhysicalObjectConfig offline = CompileOffline(description);
        PhysicalObjectConfig config = null;
        string source;

        MatterCompilerProvider active = ResolveProvider(out string key);
        if (active == MatterCompilerProvider.OfflineOnly)
        {
            source = provider == MatterCompilerProvider.OfflineOnly ? "Offline compiler" : "Offline compiler · no API key set";
        }
        else
        {
            var reply = new Reply();
            float started = Time.realtimeSinceStartup;
            yield return Request(active, description, key, reply);
            int ms = Mathf.RoundToInt((Time.realtimeSinceStartup - started) * 1000f);
            if (reply.text != null && TryParseConfig(reply.text, offline, out config))
            {
                source = $"{reply.label} · {ms} ms";
            }
            else
            {
                string why = reply.error ?? "unreadable reply";
                Debug.LogWarning($"[Materialize] {reply.label} compile failed ({why}); using the offline compiler.");
                source = $"Offline compiler · {why}";
            }
        }

        config ??= offline;
        config.prompt = description;
        if (explicitMassWins && TryParseMass(description, out float kg)) config.mass = kg;
        config.Sanitize();
        InFlight--;
        onCompiled?.Invoke(config, source);
    }

    /// <summary>The backend that answers the next prompt (OfflineOnly when no usable key is set) and its key.</summary>
    public MatterCompilerProvider ResolveProvider(out string key)
    {
        key = null;
        switch (provider)
        {
            case MatterCompilerProvider.OfflineOnly:
                return MatterCompilerProvider.OfflineOnly;
            case MatterCompilerProvider.CustomRelay:
                key = KeyFor(MatterCompilerProvider.CustomRelay); // optional bearer token
                return MatterCompilerProvider.CustomRelay;
            case MatterCompilerProvider.Auto:
#if UNITY_WEBGL && !UNITY_EDITOR
                if (!string.IsNullOrEmpty(webRelayUrl)) return MatterCompilerProvider.CustomRelay;
#endif
                foreach (MatterCompilerProvider candidate in AutoOrder)
                {
                    key = KeyFor(candidate);
                    if (key != null) return candidate;
                }
                return MatterCompilerProvider.OfflineOnly;
            default:
                key = KeyFor(provider);
                return key != null ? provider : MatterCompilerProvider.OfflineOnly;
        }
    }

    string KeyFor(MatterCompilerProvider which)
    {
#if UNITY_EDITOR
        string pref =
            which == MatterCompilerProvider.Anthropic ? AnthropicKeyPref :
            which == MatterCompilerProvider.Groq ? GroqKeyPref :
            which == MatterCompilerProvider.OpenAI ? OpenAIKeyPref : null;
        if (pref != null)
        {
            string stored = UnityEditor.EditorPrefs.GetString(pref, "");
            if (!string.IsNullOrWhiteSpace(stored)) return stored.Trim();
        }
#endif
        string variable;
        switch (which)
        {
            case MatterCompilerProvider.Anthropic: variable = anthropicKeyVariable; break;
            case MatterCompilerProvider.Groq: variable = groqKeyVariable; break;
            case MatterCompilerProvider.OpenAI: variable = openAIKeyVariable; break;
            case MatterCompilerProvider.CustomRelay: variable = relayTokenVariable; break;
            default: return null;
        }
        string value = string.IsNullOrEmpty(variable) ? null : Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // ------------------------------------------------------------------ network

    class Reply
    {
        public string label = "LLM";
        public string body;   // raw HTTP body on success
        public string text;   // the model's answer
        public string error;
        public bool retryable;
    }

    [Serializable] class ChatMessage { public string role; public string content; }
    [Serializable] class ResponseFormat { public string type; }
    [Serializable] class AnthropicRequest { public string model; public int max_tokens; public string system; public ChatMessage[] messages; }
    [Serializable] class AnthropicBlock { public string type; public string text; }
    [Serializable] class AnthropicResponse { public AnthropicBlock[] content; }
    [Serializable] class OpenAIRequest { public string model; public ChatMessage[] messages; public ResponseFormat response_format; }
    [Serializable]
    class GroqRequest
    {
        public string model;
        public ChatMessage[] messages;
        public ResponseFormat response_format;
        public string reasoning_effort;
        public bool include_reasoning;
        public int max_completion_tokens;
    }
    [Serializable] class ChatChoice { public ChatMessage message; }
    [Serializable] class ChatResponse { public ChatChoice[] choices; }
    [Serializable] class RelayRequest { public string prompt; public string system; }

    IEnumerator Request(MatterCompilerProvider active, string description, string key, Reply reply)
    {
        string user = $"Object description: \"{description}\"\nReturn the PhysicalObjectConfig JSON object.";
        var chat = new[]
        {
            new ChatMessage { role = "system", content = SystemPrompt },
            new ChatMessage { role = "user", content = user },
        };

        switch (active)
        {
            case MatterCompilerProvider.Anthropic:
            {
                reply.label = $"Claude ({anthropicModel})";
                string body = JsonUtility.ToJson(new AnthropicRequest
                {
                    model = anthropicModel,
                    max_tokens = 400,
                    system = SystemPrompt,
                    messages = new[] { new ChatMessage { role = "user", content = user } },
                });
                var headers = new Dictionary<string, string> { ["x-api-key"] = key, ["anthropic-version"] = "2023-06-01" };
#if UNITY_WEBGL && !UNITY_EDITOR
                headers["anthropic-dangerous-direct-browser-access"] = "true";
#endif
                yield return Post(AnthropicUrl, body, headers, reply);
                if (reply.body != null) reply.text = AnthropicText(reply.body);
                break;
            }
            case MatterCompilerProvider.Groq:
            {
                if (groqModels == null || groqModels.Length == 0)
                {
                    reply.label = "Groq";
                    reply.error = "no Groq model set";
                    break;
                }
                for (int attempt = 0; attempt < groqModels.Length; attempt++)
                {
                    int index = (preferredGroqModel + attempt) % groqModels.Length;
                    reply.label = $"Groq ({groqModels[index]})";
                    reply.body = reply.error = null;
                    string body = JsonUtility.ToJson(new GroqRequest
                    {
                        model = groqModels[index],
                        messages = chat,
                        response_format = new ResponseFormat { type = "json_object" },
                        reasoning_effort = groqReasoningEffort,
                        include_reasoning = false,
                        max_completion_tokens = 1024,
                    });
                    yield return Post(GroqUrl, body, Bearer(key), reply);
                    if (reply.body != null)
                    {
                        preferredGroqModel = index; // stick with the model that answered
                        reply.text = ChatText(reply.body);
                        break;
                    }
                    if (!reply.retryable) break;
                }
                break;
            }
            case MatterCompilerProvider.OpenAI:
            {
                reply.label = $"OpenAI ({openAIModel})";
                string body = JsonUtility.ToJson(new OpenAIRequest
                {
                    model = openAIModel,
                    messages = chat,
                    response_format = new ResponseFormat { type = "json_object" },
                });
                yield return Post(OpenAIUrl, body, Bearer(key), reply);
                if (reply.body != null) reply.text = ChatText(reply.body);
                break;
            }
            default:
            {
                bool web = provider == MatterCompilerProvider.Auto;
                reply.label = web ? "Groq (web relay)" : "Relay";
                string body = JsonUtility.ToJson(new RelayRequest { prompt = description, system = SystemPrompt });
                var headers = string.IsNullOrEmpty(key) ? new Dictionary<string, string>() : Bearer(key);
                yield return Post(web ? webRelayUrl : relayUrl, body, headers, reply);
                reply.text = reply.body;
                break;
            }
        }
        if (reply.text == null && reply.error == null) reply.error = "empty reply";
    }

    IEnumerator Post(string url, string json, Dictionary<string, string> headers, Reply reply)
    {
        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.CeilToInt(timeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json");
            foreach (var header in headers) request.SetRequestHeader(header.Key, header.Value);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                reply.body = request.downloadHandler.text;
                yield break;
            }

            long code = request.responseCode;
            string hint = code == 401 || code == 403 ? " (check the API key)" : code == 429 ? " (rate limited)" : "";
            reply.error = code > 0 ? $"HTTP {code}{hint}" : request.error;
            // Another model helps with retired models, rate limits and rejected generations;
            // it does not help with a bad key or a dead network.
            reply.retryable = code == 400 || code == 404 || code == 413 || code == 422 || code == 429 || code >= 500;
            string detail = request.downloadHandler?.text;
            if (!string.IsNullOrEmpty(detail) && detail.Length > 300) detail = detail.Substring(0, 300);
            Debug.LogWarning($"[Materialize] {reply.label}: {reply.error} {detail}");
        }
    }

    static Dictionary<string, string> Bearer(string key) =>
        new Dictionary<string, string> { ["Authorization"] = "Bearer " + key };

    static string AnthropicText(string raw)
    {
        try
        {
            var response = JsonUtility.FromJson<AnthropicResponse>(raw);
            if (response?.content == null) return null;
            var sb = new StringBuilder();
            foreach (AnthropicBlock block in response.content)
                if (block != null && block.type == "text") sb.Append(block.text);
            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string ChatText(string raw)
    {
        try
        {
            var response = JsonUtility.FromJson<ChatResponse>(raw);
            return response?.choices != null && response.choices.Length > 0 ? response.choices[0].message?.content : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static readonly string[] SchemaKeys =
        { "shape", "dimensions", "mass", "bounciness", "dynamicFriction", "staticFriction", "hexColor", "roughness", "metalness" };

    /// <summary>Pulls the first JSON object out of a model reply; keys the model left out come from <paramref name="fallback"/>.</summary>
    public static bool TryParseConfig(string text, PhysicalObjectConfig fallback, out PhysicalObjectConfig config)
    {
        config = null;
        if (string.IsNullOrEmpty(text)) return false;
        int start = text.IndexOf('{'), end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return false;
        string json = text.Substring(start, end - start + 1);

        PhysicalObjectConfig parsed;
        try { parsed = JsonUtility.FromJson<PhysicalObjectConfig>(json); }
        catch (Exception) { return false; }
        if (parsed == null) return false;

        bool Has(string key) => json.Contains("\"" + key + "\"");
        int found = 0;
        foreach (string key in SchemaKeys) if (Has(key)) found++;
        if (found < 3) return false;

        if (!Has("shape")) parsed.shape = fallback.shape;
        if (!Has("dimensions") || parsed.dimensions == null || parsed.dimensions.Length < 3)
            parsed.dimensions = (float[])fallback.dimensions.Clone();
        if (!Has("mass")) parsed.mass = fallback.mass;
        if (!Has("bounciness")) parsed.bounciness = fallback.bounciness;
        if (!Has("dynamicFriction")) parsed.dynamicFriction = fallback.dynamicFriction;
        if (!Has("staticFriction")) parsed.staticFriction = fallback.staticFriction;
        if (!Has("hexColor")) parsed.hexColor = fallback.hexColor;
        if (!Has("roughness")) parsed.roughness = fallback.roughness;
        if (!Has("metalness")) parsed.metalness = fallback.metalness;
        config = parsed;
        return true;
    }

    // ------------------------------------------------------------------ offline compiler

    sealed class MatterProfile
    {
        public readonly Regex words;
        public readonly float density, bounce, dynamicFriction, staticFriction, roughness, metalness;
        public readonly string color;

        public MatterProfile(string pattern, float density, float bounce, float dynamicFriction, float staticFriction,
            string color, float roughness, float metalness)
        {
            words = Words(pattern);
            this.density = density;
            this.bounce = bounce;
            this.dynamicFriction = dynamicFriction;
            this.staticFriction = staticFriction;
            this.color = color;
            this.roughness = roughness;
            this.metalness = metalness;
        }
    }

    static Regex Words(string pattern) =>
        new Regex($@"\b({pattern})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static bool Has(string text, string pattern) =>
        Regex.IsMatch(text, $@"\b({pattern})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // First match wins, so specific materials come before generic ones.
    // density kg/m3, restitution, dynamic / static friction, colour, roughness, metalness
    static readonly MatterProfile[] Profiles =
    {
        new MatterProfile("tungsten", 19300f, 0.05f, 0.40f, 0.50f, "#8A8D8F", 0.35f, 1f),
        new MatterProfile("osmium", 22590f, 0.05f, 0.40f, 0.50f, "#9AA2AA", 0.30f, 1f),
        new MatterProfile("gold|golden", 19320f, 0.10f, 0.45f, 0.55f, "#FFC83D", 0.20f, 1f),
        new MatterProfile("lead", 11340f, 0.02f, 0.60f, 0.80f, "#5B6168", 0.55f, 0.9f),
        new MatterProfile("silver", 10490f, 0.12f, 0.40f, 0.50f, "#D8D8DA", 0.15f, 1f),
        new MatterProfile("copper", 8960f, 0.15f, 0.36f, 0.53f, "#C8703A", 0.30f, 1f),
        new MatterProfile("brass|bronze", 8600f, 0.15f, 0.35f, 0.50f, "#C9A23B", 0.30f, 1f),
        new MatterProfile("steel|iron|metal|metallic|anvil", 7850f, 0.15f, 0.42f, 0.74f, "#8C9196", 0.40f, 1f),
        new MatterProfile("titanium", 4500f, 0.20f, 0.36f, 0.50f, "#B8BCC2", 0.35f, 1f),
        new MatterProfile("alumini?um", 2700f, 0.20f, 0.45f, 0.61f, "#C9CDD1", 0.30f, 1f),
        new MatterProfile("graphite|carbon", 2200f, 0.10f, 0.15f, 0.20f, "#2B2B2E", 0.60f, 0.3f),
        new MatterProfile("diamond", 3510f, 0.15f, 0.10f, 0.15f, "#E8F6FF", 0.02f, 0f),
        new MatterProfile("concrete|cement", 2400f, 0.10f, 0.60f, 0.70f, "#8E8C86", 0.90f, 0f),
        new MatterProfile("stone|rock|granite|boulder", 2650f, 0.10f, 0.55f, 0.65f, "#7D7A74", 0.85f, 0f),
        new MatterProfile("marble", 2700f, 0.10f, 0.45f, 0.60f, "#EDEBE6", 0.30f, 0f),
        new MatterProfile("brick", 1900f, 0.10f, 0.60f, 0.70f, "#A2482F", 0.90f, 0f),
        new MatterProfile("glass", 2500f, 0.30f, 0.40f, 0.90f, "#BFE6EE", 0.05f, 0f),
        new MatterProfile("ice|icy|frozen", 917f, 0.05f, 0.02f, 0.05f, "#CFEFFF", 0.05f, 0f),
        new MatterProfile("rubber|rubbery", 1100f, 0.85f, 0.80f, 1.00f, "#E0442F", 0.85f, 0f),
        new MatterProfile("gel|jelly|slime", 1050f, 0.60f, 0.70f, 0.90f, "#7CDB5A", 0.40f, 0f),
        new MatterProfile("cork", 240f, 0.50f, 0.50f, 0.60f, "#C79A6B", 0.90f, 0f),
        new MatterProfile("balsa", 150f, 0.20f, 0.40f, 0.50f, "#E6D3A3", 0.80f, 0f),
        new MatterProfile("wood|wooden|oak|pine|timber|log", 700f, 0.25f, 0.40f, 0.50f, "#9C6B3C", 0.80f, 0f),
        new MatterProfile("plastic|polymer", 950f, 0.40f, 0.35f, 0.40f, "#3A7BD5", 0.50f, 0f),
        new MatterProfile("teflon|ptfe", 2200f, 0.10f, 0.04f, 0.04f, "#F4F4F0", 0.30f, 0f),
        new MatterProfile("foam|sponge|styrofoam|polystyrene", 30f, 0.30f, 0.80f, 0.90f, "#F2E6A0", 1.00f, 0f),
        new MatterProfile("cardboard|paper", 700f, 0.10f, 0.50f, 0.60f, "#C8B08A", 0.90f, 0f),
    };

    static readonly MatterProfile GenericMatter = new MatterProfile("matter", 1000f, 0.30f, 0.50f, 0.60f, "#9AA3AD", 0.60f, 0f);

    static readonly Regex WedgeWords = Words("wedge|wedges|ramp|slope|incline");
    static readonly Regex SphereWords = Words("ball|sphere|orb|globe|bowling|boulder");
    static readonly Regex BeamWords = Words("beam|plank|bridge|bridging|girder|rail|board|lintel");
    static readonly Regex CylinderWords = Words("cylinder|cylindrical|pipe|rod|barrel|pillar|column|can|drum|disc|disk|puck|wheel|coin|log|pole");
    static readonly Regex PadWords = Words("pad|slab|plate|platform|tile|sheet|trampoline|mat");
    static readonly Regex WallWords = Words("wall|barrier|shield");

    static readonly (Regex words, string hex)[] ColorWords =
    {
        (Words("red|crimson"), "#D23B2E"), (Words("orange"), "#E8862A"), (Words("yellow"), "#F2C230"),
        (Words("green"), "#3FAE49"), (Words("cyan|teal"), "#2EC4C6"), (Words("blue"), "#2F6FD6"),
        (Words("purple|violet"), "#7B4BC9"), (Words("pink"), "#E86FA8"), (Words("black"), "#1C1C1E"),
        (Words("white"), "#F2F2F2"), (Words("gr[ae]y"), "#8A8A8A"),
    };

    static readonly Regex TripleSize = new Regex(
        @"(\d+(?:\.\d+)?)\s*[x×*]\s*(\d+(?:\.\d+)?)\s*[x×*]\s*(\d+(?:\.\d+)?)\s*(cm|m)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex SingleSize = new Regex(
        @"(\d+(?:\.\d+)?)\s*(cm|centimet(?:er|re)s?|m|met(?:er|re)s?)\b(?:\s+(tall|high|long|wide|deep|thick|across|diameter))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex MassAmount = new Regex(
        @"(\d+(?:\.\d+)?)\s*(kg|kgs|kilograms?|kilos?|tonnes?|tons?|t|grams?|g|lbs?|pounds?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Keyword physics: material, shape and size words plus explicit numbers ("2m tall", "1000kg").
    /// Used whenever the LLM is unavailable, and as the defaults for any field the LLM omits.
    /// </summary>
    public static PhysicalObjectConfig CompileOffline(string description)
    {
        string text = StripThousandsSeparators(description ?? "");

        MatterProfile matter = GenericMatter;
        foreach (var profile in Profiles)
        {
            if (profile.words.IsMatch(text))
            {
                matter = profile;
                break;
            }
        }

        string shape = "cube";
        bool beam = false;
        Vector3 size;
        if (WedgeWords.IsMatch(text))
        {
            shape = "wedge";
            size = new Vector3(1.5f, 1f, 2.5f);
        }
        else if (SphereWords.IsMatch(text))
        {
            shape = "sphere";
            size = Vector3.one * (Has(text, "bowling") ? 0.218f : 0.5f);
        }
        else if (BeamWords.IsMatch(text))
        {
            beam = true;
            size = Has(text, "bridge|bridging") ? new Vector3(0.4f, 0.25f, 6.5f) : new Vector3(0.3f, 0.25f, 4.5f);
        }
        else if (CylinderWords.IsMatch(text))
        {
            shape = "cylinder";
            if (Has(text, "disc|disk|puck|wheel|coin")) size = new Vector3(0.6f, 0.15f, 0.6f);
            else if (Has(text, "pipe|rod|pole|log")) size = new Vector3(0.15f, 0.15f, 3f);
            else if (Has(text, "pillar|column")) size = new Vector3(0.6f, 2.5f, 0.6f);
            else size = new Vector3(0.5f, 0.9f, 0.5f);
        }
        else if (PadWords.IsMatch(text)) size = new Vector3(2f, 0.3f, 2f);
        else if (WallWords.IsMatch(text)) size = new Vector3(3f, 2.5f, 0.4f);
        else size = Vector3.one * 0.6f;

        if (Has(text, "tiny|miniature|mini")) size *= 0.35f;
        else if (Has(text, "small|little")) size *= 0.6f;
        else if (Has(text, "huge|giant|gigantic|enormous|colossal|massive")) size *= 3f;
        else if (Has(text, "large|big")) size *= 1.8f;
        if (Has(text, "long")) size.z = beam ? Mathf.Max(size.z, 6.5f) : size.z * 2.5f;
        if (Has(text, "tall|towering")) size.y *= 2.5f;
        if (Has(text, "wide|broad")) size.x *= 2f;
        if (Has(text, "flat")) size.y *= 0.3f;
        if (Has(text, "thin|slim|skinny")) size = ScaleCrossSection(size, 0.5f);
        if (Has(text, "thick|chunky")) size = ScaleCrossSection(size, 1.6f);

        bool explicitSize = ApplyExplicitSize(text, shape, ref size);
        if (shape == "sphere") size = Vector3.one * Mathf.Max(size.x, Mathf.Max(size.y, size.z));

        float volume = PhysicalObjectConfig.ShapeVolume(shape, size);
        float mass;
        if (TryParseMass(text, out float kg))
        {
            mass = kg;
            // a stated mass with no stated size: size the object from the material's real density
            if (!explicitSize && volume > 0f) size *= Mathf.Pow(kg / matter.density / volume, 1f / 3f);
        }
        else
        {
            mass = matter.density * volume;
        }
        if (Has(text, "hollow")) mass *= 0.3f;

        float bounce = matter.bounce, dynamicFriction = matter.dynamicFriction, staticFriction = matter.staticFriction;
        if (Has(text, @"super\s?bouncy|ultra\s?bouncy|hyper\s?bouncy|flubber|trampoline")) bounce = 0.97f;
        else if (Has(text, "bouncy|bouncing|springy|elastic")) bounce = Mathf.Max(bounce, 0.85f);
        if (Has(text, "dead|inelastic|putty|clay")) bounce = 0f;
        if (Has(text, "frictionless|slippery|greased|greasy|slick|oiled"))
        {
            dynamicFriction *= 0.1f;
            staticFriction *= 0.1f;
        }
        if (Has(text, "sticky|grippy|tacky|velcro"))
        {
            dynamicFriction = 1f;
            staticFriction = 1f;
        }

        string color = matter.color;
        foreach (var (words, hex) in ColorWords)
        {
            if (words.IsMatch(text))
            {
                color = hex;
                break;
            }
        }

        var config = new PhysicalObjectConfig
        {
            shape = shape,
            dimensions = new[] { size.x, size.y, size.z },
            mass = mass,
            bounciness = bounce,
            dynamicFriction = dynamicFriction,
            staticFriction = Mathf.Max(staticFriction, dynamicFriction),
            hexColor = color,
            roughness = matter.roughness,
            metalness = matter.metalness,
            prompt = description ?? "",
        };
        config.Sanitize();
        return config;
    }

    /// <summary>Reads a stated mass such as "1000kg", "2.5 t", "1,000 kg" or "50 lbs".</summary>
    public static bool TryParseMass(string description, out float kg)
    {
        kg = 0f;
        if (string.IsNullOrEmpty(description)) return false;
        Match m = MassAmount.Match(StripThousandsSeparators(description));
        if (!m.Success) return false;
        float value = Number(m.Groups[1].Value);
        string unit = m.Groups[2].Value.ToLowerInvariant();
        float scale;
        if (unit.StartsWith("k")) scale = 1f;
        else if (unit == "t" || unit.StartsWith("ton")) scale = 1000f;
        else if (unit.StartsWith("lb") || unit.StartsWith("pound")) scale = 0.4536f;
        else scale = 0.001f;
        kg = value * scale;
        return kg > 0f;
    }

    static bool ApplyExplicitSize(string text, string shape, ref Vector3 size)
    {
        Match triple = TripleSize.Match(text);
        if (triple.Success)
        {
            float unit = triple.Groups[4].Value.Equals("cm", StringComparison.OrdinalIgnoreCase) ? 0.01f : 1f;
            size = new Vector3(Number(triple.Groups[1].Value), Number(triple.Groups[2].Value), Number(triple.Groups[3].Value)) * unit;
            return true;
        }

        bool any = false;
        foreach (Match m in SingleSize.Matches(text))
        {
            float v = Number(m.Groups[1].Value) * (m.Groups[2].Value.StartsWith("c", StringComparison.OrdinalIgnoreCase) ? 0.01f : 1f);
            if (v <= 0f) continue;
            any = true;
            switch (m.Groups[3].Value.ToLowerInvariant())
            {
                case "tall": case "high":
                    size.y = v;
                    break;
                case "long": case "deep":
                    size.z = v;
                    break;
                case "wide": case "across":
                    size.x = v;
                    break;
                case "thick":
                    size[Smallest(size)] = v;
                    break;
                case "diameter":
                    if (shape == "sphere") size = Vector3.one * v;
                    else if (shape == "cylinder" && PhysicalObjectConfig.CylinderLiesAlongZ(size)) { size.x = v; size.y = v; }
                    else { size.x = v; size.z = v; }
                    break;
                default:
                    size *= v / Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                    break;
            }
        }
        return any;
    }

    static Vector3 ScaleCrossSection(Vector3 size, float k)
    {
        int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        for (int i = 0; i < 3; i++)
            if (i != longest) size[i] *= k;
        return size;
    }

    static int Smallest(Vector3 s) => s.x <= s.y && s.x <= s.z ? 0 : s.y <= s.z ? 1 : 2;

    static float Number(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    static string StripThousandsSeparators(string text) => Regex.Replace(text, @"(?<=\d),(?=\d{3}\b)", "");
}
