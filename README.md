# Materialize: Matter Synthesis

Cambridge × Arcade AI Hackathon, Game Tech Track. Unity 6.3 LTS (6000.3.2f1) + URP 17.3.

**Play in your browser: https://materialize-eight.vercel.app** (desktop Chrome, Edge or Firefox; click the game to capture the mouse)

Type a description of an object at runtime. A physics compiler (an LLM, with an offline keyword fallback) turns it into a strict JSON physics profile (shape, size, mass, restitution, friction, PBR colour). The Matter Gun then materializes that object, with a real Rigidbody and PhysicsMaterial, to solve ten test chambers. Each chamber is built around a different physical property.

**No hints in-game.** Signs and the HUD show only each chamber's name. The bucket gauge shows its current load but not the target, and the force field, scale and gates give feedback without explaining themselves. Players work out every puzzle from the room itself. The solutions below are for the team only.

## Play

1. Open `Assets/Scenes/Materialize.unity` and press **Play**. Click the Game view to capture the mouse.
2. Controls: **WASD** move · **Space** jump · **Shift** sprint · **T** open the synthesis prompt · **Enter** fire · **Esc** cancel / release mouse · **G** re-fire the last matter · **X** recycle the matter under the crosshair.
3. Matter lands where the crosshair points. Long objects (≥ 2.5 m) land with their **far end** on the crosshair, so a beam aimed at a far target spans back towards you. At most 12 objects exist at once; the oldest dissolves.

### LLM physics compiler

`Systems > PhysicsPromptService` chooses the provider. The default is `Auto`, which uses the first provider with a key, in this order:

1. Anthropic (`claude-sonnet-5-5`)
2. Groq (`openai/gpt-oss-120b`, then `openai/gpt-oss-20b` if that model is retired or rate-limited; reasoning effort is set to low for speed)
3. OpenAI (`gpt-4.1-mini`)

You can also pick one provider explicitly, `CustomRelay` (POST `{prompt, system}` to `relayUrl`), or `OfflineOnly`.

- **Editor:** use **Materialize > Set LLM API Keys…**, paste the key into its box (Anthropic, Groq or OpenAI) and click Save. Keys are stored in this machine's EditorPrefs and never in the scene or the repo.
- **Builds:** set `ANTHROPIC_API_KEY`, `GROQ_API_KEY` or `OPENAI_API_KEY` in the environment.
- **No key, no network, timeout (12 s) or an unreadable reply:** the offline compiler answers instantly, so a demo never stalls. The telemetry panel shows which compiler answered.
- A mass written in the prompt ("1000kg", "2.5 t") always wins over the model's estimate.

## Chamber solutions (spoilers: team only)

