# ADR-0247: The client may hold a user's own model key for an external script, but never calls a model itself

- Status: accepted (2026-10-02) and reflected in the docs. The user chose option A of three, verbatim: *"concordo com a opcao A que vc sugeriu"*, then accepted the wording (*"Aceitar"*, same day). Go-ahead for the text edit, verbatim: *"Sim, edite agora (Recomendado)"*, then *"sim, pode seguir"*. PRD Part A §1 principle 5 now carries Decision 1's wording, and the Phase 10 constraint that quoted it was updated with it. ADR-0242's implementation (F14.20) is no longer blocked by this ADR.
- Date: 2026-10-02
- Related: PRD Part A §1 (principle 5), PRD Part A §4 Phase 10 ("Constraints that hold regardless of outcome"), ADR-0154 (what an external tool may send off the machine; §4), ADR-0192, ADR-0242 (AI recorder, Q1: OS credential store), ADR-0245 (cheats; Decision 4's LLM phases), ADR-0238 (Jev harness), ADR-0188 (an AI's judgement is a proposal, never evidence), ADR-0199 (tool-free Gemini call in CI)
- Supersedes / amends: amends PRD Part A §1 principle 5. It makes ADR-0242's Q1 and Decision 4 consistent with that principle, and constrains ADR-0245 Decision 4 (below). ADR-0154 is unchanged.

## Context

Part A §1 principle 5 reads:

> **No LLM in the client.** LLMs run only in CI (the community-pack
> classify step); whatever they emit is validated by deterministic scripts
> before a human or the client sees it. `Core/`, `UI/` and the installer
> never call a model, hold a key or carry a prompt. An external tool under
> `scripts/` is not the client, but what it may send off the machine is
> governed by ADR-0154, not by this principle (see Phase 10).

ADR-0242, accepted on 2026-10-02, keeps most of it:

- `scripts/jev_harness.py`, an external script, makes every model call;
- its artifact is a button script that replays without the model;
- only numbers read from RAM leave the machine.

It breaks one clause. The UI asks for the user's OpenRouter key, stores
it in the OS credential store (Q1) and passes it to the child process.
That is "hold a key" in `UI/`. The conflict surfaced while re-reading
§1 for the Share upload question, after ADR-0242 was already accepted.

Three ways out were put to the user:

- **A.** A narrow custody exception in the principle.
- **B.** Keep the principle strict: the script asks for and stores the key
  itself (Python `keyring`), and the UI never sees it.
- **C.** Return ADR-0242 to `proposed` until the wording is settled.

B keeps the letter, not the intent. The user would get a second key
prompt outside the app, and a new Python dependency. The key would still
sit on the same machine, in the same credential store. C reopens a decision
over wording.

What the principle protects, and must keep protecting:

- the client has no vendor dependency, and no model call of its own;
- nothing a model produces becomes truth in the client without a
  deterministic check (ADR-0188);
- the project ships no key and pays for nothing;
- egress stays under ADR-0154.

Non-goals:

- No model call from `Core/`, `UI/` or the installer, not even a "small"
  one (for example, ADR-0245's search by intent). Every call runs in a
  script.
- No change to what may leave the machine. ADR-0154 §4 still forbids
  sending ROM-derived art.
- No key bundled, defaulted or proxied by the project.

## Decision

1. **Principle 5 becomes:**

   > **No LLM in the client.** `Core/`, `UI/` and the installer never call
   > a model, never carry a prompt and never ship a key. Models run in CI
   > (the community-pack classify step) or in an external script under
   > `scripts/` that the user starts. The client may keep a key **the user
   > entered** in the OS credential store, and hand it to such a script
   > only through the child process's environment — never on a command
   > line, in `settings.json`, logs, `runs/` sidecars or crash reports
   > the app writes.
   > Whatever a model returns reaches the client only as data checked by
   > deterministic code. What a script may send off the machine is governed
   > by ADR-0154, not by this principle.

2. **What counts as "checked by deterministic code"** is defined per
   feature, in its ADR:
   - ADR-0242 (AI recorder): the script replays through the ordinary
     recorder without the model;
   - ADR-0245 phase 1 (search by intent): the answer must be one of the
     listed database entries, and anything else is discarded;
   - ADR-0245 phase 2 (web lookup): a headless RAM check on the user's
     copy.

   A feature whose output cannot be checked this way does not qualify for
   the exception.
3. **One custody interface** (ADR-0242 Q1) serves every BYOK feature:
   - macOS Keychain, Windows Credential Manager, libsecret on Linux;
   - one entry per vendor, removable by the user from the feature's
     sheet (*Change…* / *Remove key*).

   Phase 10's skin studio, if it ever ships with a hosted model, uses the
   same interface and the same rule.
4. **ADR-0245 Decision 4 runs as scripts.** Search by intent and the web
   lookup are external scripts started from the cheats sheet, like
   `jev_harness.py`. The cheats sheet never calls a model itself.

## Consequences

- ADR-0242 needs no change in substance; it gains an "Amended by" line.
  F14.20 must not start before this is accepted and principle 5 is edited,
  or the code would contradict a principle that still reads the old way.
- ADR-0245's LLM phases are slower to build: each needs a script, a
  process boundary and the job card (W-R3 pattern). In exchange, the
  client gains no model client.
- The client becomes the place where a user's secret lives. A leak through
  logs or crash reports is now possible in principle, so every BYOK slice
  ships a test that the key never reaches the sinks the app writes:
  `settings.json`, logs, `runs/` sidecars, the child's argv, and the text
  of `MesenMsgBox.ShowException` (the only crash surface today; there is no
  crash reporter). The key is read from the credential store only when a
  job starts and is not kept in a long-lived field. An OS-level dump
  (macOS crash reporter, Windows WER, a core dump) of the client or the
  child can still contain it; that is outside what the app controls, and
  the key sheet says the key is stored on this computer.
- The Phase 10 constraint "no model call, key or prompt in `Core/`, `UI/`
  or the installer" is read against the new wording: the studio may
  receive a key from the client, and still calls the model only from
  `scripts/`.
