# Presentation, Camera, Telemetry, Audio and Commentary

Everything that is derived state rather than simulation. Delete all of it and the ring behaves identically - which is what makes it safe to change against a policy calibrated on that ring.

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
| `FightStats` | `FightStatsHudView` | The telemetry board — thrown/landed/connect/blocked/slips/damage for the pair the director is watching, plus a momentum bar and sparkline |
| `CameraRig` | `CameraDirectorView` | Owns `ImpactCam` and hands it the frame on a knockout or a landed haymaker |
| `Commentary` | `CommentaryView` | Speaks the baked commentary and prints the subtitle |
| `MainMenu` | `MainMenuView` | The title screen — owns the `Title` phase outright |
| `Crowd` | `CrowdAmbienceView` | The crowd bed and its reaction swells |

## The Camera Director

`DirectorSystem` picks a pair and a shot; two views act on the decision and neither makes one
of its own. `TensionMath.ScorePair` is the rule, and it is pure and static for the same reason
`CombatMath` and `ThreatMath` are — the camera and its tests must agree on where the fight is.

- **Proximity scales the whole score rather than merely contributing to it.** Without that, a
  dying fighter alone across the ring outscores a healthy pair in close, and the camera frames
  the most hurt boxer plus whoever happens to be nearest them — with the ring in between.
  `TensionMathTests.ADyingFighterAloneAcrossTheRingLosesToAHealthyPairInClose` pins it.
- **Three things make it a director rather than a twitch**, and all three are load-bearing: a
  minimum shot length (1.6s), hysteresis on the pair (tension moves several times a second
  across forty-five pairs, so the best one by a hair changes constantly), and a hard
  preemption for impacts — a knockout the camera reaches a second and a half late is one it
  missed.
- **`Wide`, `Tracking` and `Duel` are the same camera at different padding; only `Impact` is a
  second camera.** The shot scales `SpectatorCameraView._framingPadding` rather than writing
  an orthographic size, so every clamp above it still applies — a tight shot on two fighters
  in a corner still cannot point the camera out of the ring, and the portrait/landscape
  ring-fit rule does not have to be repeated.
- **The brain's default blend is `Cut`, and the two cameras' priorities must not tie.**
  `SpectatorCam` is 10, `ImpactCam` rests at 0 and is raised to 30. Both shipped at the
  default 0 at first, which makes the choice a tie resolved by activation order — and strands
  the cut camera live after the first knockout of the session.
- **The human seat outranks the director.** `SpectatorCameraView` frames the human
  unconditionally when there is one; you should never have to hunt the ring for yourself, and
  no amount of drama elsewhere outranks that. The shipped Android build sets `_humanBoxerId`
  to -1, so there the director always wins.
- **Both new systems are ticked from `MatchDirector.Tick` on unscaled time, and skipped
  entirely in training.** Scaled time would stretch an impact cut by exactly the factor the
  knockout hold just applied — the same trap the flow loop already documents — and a training
  scene has no camera to direct and no board to fill in, so the tension score's forty-five
  pairs a frame would be bought for no return.

## The Telemetry Board

- **`PunchThrownMessage` exists solely so the connect rate is not a tautology.** Every other
  punch message reports something the punch ran into, so a rate computed over landed,
  blocked and evaded counts only punches that reached somebody and reports something close to
  100%. `FightStatsTests.ConnectRateIsLandedOverThrownRatherThanOverPunchesThatReachedSomebody`
  pins it.
- **The board reports the director's pair, not a fixed pair and not the whole field.** Ten
  rows of punch counts is a spreadsheet; two columns either side of one label is a stat bar.
  It also means the figures always belong to what is on screen.
- **`FightStatsModel` is sized lazily from `Tick`, not only from the phase subscription.**
  `MatchModel` starts in `InProgress`, so subscribing fires immediately — before `SpawnSystem`
  has put a single boxer in the ring — and sizes every table to zero. Every tally then
  silently goes nowhere and the board draws a blank column for the whole match, which is
  exactly how it first ran.
- **The sparkline is `Painter2D`, and that is a decision rather than an omission.** Every
  third-party 2D charting package for Unity is built on UGUI; this HUD is UI Toolkit
  throughout, so pulling one in would mean a second canvas, a second event system and a second
  set of scaling rules over the same screen, to draw forty-eight line segments.
