# Squad operations

Index for the coding squad. The operating rules are in `CLAUDE.md`
("Coding squad (agent-squad) and squad hub"); this file covers the files in
this folder and the global tooling they refer to.

- `workflows/dynamic.graph.json` — the dynamic workflow and its caps
  (`max_parallel`, `max_children`, `spend_cap`).
- `land-tip-not-final-state.md` — a land closed #1809 while the run's source
  changes were still uncommitted on the branch; the fix is pending deployment.
- `fork-gate-full-skips-pytest.md` — the gate's full suite class could skip the
  pytest suite on a branch whose diff selects it.
- `provider-outage-falls-through.md` — a 402 from one provider ended a dispatch
  at its first model instead of falling through the chain.
- `router-model-overrun.md` — notes on router-model overruns.

Global tooling (outside the repo):

- `~/.claude/skills/squad-goal/SKILL.md` — the autonomous delivery loop.
- `~/.claude/skills/squad-goal/scripts/launch.py` — launches a run from a
  request file; its dashboard shows in the hub, never in its own tab.
- `~/.claude/skills/squad-goal/scripts/hub.py` — the squad hub, one page with
  every live run dashboard (port 7700, loopback, read-only).
