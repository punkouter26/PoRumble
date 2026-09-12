# Training Charts, Explained in Plain English

What every line on the TensorBoard screen actually means, what a healthy one looks like, and
what this project has already learned the hard way about reading them. Captured 2026-09-12.

---

## Tier 1 — Quick Look (30 seconds)

While a fighter is learning, five charts matter. In plain terms:

| Chart | Think of it as | You want |
|---|---|---|
| **Cumulative Reward** | The scoreboard | Trending up, noisily |
| **Episode Length** | How long a match lasts | Coming down — matches are being *finished* |
| **Policy Loss** | Confusion level | Restless early, calmer later |
| **Value Loss** | "Did I see that coming?" | Falling and staying low |
| **Entropy** | Curiosity | High early, drifting down |

**The one thing to remember about this project:** the scoreboard lies. Score sat flat at about
6.05 for three million steps while the fighter got visibly better *and then worse and then
better again*. The honest question here is not "is the score going up" — it is **"are matches
actually ending?"** Everything below is built around that.

**The second thing:** never start a run without TensorBoard. The console prints a score only
every 10,000 steps, which is far too coarse to catch a fighter that has found a cheat or a
brain that stopped improving an hour ago. This is a standing project rule.

---

## Tier 2 — Core Mechanics

### Cumulative Reward — "The Scoreboard"

**What it is:** the total points a fighter collected in an average match, adding up everything
it was paid for — landing punches, knockouts, the win bonus — minus everything it was docked
for.

**What healthy looks like:** a line that climbs unevenly, with plenty of jitter. Flat is not
automatically bad and rising is not automatically good.

**What to distrust:**

- **A rising score with a *shrinking* spread** is usually a cheat, not skill. Every fighter has
  found the same cheap trick and settled on it. This project has been bitten by exactly that:
  the boxers learned to huddle against a wall together to farm a proximity reward. The fix was
  to stop paying that reward once already in range.
- **Any single reading.** Score swings about ±0.4 between reports here, so one line is noise.
  Judge a run on windowed averages, never on the last summary.
- **The score at all, when picking which save to ship.** This is the big one — see the warning
  box below.

> ### Why score is the wrong thing to pick a winner on here
>
> Finishing a match early cuts the episode short, which caps how much damage-reward can pile
> up. So the reward function **mildly punishes winning quickly**. Choosing the best-scoring
> save would have shipped a brain that finished **21%** of its matches over the one that
> finishes **76%**. The score and the actual goal point in different directions. Pick on
> finishes.

### Episode Length — "Survival Time"

**What it is:** how many steps an average match lasted before it ended.

**What it means here:** matches end one of two ways — somebody wins, or the clock runs out. So
a *falling* episode length means fighters are landing decisive work. A length **pinned at the
cap** means nothing is resolving at all, and it is the single most diagnostic chart in the set.

**What healthy looks like at each stage:**

| Stage | Cap | Healthy | Pinned at the cap means |
|---|---|---|---|
| 1v1 | 2500 steps (50 s) | ~280 steps to a knockout | Something is broken — see below |
| 10-way | 2500 steps (50 s) | **Often the full cap, and that is expected** | Normal. 270 health has to come off ten fighters spread across the ring |

**The history behind this chart is worth knowing.** For a long time the training config ran
fighters at 6 HP instead of 30, because at 30 every episode timed out with nothing to learn
from. That turned out to be the wrong fix for the right symptom: the fighters could not *see*
each other — the ray sensor was reporting the boxer's own torso — so they never landed
anything. With perception fixed, a 30 HP knockout resolves in about 280 steps, and the training
config now matches the game exactly.

**So if episode length pins at the cap in a 1v1 run: check what the rays are actually returning
before touching the health number.**

### Policy Loss — "Confusion Level"

**What it is:** how much the fighter's strategy is being rewritten with each lesson.

**What healthy looks like:** noisy and elevated early — it is rewriting a lot, because it knows
nothing — settling to a low hum later. Some movement forever is correct; a fighter whose policy
loss has gone to nothing has stopped learning.

**What to distrust:** a sudden spike long after things settled usually means the fighter just
discovered something new. That is sometimes a breakthrough and sometimes a cheat. Watch what
happens on screen, not only on the chart.

### Value Loss — "Did I See That Coming?"

**What it is:** how badly the fighter mispredicted the points it was about to earn.

**What healthy looks like:** rising early — early on, everything is a surprise — then falling
and staying down.

**What to distrust:** **value loss that falls to nothing while reward oscillates around zero.**
That exact pair was this project's worst training failure, and it had a specific cause: the
ten-way never ended at all. The trainer closed every episode at the step cap, but the match
itself never reached a decided state, so no winner was declared, the ring was never re-racked,
and the win bonus landed in the *next* episode. Fixed by making the match resolve on health a
few steps before the trainer cuts the episode.

### Entropy — "Curiosity / Exploration"

**What it is:** how much the fighter is still trying things at random rather than doing what it
believes works.

**What healthy looks like:** high at the start, drifting down as the fighter commits.

**What to distrust — both directions:**

- **Flat and high** means the policy is still essentially random and is not committing to
  anything. This project saw entropy pinned near maximum at 190k steps; the fix was lowering
  the curiosity dial (`beta`) from 0.005 to 0.001 in the sparring config.
- **Collapsing to near-zero early** means the fighter has locked onto one behaviour and stopped
  exploring. Usually the first sign of a cheat.

**The curiosity dial is stage-specific here, on purpose:**

