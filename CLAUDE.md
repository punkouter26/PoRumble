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
| Controls | **WASD** move + aim · **J** left punch · **K** right punch · **Space** hold to charge a haymaker · **L** slip · **Tab** or the **MENU** button the fight card (between matches) · **R** restart at the results screen · **F3** or the **STATS** button the diagnostics overlay |
| Train | Activate `.venv`, run `mlagents-learn Assets/Config/porumble_ppo.yaml --run-id=pr_1v1`, then open `Training1v1.unity` and press Play |
| Watch training | `tensorboard --logdir results` |
| Tests | `unity command run_tests --mode EditMode` — 163 EditMode tests |
| Evaluate | **PoRumble ▸ Evaluate Checkpoints…** plays each `.onnx` in a folder through the ten-way and writes `results/evaluation/*.json`. Select on `knockoutRate`, never on reward |
| Parallel training | **PoRumble ▸ Build 8-Arena Training Scene** generates `Training10x8.unity` — eight rings, eight times the experience per step |
| Android APK | **PoRumble ▸ Build Android APK** switches target, applies signing and writes `Build/Android/PoRumble-<version>-<code>.apk` |

**Art is in Git LFS.** A fresh clone that has not run `git lfs pull` leaves every `.png` as a
129-byte pointer file, and Unity imports those as nothing at all: the sprites silently resolve
to null, the fighters render as invisible transforms and the prefab looks broken rather than
unfetched. `ls -la Assets/Art/Sprites` tells you immediately - a real sprite is kilobytes.

**Two configs on purpose**, though they now carry the same numbers. `BoxerConfig.asset` is
the game; `BoxerConfig_Training.asset` is what the training scenes load, so the curriculum
can diverge from the shipped tuning without touching the game.

It held 6 HP for a long time, because at 30 HP every episode timed out with no terminal
signal to learn from. That was a symptom of the blind ray sensor, not of the health value:
the agents could not see an opponent, so they never landed anything. With perception fixed,
a 30 HP knockout resolves in roughly 280 steps against a 1500-step cap, so the training
config now matches the game exactly and there is no sim-to-real gap left to cross. If
episode length ever pins at the cap again, that is the number to look at first — but check
what the rays are actually returning before blaming it.

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
- **The back of the head is the weak spot, and the face arc decides what the *guard* covers -
  not what may be hit.** A punch that reaches the head lands from any angle;
  `CombatMath.IsInFaceArc` then decides whether the hands were between it and the face, so a
  punch from behind is unblockable. That is the reason to keep an opponent in front of you.
  **This was the cause of the zero-knockout result**, and it is measured rather than
  reasoned. The arc used to reject anything outside the forward 120 degrees *before* damage,
  block or evade were considered, so turning your back was a perfect defence - and it defended
  twice over, because `HeadOffset` puts the head along the facing, so turning away also moved
  the head further from the attacker. The two reachable bands did not overlap: frontal
  separation 1.69-3.29 against rear 0-1.51, with fighters sitting at ~1.95. Measured in a live
  1v1: **1988 steps, both fighters on 30/30, zero damage**, the scripted brain aimed dead on
  (`dot 1.00`) and throwing into a back whose head sat 1.23 away against a 0.80 requirement.
  After the change the same scene knocks a fighter down inside ~1200 steps.
- **`HeadOffset` is 0.36 because that is where the head is drawn.** It was 0.89, while the
  drawn head and its `HeadCollider` both sit at 0.36 - so the thing you aimed at was never the
  thing you could hit. An offset that large also made reach depend almost entirely on the
  defender's facing; at 0.36 the frontal and rear bands are 1.16-2.76 and 0.44-2.04, which
  overlap. Changing it moves every punch's range, so re-measure before believing any tuning
  that predates it.
- **The arc is still judged from the attacker's position**, not the glove's offset from the
  head. A glove landing dead-centre gives a zero-length vector carrying no direction, and
  judging from it would read a punch thrown from directly behind as a frontal one. That used
  to wrongly *score* a hit; it would now wrongly let hands nowhere near the punch block it.
- **Hits are buffered for a whole tick** and the match resolved once at end of tick, so
  simultaneous knockouts both count.
- **A boxer throws one punch at a time.** `BoxerSystem.ThrowPunch` refuses while either arm
  is `Extending` or `Retracting`, so a second fist can never be out alongside the first.
  Cooling down deliberately does *not* count - the fist is already back at the guard by then,
  which is what keeps held input alternating left and right instead of stalling on one arm.
  The rule sits in the system rather than in a controller, so it applies identically to the
  keyboard, the scripted brains and the trained policy.
  This halved punch throughput to ~2.2/sec, which quietly killed stamina: at the old
  `PunchStaminaCost` of 0.035 the drain was 0.077/s against 0.09/s recovery, so spamming
  punches *gained* breath. The cost was doubled to 0.07 to restore the previous pressure -
  measured equilibrium under constant punching is now 0.20, against roughly 0.24 before.
- **Movement is anisotropic to the facing.** `BoxerSystem.ScaleByStance` caps sidesteps and
  retreats against the forward shuffle, and turning drops to `CommittedTurnScale` while a
  punch is on its way out. Feed `MoveInput` straight through and a boxer sprints backwards as
  fast as it advances while pivoting mid-swing to track a target that already stepped off.
- **Boxers are clamped to the ring in `BoxerSystem`.** Positions are model-driven, so the
  wall colliders alone contain nobody.
- **Overlap separation transfers what the ropes refuse.** `ResolveOverlaps` splits the push
  between two bodies, and clamping each half independently *loses* the half that lands in a
  wall - so a pair with one man on the ropes stayed overlapped for ever, the correction
  reapplied and half-discarded on every tick, until the two settled where neither moved.
  Measured in the 1v1 as byte-identical positions across 1400+ steps at separation 1.92
  against a required 1.96, both sitting exactly on the clamp boundary
  (`ArenaHalfExtent 7 - BodyRadius 0.98 = 6.02` against an observed `y = 6.0`). Each body's
  denied movement is now handed to the other, which resolves the overlap fully whenever
  *either* has room; separation measures exactly 1.96 afterwards.
  `ArenaContainmentTests.TwoBoxersCrushedIntoTheRopesStillSeparate` pins it.
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
- **A training match must be able to end on the clock.** `MatchDirector` resolves an
  unfinished match through `MatchSystem.EndByTimeout` a few steps before the agents' own
  `MaxStep` cuts their trajectories. Without that the ten-way never ends at all: ML-Agents
  closes every trajectory at the cap, but `MatchPhase` never reaches `Ended`, so the arena is
  never re-racked, no winner is declared, and the win reward lands in the *next* episode.
  Rewards then oscillate around zero and value loss falls to nothing. `EndByTimeout` sat
  written but uncalled for a long time; 1v1 hid it, because those matches resolve well inside
  the cap.
- **A ten-way does not finish inside `MaxStep`, and that is expected.** At 30 HP, 270 health
  has to come off ten fighters spread across a 40x40 ring, and episodes run to the 2500-step
  cap. The timeout resolution is the normal path there, not the exception - which is why it
  has to award a real winner rather than lapse.
- **Training bypasses the presentation loop entirely.** `MatchDirector` branches on
  `BoxerSpawnPoints.AutoRestart`: a training scene jumps straight to `Fighting` and keeps the
  old per-episode reset. A countdown would burn episode steps on animation.
- **A boxer must never perceive itself.** A 2D cast cannot skip the collider that fired it.
  Two things keep the ray sensor honest, and both are load-bearing:
  `Physics2D.queriesStartInColliders` is **off** (the body collider the sensor sits inside),
  and `BoxerSpawnPoints.IsolatePerception` moves each fighter's colliders onto its own
  `BoxerBody<id>` layer and subtracts that layer from that fighter's `RayLayerMask` (the face
  probe 0.9 units ahead and the gloves beyond it). Turn either off and the forward rays —
  the ones pointing where the boxer is about to punch — report the boxer's own `BoxerFace`
  at half a metre, permanently. `PerceptionSettingsTests` pins the first half.
- **Spawn separation must stay inside `RayLength` (24).** Fighters that start further apart
  than their own sensors reach open every episode blind, wandering until something enters
  range. Ten-boxer rings are fine at `_spawnRadius: 15`; the 1v1 ring is not, which is why it
  spawns at 4.5.
- **The guard pose is folded; the punch pose is not negotiable.** At rest the hands are
  carried at the ears: level with the head (`_guardHandForward` 0.38 against a drawn head at
  0.36) and just outside it (`_guardHandLateral` 0.50 against a head radius of 0.30). They used
  to sit at forward 0.62, well in front of the face, which read as holding the arms out rather
  than guarding. The two-link solve folds the elbow to about 150 degrees to reach it, which is
  what a real tight guard does. A punch
  extends the elbow to 8 degrees and drives the fist out to `ArmReach`. Only the guard half
  is free to restyle: hits resolve at full extension, so the punch angles have to keep
  putting the drawn glove at 1.6 forward.
  Two things fell out of folding the guard that far. The arm now slews 117 degrees instead
  of 37 in the same `ArmExtendDuration`, so `_servoGain` had to rise from 25 to 60 or the
  fist visibly fell short (measured 1.53 against a 1.6 reach). And a glove tucked to the
  face sits inside the torso's own collider, so `BoxerSpawnPoints.DisableSelfCollision`
  turns off collisions among each boxer's own parts - a HingeJoint2D only excludes the pair
  it directly connects, and without this the servo fights a contact it can never win.
