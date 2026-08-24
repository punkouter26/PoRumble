# Training Rules

## Always start TensorBoard alongside training

Whenever an ML-Agents training run is started, start TensorBoard too. Training
without it is flying blind: the console only prints a mean reward every
`summary_freq` steps, which is far too coarse to spot reward hacking, a
collapsing entropy, or a policy that stopped improving an hour ago.

```powershell
# 1. TensorBoard first, in its own process
Start-Process -WindowStyle Hidden .venv\Scripts\tensorboard.exe -ArgumentList "--logdir results --port 6006"

# 2. Then the trainer
mlagents-learn Assets/Config/porumble_ffa.yaml --run-id=<run-id>

# 3. Then press Play in the Editor
```

Then open <http://localhost:6006>.

Verify it actually came up rather than assuming — check the port is listening
before reporting that it is running.

### What to watch

| Scalar | Reads as |
|---|---|
| `Environment/Cumulative Reward` | The headline. Should trend up, noisily |
| `Environment/Episode Length` | Pinned at the cap means nothing is resolving — no knockouts |
| `Policy/Entropy` | Flat and high means the policy is still essentially random |
| `Losses/Value Loss` | Spiking means the critic cannot predict the reward it is being given |
| `Self-play/ELO` | Only meaningful in the 1v1 stage; a free-for-all has no teams |

A rising reward with a **falling** standard deviation is usually reward
hacking, not skill: the agents have found one cheap behaviour and all settled
on it. This project has already hit that once, when every boxer learned to
huddle against a wall to farm the proximity reward.
