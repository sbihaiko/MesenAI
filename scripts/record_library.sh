#!/usr/bin/env bash
# F12.10 - unattended recording job, one row per ROM.
#
# Records every ROM in a folder with the best driver that matches it, builds an
# artist kit from each recording, and writes <out-dir>/library-report.md saying
# what happened to each. It never waits on a human and it never aborts on one
# ROM: a game that cannot be recorded is a row in the report, not a stop.
#
# Usage:
#   scripts/record_library.sh <roms-dir> <out-dir> [seconds=60]
#
# The driver per ROM is resolved by scripts/library_job.py, in this order:
#   (a) routes - a scripts/stages/<game>/ set whose stage-set.json declares this
#       ROM's No-Intro SHA1 -> record_stages.sh, one pack per stage
#   (b) movie  - a .bk2 beside the ROM or in <set>/movies/ whose header SHA1 is
#       this ROM's whole-file SHA1 -> headless_record ... movie=
#   (c) entry  - a matched set with only an entry script -> one power-on run
#   (d) static - nothing matched; no recording is possible here
#
# (d) is where F12.9 (static kit from the ROM alone, ADR-0219) takes over for a
# CHR ROM game, since 2026-09-20: the ROM's own CHR is projected into pattern
# pages with every cell `fill` / `seen: false`, and no emulator is started. A
# CHR RAM game still has nothing to fall back on - the generator refuses it,
# naming the index import (F12.12), and the report says so per ROM.
#
# Runs under caffeinate so a long job is not suspended by App Nap.
set -uo pipefail

