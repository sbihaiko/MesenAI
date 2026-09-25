# F12.19 (4) cold read — evaluator briefing (2026-09-23)

This is the only evaluator-facing document of the F12.19 (4) cold read
(ADR-0214 §1–§2, "the briefing is the goal, not the path"). It went to a
fresh Opus agent (`Agent`, model `opus`, no fork, no builder context) as its
entire prompt. It is kept here verbatim, character for character, so a
reviewer can check that it names the goal (repaint the player's run) and not
the path. It names no file to open, no `usrNNN` id, no cycle id, and does
not say how many phases the run has. The only pointer is the kit's own entry
point, `kit/ARTIST.md`. The sandbox held a copy of the regenerated kit plus
`docs/remastering-a-game.md` and `docs/hd-pack-authoring.md`. The result is
`f1219-contra-kit-coldread-2026-09-23.md`.

Verbatim prompt:

```text
You are an artist opening a remastering kit for the NES game Contra for the first time. You want to repaint the player character (Bill, the shirtless soldier) running. Your whole world is this folder:

/private/tmp/claude-503/-Users-bihaiko-VSCodeProjects-MesenCE/7cb9a096-4df1-46fe-8c4d-c9925ee60cbe/scratchpad/coldread-contra-kit/

- `kit/` — the kit, as handed to an artist. Start at `kit/ARTIST.md`.
- `guides/` — two artist guides you may read.
- `out/` — write your log here.

Rules: read ONLY files inside that folder. Do not open anything in any git repository, any `docs/adr`, PRD, `scripts/*.py` source, memory directory or transcript. If you do read something outside the folder, log it with the path and continue. Do not open any `hires.txt`. Do not run any tool that modifies files in `kit/`. You may view PNGs (use the Read tool on the image). No git commands, no commits.

Your task, using only what the kit and guides tell you:
1. Find the file(s) you would paint to restyle Bill's run animation. Note the clock time you found it and the path you took (which file told you).
2. Look at them. For each figure: does it read as one complete, undistorted character (head, torso, legs joined, nothing offset or gapped inside the figure)? How many run frames are there, and do they look like consecutive phases of one run?
3. Are the other figure rows (the other `usr*` grids under `figures/` and `sheets/`) understandable — can you tell what each is, and is anything confusing, duplicated, or visibly broken?
4. Everything you had to guess because neither the kit nor a guide said it.

Write `out/log-contra-kit-coldread.md` (en-US) with: start/end time, the path you took, a table per figure file (file, what it shows, frames, complete figure yes/no, notes), a verdict line "Run cycle identifiable and paintable unaided: yes/no", the list of guesses and friction points, and any stops (files read outside the sandbox). Report the verdict and the top 3 friction points in your final message.
```
