# Agent & Rig Inventory

Everything in this project that thinks, and everything it thinks with. Captured 2026-09-12.

---

## Tier 1 — Quick Look (30 seconds)

There is **one trained brain in the whole project**: a single 475 KB file called
`PoRumbleBoxer.onnx`. Nine of the ten boxers in a match run it at once.

They do not all fight the same, and that is a deliberate trick rather than nine brains. Each
AI fighter has a thin **style layer** that nudges the shared brain's answer — press forward
more, circle more, throw fewer punches, take the occasional free shot — plus four physical
attributes (power, chin, speed, recovery) that change what its punches are worth. The brain
underneath is byte-for-byte identical in all of them.

Alongside the trained fighters sits one **hand-written** opponent that is not AI at all. It
follows rules a person wrote, it is completely predictable, and it is always red — that is how
you spot a scripted bot on sight in this project.

**Status: the trained brain is finished and shipping.** It drives the game today. There is no
second model in testing, and no training history left in the working tree to compare against.

---

## Tier 2 — Core Mechanics

### Agent Model Table

| Agent | Engine | Brain file | Size | Task type | Success rate | Deployment |
|---|---|---|---|---|---|---|
| **PoRumbleBoxer** | Unity 6000.6.0f1, ML-Agents 4.1.0 | `Assets/ML-Agents/Models/PoRumbleBoxer.onnx` | **475 KB** (486,595 bytes) | Last-one-standing melee, 2–10 fighters | **76%** of matches finished before the bell | **Ready for game** — drives every policy seat in `SampleScene` |

That is the complete list. One file, one behaviour name, one network shape — 2 hidden layers
of 256 units, taking 15 self-numbers and 17 ray feelers (both stacked 2 deep), producing 4
continuous controls and 2 on/off controls.

**Read that success number carefully.** It is *not* a win rate — in a ten-way free-for-all
every fighter's win rate is 10% by construction, so win rate says nothing. It is the fraction
of matches that reach a real finish instead of running out of clock. That metric was chosen
over score deliberately, and the choice mattered: picking the checkpoint with the best **score**
would have shipped a brain that finished **21%** of its matches instead of 76%. The reward
function mildly punishes winning quickly — a short match truncates the episode, capping how
much damage-dealt reward can pile up — so score and the actual objective pull in opposite
directions here.

### The roster — one brain wearing eight hats

`Assets/Config/Fighters/` holds eight selectable contestants. Six of them are the same
`PoRumbleBoxer.onnx` with a style layer on top; one is the shared brain driven straight
through with nothing bent; one is not a brain at all.

| Contestant | Driven by | Tint | Style: pressure / circling / punch gate / opportunism | Charge · Slip | Power · Chin · Speed · Recovery |
|---|---|---|---|---|---|
| **HEURISTIC** | Hand-written brain, Journeyman tier | 🔴 `#D91F1F` | — scripted, no style layer — | — | 1.00 · 1.00 · 1.00 · 1.00 |
| **STANDARD RL** | `PoRumbleBoxer.onnx`, unbent | 🟢 `#1FBF40` | 0 / 0 / 1.00 / 0 | 0 · 0 | 1.00 · 1.00 · 1.00 · 1.00 |
| ALAN | `PoRumbleBoxer.onnx` + style | 🔵 `#4A82D6` | −0.15 / 0.25 / 0.55 / 0.05 | 0.15 · 0.55 | 1.15 · 0.95 · 1.00 · 1.05 |
| BIGGIE | `PoRumbleBoxer.onnx` + style | 🟥 `#D94A3D` | 0.55 / 0 / 0.70 / 0.15 | 0.55 · 0 | **1.45** · 0.70 · 0.80 · 0.85 |
| BLIPPI | `PoRumbleBoxer.onnx` + style | 🟡 `#F2C740` | 0.20 / **0.70** / 1.00 / 0.55 | 0.05 · 0.25 | 0.75 · **1.35** · **1.30** · 1.20 |
| BRENDAN | `PoRumbleBoxer.onnx` + style | 🟩 `#66BA5C` | 0.30 / 0.20 / 1.00 / 0.40 | 0.10 · 0.15 | 0.90 · 1.00 · 1.05 · **1.50** |
| DUPEE | `PoRumbleBoxer.onnx` + style | 🟣 `#B866C7` | **−0.45** / 0.35 / 0.45 / 0.10 | 0.35 · 0.40 | 1.30 · 1.05 · 1.00 · 1.00 |
| EPSTEIN | `PoRumbleBoxer.onnx` + style | 🟦 `#59C2C2` | −0.20 / 0.55 / 0.60 / 0.05 | 0 · **0.90** | 0.70 · 0.85 · 1.25 · 1.15 |

Reading the style columns: **pressure** positive walks you down and negative fights off the
back foot; **circling** is how much sideways movement gets added; **punch gate** is the share
of the brain's punch requests that actually get through (1.00 = all of them); **opportunism**
is the chance of a free extra punch when one is available. Charge and slip are probabilities
of using the two mechanics the brain itself cannot reach.