if [ $# -lt 2 ]; then
  sed -n '2,30p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
fi

here="$(cd "$(dirname "$0")" && pwd)"
romsdir="$1"
mkdir -p "$2"; out="$(cd "$2" && pwd)"
seconds="${3:-60}"

record="$here/headless_record"
[ -x "$record" ] || { echo "missing $record - run: make capture-tool" >&2; exit 2; }
[ -d "$romsdir" ] || { echo "not a directory: $romsdir" >&2; exit 2; }

# One `caffeinate` around the whole job rather than per run: a 20-minute job
# that sleeps halfway through reports numbers from a machine that was asleep.
# Re-exec before any work, so nothing is done twice.
if command -v caffeinate >/dev/null 2>&1 && [ -z "${MESEN_NO_CAFFEINATE:-}" ]; then
  exec caffeinate -dimsu env MESEN_NO_CAFFEINATE=1 "$0" "$@"
fi

plan="$out/plan.json"
python3 "$here/library_job.py" plan "$romsdir" --stages "$here/stages" \
        --seconds "$seconds" > "$plan" || exit 2

count="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))["roms"]))' "$plan")"
echo "== $count ROM(s) planned, ${seconds}s per run -> $out"

results="$out/results.json"
echo "[]" > "$results"

i=0
while [ "$i" -lt "$count" ]; do
  # Read the row as NUL-separated fields so a ROM name with spaces, commas or
  # parentheses survives - the library is full of "Contra (1988) (Konami).nes".
  { IFS= read -r -d '' name
    IFS= read -r -d '' rom
    IFS= read -r -d '' driver
    IFS= read -r -d '' stages
    IFS= read -r -d '' movie
    IFS= read -r -d '' entry
  } < <(python3 -c '
import json, sys
r = json.load(open(sys.argv[1]))["roms"][int(sys.argv[2])]
for k in ("name", "rom", "driver", "stages", "movie", "entry"):
    sys.stdout.write((r.get(k) or "") + "\0")
' "$plan" "$i")

  romout="$out/$name"
  mkdir -p "$romout"
  status="not attempted"
  echo "== [$((i + 1))/$count] $name: $driver"

  case "$driver" in
    routes)
      # A `.mss` save state is never versioned (see .gitignore: "None of that is
      # ever versioned"), so a fresh checkout has the scripts and none of the
      # states they start from. Recording straight away would run every stage
      # from power-on and keep a title screen - which is exactly what the first
      # run of this job did on Mega Man 3: 1 retained frame per stage. So the
      # job mints what it needs first, into its own copy of the set. The
      # repository tree is never written to.
      work="$romout/stages-src"
      rm -rf "$work"; mkdir -p "$work"
      cp "$stages"/*.txt "$work"/ 2>/dev/null || true
      cp "$stages"/*.json "$work"/ 2>/dev/null || true
      # A mint's state is named for the stage that will *use* it
      # (scripts/stages/README.md), and one mint may serve several stages -
      # `<stage>.mss` and the probe's own copy. library_job.py does that
      # pairing, by longest matching prefix, so it can be unit-tested.
      minted=0; mintfail=0
      while IFS= read -r -d '' mint && IFS= read -r -d '' servedlist; do
        first="${servedlist%%,*}"
        mkdir -p "$romout/mint/$first"
        mintrom="$romout/mint/$first/$(basename "$rom")"
        ln -f "$rom" "$mintrom" 2>/dev/null || cp "$rom" "$mintrom"
        if "$record" "$mintrom" "$seconds" "$romout/mint/$first/rec" \
             "input=$mint" "save-state=$work/$first.mss" \
             >> "$romout/mint.log" 2>&1 && [ -s "$work/$first.mss" ]; then
          minted=$((minted + 1))
          # Every other stage this mint serves starts from the same state.
          rest="${servedlist#*,}"
          if [ "$rest" != "$servedlist" ]; then
            IFS=',' read -ra more <<< "$rest"
            for st in "${more[@]}"; do cp "$work/$first.mss" "$work/$st.mss"; done
          fi
        else
          mintfail=$((mintfail + 1))
        fi
      done < <(python3 "$here/library_job.py" mints "$work" 2>>"$romout/mint.log")
      [ "$mintfail" -gt 0 ] && echo "   $mintfail state(s) could not be minted" >&2

      if "$here/record_stages.sh" "$rom" "$work" "$romout/stages" "$seconds" \
           > "$romout/record.log" 2>&1; then
        status="recorded"
      else
        # A partial batch is still worth a kit: record_stages.sh exits 1 when
        # any stage failed, and the ones that passed left their packs.
        status="recorded with failures"
      fi
      [ "$minted" -gt 0 ] && status="$status ($minted state(s) minted)"
      [ "$mintfail" -gt 0 ] && status="$status ($mintfail mint failed)"
      ;;
    movie|entry)
      romname="$(basename "$rom")"
      mkdir -p "$romout/run"
      ln -f "$rom" "$romout/run/$romname" 2>/dev/null || cp "$rom" "$romout/run/$romname"
      rm -rf "$romout/run/${romname%.*}" "$romout/run/.bootstrap"
      args=("$romout/run/$romname" "$seconds" "$romout/run/rec" bootstrap hdpack-off)
      [ "$driver" = "movie" ] && args+=("movie=$movie")
      [ "$driver" = "entry" ] && args+=("input=$entry")
      if "$record" "${args[@]}" > "$romout/record.log" 2>&1; then
        status="recorded"
      else
        status="recording failed"
      fi
      ;;
    static)
      # No recording is possible, so the kit is projected over the ROM alone
      # (F12.9 / ADR-0219). The pack folder named here is never written to: the
      # generator takes it as an output location and refuses one that holds a
      # recording.
      if python3 "$here/artist_chr_kit.py" "$romout/no-recording" --rom "$rom" \
           --static --out "$romout/kit" --verify > "$romout/kit-chr.log" 2>&1; then
        status="static kit from the ROM alone - nothing was seen in play"
      else
        status="no kit - $(tail -n 1 "$romout/kit-chr.log")"
      fi
      ;;
  esac

  # --- the kit, from whatever packs the recording left -----------------------
  packs=()
  while IFS= read -r p; do packs+=("$p"); done < <(
    find "$romout" -maxdepth 4 -type d -name auto 2>/dev/null | sort)

  kit="$romout/kit"
  if [ "${#packs[@]}" -gt 0 ]; then
    primary="${packs[0]}"
    if ! python3 "$here/artist_kit.py" "$primary" --out "$kit" --verify \
           > "$romout/kit-sprites.log" 2>&1; then
      status="$status; artist_kit failed"
    fi
    if ! python3 "$here/artist_bg_kit.py" "$primary" --out "$kit" --verify \
           > "$romout/kit-bg.log" 2>&1; then
      status="$status; artist_bg_kit failed"
    fi
    # No artist_map.py here on purpose: a panorama needs a per-stage
    # MESEN_SHEET_GRID_DUMP, and one Contra route's dump is ~190 MB. A
    # library job would write gigabytes of them for a surface this job does not
    # otherwise use, so the report's `maps` column stays `-` and says why.
    also=()
    for p in "${packs[@]:1}"; do also+=(--also "$p"); done
    if ! python3 "$here/artist_chr_kit.py" "$primary" --rom "$rom" --out "$kit" \
           ${also[@]+"${also[@]}"} --verify > "$romout/kit-chr.log" 2>&1; then
      status="$status; artist_chr_kit failed"
    fi
    if ! python3 "$here/artist_kit_assemble.py" "$kit" --title "$name" \
           > "$romout/kit-assemble.log" 2>&1; then
      status="$status; assemble failed"
    fi
  elif [ "$driver" = "static" ] && [ -f "$kit/kit-part-chr.json" ]; then
    # The static kit has one part and no recording behind it; assembling it is
    # what puts ADR-0219's first line at the top of ARTIST.md.
    if ! python3 "$here/artist_kit_assemble.py" "$kit" --title "$name" \
           > "$romout/kit-assemble.log" 2>&1; then
      status="$status; assemble failed"
    fi
  elif [ "$driver" != "static" ]; then
    status="$status; no pack was written"
  fi

  python3 "$here/library_job.py" collect "$plan" "$i" "$results" "$romout" "$status" \
    || echo "   WARN: could not record the result row for $name" >&2

  i=$((i + 1))
done

python3 "$here/library_job.py" report "$results" --out "$out/library-report.md" \
        --title "Library recording job (${seconds}s per run)" || exit 2
echo "== wrote $out/library-report.md"
