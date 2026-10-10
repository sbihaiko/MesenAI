# Squad operations

Index for the coding squad. The operating rules are in `CLAUDE.md`
("Coding squad (agent-squad) and squad hub"); this file covers the files in
this folder and the global tooling they refer to.

- `workflows/dynamic.graph.json` — the dynamic workflow and its caps
  (`max_parallel`, `max_children`, `spend_cap`).
- `router-model-overrun.md` — notes on router-model overruns.

Global tooling (outside the repo):

- `~/.claude/skills/squad-goal/SKILL.md` — the autonomous delivery loop.
- `~/.claude/skills/squad-goal/scripts/launch.py` — launches a run from a
  request file and opens its dashboard.
- `~/.claude/skills/squad-goal/scripts/hub.py` — the squad hub, one page with
  every live run dashboard (port 7700, loopback, read-only).