**Chin is inverted, and it is the one column that reads backwards.** It multiplies damage
*taken*, so BIGGIE at 0.70 is the hardest man in the ring to hurt and BLIPPI at 1.35 is made
of glass. The taglines in the asset files agree: BIGGIE is "granite chin", BLIPPI "cannot take
one back".

**The aim is never bent.** Pointing at an opponent is the one thing the network is genuinely
good at, and rotating its output produces a worse fighter rather than a different one. Every
style changes a decision *about* an aim the policy already found.

### The four scripted difficulty tiers

The red hand-written opponent is not one bot — it is four, from `Assets/Config/Brains/`.

**Which of them actually appear depends on the fight card, and right now only one does.**
`SampleScene` has all eight contestants assigned to `_fighterProfiles`, and assigning any
card **replaces the difficulty-tier path outright**. So the four-tier block still configured on
that object (2 + 2 + 1 + 1 = six scripted seats) is dead configuration — it is not what the
scene fields. What it actually fields is the card, dealt cyclically into ten chairs:

| Seat | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|---|
| Contestant | HEURISTIC | STANDARD RL | ALAN | BIGGIE | BLIPPI | BRENDAN | DUPEE | EPSTEIN | HEURISTIC | STANDARD RL |

That is **2 scripted seats and 8 policy seats** by default, with the first two contestants
fighting twice because the card is shorter than the ring. Only `Brain_Journeyman` is in play,
because that is the tier `Fighter_HEURISTIC` points at; Rookie, Pressure and Counter-Puncher
are configured and unused until someone changes the card or clears it.

| Tier | Reads as | Aggression | Reaction delay | Accuracy | Counters | Slips |
|---|---|---|---|---|---|---|
| `Brain_Rookie` | Slow to react, wild aim, never commits | 0.25 | **0.34 s** | **0.35** | 0.15 | — |
| `Brain_Journeyman` | Competent, unremarkable | 0.50 | 0.20 s | 0.62 | 0.45 | 0.20 |
| `Brain_Pressure` | Walks you down and throws haymakers | **0.95** | 0.10 s | 0.80 | 0.55 | 0.05 |
| `Brain_CounterPuncher` | Patient, accurate, punishes a blocked punch | 0.40 | 0.12 s | **0.92** | **0.95** | 0.55 |

The reaction delay is the honest difficulty dial: it is how long the bot is *not allowed* to
respond to what it can already see. A rookie at 0.34 s is a third of a second behind the
fight; a pressure fighter at 0.10 s is on top of it.

### Physics & Rig Table

Every contestant above — human, scripted and trained alike — wears exactly the same body. There
is one rig in this project.

| Character | Physics setup | Movement type | Moving parts | Main objective |
|---|---|---|---|---|
| **Boxer** (`Assets/Prefabs/Boxer.prefab`) | Unity `HingeJoint2D` chain on `Rigidbody2D` bodies; kinematic torso, dynamic arms | **Hybrid** — torso position written from the model each tick; arms servo to joint angles via hinge motors | **7 bodies, 6 joints**: torso, 2 upper arms, 2 forearms, 2 gloves | Be the last fighter standing |

Broken out:

| Part | Body type | Mass | Collider | Job |
|---|---|---|---|---|
| Torso | **Kinematic** | 1.00 | Circle r 0.64 | Carries the head, the agent, the ray sensor. Driven by `MovePosition` from the model |
| Upper arm L/R | Dynamic | 0.16 | **none** | Hinge segment only — sweeps through opponents on purpose |
| Forearm L/R | Dynamic | 0.09 | **none** | " |
| Glove L/R | Dynamic | 0.07 | Circle r 0.125 | The only part of the arm with physical presence |
| FaceProbe | Kinematic | — | Circle r 0.80, **trigger** | The hit-and-perception volume. Much larger than the drawn head (0.30) |
| Head | — | — | none | Drawn only; carries the swelling and cut effects |

**On gravity and mass, since those are usually the first questions.** `GravityScale` is 0 on
every body, and that is not floaty scaling — this is a **top-down** view, so Earth gravity
points *into the screen*, perpendicular to everything that moves. There is no in-plane fall to
simulate. The masses above are ratios tuned so the hinge servos settle rather than oscillate,
not kilograms; the torso is kinematic, which makes it infinitely massive to the solver
regardless of the 1.00 written on it.

The joint limits, by contrast, are anatomical and are worth checking against a real arm: the
elbow runs 0°–145° and **cannot hyperextend**, the shoulder runs −45° to 80°, and the wrist is
held to ±25°.

### Engine mapping — Unity ↔ Unreal

