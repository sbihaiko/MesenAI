"""Upsert-mep-meta step structural checks for community-pack-validate.yml (T6).

Covers the "Upsert mep-meta comment" step (ADR-0138 §5/§13/§16/§18): it
runs on every successful classify+apply-verdict pass regardless of
recipe_status (never gated on recipe_status == 'present'), finds an
existing marked comment via `gh api` and PATCHes it (POSTs only when none
exists yet), the marker appears both in the find-query and the built body,
the exact literal provenance line about submitter-declared/verified-on-install
dep digests is present, the body/payload is built with Python's json
module (never bash string concatenation), and the deps/recipe_hash fields
are omitted entirely (never emitted empty/null) when no recipe was
assembled.
"""
from ._shared import fail

MEP_META_MARKER = "<!-- mep-meta -->"
MEP_META_PROVENANCE_LINE = "dep digests: submitter-declared, verified on install"
MEP_META_WRONG_GATE = "steps.assemble-recipe.outputs.recipe_status == 'present'"


def _mep_meta_block(text):
    blocks = [b for b in text.split("\n      - name:") if "id: upsert-mep-meta" in b]
    if not blocks:
        fail("Upsert mep-meta comment step (id: upsert-mep-meta) not found")
        return None
    return blocks[0]


def check_mep_meta_step_present_and_not_gated_on_recipe_status(text):
    # T6 (ADR-0138 §5/§13): the upsert runs on EVERY successful
    # classify+apply-verdict pass, regardless of recipe_status — it must
    # never be gated on recipe_status == 'present' (that would drop
    # provenance for every 'absent'/'refused' submission, which still has
    # a real verdict/labels/hash to record).
    block = _mep_meta_block(text)
    if block is None:
        return
    if "steps.classify.outcome" not in block:
        fail("Upsert mep-meta comment step does not gate on steps.classify.outcome")
    if MEP_META_WRONG_GATE in block:
        fail(
            "Upsert mep-meta comment step is gated on recipe_status == "
            "'present' — it must run on every pass regardless of recipe_status"
        )
    if "steps.apply-verdict.outputs.verdict" not in block or "steps.apply-verdict.outputs.labels" not in block:
        fail("Upsert mep-meta comment step does not consume apply-verdict's verdict/labels outputs")


def check_mep_meta_find_then_patch(text):
    # T6 (ADR-0138 §5): find the marked comment via `gh api`, then PATCH it
    # wholesale; POST a new comment only when none exists yet.
    block = _mep_meta_block(text)
    if block is None:
        return
    if "gh api" not in block:
        fail("Upsert mep-meta comment step does not call 'gh api'")
    if MEP_META_MARKER not in block:
        fail(f"Upsert mep-meta comment step does not search for the marker: {MEP_META_MARKER}")
    if "--method PATCH" not in block:
        fail("Upsert mep-meta comment step never PATCHes an existing comment")
    if "--method POST" not in block:
        fail("Upsert mep-meta comment step never POSTs a new comment when none exists")


def check_mep_meta_marker_in_comment_body(text):
    # T6: the marker must appear in the BUILT comment body itself (not
    # just the find-query above), so a fresh comment is itself discoverable
    # on the next pass.
    block = _mep_meta_block(text)
    if block is None:
        return
    if block.count(MEP_META_MARKER) < 2:
        fail(
            "Upsert mep-meta comment step must reference the marker twice: "
            "once to find an existing comment, once inside the body it writes"
        )


def check_mep_meta_provenance_line(text):
    # T6 (ADR-0138 §16): the literal line stating dep digests are
    # submitter-declared and verified only on install, exactly as worded
    # in the ADR — never paraphrased.
    block = _mep_meta_block(text)
    if block is None:
        return
    if MEP_META_PROVENANCE_LINE not in block:
        fail(f"Upsert mep-meta comment step is missing the literal provenance line: {MEP_META_PROVENANCE_LINE}")


def check_mep_meta_body_built_via_python_json(text):
    # T6: the request body (including the embedded metadata block) is
    # built with Python's json module, never bash string concatenation.
    block = _mep_meta_block(text)
    if block is None:
        return
    if "import json" not in block:
        fail("Upsert mep-meta comment step does not build its payload with Python's json module")
    if "json.dumps(" not in block or "json.dump(" not in block:
        fail("Upsert mep-meta comment step does not call json.dumps/json.dump to build the comment body/payload")


