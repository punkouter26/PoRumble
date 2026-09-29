# AGENTS.md

Standing rules for any AI agent working in this project, and in any RL project like it. They
override defaults. Project detail lives in `CLAUDE.md` and `DOCS/`.

## Git

- **Work only on the default branch.** The user calls it "master"; in this repo it is `main`.
  Use another branch only when explicitly asked to.
- **A git sync commits everything first.** Never pull, push or sync with uncommitted changes.

## Orientation

- **Read `DOCS/` at the repo root before starting.** It is the overall summary of the project.

## Answering

- **Any answer longer than 100 words ends with a TLDR of about 20 words.**

## Unity tooling

- **Use whichever of these gives the best result for the task:**
  - the Unity CLI pipeline (`unity command ...`, MCP server `unity-pipeline`)
  - CoplayDev unity-mcp - <https://github.com/CoplayDev/unity-mcp> (MCP server `coplay-unity`)
  - IvanMurzak Unity-MCP - <https://github.com/IvanMurzak/Unity-MCP>
- **Keep the Editor from stalling in the background.** Through MCP: set Preferences > General >
  Interaction Mode to **No Throttling**, turn on **Run In Background** in Player settings, and turn
  on the pipeline's **auto tick** (`unity command set_autotick`) so the Editor keeps updating
  while unfocused.
- **Build scene objects and prefabs through MCP, not from code.** Anything static - positions,
  props, rigs - should exist in the scene or a prefab where it can be dragged and adjusted, not be
  created by a script at runtime.

## UI changes

- **After any UI change, take an annotated screenshot showing the old UI and the new one, with
  the changes marked, and put it in an HTML file.**

## Training

- **Start TensorBoard whenever training starts**, so progress can be watched live. Check the port
  is actually listening rather than assuming.
- **Before training, remove obsolete runs from TensorBoard** so they do not clutter the view.
  Keep anything under `results/_preserved/`.
- **For training of 30+ minutes, close the Unity or Unreal Editor** if that speeds training up
  materially, having saved and committed first. Say explicitly when training is over and the
  Editor can be reopened.
- **Once training has run 30+ minutes, screenshot the three most consequential TensorBoard
  charts,** review them, and put the screenshots and the review in an HTML file. Explain each chart
  at three levels: **1. toddler, 2. child, 3. adult.**

## Physics engines outside Unity

- **Train in MuJoCo or Newton** - unless the app uses ML-Agents, in which case every MuJoCo and
  Isaac Lab rule in this section is ignored. PoRumble uses ML-Agents, so none of this section
  applies to it.
- **Before training, ask the user for a skinned mesh.** Take the rig structure from that model
  and import it into MuJoCo or Newton to train against.
- **Compile MuJoCo for Android phones from <https://github.com/joanllobera/mujoco-bin/>.**
- **When training in MuJoCo or Isaac Lab, show that app's UI** so the creature's movement can be
  watched during and after training. Use Newton for the view instead where it is the better option.

## Simulation fidelity

- **Creatures move realistically under Earth gravity,** with realistic joint movement and mass
  for their size.
- **When the trained agent is a human, joints move at a human speed and force.**
- **Every body part of every creature collides accurately** with every other creature's and with
  the environment. Nothing passes through anything.
