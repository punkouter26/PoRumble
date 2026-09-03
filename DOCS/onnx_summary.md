# ONNX & Rig Model Inventory

**Project:** PoRumble — top-down 2D boxing battle royale, ML-Agents 4.1.0 / Unity 6000.5.8f1
**Generated:** 2026-09-03
**Inference runtime:** Unity Inference Engine (`Unity.InferenceEngine`, the package formerly called Sentis). ML-Agents 4.1.0 references it directly; Barracuda is not present in this project.

---

## Tier 1 — 30-second summary

One network, 119,313 parameters, drives every AI fighter in the ring. The observation space was rebuilt (20 scalars, single ray stack) and the compiled model still declares the old one (15 scalars, double ray stack), so its inputs no longer describe the game it plays. It runs anyway — ML-Agents raises no error — but **degraded**, aiming at 0.33–0.57 where a healthy policy manages 0.7+. A 595k-step replacement was trained and measured **worse** than it (−0.24 to +0.06): stage 1 does not transfer to a ten-way. Finish the curriculum before promoting anything.

There are **no ArticulationBody or ConfigurableJoint rigs in this project.** Every fighter is a 2D chain of six `HingeJoint2D` revolute joints driven by velocity-target motors. That is the honest answer to Matrix B, and the sections below give the real components rather than mapping them onto 3D equivalents they do not have.

---

## Tier 2 — Matrix A: Models

| Field | `PoRumbleBoxer` |
|---|---|
| **Prefab** | `Assets/Prefabs/Boxer.prefab` → `Torso/BehaviorParameters` |
| **Behaviour name** | `PoRumbleBoxer` (must match the config exactly) |
| **ONNX path** | `Assets/ML-Agents/Models/PoRumbleBoxer.onnx` |
| **File size** | 486,595 bytes (475 KB) |
| **Producer** | pytorch 2.5.1 · IR version 4 · opset ai.onnx 9 |
| **Parameters** | **119,313** |
| **Run ID** | `ffa_v5`, ~21M cumulative steps (recorded in CLAUDE.md; `results/` is absent from this clone, so the figure is **documented, not independently verified here**) |
| **Final mean reward** | ~6.05–6.12 on the ten-way (same caveat — from project notes, not from a local TensorBoard event file) |
| **Promotion status** | **Shipped, but mis-calibrated** — see the shape mismatch below |

### Input / output tensors — read from the file, not assumed

| Tensor | Direction | Shape | Meaning |
|---|---|---|---|
| `obs_0` | in | `[batch, 170]` | `RayPerceptionSensorComponent2D` — 17 rays × 5 floats × **2 stacks** |
| `obs_1` | in | `[batch, 30]` | vector sensor — **15** self scalars × 2 stacks |
| `action_masks` | in | `[batch, 4]` | 2 discrete branches × 2 options |
| `continuous_actions` | out | `[batch, 4]` | `moveX, moveY, aimX, aimY` |
| `discrete_actions` | out | `[batch, 2]` | punch left, punch right |
| `deterministic_continuous_actions` | out | `[·, 4]` | greedy variant |
| `deterministic_discrete_actions` | out | `[·, 2]` | greedy variant |
| `version_number`, `memory_size`, `continuous_action_output_shape`, `discrete_action_output_shape` | out | `[1]` / `[1,2]` | ML-Agents metadata |

### Layer stack

| Layer | Op | Shape | Weights |
|---|---|---|---|
| body encoder 0 | `Gemm` | 200 → 256 | 51,200 |
| body encoder 2 | `Gemm` | 256 → 256 | 65,536 |
| continuous μ head | `Gemm` | 256 → 4 | 1,024 |
| discrete branch 0 | `Gemm` | 256 → 2 | 512 |
| discrete branch 1 | `Gemm` | 256 → 2 | 512 |
| | | **total** | **119,313** (incl. biases and log-sigma) |

Graph ops present: `Add, ArgMax, Clip, Concat, Constant, Div, Exp, Gemm, Identity, Log, Mul, Multinomial, RandomNormalLike, Sigmoid, Slice, Softmax, Sub`. No convolution, no recurrence — `memory_size` is 0 and there is no LSTM, so the policy is purely reactive over a 2-frame stack.

### The shape mismatch — and what actually happens

The model declares `obs_0[170] + obs_1[30]` = **200 encoder inputs**. The current build emits:

- ray sensor: 17 × 5 × **1 stack** = **85** (stacks were cut from 2 to 1)
- vector sensor: **20** scalars × 2 stacks = **40**
- **total 125**

125 ≠ 200. **It does not refuse to load.** ML-Agents logs no error and the fighters play — measured
in `SampleScene`, mean `dot(facing, toNearestOpponent)` is **0.33–0.57**, against the **0.7+** a
genuinely aiming policy produces. So the honest description is *degraded, not broken*: enough of the
ray block still lines up that aiming partly survives, but it is reading a layout it was never
trained on. The reward rebalance, the shaping curriculum and the punch-clash mechanic each invalidate it independently. `PoRumbleBoxer_obs11_legacy.onnx`, referenced in project notes, is **not present in this clone**.

### In-flight run