| Stage | `beta` | Reasoning |
|---|---|---|
| 1v1 self-play | 0.005 | New task, nothing known yet |
| 1v1 vs scripted | 0.001 | Entropy was stuck at maximum; the fighter needed to commit |
| 10-way | 0.002 | Starts from the 1v1 brain, whose entropy is already down at ~2.5. A big bonus would push it straight back up and wash out the transfer — but a ten-way genuinely *is* a new task, so it earns more exploration than the 1v1 stage |

### Self-play ELO

**What it is:** a chess-style rating of the fighter against its own past selves.

**When it means anything:** **stage 1 only.** Self-play models two teams taking turns; a
free-for-all has no teams. In stages 2 and 3 the `self_play` block is removed from the config
and this chart will not appear. If it does appear in a ten-way run, the config is wrong.

### How long before a trend is a trend

**Nothing shorter than about 2 million steps counts as a trend in this project.** The rate at
which matches finish before the bell oscillates on roughly that period — 50% at 1M, down to 34%
by 3M, back to 50% by 4M — while the score sits flat at ~6.05 throughout and hides all of it.

A four-window slide looks exactly like a regression and is not one. This is why
`keep_checkpoints` is set to 15 rather than the default 5: preserve saves across the whole run
and pick at the end.

---

## Tier 3 — Setup Guide

### Getting the charts up

```powershell
# TensorBoard FIRST, in its own process
Start-Process -WindowStyle Hidden .venv\Scripts\tensorboard.exe -ArgumentList "--logdir results --port 6006"

# Prove it actually came up - Start-Process succeeds even if the process dies a second later
Test-NetConnection -ComputerName localhost -Port 6006 -InformationLevel Quiet

# Then the trainer, then press Play
mlagents-learn Assets/Config/Training/porumble_1v1_selfplay.yaml --run-id=pr_1v1
```

Then open <http://localhost:6006>. Prune stale run directories from `results/` first — keep
anything under `results/_preserved/` — or the scalar view is unreadable.

**Note for this working tree:** there is currently **no `results/` directory at all**. No run
history is preserved here, so TensorBoard will open empty until a new run starts.

### Where to find each chart

| Plain name | TensorBoard scalar |
|---|---|
| The scoreboard | `Environment/Cumulative Reward` |
| Survival time | `Environment/Episode Length` |
| Confusion level | `Losses/Policy Loss` |
| Did I see that coming | `Losses/Value Loss` |
| Curiosity | `Policy/Entropy` |
| Learning pace | `Policy/Learning Rate` |
| Rating vs past selves | `Self-play/ELO` (stage 1 only) |

### Agent Comparison Grid

Every brain this project has trained, rated across the five charts. Only the last row is
present in the working tree — the others are read from the project's written record and are
here because *what went wrong* in each is the most transferable thing in this document.

| Brain | Scoreboard | Survival time | Confusion | Curiosity | Finishes matches | Verdict |
|---|---|---|---|---|---|---|
| **Legacy pre-perception fix** | Low | **Pinned at cap** | High | **Flat, high** | ~none | ❌ **Broken.** The ray sensor reported the boxer's own torso, so nobody could see anybody. Every symptom above is downstream of that one bug. Trained against an 11-wide observation vector and will no longer load |
| **`spar` — 1v1 vs scripted** | Medium | Healthy (~280 steps) | Healthy | Low after the `beta` fix | n/a — 1v1 always finishes | ✅ **Good stage-1 base.** Entropy was pinned at maximum until the curiosity dial came down from 0.005 to 0.001 |
| **`ffa_v3` — 10-way** | Medium, **flat and misleading** | At cap (expected) | Healthy | Healthy | 0/80 summaries at 1.6M → **28/80 at 4M** | ⚠️ **Not ready, but not failing.** Sat in a trough at 1–1.6M that looked exactly like convergence, then climbed out and gained another 8%. Score moved only 5.68 → 6.12 across all of that |
| **`ffa_v5` — SHIPPED** | Medium (~6.05, flat) | At cap (expected) | Healthy | Healthy | **76%** | ✅ **Ready for game.** ~21M cumulative steps. This is `PoRumbleBoxer.onnx` |

Scale: **Low / Medium / High** for level, **Healthy** where the shape is right regardless of
level. "Pinned at cap" in the survival column is a failure in a 1v1 and normal in a ten-way.

### Quick triage — chart symptom to likely cause

| What you see | Look here first |
|---|---|
| Episode length pinned at cap in a **1v1** | The ray sensor. Is `Physics2D > Queries Start In Colliders` off? Is `IsolatePerception` running? Fighters that cannot see cannot fight |
| Reward oscillating around zero, value loss falling to nothing | The match is never reaching a decided state. The win bonus is landing in the next episode |
| Entropy flat and high after 200k steps | `beta` is too high for this stage |
| Score rising, spread shrinking, fighters clumping on screen | A reward is being farmed. Check the dense shaping terms first |
| Score flat for a million steps | Probably nothing. Check the finish rate instead and give it 2M steps before judging |
| An ELO chart in a ten-way run | The `self_play` block was left in the config |
| No fighters move at all, no error | Behaviour name mismatch, or the observation count on the prefab no longer equals what the code writes (must be 15) |

### What this project counts as "ready for the game"

1. **Finish rate**, not score, is the selection metric.
2. At least **2 million steps** of evidence before calling a trend.
3. Checkpoints preserved across the **whole** run, chosen at the end — not the last one
   standing after rotation.
4. Watched live on TensorBoard from step one, so a cheat is caught while it is forming rather
   than discovered in the finished brain.

---

## TLDR

Score lies here — judge on whether matches finish. Episode length pinned at the cap means a
1v1 fighter cannot see. Give any trend 2M steps.
