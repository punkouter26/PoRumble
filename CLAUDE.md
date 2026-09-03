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
| Controls | **WASD** move + aim · **J** left punch · **K** right punch · **Space** hold to charge a haymaker · **L** slip · **Tab** the fight card (between matches) · **R** restart at the results screen · **F3** diagnostics overlay |
| Train | Activate `.venv`, run `mlagents-learn Assets/Config/porumble_ppo.yaml --run-id=pr_1v1`, then open `Training1v1.unity` and press Play |
| Watch training | `tensorboard --logdir results` |
| Tests | `unity command run_tests --mode EditMode` — 131 EditMode tests |

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
  **The action vector is frozen.** Any compiled policy is built against exactly this shape;
  growing it stops the model loading. The haymaker was
  therefore built as a side channel (`BoxerSystem.SetCharge`) rather than a third branch, and
  the counter window needs no action at all. Retrain before changing either.
- Observations: `RayPerceptionSensorComponent2D` (17 rays, reach 24) plus 15 self scalars —
  health, facing, move input, both arms' extension and readiness, survivors, stamina, ring
  position and velocity. Rays are
  used because the opponent count shrinks during a match and a fixed vector cannot encode a
  variable-length list.
- Rewards: damage dealt/taken, elimination, win, an existential penalty (the ring does not
  shrink, so idling must cost), plus dense shaping for aiming at and holding range on the
  nearest opponent.
- **`gamma` is 0.995, not the usual 0.99.** An episode is `MaxStep` 1500 physics steps at
  `DecisionPeriod` 5 — 300 decisions. At 0.99 the +2 win bonus is worth 0.05 at the opening
  bell, too faint to shape anything; 0.995 leaves it worth 0.22.
- **`VectorObservationSize` on the prefab must equal what `CollectObservations` writes.** It is
  15. ML-Agents does not fail loudly on a mismatch in every path, and a compiled policy simply
  refuses to load. Change one and you must change the other, and retrain.
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
- **Every sprite carries a normal map, and a Light2D has to be told to read it.** The maps are
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
| `DiagnosticsHud` | `DiagnosticsHudView` | **F3** telemetry overlay |
| `MatchInput` | `MatchInputView` | The restart key |
| `MatchHud` | `MatchHudView` | Survivors, per-fighter health, countdown, result banner |
| `RosterCard` | `RosterSelectionView` | The fight card — pick who is in the ring (**Tab**) |
| `KnockoutMood` | `KnockoutMoodView` | Blends a desaturated, vignetted grade **and** the mixer's `Knockout` snapshot for the knockout hold |
| `Standings` | `StandingsHudView` | Top three of the Elo table |

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
| Min SDK | 24 | |
| Panel reference | 1080x1920, match width | The HUD was authored against a landscape canvas; in portrait the reference has to be portrait too |

`adb` ships with the Editor rather than on PATH, under
`Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/`. `Temp/deploy_android.sh`
installs, launches and dumps the Unity log in one step.

### Things that are easy to get wrong

- **The camera framing rule is orientation-dependent, and has to be.** The ring is square and
  no screen is. Landscape crops to fill: the camera pulls out only until the view is as wide
  as the ring, so the fighters stay large and the camera pans over the ring's height. Portrait
  letterboxes instead - cropping a 0.56 aspect to fill shows barely half the ring's width, so
  most of a ten-way brawl would be off-screen while the HUD still claimed ten were alive.
  `SpectatorCameraView.ClampToRing` then keeps the view inside the ropes on whichever axis the
  ring is larger, and centres it on the axis where it is not.
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
- **`MatchHudView` picks its restart prompt from the devices present,** not from a platform
  define, so the editor still reads "PRESS R" while a phone reads "TAP".
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