| This project (Unity) | Unreal equivalent |
|---|---|
| `PoRumbleBoxer.onnx` | `ULearningAgentsNeuralNetwork` data asset |
| `BehaviorParameters` component | `ULearningAgentsPolicy` |
| `FighterProfile` ScriptableObject | Primary Data Asset holding the same fields |
| `BrainProfile` ScriptableObject | Primary Data Asset for the scripted tiers |
| `Boxer.prefab` | Blueprint Actor |
| `HingeJoint2D` | `UPhysicsConstraintComponent`, angular limits on one axis |
| Kinematic `Rigidbody2D` | Primitive component with physics off, moved by `SetWorldLocation` |
| `StyleModulator` (plain C# class) | A `UObject` or struct applied between policy output and pawn input |

---

## Tier 3 — Setup Guide

### Asset paths

| What | Path |
|---|---|
| Trained brain | `Assets/ML-Agents/Models/PoRumbleBoxer.onnx` |
| Fighter rig | `Assets/Prefabs/Boxer.prefab` |
| Contestants | `Assets/Config/Fighters/Fighter_*.asset` (8) |
| Scripted tiers | `Assets/Config/Brains/Brain_*.asset` (4) |
| Game tuning | `Assets/Config/BoxerConfig.asset` |
| Training tuning | `Assets/Config/BoxerConfig_Training.asset` |
| Trainer configs | `Assets/Config/Training/porumble_*.yaml` (3) |
| Game scene | `Assets/Scenes/SampleScene.unity` |
| Training scenes | `Assets/Scenes/Training1v1.unity`, `Training10Way.unity` |
| Faces | `Assets/Art/Sprites/Faces/` (6, Git LFS) |
| Elo table, at runtime | `porumble_ratings.json` under `persistentDataPath` |

### Adding a contestant to the card

1. `Create > PoRumble > Fighter Profile` into `Assets/Config/Fighters/`.
2. Set **Display Name** and **Tagline** — both appear on the fight card.
3. Set **Control**: `Policy` for a trained fighter, `Scripted` for a hand-written one.
4. **Scripted only:** assign a `BrainProfile`. Leave it empty on a policy fighter.
5. Assign a **Face** sprite and a **Tint**. Mind the convention below.
6. Set the four style values and the four attributes. Start from a neighbour in the table
   above rather than from zero — the ranges are narrow and the fight is sensitive to them.
7. Add the asset to `BoxerSpawnPoints._fighterProfiles` on the `SpawnPoints` object in
   `SampleScene`.

**Two rules that are easy to trip over:**

- **Assigning any `_fighterProfiles` replaces the difficulty-tier path outright.** The training
  scenes deliberately assign none, which is what keeps a run learning against the unmodified
  policy and keeps checkpoints comparable across the curriculum. Do not add a card to a
  training scene.
- **The ring always seats ten and the card is usually shorter**, so contestants are dealt round
  the ring cyclically. With all eight selected, the first two fight twice. Changing the card
  never destroys or respawns an agent — the ten boxers that already exist are reconfigured in
  place.

### Roster colour convention — verified

The project rule is that a hand-coded bot is **red**, the reference RL bot is **green and
untextured**, and only custom variants are free to look like anything. Those colours live in
two places and both have to agree. As of 2026-09-12 they do:

| | Fight card (`FighterProfile._tint`) | Role colours (`BoxerView` on the prefab) |
|---|---|---|
| Scripted | `#D91F1F` (HEURISTIC) | `_scriptedColor` `#D91F1F` |
| Reference RL | `#1FBF40` (STANDARD RL) | `_rlColor` `#1FBF40` |

The two paths are used by different scenes: `SampleScene` has `_useRoleColors` **off** because
the card supplies a tint per contestant, and `Training1v1` has it **on**. Change one and the
bot goes red in only half the project.

### Before training, check these

| Check | Why |
|---|---|
| `git lfs pull` has been run | Art *and* fonts are LFS. Unfetched, sprites import as nothing and every text element floods the console with a font-initialisation failure. `ls -la Assets/Art/Fonts` — a real face is ~100 KB against a 131-byte pointer |
| TensorBoard is up and the port answers | Project rule. `Start-Process` returns success even when the process dies a second later, so check the port, do not assume |
| Stale runs pruned from `results/` | Keep anything under `results/_preserved/` |
| Unity Editor closed for runs over 30 minutes | Give the run the machine |
| No `.cs` file is about to be edited | Editing any script under `Assets/` triggers a domain reload, which exits Play mode and kills the run |

### Known gaps, flagged 2026-09-12

| Finding | Why it matters |
|---|---|
| **No `results/` directory exists in this working tree.** No TensorBoard history, no checkpoint archive, no `_preserved/` | The shipped brain cannot be re-derived, compared against its own past, or rolled back. Every success figure in this document comes from the written record in `DOCS/ML_AGENTS.md`, not from a log anyone can re-open |
| **Only one model, so "comparison" is thin.** There is no second checkpoint in testing | The benchmark dashboard compares roster *entries* and scripted tiers, not rival brains — because there are no rival brains to compare |
| **The brain was trained in a 40×40 ring and now fights in a 17×17 one** (`Training10Way` was never rescaled with the game ring) | Distances it learned do not mean the same thing in the shipped game. This is the single most likely source of odd behaviour on screen |

---

## TLDR

One 475 KB brain drives nine of ten boxers; styles and attributes, not extra networks, make
them differ. One shared rig, 7 bodies, 6 joints. Red means scripted.
