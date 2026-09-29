# ML-Agents

The behaviour contract, the observation and action vectors, the training curriculum and the Python pins. The action vector is frozen; read this before changing anything the compiled policy is built against.

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
- **`PoRumbleBoxer.onnx` is `pr_ffa_0929b` at 13M steps** (`PoRumbleBoxer-12999948.onnx`),
  shipped 2026-09-29: 1.0M steps of spar against the scripted partner, then the ten-way in the
  game's ring. It is the checkpoint with the best finish rate in that run (3.8%, 6.7 knockouts a
  match) - see *Run 0929b* below. Watched in `SampleScene`, one ten-way went to the 3:00 bell
  with three standing: most knockouts came once sudden death closed the ropes, and in the
  full-size ring it opens on the ropes. It replaced `ffa_v5` (~21M steps, trained in the 40x40
  ring); that model is in git history up to commit `0d95c8e`. The run's
  checkpoints are preserved under `results/_preserved/pr_ffa_0929b/`, which is not in the
  repository; they are also what the evolution exhibition seats as
  `FighterProfile._policyCheckpoint` fighters - see `GAMEPLAY.md`, *Checkpoint fighters*.
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


## Audit of 2026-09-29

- **The ten-way trained in a ring the game never uses.** `Training10Way` was 40x40; the game's
  ring is 17x17 and closes to 5x5 in sudden death. Positions are normalised so the observation
  was in range, but every distance the policy learned - approach, range, the rays' 24-unit reach
  against the walls - belonged to a ring more than twice the size. Run with the shipped model in
  the game-size ring, a 2,500-step episode ended with one knockout. The ring, spawn radius and
  half extent now copy `SampleScene`'s exactly.
- **The ten-way now seats two scripted fighters** (ids 8 and 9, red), as two seats of the game's
  card are. `HeuristicOnly` agents send no experience, so eight learners per arena train against
  the mix of opponents they meet in the game.
- **`MaxStep` on the prefab is 2500, not the 1500 the config comments assume.** At `DecisionPeriod`
  5 that is 500 decisions, and the +2 win is worth 0.995^500 = 0.08 at the opening bell, not 0.22.
  Left as it is: the longer episode gives a 17x17 ring time to resolve. Remember it before
  retuning `gamma`.
- **Reward balance.** These are the values serialized on `Boxer.prefab`, which win over the code
  defaults. Landing damage (0.2 per point) dominates: emptying one 30 HP opponent is worth 6,
  against 1 for the elimination, -0.75 for being eliminated and 2 for a knockout win (1 for a
  win on the bell, since the second audit below). The dense shaping - aim 0.6,
  approach 0.25, range 0.4, each spread over `MaxStep` - totals about +1.25 an episode against the
  -1 existential cost, so standing at range facing someone is mildly positive on its own. That
  is intended as a gradient toward the first hit and is small next to one landed punch; watch
  for it if a run's reward rises while finishes fall.
- **Observations are already minimal** (15 self scalars and 17 rays, frozen by the model
  contract), and nothing in the agent runs during training that training does not use - the
  style modulator, the scripted brain and the keyboard path are all bypassed for learners.
- **There is no full-body ragdoll, but the arms are jointed** (this entry used to say there were
  no joints at all, which was wrong). The torso is a kinematic `Rigidbody2D` moved by
  `MovePosition`; each arm is a dynamic three-hinge chain (`ArmView`), servoed toward the
  extension the model decided, so combat stays deterministic. Checked against human ranges and
  left as they are: elbow 0-145° with no hyperextension, wrist ±25° (radial/ulnar deviation, the
  plane a top-down fist turns in), shoulder -45..80° mirrored; segment masses 0.16 : 0.09 : 0.07,
  the same ratio as a human upper arm, forearm and hand-plus-glove (2.1 : 1.2 : 0.9 kg). Gravity
  scale is 0 because gravity points into the screen. The arms matter to the policy in one way:
  the gloves are colliders, so they occlude rays. The locomotion is human-scale if a body
  diameter of two units is a boxer's half-metre shoulder width: about 1.3 m/s footwork, a 0.22 s
  jab, 360°/s pivots, 5 m/s² acceleration.

### Throughput on this machine (i7-10750H, 6 cores / 12 threads, no GPU training)

