# Android

The portrait phone build: player settings, the camera framing rules that orientation forces, touch input, and the things that only fail on a phone.

## Android

Ships as a portrait phone build. `SampleScene` is the only scene in the build list; the two
training arenas are editor-side tools.

| Setting | Value | Why |
|---|---|---|
| Backend | IL2CPP, ARM64 only | Not a preference. Modern devices are arm64-v8a and Mono cannot target 64-bit ARM at all |
| Orientation | Portrait, autorotate off | |
| Graphics | Vulkan, then GLES3 | The 2D renderer does a lot of render-texture work for the lights; GLES3 is the slower path |
| Package | `com.punkouter.porumble` | |
| Min SDK | 24 | |
| Panel reference | 1080x1920, match width | The HUD was authored against a landscape canvas; in portrait the reference has to be portrait too |

`adb` ships with the Editor rather than on PATH, under
`Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/`. `Temp/deploy_android.sh`
installs, launches and dumps the Unity log in one step.

### Things that are easy to get wrong

- **The camera watches one exchange, not the whole field.** Fitting the bounding box of every
  living fighter works only for the last two: with ten boxers scattered over a 40x40 ring the
  box *is* the ring, so the camera sat at its widest for most of a match and the fighters were
  a few pixels tall. `SpectatorCameraView` now picks a focus - the human if there is one, else
  the living boxer on the least health, since that is where the next elimination is coming from
  - and frames that fighter, their nearest opponent, and anyone inside `_focusRadius`. Typical
  orthographic size went from ~24 to ~9.
  Two details make it usable rather than nauseating. The focus is **sticky**: health changes
  several times a second across ten fighters, so re-picking the lowest every frame swings the
  camera across the ring on almost every landed punch - it is only given up when the current
  focus dies or a rival is `_focusSwitchMargin` HP worse off. And the nearest opponent is kept
  in frame regardless of the radius, because a focus fighter alone in shot is not a fight.
- **Position is clamped to the ropes, not to `_outsideRingMargin`.** That margin exists so the
  corner posts and stools stay visible when the camera is pulled out far enough to show the
  whole ring. Letting the *position* clamp use it too meant that at a focused zoom the same
  four units became a quarter of the screen of empty backdrop. When the view is wider than the
  ring, `ClampToRing` centres on that axis and the dressing is visible anyway.
- **The camera framing rule is orientation-dependent, and has to be.** The ring is square and
  no screen is. Landscape crops to fill: the camera pulls out only until the view is as wide
  as the ring, so the fighters stay large and the camera pans over the ring's height. Portrait
  letterboxes instead - cropping a 0.56 aspect to fill shows barely half the ring's width, so
  most of a ten-way brawl would be off-screen while the HUD still claimed ten were alive.
  `SpectatorCameraView.ClampToRing` then keeps the view inside the ropes on whichever axis the
  ring is larger, and centres it on the axis where it is not.
- **The camera's minimum zoom is orientation-dependent too, for the same reason as the maximum.**
  Orthographic size is half-*height*, so one minimum means two very different framings: at 9 a
  16:9 screen shows 32 world units across and a 9:16 phone shows 10. The ring is 40 across, so
  the landscape number was doing its job while the same number in portrait produced a tall slot
  with a duel in the middle and most of the frame empty above and below it.
  `_portraitMinOrthographicSize` is 6, because on a phone the binding dimension is width - and
  it is quoted **at the 1080x1920 reference and scaled by aspect from there**, so the tightest
  shot frames the same *width* on every phone. A flat number does not: 6 shows 6.75 world units
  across at 9:16 and 4.3 at 0.36, which is narrower than two fighters at punching range.
- **`_maxOrthographicSize` is a backstop, and the ring-fit floor is what stops it cropping.** It
  is 45, deliberately larger than any landscape screen needs, so the ring-fit rule is what
  actually binds. But a flat maximum cannot fit a ring whose required size grows as `1/aspect`,
  and on a tall enough phone the backstop bound *first* and cut the fighters off the sides -
  measured at aspect 0.36, half-width 16.25 against a ring half-width of 20, while the HUD went
  on counting ten alive. `FramingMath` therefore raises the portrait cap to at least
  `arenaHalfExtent.x / aspect`: losing the dressing margin is a blemish, losing the fighters is
  a broken build. The floor is why 45 can stay; set it back down to ~21 and the floor now
  carries portrait anyway.
- **The framing rule lives in `FramingMath`, not in `LateUpdate`.** Pure and static for the same
  reason `CombatMath` and `TensionMath` are. Both defects above shipped precisely because the
  rule could only be checked by running the game and measuring the camera; `FramingMathTests`
  pins them at four real phone aspects now.
- **The build ships as an all-AI exhibition.** `BoxerSpawnPoints._humanBoxerId` is -1, so
  every boxer is driven by a brain profile or the trained policy and no human UI is built at
  all: `PlayerStatusHudView` and `TouchControlsView` both check for a human boxer and construct
  nothing without one. Match-level input still works - a tap anywhere restarts at the results
  screen, a three-finger tap toggles the diagnostics overlay.
- **Touch controls exist in code but are not in the scene.** `TouchControlsView` renders a
  floating stick plus punch and haymaker buttons, and writes `TouchInputModel`, which
  `BoxerAgentView.Heuristic` reads in the same place it reads the keyboard - so a phone and a
  desk drive the boxer down one identical path. To switch a human back on: set
  `_humanBoxerId` to 0 and add a `TouchControls` GameObject with a `UIDocument`
  (HudPanelSettings, sorting order 5) and a `TouchControlsView` pointed at `porumble.uss`.
  The stick feeds move and aim together; there is no second stick, and a boxer that walks one
  way while facing another cannot land anything through the face arc anyway.
- **`MatchHudView` picks its prompts from the devices present,** not from a platform define, so
  the editor still reads "PRESS" while a phone reads "TAP" - and a desktop that happens to have
  a touchscreen is not told to tap when it has a keyboard sitting right there.
- **The fight card needs a button, because `Tab` does not exist on a phone.** For a long time
  `RosterToggleRequested` read the Tab key and nothing else, which meant the entire
  contestant-selection screen could not be opened in the shipping build - and could not have been
  closed if it had been. The `#open-card` button in `RosterCard.uxml` is a sibling of the panel
  so it survives the panel being hidden, and `RosterSelectionView` shows it only while
  `MatchFlowModel.CanOpenCard` is true. A two-finger tap is the shortcut for anyone who finds it.
  Both are gated between matches: re-seating the roster mid-fight would swap contestants into
  chairs that are currently mid-punch.
- **Two startup log lines are expected and harmless.** `ClassNotFoundException:
  AssetPackManager` is Unity looking for Play Asset Delivery, which a sideloaded APK does not
  use. A burst of `NullReferenceException` in `TensorProxy.Finalize` fires once as the first
  GC collects the inference tensors allocated while loading `PoRumbleBoxer.onnx`; it does not
  recur, and inference works.

