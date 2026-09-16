"""General/base structural checks for community-pack-validate.yml.

Covers: the required Project/Status/option/Pack-Hash ids, the
PROJECT_NUMBER pin, the host allow-list wiring (scripts/pack_host_allowlist.json
plus fetch_pack.py's redirect/private-address guards), the 300MB size cap,
the always-write sha256 step, the unmodified mep_lint.py invocation, the
classify step's tool-free boundary (ADR-0199) and the data-not-instruction
prompt clause, the top-of-file secret-name comment, and the Aceito*-gated
catalog-workflow dispatch (AC-2, AC-6 validate-side).
"""
import json
import re

from ._shared import REPO_ROOT, fail, prompt_block

ALLOWLIST_PATH = REPO_ROOT / "scripts" / "pack_host_allowlist.json"
FETCH_PACK_PATH = REPO_ROOT / "scripts" / "fetch_pack.py"

REQUIRED_IDS = {
    "PVT_kwHOB1MsbM4BhjpN": "Project node id",
    "PVTSSF_lAHOB1MsbM4BhjpNzhge86c": "Status field id",
    "5173b5cd": "Status option: Novo envio",
    "51951f52": "Status option: Em validação",
    "227e4623": "Status option: Inválido",
    "39e4f3a1": "Status option: Aceito parcial HD Mesen",
    "cd763737": "Status option: Aceito MEP completo",
    "PVTF_lAHOB1MsbM4BhjpNzhge9Is": "Pack Hash field id",
}

HOST_ALLOWLIST = (
    "github.com",
    "raw.githubusercontent.com",
    "gist.githubusercontent.com",
    "gist.github.com",
)

ACEITO_MARKERS = ("STATUS_ACEITO_PARCIAL", "STATUS_ACEITO_COMPLETO", "aceito")


def check_ids(text):
    for id_value, label in REQUIRED_IDS.items():
        if id_value not in text:
            fail(f"missing required id ({label}): {id_value}")


def check_project_number_only(text):
    if "PROJECT_NUMBER: 3" not in text:
        fail("PROJECT_NUMBER is not pinned to 3")
    for m in re.finditer(r"gh project item-(?:add|list)\s+(\d+)\b", text):
        fail(f"gh project call hardcodes a non-variable project number: {m.group(0)}")
    for m in re.finditer(r'gh project item-(?:add|list)\s+"\$([A-Z_]+)"', text):
        if m.group(1) != "PROJECT_NUMBER":
            fail(f"gh project call uses an unexpected project-number variable: {m.group(0)}")


def check_host_allowlist(text):
    # The allow-list itself lives in scripts/pack_host_allowlist.json (a
    # config file, not inline workflow YAML) so a new host is a config
    # change; this check follows it there instead of grepping this file.
    if "scripts/pack_host_allowlist.json" not in text:
        fail("workflow does not reference scripts/pack_host_allowlist.json")
    if not ALLOWLIST_PATH.is_file():
        fail(f"missing file: {ALLOWLIST_PATH}")
        return
    try:
        allowlist_hosts = {
            h["host"] for h in json.loads(ALLOWLIST_PATH.read_text())["hosts"] if h.get("host")
        }
    except (json.JSONDecodeError, KeyError, TypeError) as exc:
        fail(f"scripts/pack_host_allowlist.json did not parse as expected: {exc}")
        return
    for host in HOST_ALLOWLIST:
        if host not in allowlist_hosts:
            fail(f"host allow-list missing host: {host}")
    allowlist_text = ALLOWLIST_PATH.read_text()
    if "/releases/" not in allowlist_text:
        fail("host allow-list missing github.com /releases/ path restriction")
    if "mediafire" not in allowlist_text:
        fail("host allow-list missing MediaFire entries")
    if not FETCH_PACK_PATH.is_file():
        fail(f"missing file: {FETCH_PACK_PATH}")
        return
    fetch_text = FETCH_PACK_PATH.read_text()
    if "redirect" not in fetch_text.lower():
        fail("fetch_pack.py does not appear to re-validate redirects")
    if "is_private" not in fetch_text or "is_loopback" not in fetch_text:
        fail("fetch_pack.py does not appear to reject private/loopback resolved addresses")


