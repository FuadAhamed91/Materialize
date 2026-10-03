// Vercel serverless function: compiles one matter description into PhysicalObjectConfig JSON.
// The browser build can't keep an API key secret, so it posts here and this function calls Groq
// with GROQ_API_KEY from the project's environment variables. If the key is missing or Groq fails,
// the game falls back to its offline physics compiler.

const SYSTEM_PROMPT =
  "You are a strict real-world physics compiler for a video game. Given an object description, " +
  "return ONLY valid JSON matching PhysicalObjectConfig schema. Accurately calculate realistic " +
  "real-world densities, friction coefficients, and restitution values. Never return conversational " +
  "text or markdown blocks.\n\n" +
  "PhysicalObjectConfig schema:\n" +
  '{"shape": "cube" | "sphere" | "cylinder" | "wedge", "dimensions": [x, y, z] (metres), ' +
  '"mass": kg, "bounciness": 0-1, "dynamicFriction": 0-1, "staticFriction": 0-1, ' +
  '"hexColor": "#RRGGBB", "roughness": 0-1, "metalness": 0-1, "prompt": "<the description>"}\n' +
  "Conventions: x = width, y = height, z = length pointing away from the player. Beams, planks, rods " +
  "and bridges put their long side on z. A wedge is a ramp that rises towards +z. Spheres use equal " +
  "dimensions (the diameter). mass = realistic density x volume unless the description states a mass, " +
  "in which case that mass wins. bounciness is the coefficient of restitution. Metals use metalness 1.";

// Tried in order: the next model answers if one is retired, rate-limited or rejects the request.
const MODELS = ["openai/gpt-oss-120b", "openai/gpt-oss-20b"];
const MAX_PROMPT_LENGTH = 200;

module.exports = async (req, res) => {
  if (req.method !== "POST") {
    res.status(405).json({ error: "POST only" });
    return;
  }
  const key = process.env.GROQ_API_KEY;
  if (!key) {
    res.status(503).json({ error: "GROQ_API_KEY is not configured" });
    return;
  }

  // Only the description is taken from the client; the system prompt is fixed here, so the
  // endpoint can't be used as a general-purpose LLM proxy.
  const prompt = typeof req.body?.prompt === "string" ? req.body.prompt.trim().slice(0, MAX_PROMPT_LENGTH) : "";
  if (!prompt) {
    res.status(400).json({ error: "prompt is required" });
    return;
  }

  for (const model of MODELS) {
    let response;
    try {
      response = await fetch("https://api.groq.com/openai/v1/chat/completions", {
        method: "POST",
        headers: { Authorization: `Bearer ${key}`, "Content-Type": "application/json" },
        body: JSON.stringify({
          model,
          messages: [
            { role: "system", content: SYSTEM_PROMPT },
            { role: "user", content: `Object description: "${prompt}"\nReturn the PhysicalObjectConfig JSON object.` },
          ],
          response_format: { type: "json_object" },
          reasoning_effort: "low",
          include_reasoning: false,
          max_completion_tokens: 1024,
        }),
      });
    } catch {
      continue;
    }
    if (response.ok) {
      const data = await response.json();
      const content = data?.choices?.[0]?.message?.content;
      if (content) {
        res.setHeader("Content-Type", "application/json");
        res.status(200).send(content);
        return;
      }
    }
    if (response.status === 401 || response.status === 403) break; // bad key: another model won't help
  }
  res.status(502).json({ error: "physics compile failed" });
};
