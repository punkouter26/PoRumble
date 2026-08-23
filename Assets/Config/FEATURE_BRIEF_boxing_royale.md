# Feature Brief: PoRumble — Top-Down Boxing Battle Royale

> Produced by `/unity-interview` on 2026-08-23. Source inspiration:
> [Boxing (1980, Activision, Atari 2600)](https://en.wikipedia.org/wiki/Boxing_(1980_video_game)).
> This is a specification, not an implementation. Feed it to `/unity-workflow` or `/unity-feature`.

---

## Source Material — What We're Borrowing

Verified mechanics of the 1980 original:

| Original mechanic | Kept? | Adaptation for PoRumble |
|---|---|---|
| Top-down view of boxers | ✅ | Same, but 10 fighters |
| Arms extend on button press | ✅ | Two **independent** arms (L/R) instead of one button |
| Long punch = 1 pt, close punch = 2 pts | ✅ | Becomes **1 dmg / 2 dmg** — close range hits harder |
| First to 100 punches = KO | ⚠️ Reworked | Becomes **100 HP pool**; 0 HP = eliminated |
| 2-minute time limit | ⚠️ Reworked | Match runs until one boxer remains (see timeout rule) |
| "On the ropes" / juggling | ✅ | Fixed walls preserve cornering pressure |
| Exactly 2 fighters | ❌ | **10-fighter free-for-all** |
| Faces left/right only | ❌ | **Free 360° aim** |

**The key gap:** the original has no elimination — it's a 2-minute points race. Battle royale
requires one, so scoring becomes a damage/HP model.

---

## Scope

### Does
- Top-down 2D boxing with **10 fighters** in a free-for-all until one remains
- Each boxer has **two independently controlled arms**; each punch extends a glove toward the aim direction
- **A punch only deals damage when the glove contacts the target's face arc** — body, side, and back hits whiff
- **Range-dependent damage**, mirroring the original's scoring: long punch = 1, close punch = 2
- **HP pool of 100**; reaching 0 eliminates the boxer. Last one standing wins
- **ML-Agents-trained AI** boxers, developed via a 1v1 → 10-way curriculum
- **Two play modes:** all-AI simulation first, then a human-controlled boxer against 9 trained AI

### Does NOT
- No networked/online multiplayer — all 10 fighters are local (human or AI)
- No shrinking ring / storm circle — arena is **fixed** at 40×40
- No stamina, blocking, dodging animation states, or special moves in v1
- No knockdown/get-up states (HP model was chosen over knockdown counts)
- No body damage, no combo system, no ring-out elimination
- No persistence, save games, progression, or meta-economy
- No mobile build — desktop Windows standalone only
- No character art pipeline in v1 (primitives/placeholder sprites; Blender is available later)

### Trigger
Match start (scene load in sim mode, or player pressing Start). Per-boxer behavior is driven
each `FixedUpdate` by either an ML-Agents policy or human input.

### Output
- Visual: boxers moving/punching, arms extending, hit reactions, elimination
- Data: HP mutation, damage events, elimination events, match winner
- Training: ML-Agents observations/rewards streamed to the Python trainer

---

## Technical Requirements

| Item | Value |
|---|---|
| **Unity** | 6000.5.8f1 |
| **Pipeline** | URP 17.6.0, **2D Renderer** |
| **Platform** | StandaloneWindows64, Mono2x, .NET Standard 2.0 |
| **Physics** | **Physics2D only** — `Rigidbody2D`, `CircleCollider2D`, `BoxCollider2D`. No 3D colliders |
| **Input** | Input System 1.20.0 via ML-Agents `Heuristic()` |
| **ML** | ML-Agents 4.1.0 (Unity) + `mlagents` 1.1.0 (pip), communicator API 1.5.0 |
| **Performance** | **60 FPS** gameplay; **8–16 parallel arenas** in the training scene |
| **Draw calls** | Single sprite atlas for all boxer/arm/ring art; target < 50 draw calls |
| **Persistence** | **None.** No save data. Trained `.onnx` models are the only artifacts |
| **Multiplayer** | **None** — no networking, no authority model |

### New Dependencies Required

These are mandated by `.claude/rules/architecture.md` but **not currently installed**.
They must be added via an OpenUPM scoped registry before any code is written:

```jsonc
"scopedRegistries": [{
  "name": "package.openupm.com",
  "url": "https://package.openupm.com",
  "scopes": ["jp.hadashikick.vcontainer", "com.cysharp"]
}]
```
- `jp.hadashikick.vcontainer` — DI container
- `com.cysharp.messagepipe` (+ `com.cysharp.messagepipe.vcontainer`) — messaging
- `com.cysharp.unitask` — async

---

## Core Mechanics Specification

### Boxer

```
Boxer (Rigidbody2D, gravityScale 0, FreezeRotation)
├── Body        CircleCollider2D  r≈0.5   — blocks movement, takes no damage
├── Head        CircleCollider2D  r≈0.25  — offset forward from body center
│   └── FaceArc  120° forward arc on the head — the ONLY damageable region
├── ArmL        glove tip = CircleCollider2D (trigger), damage source
└── ArmR        glove tip = CircleCollider2D (trigger), damage source
```

### Punch resolution

1. Punch input → that arm enters `Extending` (~0.12s out, ~0.18s retract, then cooldown)
2. Glove tip trigger overlaps a **Head** collider
3. Compute the angle between the target's **facing vector** and the vector from target→glove
4. **If that angle > 60° (outside the face arc) → no damage, no score.** Whiff
5. Otherwise damage by range, measured attacker-center → target-center at contact:
   - `distance > CLOSE_RANGE_THRESHOLD` → **1 damage** (long punch)
   - `distance <= CLOSE_RANGE_THRESHOLD` → **2 damage** (close punch)
6. Apply knockback impulse to the target ("reel back slightly", per the original)
7. Publish `PunchLandedMessage`
8. One glove damages at most **one** target per extension (no sweeping multi-hits)

### Elimination

- HP starts at **100**, decremented by damage
- At `HP <= 0`: publish `BoxerEliminatedMessage`, disable colliders + agent, hide the boxer
- When **one** boxer remains: publish `MatchEndedMessage(winner)`
- Constants: `MAX_HEALTH = 100`, `LONG_PUNCH_DAMAGE = 1`, `CLOSE_PUNCH_DAMAGE = 2`

### Arena

40×40 fixed square, four `BoxCollider2D` walls (the "ropes"). Boxers can still punch while
pinned against a wall — cornering is a legitimate tactic, per the original's "on the ropes."

---

## ML-Agents Design

### Action space
| Type | Signals |
|---|---|
| Continuous (4) | `moveX`, `moveY`, `aimX`, `aimY` |
| Discrete (2 branches, size 2) | `punchLeft` (0/1), `punchRight` (0/1) |

### Observations
Use **`RayPerceptionSensor2D`** for opponent detection — critical, because the opponent count
drops from 9 to 0 as boxers are eliminated and a fixed-size observation vector cannot encode a
variable-length list.

- **Rays:** ~16 rays, 360°, detecting tags `Boxer` and `Wall`, with distance
- **Self scalars:** own HP (normalized), arm L state + cooldown, arm R state + cooldown,
  own velocity (2), own facing (2), boxers remaining (normalized)

### Reward function

| Event | Reward |
|---|---|
| Damage dealt | `+0.05 × damage` |
| Damage taken | `-0.02 × damage` |
| Eliminating an opponent | `+0.5` |
| Being eliminated | `-1.0` |
| Winning (last standing) | `+2.0` |
| **Existential penalty** | `-1 / MaxStep` **per step** |

> The existential penalty is the mitigation for the fixed (non-shrinking) ring. Without it,
> agents in a static arena reliably learn to flee and stall. Tune this first if training
> produces passive boxers.

### Training curriculum

| Stage | Setup | Goal | Command |
|---|---|---|---|
| 1 | 1v1 self-play | Learn range, aim, punch timing | `--run-id=pr_1v1` |
| 2 | 4-way FFA | Multi-threat awareness | `--initialize-from=pr_1v1` |
| 3 | 10-way FFA | Full battle royale | `--initialize-from=pr_4way` |

Self-play block is already stubbed (commented) in `Assets/Config/porumble_ppo.yaml`.
Behavior name must match the `BehaviorParameters` component exactly.

---

## Edge Cases

| Case | Expected Behavior |
|---|---|
| Two boxers land face hits on the same frame | Both resolve; both take damage. Mutual KO allowed |
| Punch lands on an already-eliminated boxer | Ignored — no damage, no reward, no message |
| Final two eliminate each other simultaneously | Match ends as a **draw**, no winner |
| Boxer eliminated mid-episode | `EndEpisode()` for that agent only; others continue uninterrupted |
| Episode hits `MaxStep` with >1 alive | Highest-HP boxer wins; exact tie = draw |
| Own glove overlaps own head | Ignored — attacker and target must differ |
| Glove overlaps a **body/side/back**, not the face arc | **No damage.** Explicit negative case |
| Boxer pinned against a wall | Can still move along it and punch — no lockout |
| Arm mid-extension when its owner is eliminated | Arm retracts, glove collider disabled immediately |
| Two boxers spawn overlapping | Spawn points enforce a minimum separation; Physics2D resolves residual overlap |
| Fast glove tunnels through a thin head collider | `Rigidbody2D.collisionDetection = Continuous` on gloves |
| Human `Heuristic()` active during a training run | Heuristic only used in inference; never during `mlagents-learn` |
| Scene reload / domain reload mid-match | All subscriptions disposed; no leaked MessagePipe handlers |
| 16 arenas × 10 boxers = 160 agents | Must hold 60 FPS in the training scene; profile before scaling past 8 |
| All boxers eliminated on the same frame | Match ends, winner = none. Must not hang waiting for a winner |

---

## Integration Points

Everything here is **new** — the project has zero C# today. These are the systems to create
and how they talk.

| System | Owns (Model) | Reads | Publishes | Subscribes |
|---|---|---|---|---|
| `BoxerSystem` | `BoxerModel[]` | Arena bounds | `PunchLandedMessage`, `BoxerDamagedMessage` | — |
| `CombatSystem` | — | `BoxerModel` | `BoxerEliminatedMessage` | `PunchLandedMessage` |
| `MatchSystem` | `MatchModel` | `BoxerModel[]` | `MatchEndedMessage` | `BoxerEliminatedMessage` |
| `SpawnSystem` | — | `MatchModel` | — | `MatchEndedMessage` |

### Data flow

```
  Human ──► InputView ──┐
                        ├──► BoxerAgentView ──► BoxerSystem ──► BoxerModel
  ML Policy ────────────┘   (: Agent, Heuristic)      │
                                    ▲                  │ PunchLandedMessage
                                    │                  ▼
                            AddReward()          CombatSystem
                                    │                  │ BoxerEliminatedMessage
                                    │                  ▼
                                    └──────────── MatchSystem ──► MatchEndedMessage
                                                       │
                                       BoxerView ◄─────┘ (observes BoxerModel)
```

`BoxerAgentView : Agent` is the single control path. ML-Agents drives it via
`OnActionReceived()`; the human drives the same code through `Heuristic()`. This is why
"both modes" costs almost nothing extra — it is the same seam the rules already prescribe
for `InputView`.

### Messages (all `readonly struct`)
- `PunchLandedMessage(int attackerId, int targetId, int damage, bool isCloseRange, Vector2 position)`
- `BoxerDamagedMessage(int boxerId, int newHealth)`
- `BoxerEliminatedMessage(int boxerId, int eliminatedById)`
- `MatchEndedMessage(int winnerId)` — `winnerId = -1` for a draw

---

## Assembly Placement

```
Assets/Scripts/
├── Models/   PoRumble.Models.asmdef     BoxerModel, MatchModel
├── Systems/  PoRumble.Systems.asmdef    → Models
├── Views/    PoRumble.Views.asmdef      → Models, Systems  (BoxerAgentView, BoxerView, ArmView)
└── Tests/    PoRumble.Tests.asmdef      → all three (EditMode)
```

`BoxerAgentView` needs the ML-Agents assembly reference; keep it confined to `Views` so the
Systems layer stays input-agnostic and unit-testable without ML-Agents.

---

## Acceptance Criteria

1. [ ] A glove contacting a target's **face arc** (within 60° of target facing) reduces target HP.
2. [ ] A glove contacting a target's **body, side, or back deals 0 damage** *(negative test)*.
3. [ ] A punch landing at range > `CLOSE_RANGE_THRESHOLD` deals exactly **1** damage.
4. [ ] A punch landing at range <= `CLOSE_RANGE_THRESHOLD` deals exactly **2** damage.
5. [ ] Left and right arms extend and cool down **independently**; both can be mid-extension at once.
6. [ ] A boxer's own glove **never** damages that same boxer *(negative test)*.
7. [ ] A boxer reaching exactly 0 HP is eliminated and emits `BoxerEliminatedMessage` **once** *(no double-elimination)*.
8. [ ] A match of 10 boxers ends with exactly one `MatchEndedMessage`, naming the last survivor.
9. [ ] If the final two are eliminated on the same frame, `MatchEndedMessage.winnerId == -1`.
10. [ ] Damage applied to an already-eliminated boxer is ignored and grants the attacker no reward.
11. [ ] A boxer pressed against a ring wall can still punch and move laterally.
12. [ ] `mlagents-learn` connects and completes ≥10k steps against the 1v1 scene without a communicator error.
13. [ ] A stage-1 (1v1) trained policy lands measurably more face hits than a random policy over 100 episodes.
14. [ ] The 10-boxer gameplay arena holds **≥60 FPS** on desktop.
15. [ ] The training scene with **8 parallel arenas (80 boxers)** holds **≥60 FPS**.
16. [ ] All boxer/arm/ring sprites render from **one atlas**; scene stays under **50 draw calls**.
17. [ ] No GC allocation in `Update`/`FixedUpdate` during a match (Profiler GC Alloc column reads 0).
18. [ ] Reloading the scene mid-match leaves no leaked MessagePipe subscriptions.

---

## Estimated Complexity

**Complex.** Not because any single mechanic is hard — the boxing model is genuinely simple —
but because three substantial pieces stack:

1. A greenfield architecture (DI + messaging + 4 assemblies, none of which exists yet)
2. A directional face-arc hit model that must be exactly right, or training learns nonsense
3. A three-stage RL curriculum, where each stage can fail to converge for non-obvious reasons

The riskiest item is **stage 3 convergence**. Budget for reward-tuning iterations; the
existential penalty and the elimination bonus are the two knobs most likely to need work.

---

## Recommended Approach

Build in this order, verifying each before moving on:

1. **Unblock architecture** — add the OpenUPM registry + three packages, create the 4 asmdefs.
2. **Core combat, no AI** (`unity-coder`) — Models/Systems/Views, face-arc hit resolution, HP,
   elimination. Validate against criteria 1–11 with EditMode tests (`unity-test-runner`) using
   two keyboard-driven boxers. **Get the hit model provably correct before any training.**
3. **Scale the ring** (`unity-scene-builder`) — 40×40 arena, 10 spawn points, via MCP.
4. **ML-Agents integration** — `BoxerAgentView`, ray sensors, reward function; stage 1 self-play.
5. **Curriculum stages 2 → 3**, transferring weights with `--initialize-from`.
6. **Human mode** — wire `Heuristic()` to the Input System actions.
7. **Optimization pass** (`unity-optimizer`) — sprite atlas, batching, criteria 14–17.

Recommended agents: `unity-coder`, `unity-test-runner`, `unity-scene-builder`, `unity-optimizer`.

> ⚠️ Those agents declare `mcp__unityMCP__*`, which does not match the registered servers
> (`unity-pipeline`, `coplay-unity`). Fix that naming before delegating, or they will run
> without Unity access.
