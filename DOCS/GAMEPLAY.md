# Combat Depth, the Fight Card, Elo and Difficulty Tiers

The mechanics layered on top of the base punch exchange, the contestant roster, the rating table and the scripted brain profiles.

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

