# Materialize: Matter Synthesis

Cambridge × Arcade AI Hackathon, Game Tech Track. Unity 6.3 LTS (6000.3.2f1) + URP 17.3.

**Play in your browser: https://materialize-eight.vercel.app** (desktop Chrome, Edge or Firefox; click the game to capture the mouse)

A first-person physics puzzle game across ten test chambers. Each chamber gives you an inventory of objects, and each object has real physical properties: mass, size, restitution, friction, density and conductivity. The Matter Gun materializes the selected object where you aim, as a real Rigidbody with its own PhysicsMaterial. Each chamber is built around a different physical law. Its inventory holds what solves it, plus a few things that don't, and each chamber adds a new kind of shape: cubes, then ramps and pads, beams, logs, balls, sheets and weights.

**No hints in-game.** Signs and the HUD show only each chamber's name. The telemetry panel lists the selected object's physical properties, but nothing says which object to use or where. The bucket gauge shows its current load but not the target, and the force field, scale and gates give feedback without explaining themselves. The solutions below are for the team only.

## Play

1. Open `Assets/Scenes/Materialize.unity` and press **Play**. Click the Game view to capture the mouse.
2. Controls: **WASD** move · **Space** jump · **Shift** sprint · **1–9** or the **mouse wheel** select an item · **left click** (or **F**) place it · **right click** (or **X**) recycle the object under the crosshair · **R** restart the level · **Esc** release the mouse.
3. A translucent preview shows where the selected object will land. Objects are squared to the room (nearest 90°) and face away from you. Long objects (≥ 2.5 m) land with their **far end** on the crosshair, so a beam aimed at a far target spans straight back towards you. Aiming into water drops the object onto the surface, where it floats or sinks.
4. Nothing is lost for good. Recycling, or losing an object to acid or a fall, returns it to the inventory. Walking through a chamber's exit loads the next chamber's inventory.
5. To start a level over, press **R**, or press **Esc** and click **RESTART LEVEL** (top right). The room's doors, plates, circuits, glass and scale reset, your inventory refills, and you're back at the level's entrance.

## Chamber solutions (spoilers: team only)

| Chamber | Law | Inventory | Hidden rule | Verified solution | Why the rest fail |
|---|---|---|---|---|---|
| 01 Counterweight | Mass | Tungsten Cube 1,000 kg ×1, Oak Crate ×3, Foam Block ×2 | The suspended bucket needs at least 500 kg | Tungsten Cube in the bucket | All the crates and foam together weigh 312 kg |
| 02 The Ledge | Restitution | Bounce Pad ×1, Timber Ramp ×1, Oak Crate ×2 | The exit sits on a sheer 5 m ledge | Bounce Pad in front of the ledge, then jump onto it: it relaunches you about 6 m. Or stand near the back wall, aim at the foot of the ledge and place the Timber Ramp (5 m rise, 37°), then walk up | Crates don't stack high enough |
| 03 The Chasm | Conductivity | Copper Beam ×1, Oak Beam ×1, Oak Crate ×2 | Conductive matter must touch both copper terminals | From behind the near terminal, aim at the top of the far terminal and place the Copper Beam | Oak doesn't conduct; you can walk the Oak Beam, but the exit stays locked |
| 04 The Flood | Density | Cork Raft ×2, Pine Log ×1, Steel Beam ×1, Lead Block ×1 | 7 m of deep water. Matter less dense than water floats (Archimedes); you can't swim | Aim at the water and float the two Cork Rafts (or the Pine Log) as stepping stones, then jump across | The Steel Beam is too short to span the pool, and steel and lead sink |
| 05 Shatterpoint | Momentum | Lead Ball ×1, Steel Ball ×1, Rubber Ball ×2, Oak Crate ×1 | The glass seal breaks only on an impact of at least 6,000 kg·m/s (mass × speed). Matter just placed against it does nothing | From the gallery, aim at the top of the launch ramp and place the Lead Ball. It rolls down and smashes the seal (about 60,000 kg·m/s) | The Steel Ball arrives at about 3,000 kg·m/s; the rest are far lighter |
| 06 Live Wire | Insulation | Rubber Mat 11 m ×1, Copper Sheet 11 m ×1, Oak Crate ×2 | Touching the 10 m deck zaps you back to the start. Metal lying on the deck goes live too | From the entrance, aim at the far floor just past the deck and place the Rubber Mat, then walk across it | The Copper Sheet goes live and zaps you |
| 07 The Scales | Precise mass | Steel weights: 500 kg ×1, 100 kg ×2, 50 kg ×2, 20 kg ×3, 10 kg ×2 | A sealed counterweight of 640 kg. The beam tilts towards the heavier side; the exit opens when your pan is within ±3% | Climb the viewing step, then place 500 + 100 + 20 + 20 kg (any mix totalling 630, 640 or 650 kg works) in the near-left pan | Too far off and the beam stays tilted; recycle and adjust |
| 08 Grip | Friction | Rubber Block ×1, Ice Block ×2, Rubber Ball ×1, Foam Block ×1 | A 35° slope over acid, behind a force field the Matter Gun can fire through. The pressure plate needs at least 150 kg resting for 1.5 s | Aim through the field at the striped plate and place the Rubber Block (238 kg, dynamic friction 0.8 > tan 35°) | Ice slides into the acid, the ball rolls, and foam grips but weighs 15 kg |
| 09 Marble Run | Shape | Steel Ball ×1, Steel Cube ×2, Oak Crate ×1 | A 0.6 m gutter runs through a porthole to a hidden button | Aim into the near end of the gutter and place the Steel Ball. It rolls through the wall | Cubes stay put on the shallow slope |
| 10 Synthesis | Everything | Rubber Mat 7 m, Tungsten Block 1,500 kg, Copper Beam 8 m, Lead Ball, Oak Beam 8 m, Rubber Ball, Oak Crate ×2 | First cross a live deck. Then three bars block the exit, one per lock: weigh plate ≥ 1,500 kg, terminals 7.4 m apart, glass core ≥ 4,000 kg·m/s | Cross on the Rubber Mat. Then place the Tungsten Block on the plate, the Copper Beam aimed at the far terminal, and the Lead Ball at the top of the launch ramp | Oak doesn't conduct; the crates and Rubber Ball are too light |

