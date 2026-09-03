# Fonts

Two families, both SIL Open Font License 1.1, vendored from github.com/google/fonts.

| File | Role |
|---|---|
| `Anton-Regular.ttf` | Display only — the result banner, the countdown, the big survivor count. A single very heavy condensed weight; it is the fight-poster voice and should never be used at label sizes |
| `BarlowCondensed-Medium.ttf` | The HUD's default. Condensed so long fighter names fit a narrow tile without ellipsis |
| `BarlowCondensed-SemiBold.ttf` | Emphasis — health numbers, the fighter name on a roster tile |
| `BarlowCondensed-Bold.ttf` | `-unity-font-style: bold` |
| `SpaceMono-Regular.ttf` | The F3 diagnostics overlay, and nothing else |

The overlay gets a monospace for one reason: its numbers refresh four times a second, and
in a proportional face the columns shift on every redraw as digit widths change. A readout
that jitters is a readout nobody reads. Space Mono is also the only family here whose figures
are guaranteed tabular.

Condensed faces are not a stylistic whim here: the build ships portrait on a phone
(1080x1920 panel reference), and a normal-width face at `--text-lg` overruns the
per-fighter health rows and the roster tiles well before the names get long.

UI Toolkit renders through a **FontAsset**, not the raw `.ttf`. The `.asset` files beside
these are SDF atlases generated from them; the `.ttf` is the source and is what the
licence covers. Regenerate with `Temp/evals/make_fontassets.cs` if a weight is added.

`OFL-*.txt` must ship with any build that embeds these — that is the whole of what the
licence asks, and it is why they live here rather than in the repo root.
