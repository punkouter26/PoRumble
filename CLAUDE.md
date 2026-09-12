# CLAUDE.md — PoRumble

Top-down 2D boxing battle royale, after the style of Activision's Boxing (Atari 2600, 1980).
Ten fighters, last one standing, with ML-Agents-trained opponents.

---

## Project Overview

| Property | Value |
|---|---|
| **Unity** | 6000.6.0f1 (Unity 6.5) |
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
| Train | Activate `.venv`, run `mlagents-learn Assets/Config/Training/porumble_1v1_selfplay.yaml --run-id=pr_1v1`, then open `Training1v1.unity` and press Play |
| Watch training | `tensorboard --logdir results` |
| Tests | `unity command run_tests --mode EditMode` — 179 EditMode tests |

**Art is in Git LFS, and so are the fonts.** A fresh clone that has not run `git lfs pull`
leaves every `.png` as a 129-byte pointer file, and Unity imports those as nothing at all: the
sprites silently resolve to null, the fighters render as invisible transforms and the prefab
looks broken rather than unfetched. `ls -la Assets/Art/Sprites` tells you immediately - a real
sprite is kilobytes.

The **fonts fail differently and more confusingly**, because the half of them that is not in
LFS still works. The SDF `.asset` atlases are committed as ordinary files and import fine; only
the source `.ttf` faces are LFS. TextCore needs the face to initialise the atlas, so an
unfetched clone floods the console with `Failed to load font 'Barlow Condensed' (style:
'Medium'). The font face could not be initialized.` - one per text element per frame - and the
entire HUD renders as panels and bars with no text in them at all. It looks like a stylesheet
problem and is not. `git lfs pull` is the whole fix; `ls -la Assets/Art/Fonts` is the check,
and a real face is ~100KB against a 131-byte pointer.

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

## The Detailed Docs

This file is the index and the standing rules. The detail lives in `DOCS/`, which is the
first place to look before changing any of these areas.

| Doc | Covers |
|---|---|
| [DOCS/ARCHITECTURE.md](DOCS/ARCHITECTURE.md) | The four assemblies, `Views -> Systems -> Models`, the message flow, the scene and prefab layout |
| [DOCS/ML_AGENTS.md](DOCS/ML_AGENTS.md) | The frozen action vector, observations, rewards, the curriculum, the Python pins |
| [DOCS/RENDERING.md](DOCS/RENDERING.md) | URP 2D, sorting layers, Light2D and normal maps, particle materials, the `SpriteLitFX` shader |
| [DOCS/PRESENTATION.md](DOCS/PRESENTATION.md) | The feedback rig, the camera director, the telemetry board, audio and the commentator |
| [DOCS/GAMEPLAY.md](DOCS/GAMEPLAY.md) | Haymaker, guard, counter, slip; the fight card, Elo and the brain tiers |
| [DOCS/ANDROID.md](DOCS/ANDROID.md) | The portrait build, the camera framing rules orientation forces, touch input |
| [DOCS/RUNTIME_REPORT.md](DOCS/RUNTIME_REPORT.md) | The last full runtime verification pass: errors, telemetry, frame budget |

---

## The Traps That Bite Hardest

The full reasoning for each is in the doc named beside it. These are here because they are
silent failures - nothing errors, and the thing simply does not work.

- **Run `git lfs pull` on a fresh clone.** Art *and* fonts are LFS. Unfetched, sprites import
  as nothing and every text element floods the console with a font-initialisation failure
  while the HUD renders as empty panels. `ls -la Assets/Art/Fonts` - a real face is ~100KB
  against a 131-byte pointer.
- **The ML action vector is frozen.** Four continuous plus two discrete branches. Growing it
  stops `PoRumbleBoxer.onnx` loading at all, which is why the haymaker and the slip are side
  channels rather than actions. `VectorObservationSize` on the prefab must equal what
  `CollectObservations` writes (15). → `DOCS/ML_AGENTS.md`
- **A boxer must never perceive itself.** `Physics2D.queriesStartInColliders` is off and
  `BoxerSpawnPoints.IsolatePerception` puts each fighter's colliders on its own layer. Turn
  either off and the forward rays report the boxer's own face at half a metre, permanently.
  → `DOCS/ARCHITECTURE.md`
- **Every `Light2D` must list every sorting layer**, and must be told about normal maps.
  `NormalMapQuality` is declared `Disabled = 2, Fast = 0, Accurate = 1`, so a
  SerializedProperty's `enumValueIndex` is *not* the enum's value - set `intValue`.
  → `DOCS/RENDERING.md`
- **The `UnityPerMaterial` CBUFFER must be byte-identical in all three `SpriteLitFX` passes**,
  or Unity silently drops the shader out of the SRP Batcher with no error anywhere.
  → `DOCS/RENDERING.md`
- **`CombatSystem`, `MatchSystem` and `RatingSystem` are resolved eagerly** in
  `GameLifetimeScope`. They only subscribe to messages, so nothing injects them and VContainer
  would never construct them - punches would silently do nothing.
  → `DOCS/ARCHITECTURE.md`
