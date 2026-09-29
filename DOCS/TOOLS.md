# Add-ons and External Tools

The free packages and tools installed for the broadcast layer, what each is for, and where
the ones that live outside the project are. Referenced from CLAUDE.md's doc index.

## Unity packages (in `Packages/manifest.json`)

| Package | Version | Source | For |
|---|---|---|---|
| `com.unity.timeline` | 6.6.0 | Unity (built in) | Scripted sequences: knockout replays, walkout intros. Was already present as an indirect dependency; now declared directly |
| `com.unity.recorder` | 5.1.7 | Unity registry | Exporting clips and trailers to MP4/GIF. **Editor only** - a build clips through OBS instead |
| `com.unity.ai.inference` | 2.6.1 | Unity registry | Declared directly because `PoRumble.Models` and `PoRumble.Views` now reference it (checkpoint fighters). Pinned to the version ML-Agents 4.1 already resolves - do not bump it apart from ML-Agents |
| `com.unity.services.leaderboards` | 2.3.4 | Unity registry | A global league table or prediction board. Needs the project linked to a Unity Cloud project before any call succeeds |
| `com.unity.services.cloudsave` | 3.4.1 | Unity registry | Syncing the viewer's bank and ratings across devices. Same linking requirement |
| `com.annulusgames.lit-motion` | 2.0.2 | OpenUPM (`com.annulusgames` scope) | Allocation-free tweens with UniTask integration, for HUD motion |

- **LitMotion is not signed by Unity,** so on first install Unity 6 shows a *Missing Signature*
  dialog and blocks the Editor until someone answers it. That is expected for every OpenUPM
  package - VContainer and the Cysharp packages are in the same position.
- **Installed is not wired.** None of the five new packages is used by project code yet;
  each is listed with the feature it is for so the next change can pick it up.

## Assets

| Folder | Pack | Licence |
|---|---|---|
| `Assets/Art/ThirdParty/Kenney/ParticlePack/` | Kenney Particle Pack 1.1, transparent PNGs only (80) | CC0 |
| `Assets/Audio/ThirdParty/Kenney/ImpactSounds/` | Kenney Impact Sounds - the 115 files not already curated into `Assets/Audio/Sfx/` | CC0 |
| `Assets/Audio/ThirdParty/Kenney/InterfaceSounds/` | Kenney Interface Sounds (100) | CC0 |

Each folder carries the pack's own `License.txt`. These are a library to draw from, not
assets in use: a particle sprite that ends up on screen still has to go into a sprite atlas
(the performance rules make atlases mandatory for 2D), and its import settings reviewed.

## Outside the project

### Piper TTS - `C:\Users\punko\Tools\piper`

Offline neural text-to-speech, for baking commentary lines that name fighters. The engine is
the last standalone Windows build of `rhasspy/piper` (2023.11.14-2, MIT; the repository is
archived and the maintained fork is Python-only). The voice is **`en_US-joe-medium`, trained on
a CC0 dataset** - chosen over `ryan`, which is CC BY-NC-SA and so unusable in anything sold.

```powershell
$p = "C:\Users\punko\Tools\piper"
"BIGGIE IS DOWN!" | & "$p\piper\piper.exe" --model "$p\voices\en_US-joe-medium.onnx" --output_file biggie_down.wav
```

It renders about fourteen times faster than real time on this machine. Bake to `.wav`, then add
the clip to `CommentaryBank.asset` like the existing lines - commentary stays baked, so the
game never ships the model.

### OBS Studio - not installed

`winget install --id OBSProject.OBSStudio -e` downloads and verifies the installer, but the
install needs a Windows elevation prompt that a non-interactive shell cannot answer. Run it
from your own terminal and accept the UAC prompt. obs-websocket is built into OBS 28 and later
(Tools -> WebSocket Server Settings); it is how a build would save the replay buffer on a
knockout, since Unity Recorder only exists in the Editor.

### Not set up

- **Twitch chat** - declined for now. The plan is an in-project IRC-over-WebSocket client
  rather than TwitchLib.Unity, which ships stale DLLs.
- **Sonniss GDC audio bundle** - tens of gigabytes behind a manual download page; fetch the
  pieces you want by hand.