| Field | Value |
|---|---|
| Run ID | `pr_v6_spar_01` |
| Config | `Assets/Config/porumble_spar.yaml` (stage 1b — learner vs scripted partner) |
| Scene | `Assets/Scenes/Training1v1.unity` |
| Status | complete — 6 checkpoints, final at `results/pr_v6_spar_01/PoRumbleBoxer.onnx` |
| Curve | −3.206 (step 10k) → **4.835 peak** (step 490k) |
| Throughput | 167 steps/s · 595,490 steps in 60 minutes |
| Checkpoints | 6, in `results/pr_v6_spar_01/PoRumbleBoxer/` |

**Measured head to head, it is worse than the model it would replace.** Same scene, same
conditions, `SampleScene` with the full card:

| Model | Steps | Trained on | Aim quality |
|---|---|---|---|
| `PoRumbleBoxer` (`ffa_v5`) | ~21M | ten-way | **0.33 – 0.57** |
| `pr_v6_spar_01` | 595k | 1v1 vs scripted | **−0.24 – +0.06** |

Below zero is worse than random — it is facing *away* from the nearest opponent as often as toward
it. 595k steps of stage 1 against a single scripted partner in a 20×14 ring does not transfer to a
40×40 ten-way, and the curriculum exists precisely because it does not. Finish stage 1, then
`--initialize-from` into the free-for-all before comparing again.

---

## Tier 2 — Matrix B: Physics & Actuators

**Rig type for every fighter is identical** — the roster differs by policy and attributes, not by skeleton. There is one `Boxer.prefab`.

| Property | Value |
|---|---|
| Creature | Boxer (×10 per ring) |
| Rig component | `Rigidbody2D` + `HingeJoint2D` — **not** `ArticulationBody`, **not** `ConfigurableJoint` |
| Drive mode | `JointMotor2D` — velocity-target motor with a torque ceiling. A **P controller**, not PD: `motorSpeed = clamp(DeltaAngle(jointAngle, target) × gain, ±1800°/s)`. No derivative term, no SLERP (a 2D hinge has no orientation to interpolate) |
| DOF | **6 revolute per fighter** — 3 per arm, all about Z. The torso is kinematic and scripted, contributing 0 simulated DOF |
| Gravity | `gravityScale = 0` on every body; the ring is top-down |
| Servo gain | 60 |
| Max motor speed | 1800 °/s |

### Per-segment specification

| Segment | Mass | Joint → parent | Limits | Motor torque | Colliders | Layer |
|---|---|---|---|---|---|---|
| Torso | 1.0 (kinematic) | — | — | — | `CircleCollider2D` r 0.64; `FaceProbe` r 0.80 trigger | `BoxerBody<id>` |
| UpperArm L/R | 0.030 | shoulder → Torso | −45…80° (mirrored −80…45°) | 260 | `CapsuleCollider2D` 0.28 × 0.73 | `BoxerArm` (18) |
| Forearm L/R | 0.018 | elbow → UpperArm | 0…145° (mirrored −145…0°) | 143 (×0.55) | `CapsuleCollider2D` 0.28 × 0.78 | `BoxerArm` (18) |
| Glove L/R | 0.012 | wrist → Forearm | ±25° | 52 (×0.20) | `CircleCollider2D` r 0.30 | `BoxerBody<id>` |

Masses are anatomical fractions of the torso — a human upper arm is ~2.7% of body mass, forearm ~1.6%, hand ~0.6%. The elbow limit stops at 145° because an elbow cannot hyperextend.

### Behavioural purpose of the rig — and its limits

The rig is **cosmetic**. Every hit is resolved by `CombatMath.ResolveHit`, a static function over the roster with no physics query at all, so combat stays deterministic (which RL depends on) and testable without a scene. The joints exist so the drawn limb folds and swings rather than telescoping.

Consequences that follow from that, and that a reader coming from a 3D locomotion project will not expect:

- **Training scenes switch the solver off.** `BoxerSpawnPoints._kinematicArms` stops all six bodies and six joints per fighter — 60 of each in a ten-way — and poses the glove transform directly at the position `CombatMath` already believes it occupies. Perception is therefore identical, not merely close.
- **Arm colliders are excluded from every ray sensor's mask**, not just their owner's. An untagged collider still occludes a ray, and a raised guard sits directly in front of the forward rays.
- **`HingeJoint2D.jointAngle` is cumulative and never wraps.** A raw `target − jointAngle` error runs away once a joint passes 180°: measured mid-fight at −6543° asking for 300,000 °/s, with gloves nine units from their own torso and never recovering. `Mathf.DeltaAngle` plus the speed clamp is the fix, and it is load-bearing.

---

## Tier 3 — Verification commands

Everything above was read from the artefacts, not from documentation. To reproduce:

```bash
# Tensor shapes and parameter count
.venv/Scripts/python.exe -c "
import onnx; m = onnx.load('Assets/ML-Agents/Models/PoRumbleBoxer.onnx')
print([(i.name,[d.dim_param or d.dim_value for d in i.type.tensor_type.shape.dim]) for i in m.graph.input])
print(sum(__import__('math').prod(t.dims) for t in m.graph.initializer))"

# Rig masses, limits and torques
grep -n "m_Mass:\|m_LowerAngle\|m_UpperAngle\|_maxMotorTorque\|_servoGain\|_maxMotorSpeed" Assets/Prefabs/Boxer.prefab

# What the vector sensor actually writes
grep -n "AddObservation" Assets/Scripts/Views/BoxerAgentView.cs
```

`VectorObservationSize` on the prefab **must** equal what `CollectObservations` writes — it is 20. ML-Agents does not fail loudly on a mismatch in every path; a compiled policy simply refuses to load.