- **The flow loop runs on unscaled time.** The knockout hold sets `Time.timeScale`, which is
  global and outlives Play mode; anything that touches it must restore it.
  → `DOCS/ARCHITECTURE.md`
- **Changing a `[SerializeField]` default does not retune an object that already exists.**
  The serialized value in the scene wins. This fails quietly every time.
- **Sprite pixels-per-unit equals the sprite's pixel width**, so one sprite is one world unit.
  The hit maths is tuned against those dimensions; changing a PPU silently moves the drawn
  fists away from the hitboxes. → `DOCS/RENDERING.md`
- **Editing any `.cs` under `Assets/` triggers a domain reload**, which exits Play mode and
  kills a running training session. Batch code changes before starting a run.

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
- **`recompile` can report success while compilation is actually failing.** It answered
  `failed: false, errors: []` and then `up_to_date` through four attempts while
  `PoRumble.Views` was failing on a real `CS0246`. Its error list is not trustworthy: check
  `get_console_logs --severity error`, and compare `Library/ScriptAssemblies/<Assembly>.dll`'s
  mtime against the source file's. An assembly older than the source did not build. The
  downstream symptom is misleading - the Editor keeps running the last good assembly, so a newly
  added `[SerializeField]` comes back null from `SerializedObject.FindProperty` and is absent
  from `typeof(T).GetFields()`, which reads as a serialization bug rather than a failed compile.
  Editing with a shell tool while the Editor is unfocused can also mean Unity never notices the
  change; `coplay-unity refresh_unity --mode force --compile request` triggered the build when
  `recompile` would not, and `AssetDatabase.Refresh(ForceUpdate)` alone did not.
- Editing any `.cs` under `Assets/` triggers a domain reload, which **exits Play mode and
  kills a running training session**. Batch code changes before starting a run.

> The bundled agents in `.claude/agents/` declare `mcp__unityMCP__*`, which matches neither
> registered server. They will run without Unity access until renamed.

**Blender MCP** is registered but needs Blender running with the addon on port 9876.

---


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

---

## Standing Working Rules

These are the user's standing instructions for this project and any RL project like it.
They override defaults; where one clashes with something above, this section wins.

### Git & branches

- **Work only on the default branch** — `master` here. Use another branch only when explicitly
  asked. Renamed from `main` on 2026-09-12, so "master" is now both what the user says and what
  the branch is called; the note that used to translate between the two is gone because there is
  nothing left to translate.
- **A git sync commits everything first.** Never sync, pull or push with uncommitted changes
  sitting in the tree — commit them as part of the sync.

### Orientation

- **Check for a `DOCS/` folder at the repo root** before starting work; it carries the
  overall project summary. `DOCS/` now exists; this file is the index to it.

### Training

- **Start TensorBoard whenever training starts**, so the run is watchable live. This restates
  `.claude/rules/training.md`, which is the full version.
- **Clear obsolete runs out of TensorBoard first.** Stale run directories under `results/`
  clutter the scalar view and make it hard to read the run that matters — prune them (keep
  anything in `results/_preserved/`) before launching.
- **For a training run of 30+ minutes, close the Unity Editor first** (saving work), let the
  run have the machine, and tell the user explicitly when it has finished and the Editor can
  be reopened.

### Roster conventions for RL projects

- **Scripted/heuristic bots are RED.** Always, no exceptions — that colour is how the user
  identifies a hand-coded opponent on sight.
- **The reference RL bot is GREEN and untextured** — the plain `PoRumbleBoxer.onnx` policy
  driven straight through, before any style or art variation.
- **Variant RL fighters carry custom textures and meshes** supplied by the user; those are
  the only roster entries free to look like anything.
- **Every RL app fields all three**: one heuristic bot, one reference bot, and zero or more
  custom bots. A roster missing the red or the green seat has lost its baseline.

  Here those colours live in two places and both have to agree. `Fighter_HEURISTIC._tint` and
  `Fighter_STANDARDRL._tint` drive the fight card, and `BoxerView._scriptedColor` / `_rlColor`
  (serialized on `Boxer.prefab`) drive `SetRoleColor`, which is the path the training scenes
  take — `SampleScene` has `_useRoleColors: 0` because the card supplies a tint per
  contestant, `Training1v1` has it on. Change one and the red bot goes red in only half the
  project.

### Simulation fidelity

- **Creatures move under Earth gravity with realistic joint limits and mass for their size.**
  No floaty scaling, no torque a real body could not produce.
- **Joint speed and force resemble a human's** when the trained agent is a human.

### Physics engines outside Unity

- **MuJoCo on Android** builds from <https://github.com/joanllobera/mujoco-bin/>.
- **Show the simulator's UI during and after training** (MuJoCo, Isaac Lab, or Newton where
  that is the better viewer) so the user can watch how the creature actually moves.

### Scene authoring

- **Build scene objects and prefabs through MCP, not from code.** Anything static — positions,
  props, rig objects — should exist in the scene as an asset the user can drag, not as
  something a script spawns at runtime. Code that creates static scenery takes away the
  adjustment.

### Answering

- **Any answer longer than ~100 words ends with a 20-word TLDR.**
