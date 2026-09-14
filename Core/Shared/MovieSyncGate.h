#pragma once
#include "pch.h"
#include <string>
#include <vector>

//ADR-0185 sec. 4, amended 2026-09-14 (issue #201): the desync gate.
//
//The original gate was one comparison of two totals - a movie-driven run had
//to end with more distinct tile shapes than an identical movie-less run of the
//same length. That catches a TOTAL desync (the movie plays but the game is not
//playing it, so the run records the movie-less material and nothing more) and
//it cannot catch a PARTIAL one. A run that tracks the movie for its first
//three thousand frames and then parts ways banks the whole good prefix into
//the total before it diverges, so the final number clears the bar for the rest
//of the run no matter what the console does afterwards - it dies, hits GAME
//OVER, falls back to the attract loop, and the gate still passes it.
//
//This file is the host-free half of the replacement: no Emulator, no Console,
//no filesystem. The harness (scripts/headless_record.cpp) samples a trace
//while it runs and hands the finished traces here; the rules below turn two
//traces into findings. Everything that decides anything lives here so it is
//unit-testable (scripts/core_unit_tests.cpp, Bloco T).
//
//Three ideas, in descending order of how much they can be trusted:
//
//  1. A DECLARED RAM INVARIANT (MovieSyncWatch). The one signal that is both
//     cheap and decisive. "The emulator diverged" is not observable from
//     inside the emulator, but its consequence in the game usually is: in the
//     issue #201 Castlevania run the divergence window shows up as a life lost
//     and a TIME reset - the game respawned a player the movie never intended
//     to lose. A caller who knows the game declares the address holding that
//     counter and the invariant it must keep while the movie drives the pad
//     ("never decreases"), and the first violation fails the run and names the
//     frame. It is falsifiable by construction: point it at the wrong address
//     and it fires on a good run, so the address has to be established by a
//     positive control before it is trusted.
//
//  2. MOVIE EXHAUSTION. A movie's input runs dry at a frame decided by its own
//     row count and nothing else (a 36788-row .bk2 runs dry at frame 36790 -
//     the prime poll inside the movie's PowerCycle consumes row 0, then one
//     poll per frame). If the player stopped before that frame, something
//     other than the input running out stopped it. Exact, free, and blind to
//     the #201 case, which plays every row.
//
//  3. PREFIX COMPARISON against the movie-less baseline. The old total, asked
//     at every sampled frame instead of only at the last one. It is strictly
//     more information for a fixed cost - the same two runs, the same counter,
//     read more often - and it is what localises a partial desync: the
//     movie-driven run's LEAD over the baseline (movie shapes minus baseline
//     shapes at the same frame) grows while the movie is really driving the
//     game and stops growing once it is not, because a run that has died into
//     GAME OVER and the attract loop is back in exactly the state class the
//     movie-less console occupies, and stops learning anything the baseline
//     does not already have. The total cannot see this: it is the lead at the
//     last frame only, and the lead never shrinks.
//     This rule is reported and NEVER fatal on its own. A long good run also
//     plateaus once it has seen the game's whole vocabulary, and a gate that
//     fails those would be turned off inside a week, which is worse than no
//     gate. It names the window; a human or rule 1 decides.

enum class MovieSyncRule
{
	NeverDecreases,
	NeverIncreases,
	NeverBelow,  //value >= Operand
	NeverEquals, //value != Operand
};

struct MovieSyncWatch
{
	uint16_t Address = 0;
	MovieSyncRule Rule = MovieSyncRule::NeverDecreases;
	int32_t Operand = 0;
	std::string Label;
};

//One row of the trace. The harness appends one per sample interval; a baseline
//run writes the same shape with MoviePlaying false throughout.
struct MovieSyncSample
{
	uint32_t Frame = 0;
	bool MoviePlaying = false;
	uint32_t TilesSeen = 0;
	uint32_t TilesWithArt = 0;
	uint32_t ScreensSeen = 0;
	std::vector<uint8_t> WatchValues; //parallel to the watch list, may be empty
};

struct MovieSyncFinding
{
	std::string Code;   //stable identifier, see MovieSyncGate below
	bool Fatal = false; //a fatal finding fails the run
	uint32_t Frame = 0; //the frame the evidence is at, 0 when the whole run is the evidence
	std::string Detail; //one human-readable line, en-US
};

struct MovieSyncParams
{
	//Frame the movie's input is expected to run dry on; 0 disables rule 2.
	//For a BizHawk .bk2 of N rows this is N + 2 (see the header comment).
	uint32_t ExpectedMovieEndFrame = 0;
	//Rule 3's window. 3600 frames is 60 emulated seconds at NES NTSC.
	uint32_t StallWindowFrames = 3600;
	//How many consecutive zero-gain windows are reported as a stall. Three
	//windows = three emulated minutes in which a movie-driven run learned
	//nothing the movie-less console had not already shown.
	uint32_t StallWindows = 3;
};

class MovieSyncGate
{
public:
	//"<hexaddr>:<rule>[=<n>][:<label>]", rule in
	//never-decreases|never-increases|never-below|never-equals. The address is
	//hex and must be below $0800 - the NES internal RAM, for ADR-0184's reason:
	//anything above it is not a RAM address and a watch on it is measuring the
	//wrong thing.
	static bool ParseWatch(const std::string& spec, MovieSyncWatch& out, std::string& error);
	static std::string RuleName(MovieSyncRule rule);

	//CSV, one header line then one line per sample. Watch columns are named
	//"watch:<label>" so a trace is readable on its own and a mismatched pair of
	//traces is caught rather than silently compared column by column.
	static std::string FormatTraceHeader(const std::vector<MovieSyncWatch>& watches);
	static std::string FormatTraceRow(const MovieSyncSample& sample);
	static bool ParseTrace(const std::string& text, std::vector<MovieSyncSample>& outSamples,
		std::vector<std::string>& outWatchLabels, std::string& error);

	//Codes produced, all en-US:
	//  "watch-violated"       fatal, rule 1
	//  "movie-stopped-early"  fatal, rule 2
	//  "no-gain-over-baseline" fatal, rule 3's total (the original gate)
	//  "prefix-stall"         NOT fatal, rule 3's prefix form
	//  "no-baseline"          NOT fatal, said out loud so a run without one is
	//                         never mistaken for a run that passed a comparison
	//`baseline` may be empty: the two comparative rules are then skipped and
	//"no-baseline" is reported.
	static std::vector<MovieSyncFinding> Evaluate(const std::vector<MovieSyncSample>& movieRun,
		const std::vector<MovieSyncSample>& baseline, const std::vector<MovieSyncWatch>& watches,
		const MovieSyncParams& params);

	//True when any finding is fatal - the harness's exit code.
	static bool IsFatal(const std::vector<MovieSyncFinding>& findings);
};
