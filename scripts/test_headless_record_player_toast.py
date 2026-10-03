"""The Core draws its system toast as W-P3's card in Player style.

User's decision, 2026-10-03 ("Estilizar o HUD do Core"): with
PreferencesConfig.ToastStyle = Player, SystemHud draws a toast as a rounded
dark card (PlayerHudColor, alpha 225) anchored to the bottom-right corner, with
a status glyph and white text - docs/media/gui-redesign/W-P3.png. Classic keeps
the outlined text in the bottom-left corner.

The pixels come from ADR-0167's HUD-only capture: `headless_record capture
hud-message=...` with `hud-style=player|classic` picking the style and
`hud-dump=<prefix>` writing the captured HUD (and the frame under it) as raw
little-endian ARGB, so this case reads real pixels the Core drew. The
`capture hud: ... blank=` contract is checked too, for both styles.

The ROM is a synthetic NROM image (an infinite loop), so no ROM file is needed.
The binary is a build product (`make capture-tool`); when it is absent the case
says so and exits 0, the way the suite's other environment-bound cases do.
"""

import re
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BINARY = ROOT / "scripts" / "headless_record"

HUD_LINE = re.compile(r"capture hud: (\d+)x(\d+) checksum=0x[0-9A-F]+ blank=(\d)")

#HudToastLayout.h's metrics; the card's right and bottom edges.
MARGIN_RIGHT = 8
MARGIN_BOTTOM = 10
CARD_HEIGHT = 17

_FAILURES = []
_CHECKS = []


def check(cond, name, detail=""):
    _CHECKS.append(name)
    print(("ok   " if cond else "FAIL ") + name + ("" if cond else f"\n     {detail}"))
    if not cond:
        _FAILURES.append(name)


def nrom(path):
    """16 KB PRG whose reset vector points at a JMP to itself, 8 KB CHR."""
    prg = bytearray(16384)
    prg[0:3] = b"\x4c\x00\xc0"  # $C000: JMP $C000
    prg[0x3FFA:0x4000] = b"\x00\xc0\x00\xc0\x00\xc0"  # NMI, RESET, IRQ -> $C000
    path.write_bytes(b"NES\x1a\x01\x01" + bytes(10) + bytes(prg) + bytes(8192))


def run(work, args):
    rom = work / "loop.nes"
    nrom(rom)
    prefix = work / "out" / "run"
    prefix.parent.mkdir(parents=True, exist_ok=True)
    proc = subprocess.run(
        [str(BINARY), str(rom), "0.5", str(prefix), "capture", "log"] + args,
        capture_output=True, text=True, timeout=300,
    )
    return proc.returncode, proc.stdout + proc.stderr


def read_argb(path, width, height):
    data = path.read_bytes()
    if len(data) != width * height * 4:
        return None
    return struct.unpack(f"<{width * height}I", data)


def channels(px):
    return (px >> 24) & 0xFF, (px >> 16) & 0xFF, (px >> 8) & 0xFF, px & 0xFF


def capture(work, style, message):
    args = [f"hud-style={style}", f"hud-dump={work / 'dump'}"]
    if message:
        args.append(f"hud-message={message}")
    code, out = run(work, args)
    line = HUD_LINE.search(out)
    pixels = None
    if line:
        w, h = int(line.group(1)), int(line.group(2))
        hud_path = Path(str(work / "dump") + "-hud.argb")
        if hud_path.exists():
            pixels = read_argb(hud_path, w, h)
    return code, out, line, pixels


