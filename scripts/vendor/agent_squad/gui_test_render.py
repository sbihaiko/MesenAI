# vendored-from: agent-squad local fork (~/VSCodeProjects/agent-squad), branch gui/1179, file squad/gui_test_render.py
# source-commit: ac1006d9d5d8d880e953a732d2ae37de6c672710
# sha256: a532f20a4e8e4b52c44909ed019440c7168fd10a7905eb419c650e3fde022fb5
# read-only copy: edit it in the fork, never here (scripts/checks/verify_gui_test_render.py fails on any change)
# ---- end vendored header ----
"""Render a gui-test/1 script as Markdown (the human view of the JSON source).

Self-contained on purpose: the application repositories vendor exactly this
file (stdlib only, no imports from squad) and fail their doc check when a
committed view differs from this render.

Usage: python3 gui_test_render.py <script.json>   (Markdown on stdout)
"""
import json
import sys

FORMATS = ("gui-test/1",)


class RenderError(ValueError):
    pass


def _cell(text):
    return str(text).replace("|", "\\|").replace("\n", " ")


def _call(action):
    if not action:
        return "—"
    args = action.get("args") or {}
    inner = ", ".join(f"{k}={json.dumps(v)}" for k, v in args.items())
    return f"`{action['name']}({inner})`"


def _wait(wait):
    if not wait:
        return "—"
    unit = "timeout_frames" if "timeout_frames" in wait else "timeout_ticks"
    unit_name = unit.split("_", 1)[1]
    cond = f"`{wait['check']}` " if wait.get("check") else ""
    return f"{cond}within {wait.get(unit)} {unit_name}"


def _variants(step):
    v = step.get("variants")
    return ", ".join(f"{k}={x}" for k, x in v.items()) if v else "all"


def _row(step):
    cells = [f"`{step['id']}`", step.get("role", ""), f"`{step['precondition']}`",
             _call(step.get("action")), _wait(step.get("wait")), _call(step.get("check")),
             step["expect"], _variants(step), step["severity"], step["mode"]]
    return "| " + " | ".join(_cell(c) for c in cells) + " |"


_HEAD = ("| ID | Role | Precondition | Action | Wait | Check | Expected | Variants "
         "| Severity | Mode |\n|---|---|---|---|---|---|---|---|---|---|")


def _table(steps):
    return "\n".join([_HEAD] + [_row(s) for s in steps])


def render(script):
    if not isinstance(script, dict) or script.get("format") not in FORMATS:
        got = script.get("format") if isinstance(script, dict) else None
        raise RenderError(f"unknown format {got!r}; this renderer knows {list(FORMATS)}")
    out = [f"# {script['name']}", "",
           "<!-- Rendered from the JSON script by gui_test_render.py. Do not edit; edit the JSON. -->", "",
           f"- Format: `{script['format']}`", f"- Target: `{script['target']}`"]
    req = script.get("requires") or {}
    for key in ("actions", "checks"):
        out.append(f"- Requires {key}: " + (", ".join(f"`{x}`" for x in req.get(key, [])) or "none"))
    for axis, values in (script.get("variants") or {}).items():
        out.append(f"- Variant `{axis}`: " + ", ".join(f"`{x}`" for x in values))
    fixtures = script.get("fixtures") or {}
    if fixtures:
        out += ["", "## Fixtures", "", "```json", json.dumps(fixtures, indent=2, sort_keys=True), "```"]
    for batch in script["batches"]:
        out += ["", f"## Batch `{batch['id']}`"]
        for label in ("setup", "teardown"):
            if batch.get(label):
                out += ["", f"{label.capitalize()}:", "", _table(batch[label])]
        out += ["", "Steps:", "", _table(batch["steps"])]
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    sys.stdout.write(render(json.load(open(sys.argv[1], encoding="utf-8"))))
