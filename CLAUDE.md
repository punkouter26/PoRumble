# CLAUDE.md — PoRumble

Top-down 2D boxing battle royale, after the style of Activision's Boxing (Atari 2600, 1980).
Ten fighters, last one standing, with ML-Agents-trained opponents.

---

## Project Overview

| Property | Value |
|---|---|
| **Unity** | 6000.5.8f1 (Unity 6.5) |
| **Render Pipeline** | URP 17.6.0 — **2D Renderer** |
| **Dimensionality** | **2D** — Light2D, sprites, `Physics2D` only |
| **Build Target** | StandaloneWindows64, Mono2x |
| **ML** | ML-Agents 4.1.0 (Unity) + `mlagents` 1.1.0 (pip, in `.venv`) |

Prefer `Rigidbody2D` / `Collider2D` / `Physics2D`. The URP asset is wired to the 2D
Renderer, so 3D lit materials will not light correctly.

---

## Build & Run

| Task | How |
|---|---|
| Play | Open `Assets/Scenes/SampleScene.unity` → Play. 10 boxers, HUD, boxer #0 on keyboard |
| Controls | **WASD** move + aim · **J** left punch · **K** right punch · **Space** hold to charge a haymaker · **R** restart at the results screen · **F3** diagnostics overlay |
| Train | Activate `.venv`, run `mlagents-learn Assets/Config/porumble_ppo.yaml --run-id=pr_1v1`, then open `Training1v1.unity` and press Play |
| Watch training | `tensorboard --logdir results` |
| Tests | `unity command run_tests --mode EditMode` — 84 EditMode tests |

**Two configs on purpose.** `BoxerConfig.asset` (30 HP) is the game.
`BoxerConfig_Training.asset` (6 HP) is curriculum stage 1: at 30 HP a knockout needs 15–30
landed face hits, so every episode simply timed out and there was no terminal signal to
learn from.

---

## Architecture

Four assemblies enforce `Views → Systems → Models`:

```
Assets/Scripts/
├── Models/    PoRumble.Models.asmdef    depends on nothing
├── Systems/   PoRumble.Systems.asmdef   → Models, MessagePipe, VContainer
├── Views/     PoRumble.Views.asmdef     → Models, Systems, ML-Agents, Input System
└── Tests/     PoRumble.Tests.asmdef     EditMode only
```

DI is VContainer, cross-system events are MessagePipe. `ReactiveProperty<T>` and
`CompositeDisposable` are **hand-rolled in Models** rather than taken from R3 — R3 is
installed, but keeping them local means Models depends on nothing at all, which is what the
architecture rules actually ask for.

### Data flow

```
ML policy ─┐
           ├─► BoxerAgentView ─► BoxerSystem ─► BoxerModel
keyboard ──┘   (Agent, Heuristic)     │
                                      │ PunchLandedMessage
                                      ▼
                                 CombatSystem ─► BoxerEliminatedMessage
                                      │
                                      ▼
                                 MatchSystem ─► MatchEndedMessage ─► MatchHudView
```

`BoxerAgentView` is the single control path: the policy drives it via `OnActionReceived`,
a human via `Heuristic`.

Around that sits the presentation loop. `MatchFlowSystem` owns `MatchFlowModel`
(`Introducing → Countdown → Fighting → KnockoutHold → Results`) and is the only thing that
decides when combat ticks at all. `CombatFeedbackView`, `MatchHudView`,
`PlayerStatusHudView` and `SpectatorCameraView` are pure subscribers on top of it and of the
punch messages that were already being published.

```
MatchFlowSystem ─► MatchFlowModel.Phase ─┬─► MatchDirector   (gates BoxerSystem.Tick)
                                         ├─► MatchHudView    (countdown, result, restart prompt)
                                         └─► CombatFeedbackView (bell, countdown beeps)

PunchLanded / PunchBlocked / PunchEvaded / HaymakerThrown
        ├─► BoxerAgentView      (reward shaping — as before)
        ├─► CombatFeedbackView  (hitstop, impulse shake, particles, audio)
        └─► PlayerStatusHudView (damage vignette, counter flash)
```

### Things that are easy to get wrong

- **Hit detection is pure maths, not physics.** `CombatMath.ResolveHit` is a static function
  over the boxer roster. That keeps combat deterministic (which RL depends on) and testable
  without a scene.
- **The face arc is judged from the attacker's position**, not the glove's offset from the
  head. A glove landing dead-centre gives a zero-length vector, which previously let punches
  from behind score as clean face hits.
