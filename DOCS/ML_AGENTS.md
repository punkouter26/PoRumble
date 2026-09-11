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
  here is `PoRumbleBoxer.onnx`. Preserve checkpoints again before the next run.
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

