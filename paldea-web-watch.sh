#!/bin/bash
# Runs paldea-web.py while Pokémon Scarlet/Violet is open in Eden; stops it when the game closes.
# Set POKEMON_SAVE first (see README). Add this to your desktop's autostart to have it always ready.
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MATCH="${PALDEA_WEB_MATCH:-eden.*(scarlet|violet)}"   # pgrep -f pattern for the running game
pid=""
while true; do
  if pgrep -if "$MATCH" >/dev/null; then
    [ -z "$pid" ] && { python3 "$DIR/paldea-web.py" & pid=$!; }
  elif [ -n "$pid" ]; then
    kill "$pid"; wait "$pid" 2>/dev/null; pid=""
  fi
  [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null && pid=""   # restart if it crashed
  sleep 5
done