Every solution was verified in Play mode with automated tests. They place matter through the Matter Gun's own placement code, move the player with its CharacterController, and check the doors, plates, circuits, seals and scale. The routes covered include walking up the Timber Ramp onto the ledge, bouncing 6.2 m off the Bounce Pad onto it, and standing on a floating raft. The key failures were tested the same way: the crates too light for the bucket, the Oak Beam leaving the circuit open, the Steel Ball hitting the seal too softly, the Copper Sheet zapping you, 600 kg leaving the scale tilted, ice and the Rubber Ball sliding into the acid, foam too light for the plate, and cubes staying put in the gutter. So were refunds on recycle and acid, and the level restart.

### Smoke test: Chamber 1

1. Open `Assets/Scenes/Materialize.unity` and press **Play**. You spawn at the entrance of Chamber 01, facing the portcullis. The hotbar shows Tungsten Cube, Oak Crate and Foam Block.
2. Walk forward (**W**) to the yellow-and-black pit edge and look down into the bucket. Press **1** to select the Tungsten Cube. The telemetry shows `MASS 1,000.0 kg · BOUNCINESS 5% · FRICTION 0.40 / 0.50 · DENSITY 19,300 kg/m³`.
3. Put the crosshair on the inside of the bucket so the preview sits in it, then **left click**.
4. Expected result: a 0.37 m grey metal cube materializes in the bucket. The `LOAD 1,000 kg` readout turns green and the bucket sinks 1.2 m. The portcullis rises 3.6 m, the light above the arch turns from red to green, and the message reads "Counterweight engaged · portcullis rising".
5. Walk through the arch into the lit vestibule. You are moved to Chamber 02 with its own inventory.

If the cube misses the bucket, aim at it and **right click** to recycle it, then place it again, or press **R** to restart the level.

## Rebuilding

The level is generated, not hand-placed:

1. **Blender:** run `ArtSource/build_chambers.py` (Text Editor > Run Script, or through the Blender MCP). It rebuilds the ten chamber scenes in `ArtSource/Materialize_Chambers.blend` and exports `Assets/Art/Chambers/*.fbx`.
2. **Unity:** run **Materialize > Build Scene**. It recreates `Assets/Scenes/Materialize.unity`: URP materials and FBX remaps, MeshColliders with physics materials, the puzzle rigs, each chamber's inventory (`BuildLoadouts` in `MaterializeSceneBuilder.cs`), the player, the hotbar HUD, lighting and post-processing.
3. **Unity:** run **Materialize > Validate Scene**. It checks layers, colliders and PhysicsMaterials, the inventories and hotbar, and every puzzle hook-up.

