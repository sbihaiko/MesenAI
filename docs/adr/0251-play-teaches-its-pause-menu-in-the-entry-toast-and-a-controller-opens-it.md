# ADR-0251: Play teaches its pause menu in the entry toast, and a controller opens it

- Status: accepted (2026-10-03). The user found no way to reach W-P4 while playing. They asked whether a toolbar would help (*"faria sentido alguns atalhos rápidos como uma barra de ferramentas para evita dúvidas?"*), then proposed keeping the bar to the pause (*"que tal a barra aparecer somente no pause?"*). Asked which discovery aids to write down, they picked *"Dica + controle (Recomendado)"*. This is a pending PRD Part B slice; it waits for a go-ahead.
- Date: 2026-10-03
- Related: PRD Part B §13.5.2 (W-P3 playing, W-P4 pause overlay), §13.3 rules 2 and 8; ADR-0241 (workspaces); ADR-0249 (renders are the spec); ADR-0250 (one place per door).
- Supersedes / amends: amends W-P3's toast copy only. The rule that Play shows no chrome while a game runs stays.

## Context

In Play the game fills the window. The bar and status line show only while the game is paused (`WorkspaceShell.IsBarVisible`), and W-P4, the pause overlay, opens with Esc. Nothing on screen says so. On 2026-10-03 the user pressed Esc and saw no overlay: the overlay was behind the native picture, a bug fixed separately. The question it raised, "how do I open this screen while playing?", stands on its own. A player who doesn't know Esc has no way in, and a player holding a controller has none at all, because only the keyboard is bound.

Two options were rejected:

- **A permanent toolbar.** It breaks W-P3 ("in game, no chrome") and rule 2's count, and covers the game to answer a question asked once.
- **Revealing the bar on mouse movement.** The user chose to keep the bar to the pause. On macOS, an Avalonia control over the game is also hidden behind the native picture unless the picture steps aside.

## Decision

1. **The bar stays a pause-only surface.** While a game runs in Play nothing is drawn over it. Pausing (Esc, or the controller binding below) shows W-P4 together with the bar and the status line.
2. **The entry toast teaches the way in.** The toast the Core draws when a game starts (W-P3, e.g. "Applied Contra 80s — textures") ends with "· Esc for the menu" during the first three game starts after install. A game started without a pack gets a toast with the hint alone during those three starts. The count is a persisted integer, `PlayMenuHintsShown`. The hint names the binding of the device that started the game: "· Esc for the menu" from the keyboard, "· Home for the menu" or "· Select+Start for the menu" from a controller.
3. **A controller opens W-P4.** `ToggleOverlay` gets a default second binding: the controller's Home/Guide button where the platform reports one, otherwise Select and Start pressed together. The binding goes through the same path as Esc (`ShortcutHandler`, `PlayEsc`), so the Esc order holds: game → W-P4 → resume, and sheets close back to W-P4. Players can rebind it in the shortcut settings, like any shortcut.
4. **Host-free rules.** A rule in `UI/Logic` decides whether a start shows the hint and which binding it names. It is unit-tested in `UI.Tests`, including "never after the third start" and "names the device that started the game".

## Consequences

- W-P3's render gains the hint in its toast, and the PRD's W-P3 text says when it shows.
- Select+Start is a combination a few games use for a soft reset. With Home/Guide preferred, the fallback applies only to pads without one, and the binding can be changed in settings. If a game in the library needs Select+Start, that is the trade-off to revisit.
- One more persisted value (`PlayMenuHintsShown`). It is never reset by an upgrade.