- **The momentum bar is driven by the *difference* between the pair.** Two fighters both being
  battered by the rest of the ring would otherwise fill the bar from both ends and read as a
  furious exchange between the two of them, which is the opposite of what happened.
- **Neither momentum fill carries a transition**, for the reason the stylesheet already gives
  for stamina and charge: easing a value recomputed every tick just renders it permanently
  behind the model reporting it.

## Audio
`Assets/Audio/PoRumbleMixer.mixer` routes **Master → SFX / UI / Ambience / Commentary**, and now actually
processes rather than merely routing. SFX carries a pre-fader compressor (punches are short,
loud and constantly overlapping, and without it a flurry just clips against itself) and a
post-fader `SFX Reverb` tuned as a hall rather than a cathedral. Master carries a glue
compressor and a `Lowpass` that sits wide open — it exists only so the `Knockout` snapshot has
something to close, and that muffled drop is the clearest audio cue that a match just ended.

**The commentator has his own group, and it carries +10dB.** He was routed to `UI` alongside
the bell and the countdown beeps, with his source already at 0.9 of a maximum of 1.0 - so there
was no headroom left at the source and the only remaining lever was the mixer. Boosting `UI`
would have dragged the bell up with him. `Master -> Commentary` exists so the gain lands on the
voice alone, and it is the one number to turn if he still needs to be louder: the group volume,
written into **both** snapshots because a snapshot stores an absolute value per parameter and
inherits nothing. The crowd also ducks harder while he speaks (`_duckLevel` 0.15, from 0.30),
which buys more separation than raw level does - he is one voice against a broadband bed, and
level alone just makes both louder.

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

## Commentary

`CommentarySystem` watches the message bus and decides what a commentator would say;
`CommentaryView` speaks it and prints a subtitle. Like the board and the director it is pure
observation — it mutates no boxer and publishes nothing.

- **The voice is baked, not synthesised at runtime.** Piper is a native binary plus a 63MB
  ONNX voice, and the obvious pure-C# alternative (`System.Speech`) does not exist outside
  Windows and Mono — so neither could ship in an ARM64 IL2CPP build.
  `Tools/bake_commentary.sh` turns `Tools/commentary_lines.json` into 52 WAVs that cost
  nothing at runtime and behave identically on a phone. **This is the project's first audio
  asset**; everything else is still `ProceduralSfx`.
- **`Tools/commentary_lines.json` is the single source of truth.** The bake script turns it
  into clips and `Temp/evals/build_commentary_bank.cs` turns the same file into
  `CommentaryBank.asset`, pairing each clip with its subtitle. Edit the text without
  rebuilding the bank and the printed line says something the voice does not — which is worse
  than having no subtitle.
- **A line is up to two clips, joined on the DSP clock.** Baking every name into every line
  would be eight contestants times forty-four lines; concatenating a name clip with a body
  clip keeps the bank at 52. The join uses `AudioSource.PlayScheduled`, not a frame-timed
  wait: a frame-timed join lands a whole frame late at 60fps and later still under hitstop,
  which is an audible gap in the middle of a sentence and the one artefact that gives away
  that the two halves were recorded apart.
- **Bodies for named lines are phrased to follow a name** — "is in real trouble here", not
  "he is in real trouble here" — and still read as a sentence about "he" when the name is
  missing. That is not hypothetical: a scene with no fight card has no names, so every
  training arena takes that path.
- **Almost all of the logic is about when *not* to speak.** A ten-way publishes several landed
  punches a second. A priority (a knockout cuts across a flurry, never the reverse), a hold so
  a started line finishes, and a cooldown after it. Lines are dropped rather than queued: by
  the time a backed-up flurry line reached the front, the flurry would be ten seconds gone.
- **"He's in trouble" is latched per fighter per match.** Health only falls inside a match, so
  an unlatched call re-fires on every subsequent punch — the fastest way to make a commentator
  sound broken. `CommentaryTests.AFighterIsOnlyCalledHurtOnce` pins it.
- **`CommentaryCue` carries a sequence number, and it is load-bearing.** `ReactiveProperty`
  compares with `EqualityComparer<T>.Default` before notifying, so two identical cues in a row
  — the same fighter hurt in two successive matches — would be silently swallowed without it.
  The struct also implements `IEquatable` for cost, not correctness: the default comparer for
  a struct holding a reference field compares by reflection and boxes to do it.