## Web build (Vercel)

The live site is the Unity WebGL build in `web/`, deployed on Vercel as a static site.

1. In Unity, build for **Web** to `Builds/WebGL`. Everything is already configured: the `PROJECT:Materialize` template (full window), Gzip with decompression fallback, and PC quality.
2. Copy `index.html`, `Build/` and `TemplateData/` from `Builds/WebGL` into `web/`, keeping `web/vercel.json`.
3. Run `vercel deploy web --prod`.

`Assets/link.xml` stops the web build from stripping the physics and audio classes that the Matter Gun and ImpactAudio create at runtime.

## Code map (`Assets/Scripts`)

| File | Role |
|---|---|
| `PhysicalObjectConfig.cs` | One object's physics profile: shape, dimensions, mass, bounciness, frictions, colour, roughness, metalness |
| `ChamberLoadout.cs` | A chamber's inventory: items, each with a name, a count and a config |
| `Inventory.cs` | The current chamber's hotbar: counts, selection, take and refund |
| `LevelFlow.cs` | Level restart (R or the HUD button): reloads the scene and returns to the current chamber's entrance with a full inventory |
| `MatterGun.cs` | Item selection, centre-screen aim (including water surfaces), placement preview, placement squared to the room, recycle, and building each Rigidbody with a runtime PhysicsMaterial (friction Multiply) and a URP Lit material |
| `SpawnedMatter.cs` | Materialize and dissolve effects, conductivity, impact sound and camera shake scaled by `relativeVelocity × mass`, and the refund when it's gone |
| `ShapeIcon.cs` | Hotbar icons: a small CPU ray tracer draws each item's real shape, proportions and colour |
| `MatterHUD.cs` | Crosshair, hotbar, telemetry, chamber name, flash messages |
| `WeightTriggerPlate.cs` | Sums the Rigidbody mass inside its box and opens doors at the threshold |
| `ElectricalBridge.cs` | Detects conductive matter, or a chain of it, linking two copper nodes |
| `ChamberDoor.cs` | Smooth translate and rotate movers (portcullis, sliding doors, drawbridge, counterweight bucket) |
| `FirstPersonController.cs` | CharacterController, mouse look, jump, bounce off springy matter, push loose matter |
| `BuoyancyVolume.cs` | Water: Archimedes upthrust (1000 kg/m³ × submerged volume × g) plus drag |
| `ImpactSeal.cs` | Glass that shatters into shards above a momentum threshold, then opens doors |
| `ElectrifiedFloor.cs` | Live deck: shocks the player on contact and electrifies conductive matter touching it, including chains |
| `BalanceScale.cs` | Beam balance against a sealed counterweight; tilts with the imbalance and opens when level |
| `ZoneTrigger.cs`, `HazardZone.cs`, `ChamberExit.cs`, `RopeStretch.cs`, `FlickerLight.cs`, `ImpactAudio.cs`, `CameraShake.cs`, `GameInput.cs` | Reach-the-far-side triggers, acid/pit/water respawn, chamber flow and inventory hand-over, pulley cable, coil arcs, synthesized audio, shake, input shim |
| `Editor/MaterializeSceneBuilder.cs`, `Editor/MaterializeValidator.cs` | Scene assembly (including every chamber's inventory), validation |

## Notes

- Unity 6 renamed `PhysicMaterial` to `PhysicsMaterial`, so the code uses the new name.
- Placed matter is held kinematic for 0.35 s while it materializes. It then switches to dynamic with `CollisionDetectionMode.Continuous`.
- Matter that would spawn inside a wall, fixture or other matter is first slid out sideways, then lifted clear, so the physics engine never flings it. On slopes and ramps it spawns flush with the surface.
- Pressure plates need their load to rest for a moment (0.4 s; 1.5 s in Grip), so matter sliding or bouncing across doesn't count. Their sensors reach well above the plate, so stacked matter counts too.
- Spheres use the engine's own primitive mesh. The built-in resource `Sphere.fbx` is a legacy 2 m sphere, so it isn't used.
- Active Input Handling is set to **Both**, so Unity logs a one-line "Input Manager is marked for deprecation" notice. Gameplay reads the Input System through `GameInput.cs`.
- On opening, Unity may show "Packages with Errors". The package `com.unity.nuget.newtonsoft-json`, pulled in by the `com.unity.pipeline` CLI package, reports an invalid signature. It is harmless. Click **Dismiss**.