def check_mep_meta_fence_not_hardcoded(text):
    # T4 (ADR-0138 §33): json.dumps does not escape backticks, so a
    # hardcoded exactly-3-backtick fence truncates early when the payload
    # itself (a submitter-supplied hints/license value inside the embedded
    # recipe) contains a run of 3+ backticks. The writer must compute its
    # fence length via the shared "shortest safe fence" rule
    # (mep_recipe_common.choose_fence) instead of a literal ```json/```
    # pair.
    block = _mep_meta_block(text)
    if block is None:
        return
    if '"```json"' in block:
        fail("Upsert mep-meta comment step still hardcodes a fixed-length ```json opening fence")
    if '"```",' in block:
        fail("Upsert mep-meta comment step still hardcodes a fixed-length ``` closing fence")
    if "mep_recipe_common" not in block:
        fail("Upsert mep-meta comment step does not import mep_recipe_common for the shared fence rule")
    if "choose_fence(" not in block:
        fail("Upsert mep-meta comment step never calls choose_fence() to size its JSON fence")


def check_mep_meta_records_split_game_identity(text):
    # ADR-0143: a split submission's per-game identity lives only in this
    # comment, and the write is wholesale (§5). So the step must read the
    # identity back before rewriting it, and re-emit it: `game` (which
    # `resolve_pack_id` turns into `{origin}:{game}`) and the primary's
    # `siblings`. Reading the prior body has to happen BEFORE the PATCH,
    # and `meta["game"]` has to be set BEFORE `resolve_pack_id` runs, or the
    # id degrades to the bare origin. The label read is what scopes this to
    # split issues only, so no existing non-split row re-keys.
    block = _mep_meta_block(text)
    if block is None:
        return
    if "--json labels" not in block or "pack:split" not in block:
        fail(
            "Upsert mep-meta comment step does not read the issue's labels to "
            "recognize a split submission (ADR-0143)"
        )
    if "parse_mep_meta" not in block or "mep_meta_parser" not in block:
        fail(
            "Upsert mep-meta comment step does not parse the prior comment body "
            "with the shared mep_meta_parser leaf"
        )
    if 'meta["game"]' not in block:
        fail("Upsert mep-meta comment step never re-emits the ADR-0143 `game` field")
    if 'meta["siblings"]' not in block:
        fail("Upsert mep-meta comment step never re-emits a split primary's `siblings` list")
    # Ordering, not just presence: a substring check alone passes on code
    # that reads the identity one line too late — which is exactly how the
    # regression this covers behaves (the field is dropped rather than
    # mis-set, so only the order distinguishes the two).
    prior_read_at = block.find('-q .body >"$PRIOR_META_PATH"')
    if prior_read_at == -1:
        fail("Upsert mep-meta comment step never stores the prior comment body")
    patch_at = block.find("--method PATCH")
    if prior_read_at != -1 and patch_at != -1 and prior_read_at > patch_at:
        fail(
            "Upsert mep-meta comment step PATCHes the comment before reading the "
            "prior identity out of it (ADR-0143 identity would be lost)"
        )
    body = _mep_meta_python(block)
    if body is None:
        return
    # Anchors first: an ordering assertion between two `find()` calls that
    # both return -1 passes vacuously, which would make this whole check a
    # no-op the moment the step's Python is renamed.
    game_at = body.find('meta["game"]')
    resolve_at = body.find("resolve_pack_id(")
    prior_at = body.find("parse_mep_meta(")
    for name, at in (('meta["game"]', game_at), ("resolve_pack_id(", resolve_at),
                     ("parse_mep_meta(", prior_at)):
        if at == -1:
            fail(f"Upsert mep-meta comment step's Python never mentions {name}")
            return
    if game_at > resolve_at:
        fail(
            "Upsert mep-meta comment step sets `game` after resolve_pack_id — "
            "the ADR-0143 pack_id would resolve to the bare origin"
        )
    if prior_at > game_at:
        fail(
            "Upsert mep-meta comment step uses the prior identity before "
            "parsing it out of the comment body"
        )


def _mep_meta_python(block):
    """The Python heredoc inside the step, so the ordering of its statements
    can be asserted (see the caller). The opener is matched with its trailing
    newline and the closer as a whole line: searching for a bare `PYEOF` past
    the opener's own index finds the opener's OWN text three characters in,
    which yields a three-character "body" whose every `find()` returns -1 —
    an ordering check that silently never runs."""
    start = block.find("<<'PYEOF'\n")
    if start == -1:
        fail("Upsert mep-meta comment step has no <<'PYEOF' heredoc")
        return None
    end = block.find("\n          PYEOF\n", start)
    if end == -1:
        fail("Upsert mep-meta comment step's Python heredoc is unterminated")
        return None
    return block[start:end]


def check_mep_meta_omits_deps_and_recipe_hash_when_absent(text):
    # T6 (ADR-0138 §13/§18): deps/recipe_hash fields are omitted entirely
    # (never emitted empty/null) when no recipe was assembled.
    block = _mep_meta_block(text)
    if block is None:
        return
    if 'recipe_status == "present"' not in block:
        fail(
            "Upsert mep-meta comment step does not condition the deps/"
            "recipe_hash fields on recipe_status == 'present'"
        )
    if '"recipe_hash"' not in block or '"deps"' not in block:
        fail("Upsert mep-meta comment step never emits deps/recipe_hash fields at all")
