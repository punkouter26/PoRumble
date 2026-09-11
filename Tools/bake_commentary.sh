#!/usr/bin/env bash
#
# Bakes the commentary script to WAV with Piper.
#
# Baked offline rather than synthesised at runtime, and that is not a convenience. Piper is a
# native binary plus a 63MB ONNX voice; shipping it would mean bundling both into an ARM64
# IL2CPP build, and the obvious pure-C# alternative - System.Speech - does not exist outside
# Windows and Mono. Baking gives the game ordinary AudioClips that cost nothing at runtime and
# work identically on a phone.
#
# Piper itself is GPL and espeak-ng inside it is too, which does not reach the output: running
# a program to produce data has never made the data a derivative of the program. The VOICE is
# the licence that matters, and the one pinned here is CC-BY-SA 4.0 - commercially usable with
# attribution, which is why Assets/Audio/Commentary/ carries an attribution file alongside the
# clips, exactly as Assets/Art/Fonts/ carries its OFL licences.
#
# Usage:   Tools/bake_commentary.sh            bake anything missing
#          Tools/bake_commentary.sh --force    re-bake everything
#
# The tool and the voice are downloaded on demand into Tools/piper/, which is gitignored: an
# 85MB build dependency does not belong in the history of a game repo.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PIPER_DIR="$ROOT/Tools/piper"
OUT_DIR="$ROOT/Assets/Audio/Commentary"
LINES="$ROOT/Tools/commentary_lines.json"

VOICE="en_GB-northern_english_male-medium"
VOICE_URL="https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/northern_english_male/medium"
PIPER_URL="https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip"

FORCE=0
if [ "${1:-}" = "--force" ]; then
    FORCE=1
fi

mkdir -p "$PIPER_DIR" "$OUT_DIR"

# ----------------------------------------------------------------- the tool
if [ ! -x "$PIPER_DIR/piper/piper.exe" ]; then
    echo "Fetching Piper..."
    curl -sL --max-time 300 -o "$PIPER_DIR/piper_windows_amd64.zip" "$PIPER_URL"
    ( cd "$PIPER_DIR" && unzip -q -o piper_windows_amd64.zip )
fi

if [ ! -f "$PIPER_DIR/$VOICE.onnx" ]; then
    echo "Fetching voice $VOICE..."
    curl -sL --max-time 600 -o "$PIPER_DIR/$VOICE.onnx" "$VOICE_URL/$VOICE.onnx"
    curl -sL --max-time 120 -o "$PIPER_DIR/$VOICE.onnx.json" "$VOICE_URL/$VOICE.onnx.json"
fi

# ----------------------------------------------------------------- the script
# Flattened to "id<TAB>text" with grep and sed rather than a JSON parser, because a Windows
# box that only runs Unity has neither jq nor Python on PATH - this machine does not - and a
# bake step that cannot run is worse than one that reads its input naively. The file is
# hand-written to a fixed one-entry-per-line shape, which is what makes that safe; if you
# reformat it, reformat these two patterns with it.
#
# A name key is uppercase ("BIGGIE"), which is what separates it from every other key in the
# file - all of those are lowercase - and the second sed folds the space out of "STANDARD RL"
# so it can be a filename.
extract() {
    grep -oE '^\s*"[A-Z][A-Z ]*": "[^"]*"' "$LINES" \
        | sed -E 's/^\s*"([A-Z][A-Z ]*)": "(.*)"/name_\1\t\2/' \
        | sed -E 's/^name_([A-Z]+) ([A-Z]+)\t/name_\1_\2\t/'

    grep -oE '"id": "[^"]*".*"text": "[^"]*"' "$LINES" \
        | sed -E 's/"id": "([^"]*)".*"text": "([^"]*)"/\1\t\2/'
}

SCRIPT="$(extract)"

if [ -z "$SCRIPT" ]; then
    echo "Could not read any lines out of $LINES" >&2
    exit 1
fi

# ----------------------------------------------------------------- bake
baked=0
skipped=0

while IFS=$'\t' read -r id text; do
    [ -z "$id" ] && continue
    out="$OUT_DIR/$id.wav"

    if [ -f "$out" ] && [ "$FORCE" -eq 0 ]; then
        skipped=$((skipped + 1))
        continue
    fi

    printf '%s' "$text" | "$PIPER_DIR/piper/piper.exe" \
        --model "$PIPER_DIR/$VOICE.onnx" \
        --config "$PIPER_DIR/$VOICE.onnx.json" \
        --output_file "$out" 2>/dev/null

    baked=$((baked + 1))
    echo "  $id.wav  <-  $text"
done <<< "$SCRIPT"

echo
echo "Baked $baked, skipped $skipped. Clips are in Assets/Audio/Commentary/."
echo "Now run Temp/evals/build_commentary_bank.cs through the Unity MCP bridge to refill"
echo "CommentaryBank.asset, which is what binds each clip to the subtitle that matches it."