- **The voice licence is the one that matters, not the tool's.** Piper and espeak-ng are GPL
  and are never shipped — running a program to generate data does not make the data a
  derivative of it. The *voice model's training dataset* does reach the output:
  `en_GB-northern_english_male-medium` is CC BY-SA 4.0, which permits commercial use with
  attribution, and `Assets/Audio/Commentary/ATTRIBUTION.md` carries it exactly as
  `Assets/Art/Fonts/` carries its OFL licences. The obvious alternatives were rejected for
  this reason: `en_US-ryan-high` is CC BY-**NC**-SA (non-commercial) and `en_US-lessac-medium`
  carries the Blizzard Challenge licence. **Check a replacement voice's `MODEL_CARD` for the
  dataset licence, never the repository licence** — that is MIT for every voice in the
  collection and says nothing about the recordings underneath.
- **`Tools/piper/` is gitignored.** An 85MB build dependency does not belong in a game repo's
  history; the bake script fetches it on demand.

**The `Ambience` group was routed from the day the mixer was built and nothing ever played into
it**, so between punches the ring was silent for the life of the project — which is most of what
made the synthesised one-shots sound synthesised. `CrowdAmbienceView` fills it with two
non-positional voices: a seamless bed whose level and pitch track how heated the fight is, and a
reaction swell fired on a knockout, a heavy landed punch or a committed haymaker. Non-positional
is the point — a crowd surrounds the listener, and giving it a position would seat the whole
audience in one chair and swing the room from side to side as the spectator camera tracked.

**It is mixed far quieter than it first shipped, and the reason is the commentator.** The bed is
broadband noise and he is one voice; at the levels this was first tuned to, he was simply
inaudible. Three things were wrong at once and all three had to be fixed:

1. **The bed's top band was hiss.** A real crowd heard from inside it is mostly chest and vowel,
   and an arena absorbs the air above that first. The `high - mid` term went from 0.5 to 0.08.
2. **Excitement saturated instantly.** `FighterStats.Momentum` accumulates *raw damage* and
   decays — measured live it sits around 2 and peaks near 14 — so clamping it straight to 0..1
   pinned the bed at peak seconds after the bell and held it there all match. It is now divided
   by `_momentumForPeak` first, which gives the range back.
3. **Nothing ducked.** The crowd now steps back to `_duckLevel` while a line is playing, fast in
   and slow out so the room does not pump between lines.

**`_duckSeconds` must stay below `ASSUMED_LINE_SECONDS + COOLDOWN_SECONDS` (2.55s), and matches
`ASSUMED_LINE_SECONDS` exactly at 1.9.** At 2.6 it was longer than the minimum gap between line
starts, so a busy ten-way re-armed the duck before it could release and the crowd sat permanently
ducked — a level cut wearing a ducker's clothes. The duck has to cover the line and stop, not the
line plus the cooldown after it.

**Changing a `[SerializeField]` default does not retune an object that already exists.** The
`Crowd` and `CombatFeedback` components were authored before this pass, so they kept the old
values and the retune silently did nothing — the bed measured 0.271 while the code said 0.086.
The serialized values had to be written into the scene as well. This applies to every tuning
change to an existing scene object, and it fails quietly every time.

Two further details are load-bearing. The bed is built through `ProceduralSfx.BuildLoop`, not
`Build`:
the ordinary builder fades both ends to zero so one-shots do not click, and a bed built that way
drops to silence on every wrap — a pulse rather than a loop. `BuildLoop` crossfades the clip's
own tail back over its head with **equal-power** gains and discards the tail; linear gains dip
two uncorrelated noise signals by about 3dB through the middle, which is an audible dropout once
per loop. Anything periodic in the shape must therefore complete a whole number of cycles over
the full duration, or the two ends meet at different points of it. And excitement is the *max* of
two independent measures rather than their average: the field thinning is a slow build that holds
when nothing is happening this second, and momentum alone would drop the room to nothing between
exchanges in a final that should never go quiet.

**Footsteps, breath and rope contact go through the same spatial pool as the punches.** Only
punches were positioned before, which left footwork — the thing a fighter does constantly —
completely silent. Three things keep it affordable. Steps run on a much slower cadence than the
foot dust (a puff costs a particle; a step costs one of fourteen voices shared with every punch
in the ring). All three are gated on distance to the **listener**, so ten fighters shuffling
cannot steal the pool for sounds the distance rolloff has already faded out. And the rope thud
carries a per-fighter cooldown that is not polish: `BoxerSystem` clamps positions to the ring, so
a fighter held in a corner is *at* the boundary every single frame and without it the sound
machine-guns for as long as they lean. Contact is judged from the model's position against
`MatchModel.ArenaHalfExtent` and the sign of the velocity — moving *into* the ropes, not merely
near them — because that is where the containment actually happens; the wall colliders hold
nobody, so a physics callback would miss the clamp entirely.


