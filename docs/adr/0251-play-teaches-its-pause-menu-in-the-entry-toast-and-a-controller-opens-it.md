# ADR-0251: Play teaches its pause menu in the entry toast, and a controller opens it

- Status: accepted (2026-10-03). The user found no way to reach W-P4 while playing. They asked whether a toolbar would help (*"faria sentido alguns atalhos rápidos como uma barra de ferramentas para evita dúvidas?"*), then proposed keeping the bar to the pause (*"que tal a barra aparecer somente no pause?"*). Asked which discovery aids to write down, they picked *"Dica + controle (Recomendado)"*. Implemented 2026-10-03 (`UI/Logic/PlayMenuHint.cs`, unit tests in `UI.Tests/Play/PlayMenuHintTests.cs`, headless tests in `UI.HeadlessTests/PlayEntryToastTests.cs`) on the user's go-ahead, verbatim: *"implemente o que falta na GUI e corrija os bugs. mergeie tudo no main. limpe os brantches e WTs. Use o maximio de paralelismo que puder."* No platform reports a Home/Guide button yet, so the controller binding is Select+Start (Back+Start on XInput) everywhere today.
- Date: 2026-10-03
- Related: PRD Part B §13.5.2, §13.3; ADR-0241, ADR-0249, ADR-0250.
- Supersedes / amends: amends W-P3's toast copy only; the rule that Play shows no chrome while a game runs stays.

## Context

In Play the bar and status line show only while paused (`WorkspaceShell.IsBarVisible`); W-P4 opens with Esc, and nothing says so. On 2026-10-03 the user pressed Esc and saw no overlay, raising "how do I open this screen while playing?": a keyboard player isn't told, a controller player has no binding. Rejected: **a permanent toolbar** (W-P3 "in game, no chrome" and rule 2's count would break) and **revealing the bar on mouse movement**.

## Decision

1. **The bar stays pause-only.** While a game runs in Play nothing is drawn over it; pausing (Esc, or the binding below) shows W-P4 with the bar and status line.
2. **The entry toast teaches the way in.** The Core's game-start toast (W-P3, e.g. "Applied Contra 80s — textures") ends with the menu hint for the first three starts after install, alone if no pack was applied; the count is `PlayMenuHintsShown`. The hint names the starting device's binding: "· Esc for the menu" from the keyboard, "· Home for the menu" or "· Select+Start for the menu" from a controller.
3. **A controller opens W-P4.** `ToggleOverlay` gets a default second binding — Home/Guide where the platform reports one, else Select+Start — through the same path as Esc (`ShortcutHandler`, `PlayEsc`), so the order holds: game → W-P4 → resume, sheets close back to W-P4; rebindable like any shortcut.
4. **Host-free rules.** A rule in `UI/Logic` decides whether a start shows the hint and which binding it names, unit-tested in `UI.Tests` for "never after the third start" and "names the device that started the game".

## Consequences

- W-P3's render gains the hint in its toast, and the PRD's W-P3 text says when.
- Select+Start is a combination a few games use for a soft reset; with Home/Guide preferred, the fallback applies only to pads without one, and the binding can be changed in settings — a game in the library needing Select+Start is the trade-off to revisit.
- One more persisted value (`PlayMenuHintsShown`), never reset by an upgrade.
