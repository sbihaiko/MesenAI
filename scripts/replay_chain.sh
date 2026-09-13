#!/bin/sh
# Replay a flat chain script from one save state to the next (F9.22).
#
#   scripts/replay_chain.sh <rom> <from.mss> <chain.txt> <to.mss>
#
# A `<a>-to-<b>.chain.txt` under scripts/stages/<game>/ is the headless chain
# that produced state <b> from state <a>, flattened: every step's input
# followed by the idle frames its run added past the script (round-up plus the
# recorder's one-frame overshoot). Replaying the whole thing as one run lands
# on the same RAM byte for byte, provided the run's frame target is the
# script's frame count minus one - the overshoot frame is then the last one -
# which is the arithmetic this script does so nobody has to.
set -eu
[ $# -eq 4 ] || { echo "usage: $0 <rom> <from.mss> <chain.txt> <to.mss>" >&2; exit 2; }
rom=$1; from=$2; chain=$3; to=$4
here=$(cd "$(dirname "$0")" && pwd)
frames=$(awk '!/^#/ && NF { s += substr($1, 1, length($1) - 1) } END { print s - 1 }' "$chain")
seconds=$(awk -v f="$frames" 'BEGIN { printf "%.6f", f / 60.0988 }')
work=$(mktemp -d "${TMPDIR:-/tmp}/replay_chain.XXXXXX")
"$here/headless_record" "$rom" "$seconds" "$work/run" hdpack-off "input=$chain" "state=$from" "save-state=$to"
rm -rf "$work"
[ -f "$to" ] || { echo "replay_chain: the run did not reach its frame target; no state written" >&2; exit 1; }
echo "replay_chain: $to written ($((frames + 1)) frames past $from)"