## The sound palette is deliberately two sounds

**Footsteps and landed punches. That is the whole of it**, plus the commentator. Everything
else that used to make a noise has been cut: the crowd bed and its reaction swells, blocks,
evades, the slip whoosh, the haymaker wind-up, the knockout, breath, the rope thud, the bell
and the countdown beeps.

The bed was the reason. It is three filtered bands of noise by construction, playing
continuously under everything, and it is what a listener hears as white noise - no amount of
mixing makes a broadband bed disappear behind one voice, because it occupies the same spectrum
the voice does. The `Crowd` object is **deactivated rather than deleted**: `GameLifetimeScope`
finds optional views with `FindObjectsInactive.Include`, so injection still succeeds, and the
room comes back with one checkbox.

The rest went with it because a two-sound palette only reads as deliberate if it is actually
two sounds. A block, an evade and a whiff firing several times a second across ten fighters is
another wash, and it was competing with the two events that carry information: where the feet
are and whether a punch connected.

**What was cut is audio only.** Blocks still burst particles and flash a light, the haymaker
still draws its speed lines, a knockout still flares and shakes the camera, and the ropes still
throw dust. The visual feedback layer is untouched - only the calls into the voice pool are
gone, along with the clip banks that fed them.

**The bell is the one most likely to be missed**, and it is the first thing to restore if the
match start needs marking: it was a `PlayFlat` on a non-positional source routed to the `UI`
group, and both went with it.
**Audio is otherwise synthesised at runtime** in `ProceduralSfx` — the rest of the project has no audio assets, and
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

**The USS type ramp and spacing scale are sized for the 1080x1920 portrait reference,** which is
what the panel actually uses with match-width scaling - so a USS pixel here is a device pixel on
a 1080-wide phone. They were first authored against a landscape canvas and topped out at 34px for
everything but the result banner, roughly 3% of a phone's screen width: legible on a monitor and
not on the thing this ships to. The whole match panel measured 222x447 of a 1080x1920 screen
before the rescale and 496x596 after.

**Every panel is its own `UIDocument`, so USS declares the grid and each panel opts into a
slot.** They cannot be flex siblings — there is no shared parent to lay them out in — so each is
absolutely positioned against its own root, and for a long time nothing stopped two of them
claiming the same rectangle. On the 1080-wide portrait reference several did: the diagnostics
sheet overlapped the match panel by about 94px, and the player's own panel overlapped the
standings by about 74. Only `_humanBoxerId: -1` hid the second one in the shipping build, since
`PlayerStatusHudView` then builds nothing at all.

The `--band-*` and `--col` tokens in `:root` are the fix. Two columns of `--col` with
`--band-gap` between them come to exactly 100%, so no pair of panels sharing a band can overlap
however long their content gets; `--band-top-max` caps how far the top band may grow, which is
what keeps the middle of the screen clear for the ring. The bottom stack is three tokens read
bottom-up: the card button on the floor, the player's panel above it, the commentary caption
above that. `.commentary-band` used to be a bare `bottom: 430px` measured against the panel
heights of the day and silently wrong the moment any of them moved.

Two consequences worth knowing. **Panel-internal widths had to become flexible with it** — a
fixed 190px name beside a fixed 300px bar can exceed a panel that is now a percentage of the
screen, and a `Label` overflows rather than shrinking, so `.match-hud__name` and
`.player-hud__bar` are `flex-basis` shares. And **the standings moved from bottom-right to the
right column's second row**, which is what actually removed the bottom-band conflict rather than
papering over it.

**The fight card's grid fits three tiles per row, not two.** At `max-width: 700px` only two fit,
which turned eight contestants into four rows about 1530px tall — with the title and footer on
top, the card ran off the bottom of a phone. It survived at exactly eight and would have
overflowed silently at nine.

**`MainMenuView` owns `MatchFlowPhase.Title` outright.** The phase and the loop that returns to
it already existed; what it had was a caption and a line of instruction text drawn onto the match
HUD's centre stage, naming a key that does not exist on a phone. `MatchHudView`'s `Title` branch
is now deliberately blank, and `RosterSelectionView` hides its floating `#open-card` button on
that phase specifically — the menu carries its own, and with both live the screen showed two
FIGHT CARD buttons. The results phase still needs the floating one, because no menu is up then.