| Setup | Steps/s |
|---|---|
| Editor Play mode, one arena | the old baseline; a fraction of the below |
| `Train1v1` build, 8 headless arenas, one learner each | ~410 |
| `Train10Way` build, 6 headless arenas, eight learners each, time scale 20 | ~1,720 |
| same at time scale 40 | ~1,740 - the time scale buys nothing |

Time scale 40 buys nothing for two reasons, and the second one caps it regardless of the CPU. The
trainer sets `Time.captureFramerate` to 60, so each frame advances `timeScale / 60` seconds, and
`Maximum Allowed Timestep` (0.333 s) clips that. At time scale 20 a frame is already 0.333 s,
about 16 physics steps, so anything above 20 is clipped away. `m_UseBatchedRaycasts` on the ray
sensor does nothing either: the package only batches 3D casts.

Torch runs on the CPU (`torch==2.5.1+cpu`): a 256x2 MLP is faster there than behind a PCIe
round trip, and the arenas, not the network, are the bottleneck. `threaded: true` lets the PPO
update overlap stepping.

**`.venv` is not in the repository.** Rebuild it with the pins above:

```powershell
uv venv .venv --python C:\Users\punko\AppData\Local\Programs\Python\Python310\python.exe
uv pip install --python .venv\Scripts\python.exe --index-strategy unsafe-best-match `
  --extra-index-url https://download.pytorch.org/whl/cpu "mlagents==1.1.0" "torch==2.5.1" `
  "numpy<1.24" "protobuf<3.21" "setuptools<81"