- **Sprite world sizes are load-bearing.** The boxer's parts are sized so the drawn glove sits
  where `CombatMath` expects it. Sprites are authored at a pixels-per-unit equal to their pixel
  width, so one sprite covers one world unit at scale 1 and the transforms carry over from the
  quads they replaced. Changing a sprite's PPU silently moves the fists away from the hitboxes.

---

## Scenes

| Scene | Purpose |
|---|---|
| `Assets/Scenes/SampleScene.unity` | The game: 40×40 ring, 10 boxers, HUD, feedback rig, spectator camera |
| `Assets/Scenes/Training1v1.unity` | Curriculum stage 1: 20×14 ring, one learner against the scripted sparring partner, auto-restart. Spawn radius 4.5 — see the sensor-reach note below |
| `Assets/Scenes/Training10.unity` | Curriculum stage 3: the 40×40 ten-way, auto-restart, no HUD or feedback rig |
| `Assets/Scenes/Training10x{N}.unity` | **Generated, not hand-built.** N copies of the ten-way, 80 units apart, one `ArenaLifetimeScope` each. Build with **PoRumble ▸ Build 8-Arena Training Scene**; see *Parallel arenas* |

Boxer prefab (`Assets/Prefabs/Boxer.prefab`) is an anatomical chain:

```
Boxer (container, never moved)
├── Torso        kinematic Rigidbody2D — head, neck, shoulders, colliders, agent, ray sensor
├── UpperArmL/R  HingeJoint2D → Torso   (shoulder, −45…80°; mirrored −80…45)
├── ForearmL/R   HingeJoint2D → UpperArm (elbow, 0…145°, cannot hyperextend)
├── GloveL/R     HingeJoint2D → Forearm  (wrist, ±25°, radial/ulnar deviation)
└── ArmL/R       ArmView, servos the three hinges from the model's extension
```

Tags `Boxer`, `BoxerFace`, `Wall` matter: ray sensors detect them, and the separate
`BoxerFace` collider is how an agent can tell it is looking at an attackable face rather
than someone's back.

---

## Parallel arenas

`Training10x{N}.unity` is **generated**, never hand-edited: **PoRumble ▸ Build 8-Arena
Training Scene** rebuilds it from `Training10.unity`. Throughput in ML-Agents is bounded by
experiences per physics step, and both training scenes held exactly one ring.

- **The offset lives in `MatchModel.ArenaCenter`, not in the transform hierarchy.** Forced,
  not chosen: the torso is moved with `Rigidbody2D.MovePosition`, which is world-space and
  ignores its parents, so offsetting an arena by re-parenting would move the drawn ring and
  leave every fighter simulating on top of the arena next door. `ClampToArena`, `SpawnSystem`
  and the agent's ring-position observation all read it; it is zero in both shipped scenes, so
  nothing about them changes.
- **Every arena gets its own message brokers.** Boxer ids restart at zero in each ring, so one
  shared `PunchLandedMessage` stream would pay arena three's fighter 0 for damage arena one's
  fighter 0 dealt - silently, and in a way that looks exactly like a policy learning something.
  `ArenaInstaller.InstallMessaging` is called at the root *and* in each arena scope, where it
  shadows the parent's.
- **Spacing is 80 units and that is a minimum, not a preference.** The ring is 40 across and
  `RayLength` is 24, so a fighter on the east rope can see 24 units past it. Under 64 it
  perceives the fight next door - no crash, no warning, just a policy learning about opponents
  it can never reach.
- **`GameLifetimeScope` skips the per-ring half when arena children exist**, and only forces
  `RatingSystem` in the single-ring case. `RatingSystem` takes a `MatchModel`, and in a
  multi-arena scene there is no such thing at the root - there are eight. Training rates
  nothing anyway, because `RosterModel.SeatOf` returns null without a fight card.
- **VContainer binds a child scope by the serialized `parentReference` type, not by the
  transform hierarchy**, and it *throws* if that parent has no container yet - it only
  auto-builds a parent that is the VContainerSettings root. `GameLifetimeScope` is therefore
  `[DefaultExecutionOrder(-5100)]` against `ArenaLifetimeScope`'s -5050, both earlier than
  `LifetimeScope`'s own -5000.

## Checkpoint evaluation

**PoRumble ▸ Evaluate Checkpoints…** plays every imported `.onnx` in a folder through the
ten-way and writes `results/evaluation/<name>.json`. Select on `knockoutRate`.

- **It exists because reward cannot choose between checkpoints here**, and until it was built
  nothing in the project recorded the number that can: `MatchEndedMessage.EndedOnTimeout` is
  new. Finishing a match early truncates the episode and caps the damage reward still to be
  accumulated, so reward mildly punishes winning quickly.
- **The harness is gated on a request file** (`Temp/porumble_eval_request.json`), not on a
  scene flag. The thing that starts an evaluation is an Editor script on the far side of a
  domain reload and a play-mode transition, and a scene edited to carry a harness would go on
  carrying it into every training run afterwards.
- **ML-Agents writes checkpoints outside `Assets/`,** so Unity has not imported them and they
  cannot be assigned to `BehaviorParameters`. Copy the ones to compare into
  `Assets/ML-Agents/Models/` first; the picker says so when it finds none.
- The evaluator **leaves the last checkpoint measured on the Boxer prefab.** Reassign the
  shipping model before playing the game scene.

## The 9-hour free-for-all run (pr_ffa_9h_01)

40M steps in 8h37m on `Training10x8` (8 arenas, 80 agents, ~1,300 steps/sec), fresh rather
than `--initialize-from`. `results/` is gitignored, so the numbers that matter are recorded
here rather than left in a directory git never sees.

**Measured over 25 matches each in `Training10`, via PoRumble ▸ Evaluate Checkpoints:**

