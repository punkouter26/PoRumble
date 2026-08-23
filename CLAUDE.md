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
| Controls | **WASD** move + aim · **J** left punch · **K** right punch |
| Train | Activate `.venv`, run `mlagents-learn Assets/Config/porumble_ppo.yaml --run-id=pr_1v1`, then open `Training1v1.unity` and press Play |
| Watch training | `tensorboard --logdir results` |
| Tests | `unity command run_tests --mode EditMode` — 32 EditMode tests |

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

---

## Scenes

| Scene | Purpose |
|---|---|
| `Assets/Scenes/SampleScene.unity` | The game: 40×40 ring, 10 boxers, HUD |
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

## Coding Rules

`.claude/rules/` is authoritative. The ones that bite most often:

- **Never `?.` on a UnityEngine.Object** — it bypasses the destroyed-object check. Use `== null`.
- **`[FormerlySerializedAs]` on every serialized-field rename**, or Inspector data is lost.
- **No legacy Input** — blocked by hooks.
- **No coroutines** — UniTask instead.
- **`private` by default**; no speculative public API.
- **Sprite atlases are mandatory for 2D.** None exist yet — the art is still placeholder
  quads and circles, so the draw-call criterion is not yet reachable.
