# Architecture

How PoRumble is layered, how data flows through it, and the things about that structure which are easy to get wrong. Referenced from CLAUDE.md.

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
`CompositeDisposable` are **hand-rolled in Models**, which is what lets Models depend on
nothing at all — exactly what the architecture rules ask for. R3 was installed for a while
and never used; it and the rest of the NuGet layer have been removed.

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
                                         ├─► MainMenuView    (the Title phase, and only it)
                                         ├─► RingAtmosphereView (the knockout blackout)
                                         ├─► CrowdAmbienceView  (the room goes quiet at Title)
                                         └─► CombatFeedbackView (bell, countdown beeps)

PunchThrown / PunchLanded / PunchBlocked / PunchEvaded / HaymakerThrown
        ├─► BoxerAgentView      (reward shaping — as before)
        ├─► CombatFeedbackView  (hitstop, impulse shake, particles, audio)
        ├─► PlayerStatusHudView (damage vignette, counter flash)
        ├─► FightStatsSystem    (the telemetry board's tallies)
        ├─► CrowdAmbienceView   (reaction swells, and the bed's level)
        └─► DirectorSystem      (cuts the camera on a knockout or a landed haymaker)
```

On top of that sits the broadcast layer, which is derived state and nothing else — delete all
of it and the ring behaves identically, which is what makes it safe to add to a project whose
shipped policy is calibrated against that ring.

```
FightStatsSystem ─► FightStatsModel ──┬─► FightStatsHudView (the telemetry board)
                                      └─► DirectorSystem    (recent damage → tension)

DirectorSystem ─► DirectorModel ──┬─► SpectatorCameraView (which pair, how tight)
                                  └─► CameraDirectorView  (the hard cut to ImpactCam)
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
- **`MatchPhase` and `MatchFlowPhase` are separate machines, and only one is driven by input.**
  The first says whether the fight is decided, the second what the player is looking at. Nothing
  in normal play can end a match before the bell — combat only ticks while `IsFightLive` — but
  `TryStartFight` checks `MatchPhase` anyway, because the failure is silent and total: the player
  sits through the intro and a three-second countdown, the bell rings, and `TickFighting` sees an
  already-`Ended` match and cuts straight to the knockout hold for a fight that never happened.
  `MatchFlowTests.ADecidedMatchCannotBeIntroduced` pins it.
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
- **Spawn separation must stay inside `RayLength` (14).** Fighters that start further apart
  than their own sensors reach open every episode blind, wandering until something enters
  range. Ten-boxer rings are fine at `_spawnRadius: 15`; the 1v1 ring is not, which is why it
  spawns at 4.5.
- **The guard pose is folded; the punch pose is not negotiable.** At rest the elbows are
  flexed to ~125 degrees and carried outward, so the gloves sit in front of the face (rear
  glove 0.17 from the head centre, lead glove 0.46) - that is the blocking stance. A punch
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
| `Assets/Scenes/Training10Way.unity` | Curriculum stage 3: the ten-boxer free-for-all, auto-restart. Pairs with `porumble_10way_ffa.yaml` |

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