- **Hits are buffered for a whole tick** and the match resolved once at end of tick, so
  simultaneous knockouts both count.
- **Boxers are clamped to the ring in `BoxerSystem`.** Positions are model-driven, so the
  wall colliders alone contain nobody.
- **Arm segments are siblings of the torso, never children.** A nested `Rigidbody2D` is moved
  twice — once by physics, once by the hierarchy — which makes jointed limbs drift.
- **`CombatSystem` and `MatchSystem` are resolved eagerly** in `GameLifetimeScope`. They only
  subscribe to messages, so nothing injects them and VContainer would never construct them —
  punches would silently do nothing.
- **Charging is not an ML action.** The haymaker rides `BoxerSystem.SetCharge`, a side channel
  human and scripted controllers call directly. Adding a third discrete branch would change
  the action vector and `PoRumbleBoxer.onnx` would stop loading altogether — see *ML-Agents*.
- **The flow loop runs on unscaled time.** The knockout hold sets `Time.timeScale`, so a loop
  timed on scaled time would stretch itself by exactly the factor it just applied.
  `Time.timeScale` is global and outlives Play mode: `MatchDirector.Dispose` and
  `CombatFeedbackView.OnDestroy` both restore it, and so must anything else that touches it.
- **Training bypasses the presentation loop entirely.** `MatchDirector` branches on
  `BoxerSpawnPoints.AutoRestart`: a training scene jumps straight to `Fighting` and keeps the
  old per-episode reset. A countdown would burn episode steps on animation.
- **Sprite world sizes are load-bearing.** The boxer's parts are sized so the drawn glove sits
  where `CombatMath` expects it. Sprites are authored at a pixels-per-unit equal to their pixel
  width, so one sprite covers one world unit at scale 1 and the transforms carry over from the
  quads they replaced. Changing a sprite's PPU silently moves the fists away from the hitboxes.

---

## Scenes

| Scene | Purpose |
|---|---|
| `Assets/Scenes/SampleScene.unity` | The game: 40×40 ring, 10 boxers, HUD, feedback rig, spectator camera |
| `Assets/Scenes/Training1v1.unity` | Curriculum stage 1: 16×16 ring, 2 boxers at opposite walls, auto-restart |

Boxer prefab (`Assets/Prefabs/Boxer.prefab`) is an anatomical chain:

```
Boxer (container, never moved)
├── Torso        kinematic Rigidbody2D — head, neck, shoulders, colliders, agent, ray sensor
├── UpperArmL/R  HingeJoint2D → Torso   (shoulder, −20…80°)
├── ForearmL/R   HingeJoint2D → UpperArm (elbow, 0…145°, cannot hyperextend)
├── GloveL/R     HingeJoint2D → Forearm  (wrist, ±30°)
└── ArmL/R       ArmView, servos the three hinges from the model's extension
```

Tags `Boxer`, `BoxerFace`, `Wall` matter: ray sensors detect them, and the separate
`BoxerFace` collider is how an agent can tell it is looking at an attackable face rather
than someone's back.

---

## ML-Agents

- Behaviour name is **`PoRumbleBoxer`** and must match `BehaviorParameters` exactly.
- Actions: 4 continuous (`moveX`, `moveY`, `aimX`, `aimY`) + 2 discrete branches (punch L/R).
  **This vector is frozen.** `PoRumbleBoxer.onnx` is compiled against exactly this shape and
  against 11 self observations; growing either stops the model loading. The haymaker was
  therefore built as a side channel (`BoxerSystem.SetCharge`) rather than a third branch, and
  the counter window needs no action at all. Retrain before changing either.
- Observations: `RayPerceptionSensorComponent2D` (17 rays) plus 10 self scalars. Rays are
  used because the opponent count shrinks during a match and a fixed vector cannot encode a
  variable-length list.
- Rewards: damage dealt/taken, elimination, win, an existential penalty (the ring does not
  shrink, so idling must cost), plus dense shaping for aiming at and holding range on the
  nearest opponent.
- Curriculum: 1v1 self-play → 4-way → 10-way via `--initialize-from`. **Remove the
  `self_play` block for stages 2–3** — self-play models two-team games, not a free-for-all.

### Python environment pins — do not casually upgrade

| Package | Pin | Why |
|---|---|---|
| `torch` | **2.5.1** | 2.13 dropped the legacy ONNX exporter; the replacement needs `onnxscript`, which needs numpy ≥ 2, which mlagents forbids. Training runs but cannot export a model |
| `numpy` | **< 1.24** | mlagents requirement |
| `protobuf` | **< 3.21** | mlagents requirement |
| `setuptools` | **< 81** | 81+ removes `pkg_resources`, which the trainer imports |
| `wandb` | **0.16.6** | newer versions demand protobuf ≥ 5 |

