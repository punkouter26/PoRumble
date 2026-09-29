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
- **`PoRumbleBoxer.onnx` is the `ffa_v5` model** (~21M cumulative steps), trained 1v1 against the scripted
  partner to saturation and then transferred into the ten-way free-for-all. The policy it
  replaced was compiled against the old 11-wide vector *and* trained while the ray sensor
  reported nothing but the boxer's own torso. Neither that legacy model nor the
  `results/_preserved/` checkpoint archive is present in this working tree: the only model
  here is `PoRumbleBoxer.onnx`. Preserve checkpoints again before the next run. They are also
  what the evolution exhibition seats as `FighterProfile._policyCheckpoint` fighters - see
  `GAMEPLAY.md`, *Checkpoint fighters*.
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
- **Reward balance.** Landing damage (0.35 per point) dominates: emptying one 30 HP opponent is
  worth 10.5, against 0.5 for the elimination and 2 for the win. The dense shaping - aim 0.6,
  approach 0.25, range 0.4, each spread over `MaxStep` - totals about +1.25 an episode against the
  -1 existential cost, so standing at range facing someone is mildly positive on its own. That
  is intended as a gradient toward the first hit and is small next to one landed punch; watch
  for it if a run's reward rises while finishes fall.
- **Observations are already minimal** (15 self scalars and 17 rays, frozen by the model
  contract), and nothing in the agent runs during training that training does not use - the
  style modulator, the scripted brain and the keyboard path are all bypassed for learners.
- **There is no ragdoll.** The fighters are kinematic top-down bodies with no joints; "joint
  limits" do not exist here to tune. The locomotion is already human-scale if a body diameter of
  two units is a boxer's half-metre shoulder width: about 1.3 m/s footwork, a 0.22 s jab, 360°/s
  pivots, 5 m/s² acceleration.

### Throughput on this machine (i7-10750H, 6 cores / 12 threads, no GPU training)

| Setup | Steps/s |
|---|---|
| Editor Play mode, one arena | the old baseline; a fraction of the below |
| `Train1v1` build, 8 headless arenas, one learner each | ~410 |
| `Train10Way` build, 6 headless arenas, eight learners each, time scale 20 | ~1,720 |
| same at time scale 40 | ~1,740 - CPU-bound, the time scale buys nothing |

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
1.5M steps, initialises the ten-way from that and runs it to 40M or the deadline, then copies
both runs into `results/_preserved/`. It never replaces `PoRumbleBoxer.onnx`.

**Killing `mlagents-learn` does not kill its arena workers.** A plain `Stop-Process` left twelve
Python workers running; kill the tree (`taskkill /T /F /PID`).
