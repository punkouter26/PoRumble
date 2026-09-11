# Commentary audio — attribution and licence

Every `.wav` in this folder is synthesised speech, generated offline by
`Tools/bake_commentary.sh` from the script in `Tools/commentary_lines.json`. There is no
recorded human performance here and no third-party audio has been copied into the project.

## Voice

**`en_GB-northern_english_male-medium`**, from the Piper voice collection.

| | |
|---|---|
| Voice model | <https://huggingface.co/rhasspy/piper-voices> |
| Model licence | MIT |
| **Training dataset licence** | **CC BY-SA 4.0** |
| Dataset | Northern English Male, via the Piper voice collection |

**The dataset licence is the one that matters**, and it is the reason this voice was chosen
over the more obvious candidates. `en_US-ryan-high` is trained on RyanSpeech, which is
CC BY-**NC**-SA 4.0 — non-commercial, and therefore unusable in anything that ships.
`en_US-lessac-medium` carries the Blizzard Challenge 2013 licence, which is research-oriented
and would need reading before a release. CC BY-SA 4.0 permits commercial use, and asks for
attribution and share-alike in return — which is what this file is for, and why it is vendored
here exactly as `Assets/Art/Fonts/` vendors its OFL licences beside the fonts.

## Tool

**Piper** (<https://github.com/rhasspy/piper>), with **espeak-ng** for phonemisation. Both are
GPL-licensed, and neither is shipped: they are downloaded on demand into the gitignored
`Tools/piper/`, run once to produce these files, and never touched by a build. Running a
program to generate data does not make the data a derivative work of that program, so the GPL
does not reach the clips in this folder — only the CC BY-SA 4.0 above does.

## Regenerating

```bash
Tools/bake_commentary.sh          # bake anything missing
Tools/bake_commentary.sh --force  # re-bake everything
```

Then rebuild `Assets/Config/CommentaryBank.asset` from
`Temp/evals/build_commentary_bank.cs` through the Unity MCP bridge, which is what re-pairs
each clip with the subtitle that matches it. Editing the text without rebuilding the bank
leaves the printed line saying something the voice does not.

## If the voice needs to change

Swapping voices is one line — `VOICE` in `Tools/bake_commentary.sh` — plus a `--force` bake
and a bank rebuild. Check the replacement's `MODEL_CARD` on Hugging Face for the **dataset**
licence before doing it, not the repository licence, which is MIT for every voice in the
collection and says nothing about the recordings underneath.