Unity package 4.1.0 pairs with pip `mlagents` 1.1.0 — the numbers look mismatched but both
speak communicator API **1.5.0**. There is no newer `mlagents` on PyPI.

---

## MCP

| Server | Bridge | Use for |
|---|---|---|
| `unity-pipeline` | port 7800 | Settings, builds, tests, scene graph, `eval` |
| `coplay-unity` | port 6400 | Script editing, asset generation, ProBuilder, UI |

```powershell
unity status
unity command get_scene_hierarchy
unity command create_gameobject --name Foo --primitive quad --parent "/Ring"
unity command eval_file --file "Temp/evals/script.cs"
```

- Args are `--flag value`. **Run from PowerShell** — Git Bash rewrites `/Main Camera` into a
  filesystem path.
- `eval` bodies take **no `using` directives**; fully qualify types.
- For anything long, write to a file and use `eval_file` — PowerShell quoting mangles
  multi-line C#.
- Editing any `.cs` under `Assets/` triggers a domain reload, which **exits Play mode and
  kills a running training session**. Batch code changes before starting a run.

> The bundled agents in `.claude/agents/` declare `mcp__unityMCP__*`, which matches neither
> registered server. They will run without Unity access until renamed.

**Blender MCP** is registered but needs Blender running with the addon on port 9876.

---

## Rendering

URP **2D Renderer**. The pieces that are easy to get wrong:

- **Post-processing is enabled on the camera.** `DefaultVolumeProfile` carries Bloom, ACES
  tonemapping, a colour grade, vignette, chromatic aberration and film grain. That profile
  was fully authored and completely inert until `m_RenderPostProcessing` was turned on at the
  Main Camera — the single highest-value flag in the project. Grading runs in HDR;
  anti-aliasing is SMAA (MSAA does nothing useful for alpha-blended sprites).
- **Sorting layers are `Floor / Shadow / Default / Boxer / Glove / FX / Overlay`,** in render
  order. Everything used to share one layer, so nine renderers per boxer fought over
  order-in-layer and there was nowhere to put shadows or effects.
- **Every Light2D must target every sorting layer.** A 2D light carries an explicit list of
  layers it affects. Add a sorting layer without adding it to each light's
  `m_ApplyToSortingLayers` and the fighters go unlit — silently, with no warning.
- **Shadows are the most expensive thing in the scene.** Measured live: disabling the key
  light's shadows dropped SetPass calls from 69 to 37. Only the key light casts (the renderer
  budgets a single shadow render texture) and only the fighters have `ShadowCaster2D` — the
  corner posts had theirs removed because static scenery at the ring edge did not earn it.
- **Sprite pixels-per-unit equals the sprite's pixel width,** so one sprite is one world unit
  at scale 1. The hit maths is tuned against those dimensions; changing a PPU silently moves
  the drawn fists away from the hitboxes.
- **`Assets/Art/Atlases/BoxerAtlas.spriteatlasv2`** packs the fighters, the impact spark and
  the ring dressing. The tiling `ring_canvas` and `ring_rope` are deliberately outside it:
  they are sampled by a material with Repeat wrapping, which atlasing breaks.

### Custom shader

`PoRumble/SpriteLitFX` is a variant of URP's Sprite-Lit-Default adding `_FlashAmount` (white
hit flash) and `_DissolveAmount` (knockout burn-away, procedural noise, no extra texture).
Built as a variant rather than from scratch so the fighters keep responding to 2D lights and
keep writing normals.

Two constraints when touching it:

1. **The `UnityPerMaterial` CBUFFER must be byte-identical in all three passes.** Unity
   silently drops a shader out of the SRP Batcher when pass layouts disagree.
2. **`BoxerView` clears its MaterialPropertyBlock the moment an effect ends.** A property
   block takes a renderer out of the shared sprite batch, so leaving one set permanently
   would turn ninety renderers into ninety draw calls for the whole match.

## Presentation & Game Feel

Five objects in `SampleScene` carry everything that is not simulation. All are optional —
`GameLifetimeScope` injects them only if present, which is why the training scenes can omit
every one of them.