```

`mlagents` 1.1.0 needs Python 3.10.1-3.10.12; uv's own 3.10.21 is too new.

### Overnight runs

Build `Training1v1` to `Builds/Train1v1/PoRumbleTrain.exe` and `Training10Way` to
`Builds/Train10Way/PoRumbleTrain.exe` (one scene each), close the Editor, then
`Tools\train_overnight.ps1 -Tag <tag> -Hours 8`. It checks TensorBoard is listening, spars to
1.0M steps, initialises the ten-way from that and runs it to 24M or the deadline, then copies
both runs into `results/_preserved/`. It never replaces `PoRumbleBoxer.onnx`.

**Killing `mlagents-learn` does not kill its arena workers.** A plain `Stop-Process` left twelve
Python workers running; kill the tree (`taskkill /T /F /PID`).

## Second audit of 2026-09-29 - what the overnight run showed, and what changed

**The evidence.** `pr_ffa_0929` ran the full 40M steps in 6.2 hours (1,800 steps/s). Its Episode
Length sat at 498 decisions - the 2,500-step bell - in every summary of the run: **not one
ten-way match finished on knockouts.** Reward rose 5.0 -> 5.9 in the first 4M steps and then only
to 6.3 over the remaining 36M, while entropy fell 1.95 -> 0.43. `pr_spar_0929` had converged by
about 0.8M of its 1.5M steps. Watched in `Training10Way` in the Editor, the shipped
`PoRumbleBoxer.onnx` puts 50-70% of the standing boxers on the ropes, with the whole field piled
into one quadrant, and the bell comes with 8-10 still up.

**Changes made** (trained in run `0929b`; results below the table):

| Change | Where | Why |
|---|---|---|
| Knocked-out boxers stop requesting decisions | `BoxerDecisionRequester` replaces `DecisionRequester` on `Boxer.prefab` | A match ends every episode together, so a boxer dropped early used to keep sending a corpse's observations and actions as experience until the bell. The knockout penalty still arrives, on the terminal step `EndEpisode` sends |
| TensorBoard: `Match/Finished By Knockout`, `Match/Knockouts`, `Match/Length Seconds` | `MatchDirector.RecordMatchStats` | Models are selected on finish rate, and nothing reported it. **Episode Length now means how long a learner stayed up, not the match** |
| TensorBoard: `Boxer/Punch Accuracy`, `Boxer/Damage Dealt`, `Boxer/Share On Ropes` | `BoxerAgentView`, learners only | Accuracy and output say what reward hides; the ropes share is the wall-huddle detector |
| Knockout win 2, decision win 1 (was 2 for both) | `_knockoutWinReward`, `_decisionWinReward` | A leader late in a ten-way was paid as much for running out the clock as for finishing |
| Ten-way `beta_schedule: constant` | `porumble_10way_ffa.yaml` | beta used to decay to zero with the learning rate, and entropy collapsed onto a policy that never finished a match |
| Ten-way 40M -> 24M, spar 1.5M -> 1.0M | both YAMLs | The tails bought +1.7% and +0.4% reward for 2.5 h and 18 min |
| `train_overnight.ps1` refuses stale builds and an open Editor, and prunes runs already in `_preserved` | `tools/train_overnight.ps1` | A run drives prebuilt players, so it would otherwise have trained yesterday's code without a word |
| Ropes penalty 0.5 per match spent on the ropes | `_ropesPenaltyWeight` | Being cornered is the worst place in a boxing ring, and the shipped policy lives there. Below aim + range (1.0), so holding range on someone on the ropes still beats walking away |
| Ten-way gamma 0.995 -> 0.998 | `porumble_10way_ffa.yaml` | The horizon becomes the whole 500-decision match; a knockout win is worth 0.74 from the opening bell instead of 0.08. The spar keeps 0.995: its knockouts land in ~100 decisions |
| Spar ring 20x14 -> the game's 17x17 | `Training1v1.unity` | The ten-way already trained in the game's ring; the stage feeding it did not |
| Four spar arenas per player, one `GameLifetimeScope` each (`Arena_0`..`Arena_3`, 30 units apart) | `Training1v1.unity`, `GameLifetimeScope`, `BoxerView.SetArenaOrigin` | A one-learner process is bound on the Python round trip - every decision was a batch of one - which is why the spar ran at ~450 steps/s against the ten-way's 1,800. `train_overnight.ps1` now runs six such players (24 learners) |

**How the arenas stay apart.** Models stay arena-local - positions, the ropes clamp and every
observation are measured from the ring's centre - and only `BoxerView` adds the ring's world
origin, read off `BoxerSpawnPoints`' transform. A scope that has a `BoxerSpawnPoints` among its
children binds to that one; the game and the ten-way keep their scope beside the ring and fall
through to the scene-wide search, unchanged. Walls stop every ray, so nothing perceives the next
arena. The spar speed-up has not been measured yet: read the steps/s off the first run.

**What to watch on the next run.** `Match/Finished By Knockout` first, then `Boxer/Share On
Ropes` falling. All of the above went in together at the user's request, so a change in either
cannot be pinned on one edit. If the run goes wrong, the two reward edits (ropes penalty,
decision win) and the ten-way gamma are the ones to back out one at a time.

**A directional bias in the shipped policy.** Run in `Training10Way`, and again in all four spar
arenas against the scripted partner, the shipped `PoRumbleBoxer.onnx` drifts to the top-left
corner - the same corner every time, in every arena. A symmetric task should not produce that.
It is the old model, trained in the 40x40 ring, so a retrain may simply clear it; if a new model
shows it too, look for an asymmetry in the observation or the aim path before blaming reward.

## Run 0929b - the first run with those changes (2026-09-29, 4 h 14 min)

Spar 1.0M steps in 14 minutes at ~1,240 steps/s (it was ~450 with one arena per player). Ten-way
24M steps in 3 h 59 min at ~1,670 steps/s. Both preserved under `results/_preserved/`.

| | Spar | Ten-way, best checkpoint (13M) | Ten-way, final (24M) |
|---|---|---|---|
| Matches finished by knockout | 100%, in ~17 s | 3.8% | 1.3% |
| Knockouts per match (9 finish it) | - | 6.7 | 6.3 |
| Share on ropes | ~35% | 29% | 46% |
| Punch accuracy | 65% | 28% | 27% |

Compared with `pr_ffa_0929`, which never finished one ten-way match in 40M steps, the ten-way now
finishes a few and knocks out three times as many. Two things did not hold:

- **The ropes penalty wore off.** Share on ropes fell from 45% to 4% by 2M steps, then climbed
  back past where it started. Knockouts rose over the same stretch, so the boxers are fighting on
  the ropes rather than hiding there, but at 0.5 per match the penalty is outweighed. Raise it
  before the next run.
- **Knockouts per match stall around 6.** Nine in a 50-second match with no sudden death - which
  the game has and training does not - looks like the limit, not a reward setting. The next lever
  is the training bell or sudden death, which means revisiting the rule that training never steps
  `SuddenDeathSystem`.

The best checkpoint on finish rate is `PoRumbleBoxer-12999948.onnx`, the last one before the
ropes share climbed past 30%.