| model | knockoutRate | meanSurvivors | meanWinnerHealth | draws |
|---|---|---|---|---|
| the four scripted `BrainProfile` tiers, ten seats | 0.0% | **10.00** | 0.55 | 11/25 |
| `PoRumbleBoxer.onnx` (the model that shipped before) | 0.0% | **10.00** | 0.00 | **25/25** |
| checkpoint 27.5M | 0.0% | 9.08 | 0.58 | 10/25 |
| **checkpoint 32.5M** | 0.0% | **8.12** | **0.70** | **7/25** |
| checkpoint 40.0M (the trainer's final export) | 0.0% | 9.08 | 0.31 | 17/25 |

- **The model that was shipping does nothing at all.** Ten survivors, no winner, twenty-five
  draws out of twenty-five. It was compiled against the old 15-wide vector and two ray stacks,
  so this is the observation-space mismatch the note above predicts, measured rather than
  inferred. It sets the floor at zero, which means "training improved things" is true but is a
  much weaker claim than the numbers first suggest.
- **The trainer's final export is the worst of the three checkpoints.** 17 draws against 7 at
  32.5M, and winners finishing on 31% health against 70%. Shipping `PoRumbleBoxer.onnx` as
  ML-Agents writes it - the obvious default - takes the weakest policy of the run. This is what
  `keep_checkpoints` and the evaluator are for, and the reward curve gave no hint: summaries at
  38-40M read +0.090, -0.000, +0.060, entirely healthy.
- **`knockoutRate` could not discriminate.** It is 0.0% for every model measured, so selection
  fell to `meanSurvivors`, exactly as that field's own docstring anticipates. Across 100
  evaluated matches and 40M training steps, **not one match has ever ended by knockout** and
  episode length never left the 498-decision cap. Whether that is the policy or the combat
  model was settled by running the scripted brains through the same harness: **they eliminate
  nobody either.** Ten survivors out of ten, no knockouts, across four difficulty tiers of
  purpose-built fighting logic. The ceiling is therefore the combat model and not the policy -
  270 health spread over ten fighters in a 40x40 ring cannot be removed inside 2500 steps by
  anything, trained or hand-written - and a further training run is wasted effort. The levers
  are `MaxHealth`, punch damage, the stun thresholds, or the step cap.
  **The trained policy is meanwhile the best fighter measured**: 8.12 survivors against the
  scripted brains' 10.00. The reinforcement learning genuinely beats the hand-tuned AI; it is
  just competing inside a model where nobody can win.
  This also puts a question to the existing note that a ten-way "does not finish inside
  MaxStep, and that is expected". If the bell really is the intended ending, then zero
  knockouts is not a fault - but then `knockoutRate` is the wrong selection criterion and
  `meanSurvivors` should replace it in both the docstring and the harness.
- **Reward is only comparable inside a curriculum lesson.** `shaping_scale` steps at progress
  0.15 and 0.30, which with `max_steps` 40M lands at 6M and 12M - and the reward function
  changes there. Two apparent regressions in this run were lesson boundaries, not policy
  collapse. Compare 12M+ against 12M+ and nothing else.

## ML-Agents

- Behaviour name is **`PoRumbleBoxer`** and must match `BehaviorParameters` exactly.
- Actions: 4 continuous (`moveX`, `moveY`, `aimX`, `aimY`) + 2 discrete branches (punch L/R).
  **The action vector is frozen.** Any compiled policy is built against exactly this shape;
  growing it stops the model loading. The haymaker was
  therefore built as a side channel (`BoxerSystem.SetCharge`) rather than a third branch, and
  the counter window needs no action at all. Retrain before changing either.
- Observations: `RayPerceptionSensorComponent2D` (17 rays, reach 24, **one stack**) plus 20
  self scalars — health, facing, move input, both arms' extension and readiness, survivors,
  stamina, ring position, velocity, **stun, bearing to the nearest opponent as cos/sin,
  range to it, and whether a punch is in flight toward this boxer**. Rays are used because
  the opponent count shrinks during a match and a fixed vector cannot encode a
  variable-length list.
- **The last four scalars exist because the ray fan cannot express them.** `m_MaxRayDegrees`
  is 180, so a boxer is blind behind it — deliberately, since a boxer is — but the nearest
  opponent's *signed* bearing is exact where seventeen samples are coarse, and it survives
  that opponent walking round the back. The incoming-punch scalar is what makes the slip
  usable at all: `DodgeDuration` 0.3 against a 0.22 flight only leaves a window for something
  that can see an arm start to travel, and until this went in nothing in the vector said an
  arm was travelling. Only `ArmPhase.Extending` counts — a retracting arm is a punch that has
  already resolved, and rewarding a slip against one teaches slipping after the damage.
- **Ray observation stacks went 2 → 1, and batched raycasts on.** Two stacks of a
  seventeen-ray fan is 170 floats of a 200-float vector, to say where things were five ticks
  ago — weak temporal information next to the velocity and arm-phase scalars that are in
  there explicitly. `m_UseBatchedRaycasts` was off, which left ten fighters' casts running
  single-threaded.
- Rewards: damage dealt/taken, elimination, win, an existential penalty (the ring does not
  shrink, so idling must cost), plus dense shaping for aiming at and holding range on the
  nearest opponent.
- **Damage is denominated per knockout, not per point.** `_knockoutDamageReward` (0.6) is what
  taking a *whole health bar* off somebody pays, divided by `MaxHealth` at the point of use.
  It was 0.2 *per point of damage*, which at 30 HP made one knockout worth 6.0 against a win
  bonus of 2 — the objective outscored three to one by its own scaffolding. That is the
  mechanism behind the "reward mildly punishes winning quickly" note below: finishing early
  truncates the episode and caps the damage still to be farmed. A nine-kill sweep is now
  ~19 from eliminations, 6 from the win and ~5 from damage, so the outcome is most of the
  return. A per-point figure also silently rescaled the entire reward function whenever
  `MaxHealth` moved; a per-knockout one does not.
- **The dense shaping fades out on a curriculum.** `shaping_scale` is an environment parameter
  read once per episode in `OnEpisodeBegin`, stepping 1.0 → 0.5 → 0.0 over the first 30% of a
  run. The three shaping terms were written for a policy whose ray sensor reported nothing but
  its own torso; once a fighter can see, paying per step for *standing* at punching range
  rewards hovering there without throwing. Measured on `progress`, not `reward` — a reward
  threshold would have to be guessed against a reward function these lessons rescale, and
  guessing low pins the run in lesson one for ever. Defaults to 1 with no trainer attached,
  so the game is unaffected.
- **`gamma` is 0.997, and the episode is 500 decisions — not 300.** `MaxStep` on the prefab
  is **2500** at `DecisionPeriod` 5. All three configs asserted 1500 for the whole curriculum,
  which made the real discount horizon 2.7× shorter than the tuning claimed: at 0.995 the win
  bonus was worth `0.995^500 = 0.08` at the opening bell, not the 0.22 the comment promised.
  0.997 restores it. **Read `MaxStep` off the prefab, never off a comment.**
- **`time_horizon` is 256, not 128.** A horizon far below the discount horizon bootstraps the
  value estimate before the terminal win reward can propagate into it, which is most of why
  the win signal never showed up in the returns. Costs buffer memory, not compute.
- **`keep_checkpoints` must cover the whole run, and did not.** `porumble_ffa.yaml` said it
  "keeps the whole run, not a trailing window" while `max_steps / checkpoint_interval` was 50
  against a `keep_checkpoints` of 15 — the last 1.5M of a 5M run. Given the note below that
  the peak can appear at 1M and be gone by 4M, this was silently discarding the thing it was
  written to preserve. It is now 80 (ffa, 8M @ 100k), 40 (ppo, 8M @ 200k) and 50 (spar). Any
  change to `max_steps` or `checkpoint_interval` has to be checked against it.
- **`VectorObservationSize` on the prefab must equal what `CollectObservations` writes.** It is
  **20** (it was 15). ML-Agents does not fail loudly on a mismatch in every path, and a
  compiled policy simply refuses to load. Change one and you must change the other, and
  retrain. Note the prefab also stacks the vector twice, so the network sees 40 of these.
- **`PoRumbleBoxer.onnx` no longer matches this observation space and must be retrained.** The
  vector went 15 -> 20 and the ray sensor from two stacks to one, so the compiled policy is
  reading a layout it was never trained on. Measured in the ten-way after the change, mean
  `dot(facing, toNearestOpponent)` sits at 0.26-0.39 against the 0.7+ a policy that is really
  aiming produces - it fights, but poorly. The reward rebalance, the shaping curriculum and the
  clash mechanic all invalidate it independently. Retrain the whole curriculum before judging
  any of this on how the game plays.
- **`PoRumbleBoxer.onnx` is the `ffa_v5` model** (~21M cumulative steps), trained 1v1 against the scripted
  partner to saturation and then transferred into the ten-way free-for-all. The policy it
  replaced (`PoRumbleBoxer_obs11_legacy.onnx`) was compiled against the old 11-wide vector
  *and* trained while the ray sensor reported nothing but the boxer's own torso; it is kept
  only so the two can be compared, and it will not load against the current vector.
  Earlier checkpoints from the chain are kept in `results/_preserved/`.
- **Select a model on how often matches finish, not on reward.** Reward and the objective
  pull apart here: finishing a match early truncates the episode, which caps how much
  damage-dealt reward can accumulate, so the reward function mildly punishes winning
  quickly. Picking on reward would have shipped a policy that finishes 21% of matches over
  one that finishes 76%.
- **Nothing shorter than about 2M steps is a trend here.** The rate at which matches finish
  before the bell oscillates on roughly that period - 50% at 1M, down to 34% by 3M, back to
  50% by 4M - while reward sits flat at ~6.05 throughout and hides all of it. A four-window
  slide looks exactly like a regression and is not one. Preserve checkpoints across the whole
  run and pick at the end; `keep_checkpoints` is set high for precisely this reason.
- **Judge a training run on windowed averages, not the last summary.** Per-summary reward
  swings about +/-0.4 here, so any single line is noise. `ffa_v3` sat in a trough around
  1-1.6M steps that looked exactly like convergence, then climbed out and gained another
  eight percent over the next 3M. The clearest signal is not reward at all but how often a
  match finishes before the bell: that went 0/80 summaries at 1.6M to 28/80 at 4M while
  reward moved only 5.68 to 6.12.
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
- **The fighters are flat silhouettes, after Activision's Boxing (Atari 2600, 1980).** The five
  boxer parts are a single unshaded fill with a hard edge - pure white in the PNG, so
  `BoxerView`'s `_BaseColor` tint lands exactly and the atlas cannot bleed a dark halo. Measured
  off the original: **glove diameter ≈ head diameter** and **arm thickness ≈ 0.2 × glove**, where
  this project had 0.70 and 0.59. The shaded, normal-mapped version that preceded it was
  illegible at the ten-way's zoom - head and torso merged into one dark mass and the arms
  vanished - which is the practical reason for the change rather than the nostalgic one.
  Three constraints held the resizing:
  `GloveL/R` carry the `CircleCollider2D` **on the same GameObject as the SpriteRenderer**, so
  their transform scale is untouchable - a bigger drawn glove would be a bigger *perceived* and
  *hit* glove, and the trained policy would be reading a ring it was never trained on. The glove
  therefore grew inside its own 128px canvas (87.5% → 95%) and stopped there. The head is
  decoupled - `HeadCollider` is a separate object at radius 0.3 - so it could shrink to close the
  ratio. And every PPU is unchanged, so the drawn fists still sit where `CombatMath` expects.
- **A landed punch smashes the head, which is the original's own hit feedback.** Activision's
  Boxing answers a hit by deforming the struck boxer's head - the "smashed nose" - and PoRumble
  had only a white flash, which says *that* something happened and not *what*. `BoxerView`
  squashes the head along local Y and bulges it along local X, scaled by damage, decaying as
  `t²` so it is deepest on the frame of impact. Three things make it safe and exact:
  **local +Y is forward** on this prefab (the gloves sit at y 1.6), and `CombatMath` only lets a
  punch land inside the face arc, so a landed punch came from roughly in front and needs no
  rotation - which also matters because rotating would spin the contestant's photograph and read
  as the head turning rather than being hit. **`Head` carries a SpriteRenderer and nothing else**;
  the hit radius is on `HeadCollider`, a sibling, so this changes what is drawn and not what can
  be hit or what another fighter's ray sensor sees. That is the whole reason the deform lives on
  the head: on a glove it could not, because `GloveL/R` carry their `CircleCollider2D` on the
  *same* GameObject as the renderer, so a glove cannot be scaled or nudged for effect without
  changing the ring the trained policy perceives. The blocking hand therefore gets a flash rather
  than a recoil, picked as whichever glove is nearer the impact, since `PunchBlockedMessage` says
  a block happened but not which arm made it.
- **A blocked punch recoils, and it recoils rather than stopping short for a reason.**
  `CombatMath` resolves a punch at the *peak* of its extension, so by the time
  `PunchBlockedMessage` is published the fist is already out at full reach and the message's
  `Position` is where it already is - clamping to that would draw nothing at all. `ArmView`
  therefore knocks the drawn fist `_blockRecoil` (0.35) back off its own reach and holds it
  there until the model's retraction catches up, which is what a punch running into a forearm
  looks like. Measured: collider unmoved, drawn fist 0.32 away from it.
  **`GloveL/R` had to be restructured for this.** The `SpriteRenderer` and `TrailRenderer` now
  live on a `Vis` child while the `Rigidbody2D`, `CircleCollider2D` and `HingeJoint2D` stay on
  the joint - the same split the arm segments always had, and the reason they could be
  foreshortened safely. The joint is driven to the model's fist and the child to the drawn one,
  so a block is purely visual: **nothing another fighter's ray sensor returns changes**, and the
  trained policy still sees the ring it was trained on. Anything that puts a renderer back on
  the glove GameObject silently re-couples the two.
  The message carries `AttackerArm` because nothing else identifies which fist was stopped - an
  attacker may have both travelling, and `ArmBlocks` reports only that a block happened. It has
  no default on purpose; a wrong guess stops the wrong hand.
- **A fighter's colour is chosen by value, and may never be green.** Ten silhouettes are told
  apart by hue but *read* by value, so `BoxerView.BoxerPalette` and every `FighterProfile._tint`
  are pushed to a hard light or a hard dark, alternating along the array so neighbouring seats
  contrast. Green is barred outright: the canvas is a mid yellow-green, and a dark green fighter
  was measured against it and lost - the silhouette edge becomes the only separation and the eye
  reads it as a shadow on the floor. All four `STANDARD RL` seats shipped at one mid green,
  which is how four of ten fighters were effectively invisible.
- **Every sprite carries a normal map, except the five boxer parts, and a Light2D has to be told
  to read it.** The boxers' `_NormalMap` bindings are deliberately cleared - a normal map is
  exactly what turns a flat fill back into a lit dome, so it undoes the silhouette. Clear it
  through `TextureImporter.secondarySpriteTextures`, never by editing the `.meta`. The maps are
  generated by `Temp/evals`-adjacent tooling into `Assets/Art/Sprites/Normals/` and bound through
  each sprite's **secondary texture** slot named `_NormalMap` — not through the material, which
  is why `BoxerSpriteFX` can leave `_NormalMap` empty and still light correctly, and why one
  shared material serves every fighter without breaking the sprite batch. The height field is a
  blurred distance transform of the alpha (the dome that gives a glove volume) plus a faint
  high-pass of the sprite's own luminance. Blurring the distance field is not optional: a
  chamfer transform quantises direction to eight neighbours and differentiating it unblurred
  lays radial spokes across every rounded surface.
  **The load-bearing half is on the lights.** `Light2D.normalMapQuality` defaults to `Disabled`,
  and with it disabled every one of the above is inert — the maps import, the atlas packs them,
  the SpriteRenderer binds them, the shader's `NormalsRendering` pass runs, and nothing whatever
  changes on screen. The five point lights are set to `Fast` with `normalMapDistance` 1.6; the
  global light is deliberately left disabled because it is ambient fill and normal response
  there flattens the contrast the key light exists to create. Note that
  `NormalMapQuality` is declared `Disabled = 2, Fast = 0, Accurate = 1`, so a
  SerializedProperty's `enumValueIndex` is **not** the enum's value — set `intValue`.
- **Never point a particle material at a sprite atlas page.** `ImpactParticleMat._BaseMap` held a
  direct reference to a page of `BoxerAtlas`. A `ParticleSystemRenderer` maps UV 0..1 across the
  whole bound texture and knows nothing about sprite rects, so every spark was drawing a shrunken
  copy of the entire atlas — survivable only because the sparks are tiny and it read as a warm
  blob. Adding normal maps gave the atlas a *second page*, which shifted the sub-asset file IDs,
  and the stored reference silently resolved to the `_NormalMap` page: every particle became an
  opaque black bar. Particle materials reference `Assets/Art/Sprites/impact_spark.png` directly.
  `GloveTrailMat` always did; that was the tell.
- **Particle materials are transparent, and were not.** All of them shipped as `_Surface` 0 with
  `SrcBlend` One / `DstBlend` Zero, ZWrite on, queue 2000 — opaque quads with their alpha
  ignored. Sparks, embers, the shockwave and the haymaker streaks are additive
  (`SrcAlpha`/`One`); `DustParticleMat` is alpha-blended, because dust occludes rather than
  glows and additive dust brightens the floor it is being kicked off. When changing this, the
  blend factors, the `_SURFACE_TYPE_TRANSPARENT` keyword and the render queue must all be set
  together — URP branches on the keyword, samples with the factors and sorts by the queue, and
  setting one leaves a material whose keywords describe something other than what is bound.
- **The post-processing stack is smaller than it looks, and that is fine.** The profile carries
  19 components but only Bloom, FilmGrain, ChromaticAberration, Tonemapping and the grading
  actually execute. `active: 1` in the YAML is the *component enabled* flag, not whether the
  effect runs: DepthOfField is mode `Off`, and MotionBlur, LensDistortion, PaniniProjection,
  ScreenSpaceLensFlare and ColorLookup are all at zero intensity, so `IsActive()` is false and
  URP skips them. Check `IsActive()`, never the serialized flag, before concluding an effect
  costs anything.
- **The faces are circular-cropped at import, not masked at runtime.** The six source
  photographs at the project root are centre-cropped square, resized to 256 and given a radial
  alpha with a soft edge, then written to `Assets/Art/Sprites/Faces/` at PPU 256 so one sprite
  is one world unit like every other part. A SpriteMask or a stencil shader would have cost a
  draw call per head for a result that never changes. They are packed into `BoxerAtlas`, so a
  head still batches with the body it sits on.
- **A face is tinted white while its owner is standing.** `BoxerView` takes the head colour
  separately from the body: a photograph carries its own colour and multiplying it by the
  fighter's trunk colour only makes it muddy. It still darkens on elimination, which reads
  correctly.
- **Sprite pixels-per-unit equals the sprite's pixel width,** so one sprite is one world unit
  at scale 1. The hit maths is tuned against those dimensions; changing a PPU silently moves
  the drawn fists away from the hitboxes.
- **`Assets/Art/Atlases/BoxerAtlas.spriteatlasv2`** packs the fighters, the impact spark and
  the ring dressing. The tiling `ring_canvas` and `ring_rope` are deliberately outside it:
  they are sampled by a material with Repeat wrapping, which atlasing breaks.

### Custom shader

`PoRumble/SpriteLitFX` is a variant of URP's Sprite-Lit-Default adding four effects the stock
one cannot express: `_FlashAmount` (white hit flash), `_DissolveAmount` (knockout burn-away,
procedural noise, no extra texture), `_RimAmount` (rim light read from the sprite's normal map)
and `_OutlineAmount` (an inner outline). Built as a variant rather than from scratch so the
fighters keep responding to 2D lights and keep writing normals.

**Rim and outline say different things on purpose.** The rim is *shape* — it traces the volume
the normal map describes and is set once on the material (0.38). The outline is *state*, driven
per renderer from a MaterialPropertyBlock by `BoxerView`: gold and pulsing while a counter
window is open, a faint blue standing mark on the seat the human is driving. Conflating them
would mean a fighter could not be highlighted without also changing how round it looks.

The outline is drawn **inward** from the silhouette, and that is forced rather than chosen: a
sprite's quad is tight to its own bounds and the atlas packs neighbours right against the
padding, so an outline drawn outward would either clip at the quad edge or sample whatever
sprite was packed beside it. Its width comes from `fwidth(uv)` rather than `_MainTex_TexelSize`,
so it stays a constant number of screen pixels as the spectator camera pulls out over a ten-way
— and so the shader does not need a texel-size property in `UnityPerMaterial` in all three
passes.

**The player marker is the one effect with no end condition.** It holds the property block open
on that boxer's nine renderers for the whole match — nine draw calls that will not batch. That
is affordable for exactly one fighter and is why it is a per-seat flag rather than something
every boxer could switch on. The shipped Android build sets `_humanBoxerId` to -1 and marks
nobody.

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
| `AppChrome` | `AppChromeView` | Title, frame rate, MENU, STATS and the version — the five corner elements |
| `DiagnosticsHud` | `DiagnosticsHudView` | **F3** or the STATS button; telemetry including the bound policy |
| `MatchInput` | `MatchInputView` | The restart key |
| `MatchHud` | `MatchHudView` | Survivors, per-fighter health, countdown, result banner |
| `RosterCard` | `RosterSelectionView` | The fight card — pick who is in the ring (**Tab** or MENU) |
| `KnockoutMood` | `KnockoutMoodView` | Blends a desaturated, vignetted grade **and** the mixer's `Knockout` snapshot for the knockout hold |
| `Standings` | `StandingsHudView` | Top three of the Elo table |

## Application Chrome

Five elements are pinned to fixed positions and belong to the app rather than to the fight:
the **title** top-left, the **frame rate** top-centre, **MENU** top-right, the **STATS**
telemetry toggle bottom-left and the **version** bottom-right. All five live in
`Assets/UI/Layouts/AppChrome.uxml` and are driven by `AppChromeView`.

- **One document owns the whole contract, and that is the point.** Every one of those five
  positions was already occupied - `.match-hud` held top-left, `.diag` top-right, `.player-hud`
  bottom-left and `.standings` bottom-right - so the alternative was scattering the five across
  three UXML files and three views, where nothing could check that the corners stayed where they
  were promised. The panels now reflow around the chrome through `--chrome-top` and
  `--chrome-bottom` in `porumble.uss`, so moving the chrome moves the HUD with it rather than
  requiring four rules to be edited in step.
- **The chrome sorts at 10, above every other HUD document** (the highest was 5). Forced rather
  than chosen: `.roster` is a full-screen scrim at 88% opacity, so a MENU button sorted beneath
  it would be both invisible and unclickable exactly when it is needed to close the card. Sorting
  above is what lets one button open *and* close it, which is why `#open-card` could be deleted.
- **A chrome button has to claim its own tap, or it fires twice.** `MatchInputView` deliberately
  does not hit-test - a tap anywhere is a confirmation, which is what lets the results screen work
  with no button on it - so a press on MENU at the results screen would open the card *and* start
  the next match on the same frame. `HudPointerModel` is how the chrome says the frame is spoken
  for. It stores a frame number rather than a flag, so the claim expires by itself and a consumer
  that never runs cannot leave input dead. The pre-existing `#open-card` had this bug.
- **The frame-rate label is stretched across the screen and centre-aligned, not translated.** Its
  string changes width whenever the number does, and a translated element would visibly shift
  left and right as it counted.
- **The counter only writes when the integer changes.** `Label.text` builds a string, and this is
  a HUD element that exists partly to report allocation rate.
- **The telemetry overlay reports the bound policy now**, which nothing in a build ever did:
  behaviour name, the `.onnx` actually loaded, the inference device, the observation and action
  shapes and the decision period, plus how the ring splits between the policy and the scripted
  brains. The action vector being frozen is the constraint that most often bites here, and a
  model whose observation size disagrees with `CollectObservations` does not fail loudly - it
  simply refuses to load. `ModelAsset.name` allocates on every read, so the reference is compared
  and the name rebuilt only when the asset changes. The split is recounted every refresh because
  re-dealing the card swaps controllers on agents that already exist.
- **`--text-xs` and `--text-sm` went 24/28 to 28/32.** At the 1080-wide portrait reference a USS
  pixel is a device pixel, so 24px was about 2.2% of the screen width - the health-row names and
  the whole diagnostics readout were at that size.

## Audio

`Assets/Audio/PoRumbleMixer.mixer` routes **Master → SFX / UI / Ambience**, and now actually
processes rather than merely routing. SFX carries a pre-fader compressor (punches are short,
loud and constantly overlapping, and without it a flurry just clips against itself) and a
post-fader `SFX Reverb` tuned as a hall rather than a cathedral. Master carries a glue
compressor and a `Lowpass` that sits wide open — it exists only so the `Knockout` snapshot has
something to close, and that muffled drop is the clearest audio cue that a match just ended.

**Effect parameters live in snapshots, not on the effect.** Each is keyed by a GUID the effect
allocates, and a snapshot stores an *absolute* value per parameter — it inherits nothing from
another snapshot, so every value has to be written into `Knockout` as well as `Default`. This
is why the chain was built through Unity's internal `AudioMixerController` API rather than by
hand-editing the `.mixer` YAML: hand-writing that GUID mapping is how a mixer comes back with
every value silently reset.

 Punches play
through a pool of positioned 3D voices (`SpatialVoicePool`) so a hit across the ring is
quieter and off to one side; the bell and countdown are non-positional and go to UI. DSP
buffer is 512 rather than the default 1024, because ~23ms of latency is audible on a punch.

**Audio is synthesised at runtime** in `ProceduralSfx` — the project has no audio assets, and
a boxing game where landing, blocking and whiffing all sound identical loses most of what
tells the player what happened. Swap in recorded one-shots whenever they exist; nothing but
that one class has to change.

**Every impact sound is a bank of four variants, not one clip.** A match is the same five
sounds fired hundreds of times, and the ear locks onto an identical waveform far faster than
the eye locks onto a repeated sprite. Pitch-shifting one clip at playback does not fix it —
the noise transient shifts with the body and it still reads as the same sample — so a variant
reseeds the noise *and* moves the tonal body, giving a difference in timbre. Variant 0 is
always the originally tuned clip; the rest are deviations from it. Draws are uniform rather
than from a shuffle bag: a shuffle guarantees no immediate repeat but also guarantees every
variant is heard before any repeats, which over a long exchange is its own audible pattern.

**Distance is carried by a filter as well as by volume.** Each pooled voice has an
`AudioLowPassFilter` with a `customCutoffCurve`, which Unity evaluates against that source's own
distance to the listener — so nothing needs a per-frame update and the pool never has to know
where the camera is. Volume rolloff alone reads as someone turning a knob down; losing the
crack of the transient is what actually reads as distance. Pitch and level are jittered a few
percent per playback *on top of* whatever the caller asked for, never instead of it — a counter
is deliberately pitched up and that has to survive.

**The camera writes its own transform.** `SpectatorCameraView` computes the centre and extent
from the models and drives a bare `CinemachineCamera` directly, so there is no position-control
component to configure. Cinemachine is there for the brain blend and for impulse shake.
`LensSettings.Orthographic` is read-only — projection comes from the brain's source camera.

**The USS type ramp and spacing scale are sized for the 1080x1920 portrait reference,** which is
what the panel actually uses with match-width scaling - so a USS pixel here is a device pixel on
a 1080-wide phone. They were first authored against a landscape canvas and topped out at 34px for
everything but the result banner, roughly 3% of a phone's screen width: legible on a monitor and
not on the thing this ships to. The whole match panel measured 222x447 of a 1080x1920 screen
before the rescale and 496x596 after.

**`SafeAreaView` insets every panel, and nothing did before.** The survivor count sat 20px from
the top of a 1920-tall screen, underneath the status bar on any phone that has one. It writes
padding on each document's *root* rather than margins on the panels: the HUD anchors its panels
absolutely, an absolutely positioned child resolves against its parent's padding box, so one
write moves every corner-anchored panel at once. Two things it has to get right.
`Screen.safeArea` can be larger than `Screen.width/height` - in the Editor it reports the whole
display while `Screen` reports the Game view - so the fractions are clamped to [0, 0.5] or the
insets come out negative and silently lose the base inset too. And it must keep retrying until
every panel has resolved a layout: an early pass that skipped them all while caching the current
screen size never runs again, which is exactly how it first shipped applying nothing at all.

**The result banner lives in the centre stage, not in the match panel.** It sat in the panel
alongside the ten health rows and drew straight across them - a winner's name is the largest text
the game ever shows and that panel is the densest thing on screen. The centre stage exists
precisely so text that changes length every second cannot disturb the panel's layout.

**The HUD is structured in UXML and styled from `Assets/UI/Styles/porumble.uss`,** not built in
C#. Layouts live in `Assets/UI/Layouts/`, with repeated rows cloned from templates under
`Layouts/Templates/`; views look elements up by name and write only genuinely dynamic values (a
bar's width percentage, a label's text, a state class). Tokens for colour, spacing, type,
family and motion live in `:root`. Both HUDs previously carried their own copy of the palette
as `static readonly Color` fields, free to drift apart, and built every element imperatively.

- **`CloneTree(target)` adds the template's own children straight into the target**, with no
  `TemplateContainer` in between. That matters for the repeated rows: a wrapper element would
  sit in the middle of the column's flex layout and give every row a second box to inherit
  sizing from. The freshly cloned row is `parent[parent.childCount - 1]`.
- **A UXML comment may not contain `--`,** which rules out writing BEM class names
  (`bar__fill--hurt`) inside one. The importer reports it as a bare XML parse error naming a
  line and column, with no indication that it came from a layout file.
- **Three font families, and the split is functional.** `Anton` is the fight-poster display
  face and is unreadable below ~30px, so it is bound only to `text--xl` and `text--display`.
  `Barlow Condensed` carries everything else and is condensed because the build ships portrait,
  where a normal-width face overruns the health rows and roster tiles. `Space Mono` exists only
  for the F3 overlay, whose figures have to be tabular or the columns visibly crawl on every
  refresh. All are SIL OFL and vendored with their licences in `Assets/Art/Fonts/`.
  UI Toolkit renders through a TextCore **FontAsset**, not the `.ttf`: the `.asset` files beside
  each font are SDF atlases pre-baked over printable ASCII, so the 90px result banner does not
  pay for a rasterisation on the frame a match ends. `-unity-font-definition: var(--font-body)`
  resolves through USS custom properties; check `AssetDatabase.GetDependencies` on the
  stylesheet to confirm the fonts actually bound.
- **Weight is a different file, never a synthesised smear.** `text--bold` swaps the font asset
  rather than setting `-unity-font-style: bold`, because faking bold from the medium weight
  thickens stems unevenly and blurs the SDF edge — exactly where a condensed face falls apart.
- **Only health bars transition, and that distinction is not cosmetic.** Health moves in
  discrete jumps when a punch lands, so easing the width turns a snap into a readable drain.
  Stamina and the haymaker charge are recomputed every tick, and a transition on a value that
  already changes each frame just renders it permanently behind the model it reports. The
  damage vignette carries no transition for the same reason: the view already eases its alpha.

**The diagnostics overlay's counter names were read back from
`ProfilerRecorderHandle.GetAvailable`, not assumed.** This Unity version publishes no plain
`Draw Calls Count` or `Batches Count`; a recorder asking for one reports zero forever rather
than erroring. `Shadow Casters Count` counts only 3D casters, and `Video Memory Bytes` is the
adapter total — both were tried and dropped as confidently-wrong numbers. The **Audio** category
turns out to publish timing markers only and no counter for playing voices at all, so the voice
line counts `isPlaying` over an `AudioSource` array cached once at `Start`.

The overlay reports **p95 frame time alongside the mean and the peak**, because the three answer
different questions: a single 90ms frame in a 120-frame window moves a 16ms average by under a
millisecond, the peak catches that frame but cannot tell a one-off domain reload from a stutter
happening several times a second, and p95 is the one that says how bad it *regularly* gets. The
percentile sorts into a pre-allocated scratch array — an overlay that reports allocation rate
must not allocate to do it. It also reports texture memory and count (the number that moves when
art changes, and this project just took on a normal map per sprite and an SDF atlas per font
weight) and counts `Light2D` and `ShadowCaster2D` directly at `Start`, since neither set changes
during a session and Unity's own shadow counter reads zero for 2D casters.

## Combat Depth

- **Haymaker.** Hold charge to wind up; release to throw. Costs mobility while held, locks out
  the ordinary jab, and the swing itself is slower — that telegraph is the counterplay. A
  release below `MinChargeToRelease` throws an ordinary punch, so tapping is never a wasted
  input.
- **A guard is the whole arm, not the fist.** `BoxerSystem.ResolvePunch` blocks on the distance
  from the incoming glove to the *segment* shoulder-to-glove (`CombatMath.ArmBlocks`), not to
  the defender's glove alone. Before this a punch passed straight through a forearm held across
  the body and landed clean on the face behind it: the arms had no presence in the maths, and
  none in the physics either, since only the gloves and the torso carried colliders.
  The arm is a straight line in the model even though it is drawn with a bent elbow -
  `GetGlovePosition` places the glove along `facing` at a lateral offset and the elbow lives
  only in `ArmView`'s servo - so blocking is judged against the line the model believes in.
  At extension 0 the segment collapses to a point at the shoulder, which is correct: a tucked
  arm must not guard the whole reach it would have had if it were thrown.
  **This changed what stops a punch, so the shipped policy is now slightly mis-calibrated** -
  it throws punches that used to land and now get arm-blocked. Accepted as retraining debt;
  the action vector is untouched, so `PoRumbleBoxer.onnx` still loads and still plays.
- **`ArmView.ServoTo` must use `Mathf.DeltaAngle` and clamp the result.** `HingeJoint2D.jointAngle`
  is cumulative and does not wrap at 180 - it keeps counting. A raw `target - jointAngle` is
  therefore only correct while the joint has stayed inside one revolution, and the moment it
  has not, the error is enormous, the servo asks for a proportionally larger speed and drives
  the arm *further* out. Measured mid-fight with joints wound to -6543 degrees asking for
  300,000 deg/s, gloves nine units from their own torso and never recovering. `_maxMotorSpeed`
  (1800) is the backstop. This was latent for a long time and only became reachable when the
  segment masses were cut; it would have been reachable eventually anyway.
- **Segment masses are anatomical fractions of the torso, and the torque follows them.** Upper
  arm 0.03, forearm 0.018, glove 0.012 against a torso of 1 - roughly a human's 2.7%, 1.6% and
  0.6% of body mass. They were 0.16 / 0.09 / 0.07, which made the glove about ten times too
  heavy relative to the body and the whole limb effectively rigid, so the "a wrist gives on
  impact" intent in `ArmView` never actually happened. `_maxMotorTorque` dropped 1100 to 260
  with them, since required torque scales with the inertia being moved.
- **The four arm segments carry `CapsuleCollider2D` on a shared `BoxerArm` layer (18).** Not
  the gloves - those had colliders long before the arms did, and other fighters' sensors have
  always seen them. The arm layer is subtracted from *every* ray sensor's mask, not just its
  owner's: an untagged collider still occludes a ray, and a raised guard sits directly in
  front of the forward rays, so arms visible to perception would blind the fighter holding
  them up. `BoxerSpawnPoints.IsolatePerception` skips colliders already on that layer rather
  than moving them onto the per-boxer one.
- **Self-collision is off except between the two arms.** `DisableSelfCollision` still ignores
  every pair of a fighter's own colliders - a folded guard puts the gloves inside the torso and
  the servo would spend every frame pushing against a contact it cannot win - and
  `RestoreCrossArmCollision` then puts back exactly one set: left arm against right. That pair
  is the mechanic, not a bug to suppress.
- **`_mirror` is for joint angles only, never for a world offset.** `ArmView` uses it twice and
  the two conventions are opposite: mirroring a *hinge angle* correctly inverts the sign, while
  the model puts the **Left** side at *positive* lateral - `GetShoulderPosition` and
  `GetGlovePosition` both read `arm.Side` and both give Left the plus. `PoseArmFromModel`
  borrowed the joint-angle sign for the guard hand, which put every hand on the far side of the
  body from its own shoulder: measured, the left arm ran from a shoulder at lateral +0.53 to a
  glove at **-0.50** with its elbow at **(fwd -0.31, lat -0.13)**, behind the torso and sitting
  among the *other* arm's joints. Both arms crossed in an X. The guard hand now takes its side
  from `_model.Side`, so the two can no longer disagree.
  **The bug hid a second one**, which is why it lasted: the wrong sign put the hand 1.10 from
  the shoulder, close to the arm's natural 1.51, so the two-link solve was well-conditioned and
  the shape looked plausible. Correct geometry asks a 1.51 arm to fold into 0.38.
- **The drawn arm foreshortens as the guard folds.** A boxer at guard has the elbow pointing at
  the floor, so from directly overhead the arm genuinely *is* shorter - most of it points at the
  lens. Two fixed-length segments cannot say that: solved honestly, the elbow had nowhere to go
  but straight out sideways, flaring to lateral **1.24**, 0.71 past the shoulder. `ArmView`
  shrinks both links toward the shoulder-to-hand span to hold the elbow at a constant
  `_guardElbowFlare` (0.35 of the span), which brings the elbow back to **0.65**, and the factor
  rises to 1 as the punch straightens - measured 0.44 at extension 0.42, 0.78 at 0.71, 1.0 by
  about 0.85, so the arm is at full length well before the hit resolves.
  Two things make this safe to do, and both must stay true. **The glove is placed at `target`
  outright**, not at the end of the links, so the drawn fist stays where `CombatMath` resolves
  the hit however short the arm is drawn. And **the segments' `CapsuleCollider2D` sit on the
  joint GameObjects while the sprites sit on their `Vis` children**, so only the child is scaled
  - no collider moves, and nothing any ray sensor can see changes. A glove could never be
  treated this way: `GloveL/R` carry their collider on the renderer's own GameObject.
- **Every scene poses the arms without the solver, the game included.**
  `BoxerSpawnPoints._kinematicArms` is a plain serialized flag and it is **true in
  `SampleScene`**, whose `AutoRestart` is false - so the "gated on AutoRestart" this note used
  to claim is wrong, and the consequence is not small: the servo path in `ArmView.FixedUpdate`
  runs nowhere. **Every joint angle, joint limit, segment mass and motor torque on the prefab
  is inert.** The drawn arm is `PoseArmFromModel` and nothing else, so the guard pose is
  `_guardHandForward` / `_guardHandLateral` and the punch is the model's extension through
  `ShapeStrike`. Measure the gloves in play before believing any of those fields did anything;
  a whole pass of "fixing" the elbow limit, the masses and the shoulder range changed the
  picture not at all. `SetKinematicDrive` stops the six bodies and six joints per fighter - sixty of each in a ten-way, solved fifty times a second to draw a limb that
  decides nothing - and drive the glove transform straight to the position `CombatMath` already
  believes it occupies. Perception is then not merely close to the game's but identical to it,
  so the speed costs no sim-to-real gap. It does mean the *physical* arm collision only exists
  in the game scene; the model's clash rule is what governs training, and the clash rule is
  what the policy learns from in either case.
- **The arms carry no colliders, and that was tried the other way.** Blocking is decided in
  `CombatMath.ArmBlocks`, so the guard needs no physical presence to work. Giving the four arm
  segments `CapsuleCollider2D` to make limbs physically stop each other looked like the
  matching half of the change and had to be reverted: the segments are dynamic bodies driven by
  `HingeJoint2D` motors, so a solid collider makes the drawn pose a product of contact forces
  rather than of the model's extension. Every cross-boxer touch pushed a limb while the servo
  drove back against it, and the arms visibly oscillated - the same "jointed limbs drift"
  failure the sibling-arm layout exists to avoid, reached from the other direction. If it is
  ever attempted again the colliders must also stay off the perception layers, since an
  untagged collider still *occludes* a ray and a fighter's own guard would blind it.
- **You cannot throw both fists at once, and nothing forbids it.** The gloves travel from
  the chin (`GuardLateralOffset` 0.18) out to the centreline (`ArmLateralOffset` reduced by
  `PunchConvergence` 0.85), which is how a straight punch is actually thrown - the hands cross
  the middle. Two arms in flight therefore share one corridor, so `BoxerSystem.ArmsClash`
  reports a clash whenever the *other* arm is `Extending` or `Retracting` at the moment this
  one peaks: the punch is lost, both arms are `Stagger()`ed into their recovery, and
  `PunchClashedMessage` pays the thrower `_clashPenalty`. `ThrowPunch` no longer refuses the
  second fist. That rule used to be the mechanic; the anatomy is the mechanic now, so a policy
  *learns* to punch one at a time instead of being told.
  Three things fall out of this and all three are load-bearing:
  - **The clash is judged on the other arm's phase, not on how close the gloves are.**
    Measuring separation at the peak let two arms cycle in antiphase for ever, passing through
    each other on the way past - which is precisely the thing that is supposed to be
    impossible. Cooling down does not count, so a properly sequenced one-two is still
    throwable.
  - **Controllers must not make the mistake by accident.** `BoxerAgentView` withholds the
    punch request from the keyboard, the touch stick *and* the scripted brains while either
    fist is travelling. A held button is not a policy making a choice: without this it
    reissues the request the instant the first arm comes home and the fighter knocks its own
    fists together for the whole round. The discipline belongs in the input mapping; the
    policy's two discrete branches stay deliberately unguarded, because being able to make
    the mistake is what makes it learnable.
  - **A converging punch lands from further out.** The glove now arrives 0.45 nearer the
    opponent's spine, so the range at which one can reach a head went from 3.09 to 3.28. Twice
    the arm reach is now *inside* that; `CounterWindowTests.GuardRange` had to move out to
    find a separation where gloves meet but heads do not.
  Guard geometry moved with it: the resting glove used to sit level with its own shoulder,
  0.53 out to the side, which made the guard a point far off the centreline - so once punches
  converged, almost nothing could be blocked. Shoulder-to-glove is now a diagonal across the
  chest, which is both what a guard looks like and what it covers.
- **Stun.** Landed damage banks trauma (`StunPerDamage`), shed at `StunDecayPerSecond`. Above
  `StunThreshold` the fighter is wobbled: `StunnedMobilityScale` off its feet and its hands,
  `StunnedTurnScale` off its turn. The turn is cut harder than the walk on purpose - losing
  the ability to keep the guard pointed at the attacker is what opens the face arc, and it is
  what makes pressing an advantage the right play instead of resetting to range. A knockout in
  a real fight is trauma arriving faster than it can be shed, not a health bar reaching zero,
  and without this a boxer on 1 HP fought exactly as well as one on 30. It also gives the
  ten-way a way to actually finish, which is the metric checkpoints are selected on.
  Stun is cleared by `ResetTo` and by `Eliminate`, or it poisons the next episode.
- **A punch steps in.** `BoxerSystem.StepIntoPunch` adds `PunchLungeSpeed` along the facing
  when a punch starts. Force comes from the legs and hips driving the shoulder through; an arm
  extending off a static torso is the weakest way a human can throw one and it reads as a
  reach. Added to velocity, not position, so the ring clamp, the overlap resolution and the
  knockback all still apply.
- **Counter window.** Blocking a punch opens `CounterWindowDuration` seconds during which your
  next landed punch takes `CounterDamageBonus`. Consumed by the punch that uses it, so one
  block buys exactly one counter. This applies to every fighter, the trained policy included —
  it needs no new action.
- **Slip.** A short burst sideways during which the face cannot be hit at all, bought with
  stamina and a cooldown. Rides `BoxerSystem.Dodge`, a side channel like `SetCharge` and for
  the same reason. Cannot be started out of a punch already thrown and cannot be punched out
  of, so it is a real trade rather than a free option.
  **`DodgeDuration` must stay above `ArmExtendDuration`.** A fighter cannot slip earlier than
  the moment it sees an arm start to travel, so a window shorter than the punch's flight time
  closes before the punch arrives and the mechanic does nothing whatsoever. It was 0.2 against
  a 0.22 flight and every reactive slip was hit; `DodgeTests.TheWindowOutlastsAPunchInFlight`
  pins it now.
  A slipped punch falls through the ordinary miss path, so it reports as an **evade** and pays
  the evader the evade reward it has always paid — the trained policy needed no retraining to
  benefit from being slipped past.

## The Fight Card

Eight selectable contestants live in `Assets/Config/Fighters/` as `FighterProfile` assets:
`HEURISTIC` (the scripted sparring brain), `STANDARD RL` (`PoRumbleBoxer.onnx` driven straight
through) and six named fighters wearing the photographs in `Assets/Art/Sprites/Faces/`.
**Tab** between matches opens the card; clicking a tile adds or drops that fighter.

- **The ring always seats ten and the card is usually shorter, so entrants are dealt round the
  corners cyclically.** With all eight selected the first two fight twice. Changing the card
  therefore never destroys or respawns an agent — `BoxerSpawnPoints.SeatRoster` reconfigures
  the ten boxers that already exist, swapping face, colour, controller, style and attributes.
  A variable ring size would mean rebuilding `MatchModel`'s roster, every agent's ML-Agents
  lifecycle and the HUD's health bars; the cyclic deal buys the same freedom for none of that.
- **Assigning any `_fighterProfiles` replaces the `_rosterTiers` path outright.** The training
  scenes deliberately assign none, which is what keeps a run learning against the unmodified
  policy and the checkpoints comparable across the curriculum.
- **Six fighters, one network.** `PoRumbleBoxer.onnx` is a single set of weights, so left alone
  ten policy boxers fight identically. `StyleModulator` bends the actions the shared network
  produced on the way to the boxer — forward pressure, circling, a gate on punch volume,
  opportunist extra punches — and reaches the two mechanics that were never ML actions
  (`SetCharge`, `Dodge`). Training six separate policies is the honest answer and an enormous
  one; growing the action vector so a style could be an *input* stops the compiled model
  loading at all.
- **The aim is never bent.** Pointing at an opponent is the one thing the network is genuinely
  good at, and rotating its output produces a worse fighter rather than a different one.
  Everything a style changes is a decision *about* an aim the policy already found.
- **The modulator re-rolls only on decision steps.** `OnActionReceived` fires every physics
  tick — the `DecisionRequester` repeats the last decision in between — so rolling there would
  run every probability in a `FighterStyle` five times per decision and make each one mean five
  times what it says.
- **`FighterAttributes` are what make the difference measurable**, not just behavioural: power,
  chin, speed and stamina recovery. Power and chin are folded in at `BoxerSystem.ResolvePunch`
  rather than when the health comes off, so the number in `PunchLandedMessage` is the number
  actually taken — the reward shaping reads that message, and a policy paid for damage it did
  not do would learn the wrong lesson.
- **A seat switching from scripted to policy has to get its policy back.** `BoxerAgentView`
  captures the prefab's authored `BehaviorType` on first use, because forcing `HeuristicOnly`
  for a scripted contestant is otherwise a one-way door and the chair stands there doing
  nothing for the rest of the session.

## Elo

`RatingSystem` rates the *contestants*, not the boxer slots, and carries the table between
sessions through `FileRatingStore` (`porumble_ratings.json` under `persistentDataPath`).

- **A free-for-all is scored as every pairwise result its finishing order implies, divided by
  the opponent count.** Without that division a fighter in a ten-way would swing nine times as
  far as one in a 1v1 and the table would describe how crowded the ring was rather than who is
  any good. `RatingSystemTests.RingSizeDoesNotChangeHowFarAWinnerMoves` pins it.
- **The finishing order is survivors by health, then the fallen in reverse elimination order.**
  A ten-way normally resolves on the bell with several still standing, so ordering survivors on
  health is what stops a timeout rating everyone who lasted as equal.
- **Same-contestant pairs are skipped.** The cyclic deal seats a fighter twice; beating yourself
  proves nothing. Both chairs' results still accumulate onto the one record, so a
  double-seated fighter's `Matches` legitimately counts two.
- **`RatingSystem` is resolved eagerly in `GameLifetimeScope`**, for the same reason
  `CombatSystem` and `MatchSystem` are: it only subscribes to messages, so nothing injects it
  and VContainer would never construct it — every match would resolve with the standings
  silently untouched.
- **A scene with no card rates nothing.** `RosterModel.SeatOf(0)` returning null is the test,
  which is what stops a training run writing a league table nobody asked for.

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

## Android

Ships as a portrait phone build. `SampleScene` is the only scene in the build list; the two
training arenas are editor-side tools.

| Setting | Value | Why |
|---|---|---|
| Backend | IL2CPP, ARM64 only | Not a preference. Modern devices are arm64-v8a and Mono cannot target 64-bit ARM at all |
| Orientation | Portrait, autorotate off | |
| Graphics | Vulkan, then GLES3 | The 2D renderer does a lot of render-texture work for the lights; GLES3 is the slower path |
| Package | `com.punkouter.porumble` | |
| Min SDK | 26 | |
| Target SDK | 35 | Pinned. It was 0 — "highest installed" — so two machines built different APKs from the same commit |
| Frame rate | 60, set in code | Android caps a player at **30** unless `Application.targetFrameRate` says otherwise, and nothing set it. See `PlatformBootstrap` |
| Signing | `Build/keystore/porumble-dev.keystore` | A dev key, gitignored with the APKs. Swap for a real one before any store build |
| Panel reference | 1080x1920, match width | The HUD was authored against a landscape canvas; in portrait the reference has to be portrait too |

`adb` ships with the Editor rather than on PATH, under
`Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/`. So does `keytool`, under
`AndroidPlayer/OpenJDK/bin/` — which is what generated the dev keystore, since there is no
system JDK here.

**Never edit `ProjectSettings.asset` on disk while the Editor is open.** It holds those values
in memory and re-serialises the file over any external change, with no warning and no conflict.
A version bump written straight to the YAML was silently reverted and the build came out
labelled with the old version - which is precisely the failure the version label exists to
catch. Set them through `PlayerSettings` (the API), as `AndroidBuilder` and the `set_version`
eval do. The settings that *did* survive earlier in this project survived by luck of timing,
not because the file is a supported way in.

**The signing passwords cannot live in `ProjectSettings.asset`.** Unity keeps them in
per-machine EditorPrefs, so a build made without setting them writes an unsigned APK and only
says so at the very end. `AndroidBuilder` sets them on every run for that reason; the
keystore path and alias *are* serialized, and they are the half that is safe to commit.

### Things that are easy to get wrong

- **The camera watches one exchange, not the whole field.** Fitting the bounding box of every
  living fighter works only for the last two: with ten boxers scattered over a 40x40 ring the
  box *is* the ring, so the camera sat at its widest for most of a match and the fighters were
  a few pixels tall. `SpectatorCameraView` now picks a focus - the human if there is one, else
  the living boxer on the least health, since that is where the next elimination is coming from
  - and frames that fighter, their nearest opponent, and anyone inside `_focusRadius`. Typical
  orthographic size went from ~24 to ~9.
  Two details make it usable rather than nauseating. The focus is **sticky**: health changes
  several times a second across ten fighters, so re-picking the lowest every frame swings the
  camera across the ring on almost every landed punch - it is only given up when the current
  focus dies or a rival is `_focusSwitchMargin` HP worse off. And the nearest opponent is kept
  in frame regardless of the radius, because a focus fighter alone in shot is not a fight.
- **Position is clamped to the ropes, not to `_outsideRingMargin`.** That margin exists so the
  corner posts and stools stay visible when the camera is pulled out far enough to show the
  whole ring. Letting the *position* clamp use it too meant that at a focused zoom the same
  four units became a quarter of the screen of empty backdrop. When the view is wider than the
  ring, `ClampToRing` centres on that axis and the dressing is visible anyway.
- **The camera framing rule is orientation-dependent, and has to be.** The ring is square and
  no screen is. Landscape crops to fill: the camera pulls out only until the view is as wide
  as the ring, so the fighters stay large and the camera pans over the ring's height. Portrait
  letterboxes instead - cropping a 0.56 aspect to fill shows barely half the ring's width, so
  most of a ten-way brawl would be off-screen while the HUD still claimed ten were alive.
  `SpectatorCameraView.ClampToRing` then keeps the view inside the ropes on whichever axis the
  ring is larger, and centres it on the axis where it is not.
- **The camera's minimum zoom is orientation-dependent too, for the same reason as the maximum.**
  Orthographic size is half-*height*, so one minimum means two very different framings: at 9 a
  16:9 screen shows 32 world units across and a 9:16 phone shows 10. The ring is 40 across, so
  the landscape number was doing its job while the same number in portrait produced a tall slot
  with a duel in the middle and most of the frame empty above and below it.
  `_portraitMinOrthographicSize` is 6, because on a phone the binding dimension is width.
- **`_maxOrthographicSize` is deliberately larger than any landscape screen needs (45).** The
  ring-fit rule is what actually binds; the field only matters as a backstop on very tall
  displays. Set it back down to ~21 and portrait can no longer pull out far enough to fit the
  ring.
- **The build ships as an all-AI exhibition.** `BoxerSpawnPoints._humanBoxerId` is -1, so
  every boxer is driven by a brain profile or the trained policy and no human UI is built at
  all: `PlayerStatusHudView` and `TouchControlsView` both check for a human boxer and construct
  nothing without one. Match-level input still works - a tap anywhere restarts at the results
  screen, a three-finger tap toggles the diagnostics overlay.
- **Touch controls exist in code but are not in the scene.** `TouchControlsView` renders a
  floating stick plus punch and haymaker buttons, and writes `TouchInputModel`, which
  `BoxerAgentView.Heuristic` reads in the same place it reads the keyboard - so a phone and a
  desk drive the boxer down one identical path. To switch a human back on: set
  `_humanBoxerId` to 0 and add a `TouchControls` GameObject with a `UIDocument`
  (HudPanelSettings, sorting order 5) and a `TouchControlsView` pointed at `porumble.uss`.
  The stick feeds move and aim together; there is no second stick, and a boxer that walks one
  way while facing another cannot land anything through the face arc anyway.
- **`MatchHudView` picks its prompts from the devices present,** not from a platform define, so
  the editor still reads "PRESS" while a phone reads "TAP" - and a desktop that happens to have
  a touchscreen is not told to tap when it has a keyboard sitting right there.
- **The fight card needs a button, because `Tab` does not exist on a phone.** For a long time
  `RosterToggleRequested` read the Tab key and nothing else, which meant the entire
  contestant-selection screen could not be opened in the shipping build - and could not have been
  closed if it had been. That button is now `#menu` in the application chrome rather than the
  bottom-centre `#open-card` it used to be; the chrome sorts above the card's full-screen scrim,
  so the same control both opens and closes it. A two-finger tap is the shortcut for anyone who
  finds it. Both are gated between matches: re-seating the roster mid-fight would swap
  contestants into chairs that are currently mid-punch.
- **Two startup log lines are expected and harmless.** `ClassNotFoundException:
  AssetPackManager` is Unity looking for Play Asset Delivery, which a sideloaded APK does not
  use. A burst of `NullReferenceException` in `TensorProxy.Finalize` fires once as the first
  GC collects the inference tensors allocated while loading `PoRumbleBoxer.onnx`; it does not
  recur, and inference works.

## Coding Rules

`.claude/rules/` is authoritative. The ones that bite most often:

- **Never `?.` on a UnityEngine.Object** — it bypasses the destroyed-object check. Use `== null`.
- **`[FormerlySerializedAs]` on every serialized-field rename**, or Inspector data is lost.
- **No legacy Input** — blocked by hooks.
- **No coroutines** — UniTask instead.
- **`private` by default**; no speculative public API.
- **Never start a training run without TensorBoard.** `.claude/rules/training.md` carries the
  full rule; the short version is that the console prints a mean reward only every
  `summary_freq` steps, far too coarse to catch reward hacking or a collapsed entropy. Start
  it first, and check the port is listening rather than assuming the process survived.
- **Sprite atlases are mandatory for 2D.** `Assets/Art/Atlases/BoxerAtlas.spriteatlasv2`
  packs the boxer parts and the impact spark. The tiling `ring_canvas` and `ring_rope` are
  deliberately *outside* it: they are sampled by a material with Repeat wrapping, which
  atlasing breaks. Sprite Atlas V2 is the project's packer mode.