def check_size_cap(text):
    if "314572800" not in text:
        fail("300MB cap constant (314572800 bytes) not found")
    if "--max-bytes" not in text:
        fail("fetch_pack.py --max-bytes invocation (during-download cap) not found")
    if not FETCH_PACK_PATH.is_file() or "Content-Length" not in FETCH_PACK_PATH.read_text():
        fail("pre-download Content-Length check not found in fetch_pack.py")


def check_hash_write(text):
    if "sha256sum" not in text:
        fail("sha256 computation (sha256sum) not found")
    if "PACK_HASH_FIELD_ID" not in text:
        fail("Pack Hash field id constant not referenced")
    if "independent of the verdict" not in text:
        fail("no comment documenting the unconditional (always) Pack Hash write")


def check_mep_lint_call(text):
    if "python3 scripts/mep_lint.py" not in text:
        fail("exact 'python3 scripts/mep_lint.py' invocation not found")


def _has_data_not_instruction_clause(text):
    lowered = text.lower()
    return "data" in lowered and "never" in lowered


def _step_block(text, name):
    """The YAML block of the step whose name starts with `name`."""
    for block in text.split("\n      - name:"):
        if block.lstrip().startswith(name):
            return block
    return ""


def check_classify_is_tool_free(text):
    """ADR-0199: classify is a direct, tool-free API call.

    Until 2026-09-16 the classify step ran inside
    `anthropics/claude-code-action` and its boundary was a denial list
    (`--disallowedTools Bash,Read,...`, issue #148). The step is now a
    `run:` that calls the Gemini API with no `tools` in the request body,
    so what this check can assert is stronger: the step names the script,
    reads the API key, and uses no action at all.
    """
    block = _step_block(text, "Classify pack")
    if not block:
        fail("no step named 'Classify pack ...' in the workflow")
        return
    if "scripts/gemini_classify.py" not in block:
        fail("classify step does not call scripts/gemini_classify.py (ADR-0199)")
    if "GEMINI_API_KEY" not in block:
        fail("classify step does not read GEMINI_API_KEY")
    if "anthropics/claude-code-action" in block:
        fail("classify step still uses anthropics/claude-code-action (ADR-0199 replaced it)")
    if "\n        uses:" in block:
        fail("classify step still delegates to a third-party action")
    # The request body is built in scripts/gemini_classify.py, whose unit
    # test asserts it carries no `tools` key — the workflow itself must not
    # start assembling its own payload.
    if "tools" in block:
        fail("classify step mentions tools; the request payload is the script's job")
    if not _has_data_not_instruction_clause(text):
        fail("prompt lacks an explicit data-not-instruction clause")


def check_prompt_file_data_not_instruction(text):
    # F6.5: since the classify prompt now lives in .github/ai/validate-classify.md
    # (single source), the data-not-instruction clause must live in that
    # PROMPT block too — that is the text actually sent to the classify LLM.
    prompt = prompt_block()
    if not prompt:
        fail("Cannot validate data-not-instruction clause: PROMPT block missing")
        return
    if not _has_data_not_instruction_clause(prompt):
        fail(
            ".github/ai/validate-classify.md PROMPT block lacks an explicit "
            "data-not-instruction clause"
        )


def check_secret_name_comment(text):
    # GEMINI_API_KEY is the live classify credential (ADR-0199); the two
    # Anthropic names stay listed because the dormant autofix subsystem
    # still declares them.
    header = "\n".join(text.splitlines()[:15])
    for secret in ("GEMINI_API_KEY", "ANTHROPIC_API_KEY", "CLAUDE_CODE_OAUTH_TOKEN"):
        if secret not in header:
            fail(f"top-of-file comment does not name required secret: {secret}")


def check_catalog_dispatch_gated_on_aceito(text):
    if "community-pack-catalog.yml" not in text:
        fail("no literal reference to community-pack-catalog.yml (dispatch/uses)")
        return
    blocks = [b for b in text.split("\n      - name:") if "community-pack-catalog.yml" in b]
    if not any(any(m in b for m in ACEITO_MARKERS) for b in blocks):
        fail("community-pack-catalog.yml call is not gated on an Aceito* status")
