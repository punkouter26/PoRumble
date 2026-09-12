# Combat Depth, the Fight Card, Elo and Difficulty Tiers

The mechanics layered on top of the base punch exchange, the contestant roster, the rating table and the scripted brain profiles.

## Scale, and the Ring

The project's scale is fixed by the fighters, not by the ring. The drawn boxer is **1.40 world
units across the shoulders**; a real boxer is about **0.50 m** across the shoulders, so

```
1 world unit = 0.357 m
```

Everything else follows from that, and should be checked against it before it is changed:

| Thing | World units | Real |
|---|---|---|
| Shoulder width (drawn torso) | 1.40 | 0.50 m |
| Body separation radius (`_bodyRadius`) | 0.98 | 0.35 m |
| Reach, centre to glove tip | 1.80 | 0.64 m |
| Ring inside the ropes (`_arenaHalfExtent` 8.5) | 17.0 | 6.07 m — **19.9 ft** |

A standard professional ring is 20 ft inside the ropes, which is where the 8.5 half extent
comes from. It was **20** for a long time, making the ring 40 units — 14.3 m, or **47 feet**
across. That is not a boxing ring, it is most of a tennis court, and it is the reason ten
fighters read as specks who spend the first half of every match walking toward each other.

Rescaling the ring means moving the drawn ring **and** `BoxerSpawnPoints._arenaHalfExtent`
together. The walls have colliders but do not contain anyone: positions are model-driven and
`BoxerSystem.ClampToArena` is what actually holds the fighters in, so a drawn ring that
disagrees with the half extent produces fighters who stop short of the ropes or walk through
them, with nothing logged either way.

## Body Parts and Collision

What actually carries a shape, and why the obvious additions are not there:

| Part | Body | Shape |
|---|---|---|
| Torso | Kinematic | Circle r 0.64 |
| Glove L/R | Dynamic | Circle r 0.125 |
| FaceProbe | Kinematic | Circle r 0.80 — **trigger** |
| Head | — | none |
| Upper arm L/R | Dynamic | **none** |
| Forearm L/R | Dynamic | **none** |

The arms are a real hinge chain — shoulder, elbow, wrist, each a `HingeJoint2D` over a
Rigidbody2D — and the upper arms and forearms have **bodies but no colliders**, so they sweep
through opponents. That looks like an oversight and is not one.

**Do not give the arm segments colliders.** It was tried, and the failure is spectacular rather
than subtle: the limbs come off and scatter across the ring. The chain is *dynamic* and hangs
off a *kinematic* torso driven by `Rigidbody2D.MovePosition` from the model. A kinematic body is
infinitely massive to the solver, so an arm caught between two closing torsos has nowhere to go
and is ejected at enormous velocity, taking the hinge chain with it. Ten fighters in a 17-unit
ring put an arm in that pinch constantly. The gloves survive the same treatment only because
they are small and sit at the end of the chain, where they were tuned.

Two further traps, both measured rather than assumed:

- **It is not a layer problem, so do not go looking there.** All of a fighter's colliders share
  one `BoxerBody{id}` layer and that layer **ignores itself**, so nothing on a boxer ever
  collides with anything else on the same boxer; every `HingeJoint2D` also has
  `enableCollision` off. All 45 boxer-vs-boxer layer pairs *do* collide. Self-collision has
  never been the cause of anything here.
- **`FaceProbe` must stay a trigger.** It is the hit and perception volume and is far larger
  than the drawn head — 0.80 against 0.30. Make it solid and it stops being a sensor and starts
  shoving fighters around at nearly a metre of reach.
- **Do not separate bodies on glove contact.** The obvious reading of "body parts should
  collide" is to push two boxers apart whenever a glove enters the other's body circle. That
  breaks the damage tiers silently: the glove reaches 1.80 from centre and the body radius is
  0.98, so the enforced minimum separation becomes 2.78 — and `_closeRangeThreshold` is **2.50**,
  so close-range punches, the entire 2-damage tier, become physically unreachable.

Interpenetration between fighters is therefore held by `BoxerSystem.ResolveOverlaps` — one
circle of `_bodyRadius` per boxer — and the drawn arms are allowed to overlap. Fixing that for
real means making the torso dynamic, or driving the whole fighter through physics rather than
writing model positions into it. It is not a collider change.

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
- **Damage shows on the face.** `BoxerModel` carries `SwellLeft`, `SwellRight` and `Cut`,
  written by `CombatSystem.MarkFace` and drawn by two new `SpriteLitFX` properties. Swelling
  is volume and rises with every punch; a cut is a single event and needs one heavy punch, so
  it is gated on damage share first and scaled afterwards. Both constants are calibrated
  against measured matches — the numbers and what went wrong at other values are in the
  source.
  **Which side is the whole tell.** `CombatMath.ResolveHit` returns `ApproachLateral` — the
  approach vector projected onto the defender's own right — so a fighter who has spent a match
  circling into a right hand is marked on one side. A single averaged number renders every
  boxer equally puffy, which is as uninformative as no swelling at all.
  **It is presentational and stays that way.** Nothing reads these back into combat, so a
  fighter with a shut eye is exactly as effective as one without and the shipped policy is not
  being quietly recalibrated underneath. The same applies to the guard droop: `ArmView`
  biases only the *guard* pose by fatigue, and because both angles are reached by
  `LerpUnclamped` at extension 1, a drooping arm still puts the drawn fist at `ArmReach` on
  the frame the hit resolves. Blocking is still judged against the straight shoulder-to-glove
  segment the model believes in.
  **Damage is written to the head renderer only.** Swelling has no end condition inside a
  match, so whatever carries it stays out of the shared sprite batch until the bell — one
  renderer per marked fighter is affordable, all nine would be the ninety-draw-call trap
  `BoxerView`'s effect loop exists to avoid. `ClearEffectProperties` therefore clears all nine
  and immediately puts the head's block back carrying the bruise and nothing else.
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