| Object | Component | Does |
|---|---|---|
| `CombatFeedback` | `CombatFeedbackView` | Hitstop, impulse shake, five particle systems, impact lights, all audio |
| `RingLighting` | `RingAtmosphereView` | House lights down and key light in as the field thins |
| `CameraRig` | `SpectatorCameraView` | Frames the living fighters; tightens as the field thins |
| `PlayerStatusHud` | `PlayerStatusHudView` | Player health, breath, haymaker meter, hit vignette, behind-you warning |
| `DiagnosticsHud` | `DiagnosticsHudView` | **F3** telemetry overlay |
| `MatchInput` | `MatchInputView` | The restart key |
| `MatchHud` | `MatchHudView` | Survivors, per-boxer health, countdown, result banner |

## Audio

`Assets/Audio/PoRumbleMixer.mixer` routes **Master → SFX / UI / Ambience**. Punches play
through a pool of positioned 3D voices (`SpatialVoicePool`) so a hit across the ring is
quieter and off to one side; the bell and countdown are non-positional and go to UI. DSP
buffer is 512 rather than the default 1024, because ~23ms of latency is audible on a punch.

**Audio is synthesised at runtime** in `ProceduralSfx` — the project has no audio assets, and
a boxing game where landing, blocking and whiffing all sound identical loses most of what
tells the player what happened. Swap in recorded one-shots whenever they exist; nothing but
that one class has to change.

**The camera writes its own transform.** `SpectatorCameraView` computes the centre and extent
from the models and drives a bare `CinemachineCamera` directly, so there is no position-control
component to configure. Cinemachine is there for the brain blend and for impulse shake.
`LensSettings.Orthographic` is read-only — projection comes from the brain's source camera.

**The HUD is styled from `Assets/UI/Styles/porumble.uss`,** not from inline C#. Tokens for
colour, spacing and type live in `:root`; views assign class names and set only genuinely
dynamic values (a bar's width percentage). Both HUDs previously carried their own copy of the
palette as `static readonly Color` fields, free to drift apart.

**The diagnostics overlay's counter names were read back from
`ProfilerRecorderHandle.GetAvailable`, not assumed.** This Unity version publishes no plain
`Draw Calls Count` or `Batches Count`; a recorder asking for one reports zero forever rather
than erroring. `Shadow Casters Count` counts only 3D casters, and `Video Memory Bytes` is the
adapter total — both were tried and dropped as confidently-wrong numbers.

## Combat Depth

- **Haymaker.** Hold charge to wind up; release to throw. Costs mobility while held, locks out
  the ordinary jab, and the swing itself is slower — that telegraph is the counterplay. A
  release below `MinChargeToRelease` throws an ordinary punch, so tapping is never a wasted
  input.
- **Counter window.** Blocking a punch opens `CounterWindowDuration` seconds during which your
  next landed punch takes `CounterDamageBonus`. Consumed by the punch that uses it, so one
  block buys exactly one counter. This applies to every fighter, the trained policy included —
  it needs no new action.

## Difficulty Tiers

`BrainProfile` assets in `Assets/Config/Brains/` replace what used to be a block of constants
in `ScriptedBoxerBrain`, so one roster can field a spread of opponents:

| Profile | Reads as |
|---|---|
| `Brain_Rookie` | Slow to react, wild aim, never commits |
| `Brain_Journeyman` | Competent, unremarkable |
| `Brain_Pressure` | Walks you down and throws haymakers |
| `Brain_CounterPuncher` | Patient, accurate, punishes a blocked punch |

`BoxerSpawnPoints._rosterTiers` fills the roster in order after the player; whoever is left
over keeps the trained `PoRumbleBoxer.onnx` policy. `SampleScene` currently fields six scripted
fighters across the four tiers and three on the policy.

The brain is seeded per boxer from a local xorshift rather than `UnityEngine.Random`, so it
stays deterministic — training depends on the sparring partner being reproducible.

## Coding Rules

`.claude/rules/` is authoritative. The ones that bite most often:

- **Never `?.` on a UnityEngine.Object** — it bypasses the destroyed-object check. Use `== null`.
- **`[FormerlySerializedAs]` on every serialized-field rename**, or Inspector data is lost.
- **No legacy Input** — blocked by hooks.
- **No coroutines** — UniTask instead.
- **`private` by default**; no speculative public API.
- **Sprite atlases are mandatory for 2D.** `Assets/Art/Atlases/BoxerAtlas.spriteatlasv2`
  packs the boxer parts and the impact spark. The tiling `ring_canvas` and `ring_rope` are
  deliberately *outside* it: they are sampled by a material with Repeat wrapping, which
  atlasing breaks. Sprite Atlas V2 is the project's packer mode.