| Chamber | Law | Hidden rule | Verified solution |
|---|---|---|---|
| 01 Counterweight | Mass | The suspended bucket needs at least 500 kg | `solid tungsten cube 1000kg` aimed into the bucket |
| 02 The Ledge | Restitution | The exit sits on a sheer 5 m ledge | `super bouncy rubber trampoline pad` in front of the ledge, then jump on it (it relaunches you about 6 m). Or stand against the back wall, aim at the base of the ledge and fire `large concrete ramp 5m tall 7m long` |
| 03 The Chasm | Conductivity | Conductive matter must touch both copper terminals | From behind the near terminal, aim at the top of the far terminal and fire `long copper bridging beam` |
| 04 The Flood | Density | 7 m of deep water. Matter less dense than water floats (Archimedes); you can't swim | From the near edge, aim at the far platform's edge and fire `wide wooden plank 8m long`. Or float stepping stones such as `large cork raft` and jump across |
| 05 Shatterpoint | Momentum | The glass seal breaks only on an impact of at least 6,000 kg·m/s (mass × speed). Matter just placed against it does nothing | From the gallery, aim at the top of the launch ramp and fire `giant lead ball`. It rolls down and smashes the seal |
| 06 Live Wire | Insulation | Touching the 10 m deck zaps you back to the start. Insulators are safe; metal lying on the deck goes live too | From the entrance, aim at the far platform's edge and fire `long rubber mat 11m long`, then walk across it |
| 07 The Scales | Precise mass | A sealed counterweight of 640 kg. The beam tilts towards the heavier side; the exit opens when your pan is within ±8% | `640kg steel block` in the near-left pan. Or iterate: fire weights, recycle with **X**, and watch the tilt |
| 08 Grip | Friction | A 35° slope over acid, behind a force field the Matter Gun can fire through. The pressure plate needs at least 150 kg resting for 1.5 s | Aim through the field at the striped plate and fire `rubber cube` (dynamic friction 0.8 > tan 35°). Ice, steel or a ball slide into the acid |
| 09 Marble Run | Shape | A 0.6 m gutter runs through a porthole to a hidden button | Aim into the near end of the gutter and fire `steel ball`. It rolls through the wall; cubes stay put unless they're frictionless |
| 10 Synthesis | Everything | First cross a live deck. Then three bars block the exit, one per lock: weigh plate ≥ 1,500 kg, terminals 7.4 m apart, glass core ≥ 4,000 kg·m/s | Cross on rubber, then fire `1500kg tungsten block` on the plate, `8m copper beam` aimed at the far terminal, and `giant lead ball` at the top of the launch ramp |

Every chamber was verified in Play mode with automated tests, using the offline compiler so results are repeatable. The bounce and ramp routes in Chamber 02 follow from the controller code but haven't been played by hand yet.

### Smoke test: Chamber 1 with "solid tungsten cube 1000kg"

1. Open `Assets/Scenes/Materialize.unity` and press **Play**. You spawn at the entrance of Chamber 01, facing the portcullis.
2. Walk forward (**W**) to the yellow-and-black pit edge and look down into the bucket. Put the crosshair on its inside (the floor or the far inner wall).
3. Press **T**, type `solid tungsten cube 1000kg`, and press **Enter**.
4. Expected result: a 0.37 m, 1,000 kg grey metal cube materializes in the bucket, and the telemetry shows `MASS 1,000.0 kg · BOUNCINESS 5% · FRICTION 0.40 / 0.50 · DENSITY 19,300 kg/m³`. The `LOAD 1,000 kg` readout turns green and the bucket sinks 1.2 m. The portcullis rises 3.6 m, the light above the arch turns from red to green, and the message reads "Counterweight engaged · portcullis rising".
5. Walk through the arch into the lit vestibule. You are moved to Chamber 02.

If the cube misses the bucket, aim at it and press **X** to recycle it. Then press **G** to fire it again.

## Rebuilding

The level is generated, not hand-placed:

1. **Blender:** run `ArtSource/build_chambers.py` (Text Editor > Run Script, or through the Blender MCP). It rebuilds the ten chamber scenes in `ArtSource/Materialize_Chambers.blend` and exports `Assets/Art/Chambers/*.fbx`.
2. **Unity:** run **Materialize > Build Scene**. It recreates `Assets/Scenes/Materialize.unity`: URP materials and FBX remaps, MeshColliders with physics materials, the puzzle rigs, the player, the HUD, lighting and post-processing.
3. **Unity:** run **Materialize > Validate Scene**. It checks layers, colliders and PhysicsMaterials, and every puzzle hook-up.

## Web build (Vercel)

The live site is the Unity WebGL build in `web/`, deployed on Vercel.

