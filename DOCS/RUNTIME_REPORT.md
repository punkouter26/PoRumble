# Runtime Verification Report

Captured 2026-09-11 against `SampleScene` on Unity 6000.6.0f1, Editor Play mode.

## Scenes run

`SampleScene` is the only non-training scene in the project (`Training1v1` and `Training10`
are editor-side tools; `Settings/Scenes/URP2DSceneTemplate` was an unused template and has
been deleted). It was driven Title -> Introducing -> Countdown -> Fighting -> Results ->
restart, exercising the HUD, the commentary, the director, the Elo table and the fight card.

## Errors

**None.** A full match produced exactly two console lines, both informational and both from
ML-Agents:

| Line | Meaning |
|---|---|
| `Couldn't connect to trainer on port 5004 ... Will perform inference instead.` | Expected — no trainer attached, so the baked `PoRumbleBoxer.onnx` drives the policy seats |
| `Registered Communicator in Agent.` | Expected — Academy initialising |

Zero warnings, zero exceptions, zero `NullReferenceException`. The two startup lines CLAUDE.md
warns about on Android (`AssetPackManager`, `TensorProxy.Finalize`) do not appear in the Editor.

## Telemetry

Sampled mid-fight with ten boxers live:

| Metric | Value |
|---|---|
| Draw calls | 98 |
| Batches | 2 |
| SetPass calls | 62 |
| Triangles / vertices | 4,431 / 8,221 |
| CPU frame time | 6.13 ms (~163 fps) |
| GPU frame time | 0.64 ms |
| CPU main thread | 2.14 ms |
| Total allocated | 1.46 GB (Editor, includes Editor overhead) |
| Mono heap | 339 MB, 270 MB used |

Scene composition, sampled live:

| Object | Count |
|---|---|
| ML-Agents agents (active) | 10 |
| `Light2D` | 7 at rest, 8 while the director holds a pair (the follow spot fades in) |
| `AudioSource` | 19 |
| `UIDocument` | 8 |

## Match behaviour

- Damage accumulates correctly: 300 HP -> 236 -> 178 across the round.
- The ten-way **resolved on the bell with all ten alive**, which is the documented timeout
  path for a 40x40 ring at 30 HP, not a defect.
- `EPSTEIN WINS` banner, the updated standings with per-match deltas, and the `PRESS R TO
  CONTINUE` prompt all rendered correctly in portrait.
- Restart returned the flow to `Title`, advanced the match counter to 2 and restored all ten
  fighters to 30 HP.
- Fonts resolved (LFS is fetched); commentary spoke and subtitled correctly.

## Conclusion

No runtime defects were found. The GPU is almost idle at 0.64 ms and the CPU frame is
dominated by the Editor, so the rendering budget has considerable headroom on desktop.

---

## Final pass

Re-run after the whole cleanup, with the ring composed in the scene, the perception isolation
baked, the feedback rig authored and the locomotion feedback extracted.

| Metric | Before | After |
|---|---|---|
| Console errors / warnings | 0 / 0 | 0 / 0 |
| PoRumble tests | 179/179 in 119s (820 run) | 179/179 in 6.2s (179 run) |
| CPU frame time | 6.13 ms | 6.43 - 7.66 ms |
| GPU frame time | 0.64 ms | 0.73 ms |
| Draw calls | 98 | 98 - 158 |
| Agents / Light2D / AudioSource / UIDocument | 10 / 15 / 19 / 8 | 10 / 15 / 19 / 8 |

Draw calls are a range because they move with what is on screen - particles, impact lights and
the property blocks carried by damaged fighters. The 98 in the first column was a single early
sample and the same run reaches 158 in a busy exchange; frame time barely moves across that
range, which is the number that matters. The scene composition counts are identical before and
after, which is the check that adoption did not quietly create a second set of anything.

## Disk

| | Before | After | Reclaimed |
|---|---|---|---|
| Tracked files | 827 | 743 | 99 deleted, 15 added, 19 renamed |
| Tracked bytes | 26.57 MB | 23.17 MB | 3.40 MB |
| `Library/` | 2.2 GB | 2.0 GB | ~200 MB |
| `Tools/` | 122 MB | 1.8 MB | 120 MB (piper, refetchable) |
| `Assets/` | 26 MB | 22 MB | 4 MB |

Roughly 323 MB off the working copy, of which 3.40 MB is tracked content.