**Owning the phase means the other panels have to be told.** `MatchHudView`'s branch going blank
was only half of it: the match panel itself, the tale of the tape and the standings all carried
on drawing through the menu, so the title screen showed a survivor count of ten at full health
before a punch, an all-zero stat board, and a league table bleeding through the FIGHT button.
`StandingsHudView` had no reference to `MatchFlowPhase` at all. All three are now gated on the
phase, and all three hide by **opacity rather than `display`** — a panel with no resolved layout
is one `SafeAreaView`'s retry pass waits on for ever. `FightStatsHudView` needs the phase in
*addition* to its existing pair check, because the director keeps a pair between matches.

**The centre stage is bounded by the band grid, not by the whole screen.** It spanned top 0 to
bottom 0 and centred its content at 50%, which drove the result banner — the largest text the
game ever shows — straight through the standings panel starting at `--band-second-row`. Both are
up on every results screen, so that was not an edge case, it was the results screen.
`--band-centre-top` / `--band-centre-bottom` bound it between the second row of panels and the
bottom stack, which makes the clearance structural like the rest of the grid rather than measured
against whatever the panels happened to be that day.

**`SafeAreaView` insets every panel, and nothing did before.** The survivor count sat 20px from
the top of a 1920-tall screen, underneath the status bar on any phone that has one. It writes
padding on each document's *root* rather than margins on the panels: the HUD anchors its panels
absolutely, an absolutely positioned child resolves against its parent's padding box, so one
write moves every corner-anchored panel at once. Two things it has to get right.
`Screen.safeArea` can be larger than `Screen.width/height` - in the Editor it reports the whole
display while `Screen` reports the Game view - so the fractions are clamped to [0, 0.5] or the
insets come out negative and silently lose the base inset too. And it must keep retrying until
every panel has resolved a layout: an early pass that skipped them all while caching the current
screen size never runs again, which is exactly how it first shipped applying nothing at all.

**The result banner lives in the centre stage, not in the match panel.** It sat in the panel
alongside the ten health rows and drew straight across them - a winner's name is the largest text
the game ever shows and that panel is the densest thing on screen. The centre stage exists
precisely so text that changes length every second cannot disturb the panel's layout.

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

**The overlay is a full-width sheet with two tabs, not a corner box.** At a 620px minimum
anchored top-right it overlapped the match panel, on the one screen where a diagnostics readout
most needs to be legible, and its rows are wide runs of tabular figures a corner box wrapped
anyway. It is also the one panel that is *fully* opaque: a developer reading frame times is not
also watching the fight, and at 0.92 the match panel's labels still read through and the two sets
of text interleaved.

The two tabs answer different questions and do not share a column. **FRAME** is the renderer and
the allocator — what the performance rules budget. **COMBAT** is the simulation: agent count,
Academy steps and steps/sec, whole-field punch tallies from `FightStatsModel`, and the director's
current pair, shot and tension. Three things there are easy to get wrong. The decision rate is
sampled on **both** tabs, because a ring buffer that only advanced while its own tab was up shows
a flat line for however long you were reading the other one — which looks exactly like a stalled
policy. It has its **own write head**, since the frame history is written every frame and this
once per refresh. And the agent count excludes inactive objects, unlike every other scene search
in this project: the boxers are clones of `Boxer_Template`, which stays in the hierarchy switched
off, and counting it reported eleven agents in a ten-boxer ring. `Academy.IsInitialized` is
checked before `Academy.Instance`, because touching the instance *constructs* an Academy as a side
effect of looking for one.

The overlay reports **p95 frame time alongside the mean and the peak**, because the three answer
different questions: a single 90ms frame in a 120-frame window moves a 16ms average by under a
millisecond, the peak catches that frame but cannot tell a one-off domain reload from a stutter
happening several times a second, and p95 is the one that says how bad it *regularly* gets. The
percentile sorts into a pre-allocated scratch array — an overlay that reports allocation rate
must not allocate to do it. It also reports texture memory and count (the number that moves when
art changes, and this project just took on a normal map per sprite and an SDF atlas per font
weight) and counts `Light2D` and `ShadowCaster2D` directly at `Start`, since neither set changes
during a session and Unity's own shadow counter reads zero for 2D casters.