- **LLM on the web:** a web build can't keep an API key secret, so in the browser the game posts prompts to the serverless function `web/api/materialize.js`. That function calls Groq using the `GROQ_API_KEY` environment variable on the Vercel project. To turn it on, open Vercel, go to the project's **Settings > Environment Variables**, add `GROQ_API_KEY` for Production, then redeploy. Until then the web game uses the offline compiler, which is fully playable.
- **Rebuild and redeploy:**
  1. In Unity, build for **Web** to `Builds/WebGL`. Everything is already configured: the `PROJECT:Materialize` template (full window), Gzip with decompression fallback, and PC quality.
  2. Copy `index.html`, `Build/` and `TemplateData/` from `Builds/WebGL` into `web/`, keeping `web/api` and `web/vercel.json`.
  3. Run `vercel deploy web --prod`.
- `Assets/link.xml` stops the web build from stripping the physics and audio classes that the Matter Gun and ImpactAudio create at runtime.

## Code map (`Assets/Scripts`)

| File | Role |
|---|---|
| `PhysicalObjectConfig.cs` | The JSON physics profile: shape, dimensions, mass, bounciness, frictions, hexColor, roughness, metalness, prompt |
| `PhysicsPromptService.cs` | LLM calls via UnityWebRequest (Anthropic, OpenAI, relay), strict-JSON system prompt, offline keyword compiler |
| `MatterGun.cs` | T / Enter prompt flow, centre-screen raycast, placement, Rigidbody + runtime PhysicsMaterial (friction Multiply) + URP Lit material |
| `SpawnedMatter.cs` | Materialize and dissolve effects, conductivity, impact sound and camera shake scaled by `relativeVelocity × mass` |
| `WeightTriggerPlate.cs` | Sums the Rigidbody mass inside its box and opens doors at the threshold |
| `ElectricalBridge.cs` | Detects conductive matter, or a chain of it, linking two copper nodes |
| `ChamberDoor.cs` | Smooth translate and rotate movers (portcullis, sliding doors, drawbridge, counterweight bucket) |
| `FirstPersonController.cs` | CharacterController, mouse look, jump, bounce off springy matter, push loose matter |
| `MatterHUD.cs` | Crosshair, prompt bar, telemetry, objective, flash messages |
| `BuoyancyVolume.cs` | Water: Archimedes upthrust (1000 kg/m³ × submerged volume × g) plus drag |
| `ImpactSeal.cs` | Glass that shatters into shards above a momentum threshold, then opens doors |
| `ElectrifiedFloor.cs` | Live deck: shocks the player on contact and electrifies conductive matter touching it, including chains |
| `BalanceScale.cs` | Beam balance against a sealed counterweight; tilts with the imbalance and opens when level |
| `ZoneTrigger.cs`, `HazardZone.cs`, `ChamberExit.cs`, `RopeStretch.cs`, `FlickerLight.cs`, `ImpactAudio.cs`, `CameraShake.cs`, `GameInput.cs` | Reach-the-far-side triggers, acid/pit/water respawn, chamber flow, pulley cable, coil arcs, synthesized audio, shake, input shim |
| `Editor/MaterializeSceneBuilder.cs`, `Editor/MaterializeValidator.cs`, `Editor/MaterializeApiKeyWindow.cs` | Scene assembly, validation, key storage |

## Notes

- Unity 6 renamed `PhysicMaterial` to `PhysicsMaterial`, so the code uses the new name.
- Spawned matter is held kinematic for 0.35 s while it materializes. It then switches to dynamic with `CollisionDetectionMode.Continuous`.
- Matter that would spawn inside a wall, fixture or other matter is first slid out sideways, then lifted clear, so the physics engine never flings it. On slopes and ramps it spawns flush with the surface.
- Pressure plates need their load to rest for a moment (0.4 s; 1.5 s in Grip), so matter sliding or bouncing across doesn't count.
- Active Input Handling is set to **Both**, so the uGUI prompt field works alongside the Input System. Unity logs a one-line "Input Manager is marked for deprecation" notice because of this.
- On opening, Unity may show "Packages with Errors". The package `com.unity.nuget.newtonsoft-json`, pulled in by the `com.unity.pipeline` CLI package, reports an invalid signature. It is harmless. Click **Dismiss**.