def test_the_player_toast_is_a_card_in_the_bottom_right_corner():
    message = "MEP|Applied Contra 80s — textures"
    with tempfile.TemporaryDirectory() as pw, tempfile.TemporaryDirectory() as cw, \
            tempfile.TemporaryDirectory() as bw:
        p_code, p_out, p_line, player = capture(Path(pw), "player", message)
        c_code, c_out, c_line, classic = capture(Path(cw), "classic", message)
        b_code, b_out, b_line, _ = capture(Path(bw), "player", None)
        frame_dumped = Path(pw + "/dump-frame.argb").exists()

    check(p_code == 0 and c_code == 0 and b_code == 0, "every run exits 0",
          f"player={p_code} classic={c_code} blank={b_code}\n{p_out[-1500:]}")
    check(p_line is not None and p_line.group(3) == "0", "a Player toast reads blank=0 (ADR-0167)",
          p_line.group(0) if p_line else p_out[-1500:])
    check(b_line is not None and b_line.group(3) == "1", "a Player run with no toast reads blank=1 (ADR-0167)",
          b_line.group(0) if b_line else b_out[-1500:])
    check(c_line is not None and c_line.group(3) == "0", "a Classic toast still reads blank=0",
          c_line.group(0) if c_line else c_out[-1500:])
    check(player is not None and classic is not None, "hud-dump= writes the captured HUD for both styles",
          f"player={player is not None} classic={classic is not None}\n{p_out[-1500:]}")
    check(frame_dumped, "hud-dump= also writes the frame under the HUD")
    if player is None or classic is None:
        return

    w, h = int(p_line.group(1)), int(p_line.group(2))
    #A pixel inside the card, left of its right padding and on its middle row
    #- text glyphs never reach the right padding.
    probe_x = w - MARGIN_RIGHT - 3
    probe_y = h - MARGIN_BOTTOM - CARD_HEIGHT // 2
    a, r, g, b = channels(player[probe_y * w + probe_x])
    check(a == 225 and max(r, g, b) <= 40,
          "the Player card is PlayerHudColor at alpha 225 inside its right padding",
          f"pixel ({probe_x},{probe_y}) = a{a} r{r} g{g} b{b}")
    a, r, g, b = channels(classic[probe_y * w + probe_x])
    check(a == 0, "the Classic toast draws nothing at the card's right edge",
          f"pixel ({probe_x},{probe_y}) = a{a}")

    lit = [(i % w, i // w) for i, px in enumerate(player) if (px >> 24) & 0xFF]
    xs = [x for x, _ in lit]
    ys = [y for _, y in lit]
    check(lit and max(xs) == w - MARGIN_RIGHT - 1 and max(ys) == h - MARGIN_BOTTOM - 1,
          "the card's right and bottom edges sit on the margins",
          f"max x={max(xs) if xs else None} max y={max(ys) if ys else None} (hud {w}x{h})")
    check(lit and min(ys) == h - MARGIN_BOTTOM - CARD_HEIGHT,
          "a one-line card is 17 px tall",
          f"min y={min(ys) if ys else None}")
    #Rounded corner: the card's top-right pixel is not drawn at full alpha.
    corner = player[(h - MARGIN_BOTTOM - CARD_HEIGHT) * w + (w - MARGIN_RIGHT - 1)]
    check(((corner >> 24) & 0xFF) < 225, "the card's top-right corner is rounded",
          f"corner alpha={(corner >> 24) & 0xFF}")
    green = [px for px in player if ((px >> 24) & 0xFF) == 255 and channels(px)[2] > 150 and channels(px)[1] < 120]
    check(len(green) >= 8, "the applied-pack toast carries W-P3's green check", f"green pixels={len(green)}")
    white = [px for px in player if (px & 0xFFFFFF) == 0xFFFFFF and ((px >> 24) & 0xFF) == 255]
    check(len(white) >= 50, "the message is drawn in white on the card", f"white pixels={len(white)}")
    #No black outline: Classic draws the text over a 1-px black halo, Player
    #does not, so the Player HUD has no opaque black pixel.
    black = [px for px in player if px == 0xFF000000]
    check(not black, "the Player toast has no black text outline", f"opaque black pixels={len(black)}")
    classic_black = [px for px in classic if ((px >> 24) & 0xFF) and (px & 0xFFFFFF) == 0]
    check(classic_black, "the Classic toast keeps its black outline")


def main():
    if not BINARY.exists():
        print(f"SKIP: {BINARY} not built (make capture-tool)")
        return 0
    test_the_player_toast_is_a_card_in_the_bottom_right_corner()
    print(f"\n{len(_CHECKS) - len(_FAILURES)}/{len(_CHECKS)} checks passed")
    return 1 if _FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
