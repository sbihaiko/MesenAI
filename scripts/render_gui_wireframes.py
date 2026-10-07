#!/usr/bin/env python3
"""Render the GUI redesign wireframes (PRD Part B §13, ADR-0241) as PNGs.

Writes one PNG per wireframe id (W-S1 ... W-X3) into docs/media/gui-redesign/.
The PNGs are the visual reference for implementation; the ASCII wireframes in
the PRD stay the structural spec (elements, order, wording). Every game image
is an abstract placeholder drawn here, never art from a game.

Requires Pillow (scripts/requirements.txt) and the macOS system fonts
(SF Pro, /System/Library/Fonts/SFNS.ttf); exits 2 when they are missing.

Usage: python3 scripts/render_gui_wireframes.py [--out DIR] [--only W-R1,...]
"""
import argparse
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT_PATH = "/System/Library/Fonts/SFNS.ttf"
S = 2                       # pixel scale (logical px -> image px)
IMG_W, IMG_H = 1200, 840    # logical image size
WIN = (50, 70, 1150, 810)   # logical window box

# Palette (macOS light appearance)
TEXT = (29, 29, 31)
TEXT2 = (110, 110, 115)
TEXT3 = (161, 161, 166)
SEP = (229, 229, 234)
WINBG = (245, 245, 247)
CARD = (255, 255, 255)
FILL = (233, 233, 236)
RED = (255, 59, 48)
ORANGE = (255, 159, 10)
TINT = {"play": (0, 122, 255), "remaster": (175, 82, 222), "share": (52, 199, 89)}
TINT_TEXT = {"play": (0, 102, 220), "remaster": (150, 60, 200), "share": (36, 138, 61)}
NAME = {"play": "Play", "remaster": "Remaster", "share": "Share"}

_fonts = {}


def font(size, weight=400):
    key = (size, weight)
    if key not in _fonts:
        f = ImageFont.truetype(FONT_PATH, int(size * S))
        f.set_variation_by_axes([100, max(17, min(96, size)), 400, weight])
        _fonts[key] = f
    return _fonts[key]


def sc(v):
    return int(round(v * S))


def scb(b):
    return tuple(sc(v) for v in b)


class Canvas:
    def __init__(self):
        self.img = Image.new("RGBA", (IMG_W * S, IMG_H * S), (0, 0, 0, 255))
        self.d = ImageDraw.Draw(self.img)
        # backdrop gradient
        top, bot = (214, 222, 233), (238, 240, 245)
        for y in range(IMG_H * S):
            t = y / (IMG_H * S)
            c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
            self.d.line([(0, y), (IMG_W * S, y)], fill=c)

    # --- primitives -------------------------------------------------------
    def rrect(self, b, r, fill=None, outline=None, width=1):
        self.d.rounded_rectangle(scb(b), radius=sc(r), fill=fill, outline=outline,
                                 width=max(1, sc(width)) if outline else 0)

    def shadow(self, b, r, blur=18, dy=8, alpha=60):
        pad = blur * 3
        x0, y0, x1, y1 = b
        region = (x0 - pad, y0 - pad + dy, x1 + pad, y1 + pad + dy)
        w, h = sc(region[2] - region[0]), sc(region[3] - region[1])
        mask = Image.new("L", (w, h), 0)
        ImageDraw.Draw(mask).rounded_rectangle(
            (sc(pad), sc(pad), sc(pad + x1 - x0), sc(pad + y1 - y0)), radius=sc(r), fill=alpha)
        mask = mask.filter(ImageFilter.GaussianBlur(sc(blur)))
        layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        layer.putalpha(mask)
        self.img.alpha_composite(layer, (sc(region[0]), sc(region[1])))
        self.d = ImageDraw.Draw(self.img)

    def overlay(self, b, rgba, r=0):
        layer = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).rounded_rectangle(scb(b), radius=sc(r), fill=rgba)
        self.img.alpha_composite(layer)
        self.d = ImageDraw.Draw(self.img)

    def blur(self, b, radius=6):
        box = scb(b)
        part = self.img.crop(box).filter(ImageFilter.GaussianBlur(sc(radius)))
        self.img.paste(part, box)
        self.d = ImageDraw.Draw(self.img)

    def text(self, x, y, s, size=13, weight=400, color=TEXT, anchor="la"):
        self.d.text((sc(x), sc(y)), s, font=font(size, weight), fill=color, anchor=anchor)
        return self.tw(s, size, weight)

    def tw(self, s, size=13, weight=400):
        return self.d.textlength(s, font=font(size, weight)) / S

    def para(self, x, y, s, width, size=13, weight=400, color=TEXT2, lh=1.38, anchor="la"):
        words, line, lines = s.split(), "", []
        for w in words:
            t = (line + " " + w).strip()
            if self.tw(t, size, weight) > width and line:
                lines.append(line)
                line = w
            else:
                line = t
        if line:
            lines.append(line)
        for i, ln in enumerate(lines):
            xx = x if anchor[0] == "l" else x
            self.text(xx, y + i * size * lh, ln, size, weight, color, anchor)
        return len(lines) * size * lh

    def line(self, pts, color=SEP, width=1):
        self.d.line([(sc(a), sc(b)) for a, b in pts], fill=color, width=max(1, sc(width)))

    def circle(self, cx, cy, r, fill=None, outline=None, width=1):
        self.d.ellipse(scb((cx - r, cy - r, cx + r, cy + r)), fill=fill, outline=outline,
                       width=max(1, sc(width)) if outline else 0)

    def poly(self, pts, fill):
        self.d.polygon([(sc(a), sc(b)) for a, b in pts], fill=fill)

    # --- icons --------------------------------------------------------------
    def icon(self, kind, cx, cy, s=14, color=TEXT):
        h = s / 2
        if kind == "play":
            self.poly([(cx - h * 0.6, cy - h * 0.8), (cx - h * 0.6, cy + h * 0.8), (cx + h * 0.85, cy)], color)
        elif kind == "brush":
            self.line([(cx - h * 0.7, cy + h * 0.7), (cx + h * 0.55, cy - h * 0.55)], color, s * 0.18)
            self.poly([(cx + h * 0.35, cy - h * 0.85), (cx + h * 0.85, cy - h * 0.35),
                       (cx + h * 0.65, cy - h * 0.15), (cx + h * 0.15, cy - h * 0.65)], color)
            self.circle(cx - h * 0.72, cy + h * 0.72, s * 0.13, fill=color)
        elif kind == "box":
            self.poly([(cx, cy - h * 0.9), (cx + h * 0.85, cy - h * 0.45), (cx, cy), (cx - h * 0.85, cy - h * 0.45)], color)
            self.poly([(cx - h * 0.85, cy - h * 0.3), (cx - h * 0.08, cy + h * 0.12), (cx - h * 0.08, cy + h * 0.95), (cx - h * 0.85, cy + h * 0.5)], color)
            self.poly([(cx + h * 0.85, cy - h * 0.3), (cx + h * 0.08, cy + h * 0.12), (cx + h * 0.08, cy + h * 0.95), (cx + h * 0.85, cy + h * 0.5)], color)
        elif kind == "film":
            self.rrect((cx - h * 0.85, cy - h * 0.65, cx + h * 0.85, cy + h * 0.65), s * 0.12, fill=color)
            for i in range(4):
                xx = cx - h * 0.6 + i * h * 0.4
                self.rrect((xx - s * 0.05, cy - h * 0.55, xx + s * 0.05, cy - h * 0.35), 0, fill=CARD)
                self.rrect((xx - s * 0.05, cy + h * 0.35, xx + s * 0.05, cy + h * 0.55), 0, fill=CARD)
        elif kind == "dots":
            for dx in (-0.55, 0, 0.55):
                self.circle(cx + dx * h, cy, s * 0.085, fill=color)
        elif kind == "chev_down":
            self.line([(cx - h * 0.45, cy - h * 0.2), (cx, cy + h * 0.25), (cx + h * 0.45, cy - h * 0.2)], color, s * 0.13)
        elif kind == "chev_right":
            self.line([(cx - h * 0.2, cy - h * 0.45), (cx + h * 0.25, cy), (cx - h * 0.2, cy + h * 0.45)], color, s * 0.13)
        elif kind == "chev_left":
            self.line([(cx + h * 0.2, cy - h * 0.45), (cx - h * 0.25, cy), (cx + h * 0.2, cy + h * 0.45)], color, s * 0.13)
        elif kind == "updown":
            self.line([(cx - h * 0.35, cy - h * 0.15), (cx, cy - h * 0.5), (cx + h * 0.35, cy - h * 0.15)], color, s * 0.11)
            self.line([(cx - h * 0.35, cy + h * 0.15), (cx, cy + h * 0.5), (cx + h * 0.35, cy + h * 0.15)], color, s * 0.11)
        elif kind == "check":
            self.line([(cx - h * 0.55, cy), (cx - h * 0.15, cy + h * 0.45), (cx + h * 0.6, cy - h * 0.45)], color, s * 0.15)
        elif kind == "warn":
            self.poly([(cx, cy - h * 0.9), (cx + h * 0.95, cy + h * 0.75), (cx - h * 0.95, cy + h * 0.75)], color)
            self.line([(cx, cy - h * 0.3), (cx, cy + h * 0.25)], CARD, s * 0.12)
            self.circle(cx, cy + h * 0.5, s * 0.06, fill=CARD)
        elif kind == "record":
            self.circle(cx, cy, h * 0.7, fill=color)
        elif kind == "stop":
            self.rrect((cx - h * 0.55, cy - h * 0.55, cx + h * 0.55, cy + h * 0.55), s * 0.08, fill=color)
        elif kind == "folder":
            self.rrect((cx - h * 0.9, cy - h * 0.6, cx - h * 0.1, cy - h * 0.2), s * 0.08, fill=color)
            self.rrect((cx - h * 0.9, cy - h * 0.4, cx + h * 0.9, cy + h * 0.7), s * 0.1, fill=color)
        elif kind == "arrow_ur":
            self.line([(cx - h * 0.5, cy + h * 0.5), (cx + h * 0.5, cy - h * 0.5)], color, s * 0.13)
            self.line([(cx - h * 0.1, cy - h * 0.5), (cx + h * 0.5, cy - h * 0.5), (cx + h * 0.5, cy + h * 0.1)], color, s * 0.13)
        elif kind == "gear":
            self.circle(cx, cy, h * 0.75, outline=color, width=s * 0.16)
            self.circle(cx, cy, h * 0.22, fill=color)
            for i in range(8):
                import math
                a = i * math.pi / 4
                self.circle(cx + math.cos(a) * h * 0.82, cy + math.sin(a) * h * 0.82, s * 0.09, fill=color)
        elif kind == "sparkle":
            self.poly([(cx, cy - h), (cx + h * 0.22, cy - h * 0.22), (cx + h, cy), (cx + h * 0.22, cy + h * 0.22),
                       (cx, cy + h), (cx - h * 0.22, cy + h * 0.22), (cx - h, cy), (cx - h * 0.22, cy - h * 0.22)], color)
        elif kind == "lock":
            self.rrect((cx - h * 0.6, cy - h * 0.1, cx + h * 0.6, cy + h * 0.8), s * 0.08, fill=color)
            self.d.arc(scb((cx - h * 0.4, cy - h * 0.75, cx + h * 0.4, cy + h * 0.15)), 180, 360, fill=color, width=sc(s * 0.12))
        elif kind == "pencil":
            self.line([(cx - h * 0.6, cy + h * 0.6), (cx + h * 0.5, cy - h * 0.5)], color, s * 0.22)
        elif kind == "thumb":
            self.rrect((cx - h * 0.8, cy - h * 0.1, cx - h * 0.4, cy + h * 0.8), s * 0.05, fill=color)
            self.rrect((cx - h * 0.3, cy - h * 0.15, cx + h * 0.75, cy + h * 0.8), s * 0.12, fill=color)
            self.rrect((cx - h * 0.25, cy - h * 0.85, cx + h * 0.15, cy), s * 0.12, fill=color)
        elif kind == "plus":
            self.line([(cx - h * 0.6, cy), (cx + h * 0.6, cy)], color, s * 0.14)
            self.line([(cx, cy - h * 0.6), (cx, cy + h * 0.6)], color, s * 0.14)

    def badge(self, kind, x, y, size, tint):
        """Rounded-square app-style icon badge with a white glyph."""
        self.rrect((x, y, x + size, y + size), size * 0.26, fill=tint)
        self.icon(kind, x + size / 2, y + size / 2, size * 0.58, CARD)

    # --- controls -----------------------------------------------------------
    def button(self, x, y, label, kind="secondary", tint=TINT["play"], h=28, size=13, w=None,
               icon=None, anchor="l", disabled=False):
        weight = 590 if kind == "primary" else 500
        tw = self.tw(label, size, weight)
        iw = (size + 6) if icon else 0
        width = w or tw + iw + (h * 0.95)
        if anchor == "r":
            x -= width
        elif anchor == "c":
            x -= width / 2
        b = (x, y, x + width, y + h)
        r = min(h / 2, 8 if h <= 32 else 11)
        alpha = 0.4 if disabled else 1.0

        def fade(c):
            return tuple(int(c[i] * alpha + 255 * (1 - alpha)) for i in range(3))

        if kind == "primary":
            self.shadow(b, r, blur=3, dy=1, alpha=40)
            self.rrect(b, r, fill=fade(tint))
            fg = CARD
        elif kind == "secondary":
            self.shadow(b, r, blur=2, dy=1, alpha=28)
            self.rrect(b, r, fill=CARD, outline=(214, 214, 219))
            fg = fade(TEXT)
        elif kind == "tinted":
            soft = tuple(int(tint[i] * 0.14 + 255 * 0.86) for i in range(3))
            self.rrect(b, r, fill=soft)
            fg = fade(tint)
        elif kind == "destructive":
            self.rrect(b, r, fill=(255, 235, 234))
            fg = RED
        else:  # plain
            fg = fade(tint)
        cx = x + width / 2 + iw / 2
        if icon:
            self.icon(icon, cx - tw / 2 - iw / 2 - 1, y + h / 2, size * 0.95, fg)
        self.text(cx, y + h / 2, label, size, weight, fg, "mm")
        return b

    def popup(self, x, y, w, label, h=24):
        b = (x, y, x + w, y + h)
        self.shadow(b, 6, blur=2, dy=1, alpha=26)
        self.rrect(b, 6, fill=CARD, outline=(214, 214, 219))
        self.text(x + 10, y + h / 2, label, 13, 400, TEXT, "lm")
        self.rrect((x + w - 20, y + 3, x + w - 3, y + h - 3), 4, fill=TINT["play"])
        self.icon("updown", x + w - 11.5, y + h / 2, 12, CARD)

    def field(self, x, y, w, value, placeholder=False, h=30, focused=False):
        b = (x, y, x + w, y + h)
        if focused:
            self.rrect((x - 3, y - 3, x + w + 3, y + h + 3), 9, fill=(190, 218, 255))
        self.rrect(b, 7, fill=CARD, outline=(206, 206, 212))
        self.text(x + 10, y + h / 2, value, 13, 400, TEXT3 if placeholder else TEXT, "lm")

    def toggle(self, x, y, on, enabled=True):
        w, h = 38, 22
        c = (52, 199, 89) if on else (220, 220, 225)
        if not enabled:
            c = (235, 235, 238)
        self.rrect((x, y, x + w, y + h), h / 2, fill=c)
        kx = x + w - h / 2 if on else x + h / 2
        self.shadow((kx - 9, y + 2, kx + 9, y + 20), 9, blur=1.5, dy=1, alpha=50)
        self.circle(kx, y + h / 2, 9, fill=CARD)

    def segmented(self, x, y, items, sel, h=26, size=12.5, item_w=None):
        widths = [item_w or (self.tw(t, size, 500) + 26) for t in items]
        total = sum(widths) + 4
        self.rrect((x, y, x + total, y + h), 8, fill=FILL)
        xx = x + 2
        for i, (t, w) in enumerate(zip(items, widths)):
            if i == sel:
                self.shadow((xx, y + 2, xx + w, y + h - 2), 6, blur=2, dy=1, alpha=35)
                self.rrect((xx, y + 2, xx + w, y + h - 2), 6, fill=CARD)
            self.text(xx + w / 2, y + h / 2, t, size, 590 if i == sel else 500, TEXT, "mm")
            xx += w
        return total

    def progress(self, x, y, w, frac, tint, h=6):
        self.rrect((x, y, x + w, y + h), h / 2, fill=FILL)
        if frac > 0:
            self.rrect((x, y, x + max(h, w * frac), y + h), h / 2, fill=tint)

    def card(self, b, r=12, shadow=True):
        if shadow:
            self.shadow(b, r, blur=6, dy=2, alpha=22)
        self.rrect(b, r, fill=CARD, outline=(232, 232, 236))

    def row(self, x0, x1, y, h, title, value=None, chev=True, sep=True, icon=None, tint=None,
            title_color=TEXT, value_color=TEXT2):
        tx = x0 + 14
        if icon:
            self.badge(icon, x0 + 12, y + (h - 26) / 2, 26, tint)
            tx = x0 + 48
        self.text(tx, y + h / 2, title, 13.5, 500, title_color, "lm")
        rx = x1 - 14
        if chev:
            self.icon("chev_right", rx - 4, y + h / 2, 12, TEXT3)
            rx -= 18
        if value:
            self.text(rx, y + h / 2, value, 13, 400, value_color, "rm")
        if sep:
            self.line([(tx, y + h), (x1, y + h)], SEP)

    # --- window chrome ------------------------------------------------------
    def window(self, bg=WINBG):
        x0, y0, x1, y1 = WIN
        self.shadow(WIN, 12, blur=26, dy=14, alpha=70)
        self.rrect(WIN, 12, fill=bg, outline=(200, 200, 205))

    def lights(self):
        x0, y0 = WIN[0], WIN[1]
        for i, c in enumerate([(255, 95, 87), (254, 188, 46), (40, 200, 64)]):
            self.circle(x0 + 20 + i * 20, y0 + 26, 6, fill=c)

    def titlebar(self, profile, open_switcher=False, tools_open=False):
        x0, y0, x1, y1 = WIN
        bar = (x0, y0, x1, y0 + 52)
        self.d.rounded_rectangle(scb(bar), radius=sc(12), fill=(250, 250, 251))
        self.d.rectangle(scb((x0, y0 + 30, x1, y0 + 52)), fill=(250, 250, 251))
        self.line([(x0, y0 + 52), (x1, y0 + 52)], (222, 222, 226))
        self.lights()
        # profile switcher: one profile at a time, the others only inside the popover
        px = x0 + 90
        pill = (px, y0 + 12, px + 150, y0 + 40)
        if open_switcher:
            self.rrect(pill, 8, fill=(232, 232, 236))
        self.badge({"play": "play", "remaster": "brush", "share": "box"}[profile], px + 6, y0 + 15, 22,
                   TINT[profile])
        self.text(px + 36, y0 + 26, NAME[profile], 15, 650, TEXT, "lm")
        self.icon("chev_down", px + 44 + self.tw(NAME[profile], 15, 650), y0 + 27, 12, TEXT2)
        # tools menu
        tb = (x1 - 46, y0 + 12, x1 - 14, y0 + 40)
        if tools_open:
            self.rrect(tb, 8, fill=(232, 232, 236))
        self.icon("dots", x1 - 30, y0 + 26, 18, TEXT2)

    def footer(self, s, dot=(52, 199, 89)):
        x0, y0, x1, y1 = WIN
        self.line([(x0, y1 - 26), (x1, y1 - 26)], (226, 226, 230))
        self.circle(x0 + 18, y1 - 13, 3.5, fill=dot)
        self.text(x0 + 28, y1 - 13, s, 11.5, 400, TEXT2, "lm")

    def content(self):
        x0, y0, x1, y1 = WIN
        return (x0, y0 + 53, x1, y1 - 27)

    def caption(self, wid, title, count=None, limit=7):
        self.text(52, 34, wid, 15, 700, TEXT)
        self.text(52 + self.tw(wid, 15, 700) + 12, 34, title, 15, 500, TEXT2)
        if count is not None:
            ok = count <= limit
            label = f"{count} control{'' if count == 1 else 's'} at rest" + ("" if ok else f" · over the limit of {limit}")
            w = self.tw(label, 12, 600) + 22
            c = (36, 138, 61) if ok else (200, 40, 30)
            bg = (226, 245, 231) if ok else (255, 231, 229)
            self.rrect((1150 - w, 24, 1150, 46), 11, fill=bg)
            self.text(1150 - w / 2, 35, label, 12, 600, c, "mm")

    # --- placeholders -------------------------------------------------------
    def scene(self, b, seed=0, dim=0.0):
        """Abstract pixel-art placeholder for a game frame (no real game art)."""
        rnd = random.Random(seed)
        x0, y0, x1, y1 = b
        pal = [((24, 32, 72), (60, 110, 170)), ((40, 20, 60), (190, 90, 120)),
               ((10, 40, 40), (60, 160, 140)), ((30, 30, 30), (110, 110, 140)),
               ((60, 30, 10), (210, 140, 60))][seed % 5]
        H = y1 - y0
        steps = 24
        for i in range(steps):
            t = i / steps
            c = tuple(int(pal[0][k] + (pal[1][k] - pal[0][k]) * t) for k in range(3))
            self.d.rectangle(scb((x0, y0 + H * i / steps, x1, y0 + H * (i + 1) / steps + 1)), fill=c)
        cell = H / 24
        # hills
        hill = tuple(max(0, v - 40) for v in pal[1])
        x = x0
        while x < x1:
            hw = cell * rnd.randint(4, 9)
            hh = cell * rnd.randint(3, 7)
            for k in range(int(hh / cell)):
                inset = k * cell
                hx0, hx1 = max(x0, x + inset), min(x1, x + hw - inset)
                if hx1 > hx0:
                    self.d.rectangle(scb((hx0, y1 - cell * 4 - (k + 1) * cell, hx1, y1 - cell * 4 - k * cell)), fill=hill)
            x += hw * 0.8
        # clouds
        for _ in range(3):
            cx = x0 + rnd.random() * (x1 - x0 - cell * 6)
            cy = y0 + cell * rnd.randint(2, 7)
            for dx, dy, w in ((0, 1, 6), (1, 0, 4), (2, -1, 2)):
                self.d.rectangle(scb((cx + dx * cell, cy + dy * cell, cx + (dx + w) * cell, cy + (dy + 1) * cell)),
                                 fill=(235, 240, 245))
        # ground bricks
        g1, g2 = (150, 80, 40), (110, 55, 30)
        for row in range(4):
            yy = y1 - (row + 1) * cell
            off = (row % 2) * cell
            xx = x0 - off
            while xx < x1:
                self.d.rectangle(scb((max(x0, xx), yy, min(x1, xx + cell * 2 - 1 / S), yy + cell - 1 / S)), fill=g1 if (row % 2) else g2)
                xx += cell * 2
        # floating blocks
        for _ in range(2):
            bx = x0 + rnd.random() * (x1 - x0 - cell * 4)
            by = y1 - cell * rnd.randint(9, 12)
            for k in range(rnd.randint(2, 4)):
                self.d.rectangle(scb((bx + k * cell, by, bx + (k + 1) * cell - 1 / S, by + cell - 1 / S)), fill=(220, 170, 60))
        # a figure made of squares
        fx = x0 + (x1 - x0) * 0.3
        fy = y1 - cell * 4
        shape = ["..rr..", ".rrrr.", "..ss..", ".bbbb.", "bbbbbb", ".b..b.", ".b..b."]
        cols = {"r": (220, 60, 50), "s": (240, 200, 160), "b": (50, 80, 200)}
        for j, line in enumerate(shape):
            for i, ch in enumerate(line):
                if ch != ".":
                    self.d.rectangle(scb((fx + i * cell * 0.5, fy - (len(shape) - j) * cell * 0.5,
                                          fx + (i + 1) * cell * 0.5, fy - (len(shape) - j - 1) * cell * 0.5)), fill=cols[ch])
        if dim:
            self.overlay(b, (0, 0, 0, int(255 * dim)))

    def figure(self, b, seed, dim=False):
        rnd = random.Random(seed * 7 + 3)
        x0, y0, x1, y1 = b
        n = 8
        cw = (x1 - x0) / n
        cols = [(220, 60, 50), (50, 80, 200), (240, 200, 160), (60, 160, 90), (240, 180, 40)]
        c1, c2 = rnd.sample(cols, 2)
        for j in range(n):
            for i in range(n // 2):
                if rnd.random() < 0.55 or (j in (2, 3) and i >= 1):
                    c = c1 if j < 4 else c2
                    if dim:
                        c = tuple(int(v * 0.35 + 235 * 0.65) for v in c)
                    for ii in (i, n - 1 - i):
                        self.d.rectangle(scb((x0 + ii * cw, y0 + j * cw, x0 + (ii + 1) * cw, y0 + (j + 1) * cw)), fill=c)

    # --- composite surfaces ---------------------------------------------------
    def sheet(self, w, h, title=None, dim=True):
        cx0, cy0, cx1, cy1 = WIN
        if dim:
            self.overlay((cx0, cy0 + 53, cx1, cy1), (0, 0, 0, 70), 0)
        x = (cx0 + cx1) / 2 - w / 2
        y = cy0 + 53 + 24
        b = (x, y, x + w, y + h)
        self.shadow(b, 14, blur=22, dy=10, alpha=90)
        self.rrect(b, 14, fill=CARD, outline=(220, 220, 225))
        if title:
            self.text(x + 24, y + 30, title, 17, 700, TEXT, "lm")
        return b

    def hud(self, b, r=12):
        self.shadow(b, r, blur=12, dy=4, alpha=80)
        self.overlay(b, (30, 30, 32, 225), r)

    def save(self, path):
        self.img.convert("RGB").save(path, optimize=True)


# ============================================================================
# Screens
# ============================================================================

def base(profile, status="No game loaded", dot=TEXT3, **kw):
    c = Canvas()
    c.window()
    c.titlebar(profile, **kw)
    c.footer(status, dot)
    return c


def play_home_recents(c):
    x0, y0, x1, y1 = c.content()
    c.text(x0 + 40, y0 + 42, "Continue playing", 22, 700)
    c.button(x1 - 40, y0 + 28, "Open a ROM…", "secondary", anchor="r", icon="folder")
    hero = (x0 + 40, y0 + 70, x1 - 40, y0 + 230)
    c.card(hero, 16)
    c.d.rounded_rectangle(scb((hero[0], hero[1], hero[0] + 260, hero[3])), radius=sc(16), fill=(0, 0, 0))
    c.scene((hero[0], hero[1], hero[0] + 260, hero[3]), 0)
    c.d.rectangle(scb((hero[0] + 244, hero[1], hero[0] + 260, hero[3])), fill=CARD)
    c.text(hero[0] + 288, hero[1] + 40, "Contra (USA)", 20, 700)
    c.text(hero[0] + 288, hero[1] + 66, "Last played today · Contra 80s 1.2", 13, 400, TEXT2)
    c.button(hero[0] + 288, hero[1] + 96, "Continue", "primary", TINT["play"], h=36, size=14, icon="play")
    c.text(x0 + 40, y0 + 270, "Recent", 15, 650)
    titles = ["Castlevania", "The Legend of Zelda", "Mega Man", "Metroid", "Punch-Out!!"]
    packs = [True, True, False, True, False]
    tw, th = 176, 132
    for i, (t, p) in enumerate(zip(titles, packs)):
        tx = x0 + 40 + i * (tw + 22)
        ty = y0 + 290
        c.shadow((tx, ty, tx + tw, ty + th), 10, blur=5, dy=2, alpha=40)
        c.d.rounded_rectangle(scb((tx, ty, tx + tw, ty + th)), radius=sc(10), fill=(0, 0, 0))
        c.scene((tx, ty, tx + tw, ty + th), i + 1)
        c.text(tx, ty + th + 16, t, 13, 590)
        if p:
            c.rrect((tx + tw - 30, ty + 8, tx + tw - 8, ty + 30), 6, fill=(255, 255, 255))
            c.icon("box", tx + tw - 19, ty + 19, 13, TINT["share"])


def remaster_project(c, record_state="idle", see_state="ready", popovers=False, record_enabled=True):
    x0, y0, x1, y1 = c.content()
    tint = TINT["remaster"]
    tw0 = c.tw("Contra (USA)", 22, 700)
    c.rrect((x0 + 24, y0 + 18, x0 + 32 + tw0 + 30, y0 + 50), 8, fill=(234, 234, 238))
    c.text(x0 + 32, y0 + 34, "Contra (USA)", 22, 700, TEXT, "lm")
    c.icon("chev_down", x0 + 32 + tw0 + 15, y0 + 35, 13, TEXT2)
    c.text(x0 + 32 + tw0 + 42, y0 + 35, "Project", 15, 500, TEXT2, "lm")
    c.text(x1 - 32, y0 + 35, "Project menu: switch, show folder, compose, scripted recording", 11.5, 400, TEXT3, "rm")

    def step(n, label, y):
        c.circle(x0 + 44, y, 11, fill=tint)
        c.text(x0 + 44, y, str(n), 12.5, 700, CARD, "mm")
        c.text(x0 + 64, y, label, 13, 700, TEXT2, "lm")

    # 1 Record
    step(1, "RECORD", y0 + 76)
    rb = (x0 + 32, y0 + 92, x1 - 32, y0 + 160)
    c.card(rb)
    if record_state == "idle":
        c.text(rb[0] + 18, rb[1] + 24, "2 recordings · stage 1 and the base", 14, 590)
        c.text(rb[0] + 18, rb[1] + 46, "1 240 shapes seen while you played", 12.5, 400, TEXT2)
        ai = c.button(rb[2] - 18, rb[1] + 20, "Let the AI Play…", "secondary", anchor="r", icon="sparkle",
                      disabled=not record_enabled)
        tas = c.button(ai[0] - 10, rb[1] + 20, "Record from a TAS Movie…", "secondary", anchor="r",
                       disabled=not record_enabled)
        c.button(tas[0] - 10, rb[1] + 20, "Record While I Play", "primary", RED, anchor="r", icon="record",
                 disabled=not record_enabled)
    else:
        c.circle(rb[0] + 26, rb[1] + 25, 6, fill=RED)
        c.text(rb[0] + 40, rb[1] + 25, "Recording · 01:42", 14, 650, TEXT, "lm")
        c.text(rb[0] + 168, rb[1] + 25, "318 new shapes · 2 screens captured", 12.5, 400, TEXT2, "lm")
        c.text(rb[0] + 18, rb[1] + 48, "Play through what you want to repaint. Press Stop when you are done.", 12.5, 400, TEXT2, "lm")
        c.button(rb[2] - 18, rb[1] + 20, "Stop", "primary", TEXT, anchor="r", icon="stop")

    # 2 Paint
    step(2, "PAINT", y0 + 186)
    pb = (x0 + 32, y0 + 202, x1 - 32, y0 + 470)
    c.card(pb)
    c.segmented(pb[0] + 18, pb[1] + 16, ["Figures 27", "Scenery 8", "Stage Maps 2", "Pattern Pages 24"], 0)
    names = ["run", "jump", "prone", "climb", "death", "boss", "figure 7"]
    phases = ["6 phases", "2 phases", "1 phase", "4 phases", "3 phases", "2 phases", "1 phase"]
    painted = [True, False, False, True, False, False, False]
    fill = [False] * 7  # every figure was seen; fill exists only on pattern pages
    tw = 118
    tiles = []
    for i in range(7):
        tx = pb[0] + 18 + i * (tw + 12)
        ty = pb[1] + 60
        tb = (tx, ty, tx + tw, ty + 150)
        tiles.append(tb)
        c.rrect(tb, 10, fill=(246, 246, 248), outline=(230, 230, 235))
        c.figure((tx + 27, ty + 14, tx + 91, ty + 78), i, dim=fill[i])
        c.text(tx + 12, ty + 100, names[i], 13, 590, TEXT3 if fill[i] else TEXT)
        c.text(tx + 12, ty + 119, phases[i], 11.5, 400, TEXT2)
        if painted[i]:
            c.rrect((tx + tw - 64, ty + 8, tx + tw - 8, ty + 24), 8, fill=(244, 232, 250))
            c.text(tx + tw - 36, ty + 16, "Painted", 10.5, 600, tint, "mm")
        if fill[i]:
            c.rrect((tx + tw - 68, ty + 8, tx + tw - 8, ty + 24), 8, fill=FILL)
            c.text(tx + tw - 38, ty + 16, "Not seen", 10.5, 600, TEXT2, "mm")
    c.text(pb[0] + 18, pb[3] - 26, "Click a figure to open it in your paint program. Save the same PNG and come back.",
           12.5, 400, TEXT2, "lm")

    # 3 See it
    step(3, "SEE IT", y0 + 496)
    sb_h = 64 if see_state != "problems" else 150
    sb = (x0 + 32, y0 + 512, x1 - 32, y0 + 512 + sb_h)
    c.card(sb)
    if see_state == "ready":
        c.text(sb[0] + 18, sb[1] + 32, "2 files changed since the last build", 14, 590, TEXT, "lm")
        c.button(sb[2] - 18, sb[1] + 15, "Build & Show in Game", "primary", tint, h=34, size=14, anchor="r", icon="play")
    elif see_state == "job":
        c.text(sb[0] + 18, sb[1] + 22, "Building your project…", 14, 590, TEXT, "lm")
        c.text(sb[0] + 190, sb[1] + 22, "Step 2 of 4 · figures imported (3)", 12.5, 400, TEXT2, "lm")
        c.progress(sb[0] + 18, sb[1] + 42, sb[2] - sb[0] - 140, 0.45, tint)
        c.button(sb[2] - 18, sb[1] + 18, "Stop", "secondary", anchor="r")
    elif see_state == "problems":
        c.icon("warn", sb[0] + 28, sb[1] + 24, 18, ORANGE)
        c.text(sb[0] + 46, sb[1] + 24, "2 problems stopped the build", 14, 650, TEXT, "lm")
        probs = [("“run”, phase 3", "The canvas was resized (it was 192×64). Undo the resize and save again."),
                 ("“stage 1 map”", "A pink marker is still on the image. Paint over it and save again.")]
        for i, (a, b2) in enumerate(probs):
            yy = sb[1] + 52 + i * 36
            c.text(sb[0] + 46, yy + 10, a, 13, 600, TEXT, "lm")
            c.text(sb[0] + 46 + c.tw(a, 13, 600) + 10, yy + 10, b2, 12.5, 400, TEXT2, "lm")
            c.button(sb[2] - 18, yy - 2, "Open File", "secondary", anchor="r", h=24, size=12)
        c.button(sb[2] - 18, sb[3] - 4 - 26 + 0, "Try Again", "primary", tint, anchor="r", h=24, size=12)
        c.button(sb[2] - 112, sb[3] - 30, "Show Log", "plain", tint, anchor="r", h=24, size=12)
    return tiles


# ---- individual wireframes --------------------------------------------------

def w_s1():
    c = base("play")
    play_home_recents(c)
    x0, y0, x1, y1 = WIN
    marks = [(x0 + 175, y0 + 26, "1", "Current profile only — click to switch"),
             (x1 - 30, y0 + 26, "2", "The door's tools menu"),
             (x0 + 560, y0 + 190, "3", "One profile's content"),
             (x0 + 300, y1 - 13, "4", "Status, read-only")]
    for mx, my, n, label in marks:
        c.circle(mx, my, 11, fill=(255, 45, 85))
        c.text(mx, my, n, 12, 700, CARD, "mm")
    lx = 60
    for i, (_, _, n, label) in enumerate(marks):
        c.circle(lx + 9, 58, 8, fill=(255, 45, 85))
        c.text(lx + 9, 58, n, 10, 700, CARD, "mm")
        lx += 24 + c.text(lx + 22, 58, label, 12, 500, TEXT2, "lm")
    c.caption("W-S1", "Shell — one profile at a time", 2)
    return c


def w_s2():
    c = base("play")
    play_home_recents(c)
    c.titlebar("play", tools_open=True)
    x1, y0 = WIN[2], WIN[1]
    # ADR-0250: Play's own Tools ⋯ — one place per action; the pause overlay
    # already holds Pause, Save states, Pack, Enhancements, Cheats, Settings, Quit game.
    # The Play home holds Open a ROM… and the recents, so they are not repeated here.
    # No game is loaded in this frame: the game items show disabled.
    rows = [("Reset", "-"), ("Power Cycle", "-"), None,
            ("Screenshot", "-"), ("Fullscreen", "⌃⌘F"), None,
            ("Help", ">")]
    h = 20 + sum(12 if r is None else 30 for r in rows) + 44
    mb = (x1 - 270, y0 + 44, x1 - 14, y0 + 44 + h)
    c.shadow(mb, 10, blur=14, dy=6, alpha=80)
    c.rrect(mb, 10, fill=(250, 250, 252), outline=(215, 215, 220))
    yy = mb[1] + 10
    for r in rows:
        if r is None:
            c.line([(mb[0] + 12, yy + 6), (mb[2] - 12, yy + 6)], SEP)
            yy += 12
            continue
        label, key = r
        if label == "Fullscreen":
            c.rrect((mb[0] + 6, yy, mb[2] - 6, yy + 26), 6, fill=TINT["play"])
        col = CARD if label == "Fullscreen" else (TEXT3 if key == "-" else TEXT)
        c.text(mb[0] + 18, yy + 13, label, 13.5, 500, col, "lm")
        if key == ">":
            c.icon("chev_right", mb[2] - 22, yy + 13, 11, col)
        elif key and key != "-":
            c.text(mb[2] - 16, yy + 13, key, 12.5, 400, CARD if col == CARD else TEXT3, "rm")
        yy += 30
    c.para(mb[0] + 18, yy + 6, "Disk, coin and tape items appear when the game uses them.", 220, 11.5, 400, TEXT2)
    c.caption("W-S2", "Play's Tools menu — only what the home and the pause overlay don't hold")
    return c


def w_s3():
    c = base("play")
    play_home_recents(c)
    c.titlebar("play", open_switcher=True)
    x0, y0 = WIN[0], WIN[1]
    pb = (x0 + 80, y0 + 46, x0 + 440, y0 + 46 + 326)
    c.shadow(pb, 14, blur=18, dy=8, alpha=90)
    c.rrect(pb, 14, fill=(252, 252, 253), outline=(215, 215, 220))
    rows = [("play", "Play", "Open a game and play it, enhanced."),
            ("remaster", "Remaster", "Record a game, paint its art, see it in game."),
            ("share", "Share", "Send a pack or a replay to the community."),
            ("classic", "Classic", "Every menu, debugger, Lua, HD Pack Builder.")]
    tint = dict(TINT, classic=(142, 142, 147))
    for i, (p, n, d) in enumerate(rows):
        yy = pb[1] + 12 + i * 64
        if i == 0:
            c.rrect((pb[0] + 8, yy, pb[2] - 8, yy + 58), 10, fill=(235, 243, 255))
        c.badge({"play": "play", "remaster": "brush", "share": "box", "classic": "gear"}[p], pb[0] + 20, yy + 13, 32, tint[p])
        c.text(pb[0] + 64, yy + 21, n, 14.5, 650)
        c.text(pb[0] + 64, yy + 40, d, 12, 400, TEXT2)
        c.text(pb[2] - 46, yy + 29, f"⌘{i + 1}", 12.5, 500, TEXT3, "rm")
        if i == 0:
            c.icon("check", pb[2] - 26, yy + 29, 14, TINT["play"])
    c.line([(pb[0] + 16, pb[3] - 54), (pb[2] - 16, pb[3] - 54)], SEP)
    c.para(pb[0] + 20, pb[3] - 40, "Switching keeps your game running. Only the chosen door is shown.", 320, 12, 400, TEXT2)
    c.caption("W-S3", "Door switcher — Play is the default; Classic is the original GUI", 4)
    return c


def w_p1():
    c = base("play")
    x0, y0, x1, y1 = c.content()
    cx = (x0 + x1) / 2
    c.badge("play", cx - 40, y0 + 120, 80, TINT["play"])
    c.text(cx, y0 + 250, "Drop a game here", 28, 700, TEXT, "mm")
    c.text(cx, y0 + 284, "or open one.", 15, 400, TEXT2, "mm")
    c.button(cx, y0 + 320, "Open a ROM…", "primary", TINT["play"], h=44, size=16, w=220, anchor="c")
    c.para(cx - 230, y0 + 410, "Enhanced audio is on. If the game has a community pack, it downloads, installs and loads by itself.",
           460, 13, 400, TEXT2)
    c.caption("W-P1", "Play — first run, no recent games", 1)
    return c


def w_p2():
    c = base("play")
    play_home_recents(c)
    c.caption("W-P2", "Play — home with recent games", 2)
    return c


def w_p3():
    c = Canvas()
    c.window(bg=(0, 0, 0))
    x0, y0, x1, y1 = WIN
    gw = (y1 - y0) * 4 / 3
    gx = (x0 + x1) / 2 - gw / 2
    c.scene((gx, y0 + 1, gx + gw, y1 - 1), 0)
    c.lights()
    # ADR-0251: during the first three game starts the toast ends with the
    # way into W-P4 (the binding of the device that started the game).
    tb = (x1 - 450, y1 - 80, x1 - 30, y1 - 36)
    c.hud(tb, 12)
    c.icon("check", tb[0] + 22, tb[1] + 22, 14, (52, 199, 89))
    c.text(tb[0] + 40, tb[1] + 22, "Applied Contra 80s — textures · Esc for the menu", 13.5, 600, CARD, "lm")
    c.caption("W-P3", "Play — in game, no chrome; one toast for the pack", 0)
    return c


def pause_panel(c):
    x0, y0, x1, y1 = WIN
    gw = (y1 - 53 - y0) * 4 / 3
    gx = (x0 + x1) / 2 - gw / 2
    c.d.rectangle(scb((x0 + 1, y0 + 53, x1 - 1, y1 - 27)), fill=(0, 0, 0))
    c.scene((gx, y0 + 53, gx + gw, y1 - 27), 0)
    c.blur((x0 + 1, y0 + 53, x1 - 1, y1 - 27), 8)
    c.overlay((x0 + 1, y0 + 53, x1 - 1, y1 - 27), (0, 0, 0, 90))
    pw, ph = 380, 520
    pb = ((x0 + x1) / 2 - pw / 2, y0 + 80, (x0 + x1) / 2 + pw / 2, y0 + 80 + ph)
    c.shadow(pb, 18, blur=26, dy=12, alpha=110)
    c.rrect(pb, 18, fill=(250, 250, 252))
    px0, py0, px1, _ = pb
    c.text(px0 + 24, py0 + 34, "Contra (USA)", 20, 700, TEXT, "lm")
    c.text(px0 + 24, py0 + 58, "Paused", 13, 400, TEXT2, "lm")
    c.button(px0 + 24, py0 + 82, "Resume", "primary", TINT["play"], h=44, size=16, w=pw - 48, icon="play")
    g2 = (px0 + 16, py0 + 150, px1 - 16, py0 + 150 + 250)
    c.rrect(g2, 12, fill=CARD, outline=(232, 232, 236))
    c.row(g2[0], g2[2], g2[1], 50, "Save States", "Slot 1 · 2 min ago", icon="film", tint=(88, 86, 214))
    c.row(g2[0], g2[2], g2[1] + 50, 50, "Pack", "Contra 80s 1.2", icon="box", tint=TINT["share"])
    c.row(g2[0], g2[2], g2[1] + 100, 50, "Enhancements", "4 on", icon="sparkle", tint=ORANGE)
    c.row(g2[0], g2[2], g2[1] + 150, 50, "Cheats", "2 on", icon="sparkle", tint=(255, 45, 85))
    c.row(g2[0], g2[2], g2[1] + 200, 50, "Settings", None, icon="gear", tint=(142, 142, 147), sep=False)
    c.button((px0 + px1) / 2, py0 + 434, "Quit Game", "destructive", h=36, w=pw - 48, anchor="c")
    c.text((px0 + px1) / 2, py0 + 496, "Esc to resume", 11.5, 400, TEXT3, "mm")
    return pb


def w_p4():
    c = base("play", "Contra (USA) · pack Contra 80s 1.2 · textures and audio", (52, 199, 89))
    pause_panel(c)
    c.caption("W-P4", "Play — pause overlay (Esc)", 7)
    return c


def w_p5():
    c = base("play", "Contra (USA) · 2 packs available", (52, 199, 89))
    pause_panel(c)
    b = c.sheet(500, 450, "Choose a pack for Contra (USA)")
    x0, y0, x1, _ = b
    opts = [("Contra 80s", "by Tastic · 1.2 · textures, audio", "41", None, True),
            ("Contra HD Remix", "author unknown · validated Aug 30 · textures", "9", "2 images known to be missing", False),
            ("No pack", "Play with enhanced audio only", None, None, False)]
    yy = y0 + 62
    for n, d, v, warn, sel in opts:
        h = 78 if warn else 62
        ob = (x0 + 20, yy, x1 - 20, yy + h)
        c.rrect(ob, 12, fill=(240, 246, 255) if sel else CARD, outline=TINT["play"] if sel else (225, 225, 230),
                width=2 if sel else 1)
        c.circle(ob[0] + 22, ob[1] + 22, 8, fill=TINT["play"] if sel else CARD, outline=None if sel else (200, 200, 205))
        if sel:
            c.circle(ob[0] + 22, ob[1] + 22, 3, fill=CARD)
        c.text(ob[0] + 42, ob[1] + 22, n, 14.5, 650, TEXT, "lm")
        c.text(ob[0] + 42, ob[1] + 42, d, 12.5, 400, TEXT2, "lm")
        if warn:
            c.icon("warn", ob[0] + 48, ob[1] + 62, 11, ORANGE)
            c.text(ob[0] + 58, ob[1] + 62, warn, 12, 500, (180, 100, 0), "lm")
        if v:
            c.icon("thumb", ob[2] - 46, ob[1] + 22, 13, TEXT2)
            c.text(ob[2] - 18, ob[1] + 22, v, 13, 600, TEXT2, "rm")
        yy += h + 10
    c.para(x0 + 22, yy + 6, "Remembered for this game. Change it any time from the pause menu.", 440, 12.5)
    c.button(x1 - 20, b[3] - 50, "Use This Pack", "primary", TINT["play"], anchor="r", h=32)
    c.button(x1 - 150, b[3] - 50, "Cancel", "secondary", anchor="r", h=32)
    c.caption("W-P5", "Play — pack picker (2 or more packs for this game)", 5)
    return c


def w_p6():
    c = base("play", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    pause_panel(c)
    b = c.sheet(500, 554)
    x0, y0, x1, y1 = b
    c.badge("box", x0 + 24, y0 + 24, 48, TINT["share"])
    c.text(x0 + 86, y0 + 40, "Contra 80s", 19, 700, TEXT, "lm")
    c.text(x0 + 86, y0 + 62, "by Tastic · version 1.2 · CC BY-NC 4.0", 12.5, 400, TEXT2, "lm")
    # The pack's layers, one switch each, for this game only (a layer the pack
    # lacks is a grey switch with "Not in this pack" under its name).
    g = (x0 + 24, y0 + 88, x1 - 24, y0 + 88 + 3 * 46)
    c.rrect(g, 12, fill=(248, 248, 250), outline=(232, 232, 236))
    for i, n in enumerate(("Textures", "Music", "ROM Patch")):
        yy = g[1] + i * 46
        c.text(g[0] + 16, yy + 23, n, 13.5, 500, TEXT, "lm")
        c.toggle(g[2] - 54, yy + 12, True, True)
        if i < 2:
            c.line([(g[0] + 16, yy + 46), (g[2], yy + 46)], SEP)
    c.text(x0 + 26, g[3] + 16, "The switches apply to this game only.", 12.5, 400, TEXT2, "lm")
    d = 134
    wb = (x0 + 24, y0 + 134 + d, x1 - 24, y0 + 194 + d)
    c.rrect(wb, 10, fill=(255, 244, 225))
    c.icon("warn", wb[0] + 22, wb[1] + 22, 15, ORANGE)
    c.text(wb[0] + 40, wb[1] + 22, "Some music is missing", 13.5, 650, (150, 85, 0), "lm")
    c.text(wb[0] + 40, wb[1] + 42, "3 of 17 tracks have no audio file. Add the .ogg files to the pack.", 12.5, 400, (150, 85, 0), "lm")
    c.button(x0 + 24, y0 + 214 + d, "Change Pack…", "secondary", h=30)
    c.button(x0 + 150, y0 + 214 + d, "Show Pack in Finder", "secondary", h=30, icon="folder")
    c.button(x0 + 24, y0 + 256 + d, "Restore Original Files…", "destructive", h=30)
    c.line([(x0 + 24, y0 + 306 + d), (x1 - 24, y0 + 306 + d)], SEP)
    c.text(x0 + 24, y0 + 330 + d, "Details", 13.5, 500, TEXT, "lm")
    c.icon("chev_right", x0 + 82, y0 + 331 + d, 11, TEXT3)
    c.text(x1 - 24, y0 + 330 + d, "ids and hashes", 12.5, 400, TEXT3, "rm")
    c.button(x1 - 24, y1 - 50, "Done", "primary", TINT["play"], anchor="r", h=32, w=90)
    c.caption("W-P6", "Play — current pack details (layer switches: this game only)", 5)
    return c


def w_p7():
    c = base("play", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    pause_panel(c)
    b = c.sheet(460, 390, "Enhancements")
    x0, y0, x1, y1 = b
    g = (x0 + 20, y0 + 56, x1 - 20, y0 + 56 + 5 * 46)
    c.rrect(g, 12, fill=(248, 248, 250), outline=(232, 232, 236))
    # One place per switch (ADR-0250 amendment): the pack's Textures/Music are
    # W-P6's, per game; the last row opens W-P6 (or W-P5 with 2+ packs).
    items = [("Modern instruments", None, True, True), ("Border", "Applies on reload", True, True),
             ("Widescreen", None, False, True), ("Overclock", "Not available on SMS", False, False)]
    for i, (n, sub, on, en) in enumerate(items):
        yy = g[1] + i * 46
        c.text(g[0] + 16, yy + (17 if sub else 23), n, 13.5, 500, TEXT if en else TEXT3, "lm")
        if sub:
            c.text(g[0] + 16, yy + 33, sub, 11.5, 400, TEXT2 if en else TEXT3, "lm")
        c.toggle(g[2] - 54, yy + 12, on, en)
        c.line([(g[0] + 16, yy + 46), (g[2], yy + 46)], SEP)
    yy = g[1] + 4 * 46
    c.text(g[0] + 16, yy + 23, "Pack: Contra 80s", 13.5, 500, TEXT, "lm")
    c.icon("chev_right", g[2] - 22, yy + 23, 12, TEXT3)
    c.text(x0 + 22, g[3] + 26, "How the picture looks: Settings › Look", 12.5, 400, TEXT2, "lm")
    c.button(x1 - 20, y1 - 50, "Apply & Reload", "primary", TINT["play"], anchor="r", h=32)
    c.caption("W-P7", "Play — enhancements (the picture's look moved to Settings › Look)", 6)
    return c


def settings_sheet(c, tab, h):
    b = c.sheet(480, h, "Settings")
    x0, y0, x1, y1 = b
    # System is the fifth pane (ADR-0256 Decision 8).
    tabs = ["Display", "Look", "Audio", "Controls", "System"]
    sw = 4 + 5 * 84
    c.segmented((x0 + x1) / 2 - sw / 2, y0 + 54, tabs, tab, item_w=84)
    return b


def w_p8():
    c = base("play", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    pause_panel(c)
    b = settings_sheet(c, 0, 340)
    x0, y0, x1, y1 = b
    g = (x0 + 20, y0 + 96, x1 - 20, y0 + 96 + 3 * 46)
    c.rrect(g, 12, fill=(248, 248, 250), outline=(232, 232, 236))
    rows = [("Full screen", None), ("Aspect ratio", "Auto"), ("Scale", "3×")]
    for i, (n, v) in enumerate(rows):
        yy = g[1] + i * 46
        c.text(g[0] + 16, yy + 23, n, 13.5, 500, TEXT, "lm")
        if v is None:
            c.toggle(g[2] - 54, yy + 12, True)
        else:
            c.popup(g[2] - 136, yy + 11, 120, v)
        if i < 2:
            c.line([(g[0] + 16, yy + 46), (g[2], yy + 46)], SEP)
    c.text(x0 + 22, g[3] + 26, "Everything else: Classic › Settings", 12.5, 400, TEXT2, "lm")
    c.button(x1 - 20, y1 - 50, "Done", "primary", TINT["play"], anchor="r", h=32, w=90)
    c.caption("W-P8", "Play — settings › Display (the window, not the pixels)", 5)
    return c


def slider(c, x, y, w, frac):
    c.rrect((x, y - 2, x + w, y + 2), 2, fill=(205, 205, 210))
    c.rrect((x, y - 2, x + w * frac, y + 2), 2, fill=TINT["play"])
    c.circle(x + w * frac, y, 8, fill=(255, 255, 255), outline=(200, 200, 205))


def essentials_sheet(c, tab, rows):
    """W-P8's Audio and Controls: Display's pattern - three rows in one inset
    list, "More in Options…" where Display has its hint (it expands to that tab's
    classic page, as Look's Pixels item does), then Done."""
    b = settings_sheet(c, tab, 340)
    x0, y0, x1, y1 = b
    g = (x0 + 20, y0 + 96, x1 - 20, y0 + 96 + 3 * 46)
    c.rrect(g, 12, fill=(248, 248, 250), outline=(232, 232, 236))
    for i, (name, kind, value) in enumerate(rows):
        yy = g[1] + i * 46
        c.text(g[0] + 16, yy + 23, name, 13.5, 500, TEXT, "lm")
        if kind == "switch":
            c.toggle(g[2] - 54, yy + 12, True)
        elif kind == "slider":
            slider(c, g[2] - 190, yy + 23, 150, value[0])
            c.text(g[2] - 16, yy + 23, value[1], 13.5, 400, TEXT, "rm")
        elif kind == "popup":
            c.popup(g[2] - 216, yy + 11, 200, value)
        else:
            c.text(g[2] - 16, yy + 23, value, 13.5, 400, TEXT2, "rm")
        if i < 2:
            c.line([(g[0] + 16, yy + 46), (g[2], yy + 46)], SEP)
    c.text(x0 + 22, g[3] + 26, "More in Options…", 12.5, 500, TINT_TEXT["play"], "lm")
    c.button(x1 - 20, y1 - 50, "Done", "primary", TINT["play"], anchor="r", h=32, w=90)


def w_p8b():
    c = base("play", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    pause_panel(c)
    essentials_sheet(c, 2, [("Sound", "switch", None), ("Volume", "slider", (1.0, "100")), ("Output device", "popup", "Speakers")])
    c.caption("W-P8b", "Play — settings › Audio (equalizer, latency… stay in Options)", 5)
    return c


def w_p8c():
    c = base("play", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    pause_panel(c)
    essentials_sheet(c, 3, [("Controllers", "text", "2 controllers connected"), ("Rumble", "slider", (0.5, "5")), ("Stick deadzone", "slider", (0.5, "2"))])
    c.caption("W-P8c", "Play — settings › Controls (button mapping stays in Options)", 5)
    return c


def look_group(c, x0, x1, y, label, hint, h):
    c.text(x0 + 2, y, label, 11.5, 700, TEXT2, "lm")
    c.text(x0 + 4 + c.tw(label, 11.5, 700) + 6, y, hint, 11.5, 400, TEXT3, "lm")
    g = (x0, y + 12, x1, y + 12 + h)
    c.rrect(g, 12, fill=(248, 248, 250), outline=(232, 232, 236))
    return g


def capture_note(c, x, y, captured):
    if captured:
        c.circle(x + 5, y, 4.5, fill=TEXT2)
        c.text(x + 16, y, "Shows in screenshots and videos", 11.5, 500, TEXT2, "lm")
    else:
        c.circle(x + 5, y, 4.5, outline=TEXT2, width=1.3)
        c.text(x + 16, y, "Only on your display — never in screenshots, videos or recordings", 11.5, 500, TEXT2, "lm")


def w_p10():
    c = base("play", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    pause_panel(c)
    b = settings_sheet(c, 1, 480)
    x0, y0, x1, y1 = b
    gx0, gx1 = x0 + 20, x1 - 20
    # Art
    g = look_group(c, gx0, gx1, y0 + 104, "ART", "drawn by an artist", 46)
    c.badge("box", g[0] + 12, g[1] + 10, 26, TINT["share"])
    c.text(g[0] + 48, g[1] + 23, "Contra 80s · textures", 13.5, 500, TEXT, "lm")
    c.icon("chev_right", g[2] - 18, g[1] + 23, 12, TEXT3)
    # Pixels
    g = look_group(c, gx0, gx1, y0 + 188, "PIXELS", "the emulator smooths the edges", 74)
    c.text(g[0] + 16, g[1] + 23, "Smoothing", 13.5, 500, TEXT3, "lm")
    c.rrect((g[2] - 216, g[1] + 11, g[2] - 16, g[1] + 35), 6, fill=(242, 242, 245), outline=(225, 225, 230))
    c.text(g[2] - 206, g[1] + 23, "Sharp — original pixels", 13, 400, TEXT3, "lm")
    c.text(g[0] + 16, g[1] + 50, "Off while a pack draws the art", 11.5, 600, (180, 100, 0), "lm")
    capture_note(c, g[0] + 16, g[1] + 64, True)
    # Screen
    g = look_group(c, gx0, gx1, y0 + 300, "SCREEN", "imitates a TV or a handheld", 74)
    c.text(g[0] + 16, g[1] + 23, "Effect", 13.5, 500, TEXT, "lm")
    c.popup(g[2] - 300, g[1] + 11, 200, "crt-royale.slangp")
    c.button(g[2] - 16, g[1] + 9, "Adjust…", "secondary", anchor="r", h=28)
    capture_note(c, g[0] + 16, g[1] + 56, False)
    # Footer
    c.button(x0 + 20, y1 - 50, "Hold to Compare", "secondary", h=32)
    c.text(x0 + 160, y1 - 34, "shows the original pixels while held", 11.5, 400, TEXT3, "lm")
    c.button(x1 - 20, y1 - 50, "Done", "primary", TINT["play"], anchor="r", h=32, w=90)
    c.caption("W-P10", "Play — settings › Look: art, pixels, screen", 7)
    return c


def w_p9():
    c = Canvas()
    c.window(bg=(0, 0, 0))
    x0, y0, x1, y1 = WIN
    gw = (y1 - y0) * 4 / 3
    gx = (x0 + x1) / 2 - gw / 2
    c.scene((gx, y0 + 1, gx + gw, y1 - 1), 0)
    c.lights()
    hb = (x1 - 330, y0 + 40, x1 - 24, y0 + 100)
    c.text(hb[0] + 4, hb[1] - 12, "While installing", 11.5, 650, CARD, "lm")
    c.hud(hb)
    c.badge("box", hb[0] + 14, hb[1] + 16, 28, TINT["share"])
    c.text(hb[0] + 54, hb[1] + 22, "Installing Contra 80s…", 13.5, 650, CARD, "lm")
    c.rrect((hb[0] + 54, hb[1] + 38, hb[0] + 254, hb[1] + 43), 2.5, fill=FILL)
    c.rrect((hb[0] + 104, hb[1] + 38, hb[0] + 174, hb[1] + 43), 2.5, fill=TINT["play"])  # indeterminate
    fb = (x1 - 430, y0 + 140, x1 - 24, y0 + 180)
    c.text(fb[0] + 4, fb[1] - 12, "If it fails (replaces the pill above, 5 s)", 11.5, 650, CARD, "lm")
    c.hud(fb)
    c.icon("warn", fb[0] + 20, fb[1] + 20, 14, ORANGE)
    c.text(fb[0] + 38, fb[1] + 20, "The pack could not be downloaded. Playing without it.", 12.5, 500, CARD, "lm")
    c.caption("W-P9", "Play — a pack installs while the game starts (two states)", 0)
    return c


def remaster_start(c, banner=False):
    x0, y0, x1, y1 = c.content()
    tint = TINT["remaster"]
    top = y0 + 30
    if banner:
        bb = (x0 + 40, y0 + 20, x1 - 40, y0 + 76)
        c.rrect(bb, 12, fill=(255, 244, 225))
        c.icon("warn", bb[0] + 24, bb[1] + 28, 16, ORANGE)
        c.text(bb[0] + 44, bb[1] + 19, "Painting needs Python 3.10 or newer, which MesenAI could not find.", 13.5, 650, (150, 85, 0), "lm")
        c.text(bb[0] + 44, bb[1] + 38, "You can still record. Your figures are prepared once Python is available.", 12.5, 400, (150, 85, 0), "lm")
        c.button(bb[2] - 16, bb[1] + 14, "How to Install", "plain", (150, 85, 0), anchor="r")
        c.button(bb[2] - 130, bb[1] + 14, "Locate Python…", "secondary", anchor="r")
        top = y0 + 100
    c.text(x0 + 40, top + 20, "Remaster a game", 26, 700)
    c.para(x0 + 40, top + 48, "Play the game once while MesenAI records it. You get its figures, scenery and stage maps as "
           "pictures to paint in your own program. Come back and see them in the game.", 640, 14)
    cw = (x1 - x0 - 100) / 2
    for i, (title, sub, prim) in enumerate([("Start with the running game", "Contra (USA)", True),
                                             ("Open a project folder…", "A game folder you already worked on", False)]):
        cb = (x0 + 40 + i * (cw + 20), top + 110, x0 + 40 + i * (cw + 20) + cw, top + 230)
        c.card(cb, 14)
        if prim:
            c.badge("record", cb[0] + 20, cb[1] + 20, 40, RED)
        else:
            c.badge("folder", cb[0] + 20, cb[1] + 20, 40, (142, 142, 147))
        c.text(cb[0] + 76, cb[1] + 32, title, 16, 650)
        c.text(cb[0] + 76, cb[1] + 54, sub, 12.5, 400, TEXT2)
        c.button(cb[0] + 76, cb[1] + 76, "Start Recording" if prim else "Choose Folder…",
                 "primary" if prim else "secondary", tint, h=30)
    c.text(x0 + 40, top + 272, "Recent projects", 15, 650)
    g = (x0 + 40, top + 288, x1 - 40, top + 288 + 2 * 52)
    c.card(g, 12, shadow=False)
    c.row(g[0], g[2], g[1], 52, "Castlevania (USA)", "3 recordings · 412 cells painted", icon="brush", tint=tint)
    c.row(g[0], g[2], g[1] + 52, 52, "The Legend of Zelda (USA)", "1 recording · 28 cells painted", icon="brush", tint=tint, sep=False)


def w_p11():
    c = base("play", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    pause_panel(c)
    b = c.sheet(500, 520, "Cheats")
    x0, y0, x1, y1 = b
    c.field(x0 + 20, y0 + 52, x1 - x0 - 40, "Search: lives, jump, weapon…", placeholder=True, h=30)
    g = (x0 + 20, y0 + 98, x1 - 20, y0 + 98 + 5 * 50)
    c.rrect(g, 12, fill=(248, 248, 250), outline=(232, 232, 236))
    items = [("Infinite lives — 1P game", "From the cheat list", True, True),
             ("Start with 30 lives", "From the cheat list", True, True),
             ("Keep weapon after dying", "From the cheat list", False, True),
             ("Start on stage 5", "From the cheat list", False, True),
             ("Invincibility (RAM)", "From the cheat list · allowed while recording art", False, True)]
    for i, (n, sub, on, en) in enumerate(items):
        yy = g[1] + i * 50
        c.text(g[0] + 16, yy + 18, n, 13.5, 500, TEXT, "lm")
        c.text(g[0] + 16, yy + 35, sub, 11.5, 400, TEXT2, "lm")
        c.toggle(g[2] - 54, yy + 14, on, en)
        if i < 4:
            c.line([(g[0] + 16, yy + 50), (g[2], yy + 50)], SEP)
    c.text(x0 + 22, g[3] + 24, "2 on · matched to your copy of Contra (USA)", 12.5, 400, TEXT2, "lm")
    c.text(x0 + 22, g[3] + 46, "Cheats you have on are recorded in a shared replay.", 12, 400, TEXT3, "lm")
    c.button(x1 - 20, y1 - 50, "Done", "primary", TINT["play"], anchor="r", h=32)
    c.button(x0 + 20, y1 - 50, "Add a Code…", "secondary", h=32)
    c.caption("W-P11", "Play — cheats from the bundled list (W-P4 › Cheats)", 3)
    return c


def radio(c, x, y, on, tint=TINT["play"]):
    c.circle(x, y, 8, fill=tint if on else CARD, outline=None if on else (200, 200, 205))
    if on:
        c.circle(x, y, 3, fill=CARD)


def drop_zone(c, b, title, sub):
    c.rrect(b, 12, fill=(248, 248, 250), outline=(200, 200, 205))
    cx = (b[0] + b[2]) / 2
    c.icon("folder", cx, b[1] + 30, 22, TEXT3)
    c.text(cx, b[1] + 60, title, 13.5, 600, TEXT, "mm")
    c.text(cx, b[1] + 80, sub, 12, 400, TEXT2, "mm")


def w_p12():
    c = base("play")
    x0, y0, x1, y1 = c.content()
    c.overlay((x0, y0, x1, y1), (0, 0, 0, 70), 0)
    b = c.sheet(520, 440, dim=False)
    sx0, sy0, sx1, sy1 = b
    c.badge("play", sx0 + 24, sy0 + 22, 40, TINT["play"])
    c.text(sx0 + 78, sy0 + 42, "Welcome to MesenAI", 19, 700, TEXT, "lm")
    c.para(sx0 + 24, sy0 + 82, "Two choices, then you can play. Both can be changed later in Settings.", 470, 13.5, 400, TEXT)
    c.text(sx0 + 24, sy0 + 132, "Keep your saves and settings", 13.5, 650, TEXT, "lm")
    opts = [("In your user folder", "~/Library/Application Support/MesenAI", True),
            ("Next to the app (portable)", "Move the app folder and everything comes with it", False)]
    yy = sy0 + 150
    for n, d, on in opts:
        radio(c, sx0 + 34, yy + 14, on)
        c.text(sx0 + 52, yy + 14, n, 13.5, 500, TEXT, "lm")
        c.text(sx0 + 52, yy + 33, d, 12, 400, TEXT2, "lm")
        yy += 50
    c.line([(sx0 + 24, yy + 6), (sx1 - 24, yy + 6)], SEP)
    yy += 22
    c.text(sx0 + 24, yy + 12, "Keyboard", 13.5, 650, TEXT, "lm")
    c.popup(sx0 + 150, yy, 220, "Arrow keys + S / A")
    c.para(sx0 + 24, yy + 40, "Xbox and PlayStation controllers work as soon as you plug them in. Another controller "
           "asks to be set up the first time you press a button.", 470, 12.5, 400, TEXT2)
    c.text(sx0 + 24, sy1 - 82, "Windows and Linux add two checkboxes here: Check for updates, Desktop shortcut.", 11.5, 400, TEXT3, "lm")
    c.button(sx1 - 24, sy1 - 52, "Start Playing", "primary", TINT["play"], anchor="r", h=34, icon="play")
    c.caption("W-P12", "Play — first run, one sheet (replaces the setup wizard)", 4)
    return c


def w_p13():
    c = base("play", "Opening Zelda no Densetsu (FDS)…", TEXT3)
    play_home_recents(c)
    b = c.sheet(480, 400)
    x0, y0, x1, y1 = b
    c.badge("lock", x0 + 24, y0 + 22, 40, (142, 142, 147))
    c.text(x0 + 78, y0 + 42, "This game needs a BIOS file", 18, 700, TEXT, "lm")
    c.para(x0 + 24, y0 + 82, "Famicom Disk System games start from the console's own BIOS. MesenAI does not "
           "include it — choose your copy once and it is kept for every disk game.", 430, 13.5, 400, TEXT)
    drop_zone(c, (x0 + 24, y0 + 146, x1 - 24, y0 + 246), "Drop disksys.rom here", "8 KB · stays on this computer")
    eb = (x0 + 24, y0 + 260, x1 - 24, y0 + 296)
    c.rrect(eb, 8, fill=(255, 244, 225))
    c.icon("warn", eb[0] + 18, eb[1] + 18, 12, ORANGE)
    c.text(eb[0] + 32, eb[1] + 18, "That file is 16 KB — the FDS BIOS is 8 KB. Try another file.", 12.5, 500, (150, 85, 0), "lm")
    c.button(x1 - 24, y1 - 52, "Choose File…", "primary", TINT["play"], anchor="r", h=32)
    c.button(x1 - 148, y1 - 52, "Cancel", "secondary", anchor="r", h=32)
    c.caption("W-P13", "Play — a game needs a BIOS (the orange line shows only after a wrong file)", 3)
    return c


def w_p14():
    c = base("play")
    x0, y0, x1, y1 = c.content()
    play_home_recents(c)
    ab = (x0 + 40, y1 - 120, x1 - 40, y1 - 24)
    c.shadow(ab, 12, blur=8, dy=3, alpha=40)
    c.rrect(ab, 12, fill=(255, 248, 236), outline=(245, 214, 160))
    c.icon("warn", ab[0] + 24, ab[1] + 28, 15, ORANGE)
    c.text(ab[0] + 44, ab[1] + 28, "“Contra.txt” is not a game MesenAI can open.", 13.5, 650, TEXT, "lm")
    c.text(ab[0] + 44, ab[1] + 50, "MesenAI opens NES, Game Boy, Game Boy Color, Master System and Game Boy Advance games, "
           "or a zip holding one.", 12.5, 400, TEXT2, "lm")
    c.text(ab[0] + 44, ab[1] + 72, "Other cases, same place:  the zip has no game in it  ·  the file is damaged or cut short",
           12, 400, TEXT3, "lm")
    c.button(ab[2] - 14, ab[1] + 14, "Open Another…", "secondary", anchor="r", h=30, icon="folder")
    c.caption("W-P14", "Play — a file that does not open (in place, the home stays)", 3)
    return c


def w_p15():
    c = Canvas()
    c.window(bg=(0, 0, 0))
    x0, y0, x1, y1 = WIN
    gw = (y1 - y0) * 4 / 3
    gx = (x0 + x1) / 2 - gw / 2
    c.scene((gx, y0 + 1, gx + gw, y1 - 1), 0, dim=0.55)
    c.lights()
    pb = ((x0 + x1) / 2 - 230, y0 + 120, (x0 + x1) / 2 + 230, y0 + 560)
    c.shadow(pb, 18, blur=26, dy=12, alpha=110)
    c.rrect(pb, 18, fill=(250, 250, 252))
    px0, py0, px1, py1 = pb
    c.text(px0 + 24, py0 + 34, "Set up “8BitDo SN30”", 19, 700, TEXT, "lm")
    c.text(px0 + 24, py0 + 58, "Use the controller itself — no keyboard needed.", 13, 400, TEXT2, "lm")
    pad = (px0 + 60, py0 + 90, px1 - 60, py0 + 230)
    c.rrect(pad, 40, fill=(214, 214, 219))
    cx, cy = pad[0] + 70, (pad[1] + pad[3]) / 2
    c.rrect((cx - 36, cy - 12, cx + 36, cy + 12), 3, fill=(80, 80, 85))
    c.rrect((cx - 12, cy - 36, cx + 12, cy + 36), 3, fill=(80, 80, 85))
    for k, (dx, lab) in enumerate(((-26, "Select"), (26, "Start"))):
        c.rrect(((pad[0] + pad[2]) / 2 + dx - 20, cy + 14, (pad[0] + pad[2]) / 2 + dx + 20, cy + 26), 6, fill=(120, 120, 125))
    bx = pad[2] - 70
    c.circle(bx - 26, cy + 14, 17, fill=(120, 120, 125))
    c.circle(bx + 26, cy - 8, 17, fill=TINT["play"], outline=CARD, width=3)
    c.text(bx + 26, cy - 8, "A", 13, 700, CARD, "mm")
    c.text(bx - 26, cy + 14, "B", 13, 700, CARD, "mm")
    c.text((px0 + px1) / 2, py0 + 262, "Press the button you want as  A", 16, 650, TEXT, "mm")
    c.text((px0 + px1) / 2, py0 + 288, "Step 1 of 8 · A, B, Select, Start, Up, Down, Left, Right", 12.5, 400, TEXT2, "mm")
    c.progress(px0 + 60, py0 + 308, px1 - px0 - 120, 1 / 8, TINT["play"])
    c.para(px0 + 34, py0 + 334, "Hold any button 2 seconds to skip this one. Press nothing for 10 seconds to stop — "
           "the keyboard keeps working.", 392, 12.5, 400, TEXT2)
    c.button(px0 + 24, py1 - 52, "Skip", "secondary", h=32, w=90)
    c.button(px1 - 24, py1 - 52, "Cancel", "secondary", anchor="r", h=32, w=100)
    tb = ((x0 + x1) / 2 - 250, y0 + 40, (x0 + x1) / 2 + 250, y0 + 80)
    c.text(tb[0] + 4, tb[1] - 12, "Before — the first press on an unknown controller (HUD pill, 8 s)", 11.5, 650, CARD, "lm")
    c.hud(tb, 10)
    c.icon("gear", tb[0] + 20, tb[1] + 20, 14, CARD)
    c.text(tb[0] + 38, tb[1] + 20, "New controller. Press Start on it to set it up.", 13, 600, CARD, "lm")
    c.caption("W-P15", "Play — a controller nobody has set up (driven by the controller)", 2)
    return c


def w_p16():
    c = base("play", "Contra (Japan) · pack Contra Arcade Music 2.0 · waiting for one file", ORANGE)
    pause_panel(c)
    b = c.sheet(500, 390)
    x0, y0, x1, y1 = b
    c.badge("box", x0 + 24, y0 + 22, 40, TINT["share"])
    c.text(x0 + 78, y0 + 34, "Contra Arcade Music needs one file", 17, 700, TEXT, "lm")
    c.text(x0 + 78, y0 + 56, "Everything else is installed.", 12.5, 400, TEXT2, "lm")
    c.para(x0 + 24, y0 + 90, "The pack's author could not share this file, so it is not downloaded. "
           "If you have it, add it and the pack completes.", 450, 13.5, 400, TEXT)
    ib = (x0 + 24, y0 + 146, x1 - 24, y0 + 196)
    c.rrect(ib, 10, fill=(248, 248, 250), outline=(232, 232, 236))
    c.text(ib[0] + 14, ib[1] + 17, "Arcade soundtrack (MP3 set, 23 files)", 13.5, 600, TEXT, "lm")
    c.text(ib[0] + 14, ib[1] + 36, "License: not declared", 12, 400, TEXT2, "lm")
    drop_zone(c, (x0 + 24, y0 + 210, x1 - 24, y0 + 310), "Drop the file here", "It is checked, then the game restarts with it.")
    c.button(x1 - 24, y1 - 52, "Add and Restart…", "primary", TINT["play"], anchor="r", h=32)
    c.button(x1 - 190, y1 - 52, "Play Without It", "secondary", anchor="r", h=32)
    c.button(x0 + 24, y1 - 52, "Show Folder", "plain", TINT["play"], h=32, icon="folder")
    c.caption("W-P16", "Play — a pack waits for a file only you can add", 4)
    return c


# ---- the flat game library (ADR-0264) ---------------------------------------
CONSOLE_TINT = {"NES": (84, 88, 96), "Game Boy": (104, 116, 84),
                "Game Boy Color": (128, 104, 44), "Game Boy Advance": (74, 62, 132),
                "Master System": (44, 92, 122), "Game Gear": (52, 70, 112)}


def library_tile(c, x, y, w, h, title, console, seed, kind="art", focus=False):
    """One library tile: a vertical ~3:4 cover, its clean title and console tag (ADR-0264)."""
    b = (x, y, x + w, y + h)
    if kind == "generic":
        # no database knows this ROM: a console-coloured cover carrying the title.
        c.rrect(b, 8, fill=CONSOLE_TINT[console])
        c.text(x + w / 2, y + h / 2 - 8, console, 13, 700, CARD, "mm")
        c.text(x + w / 2, y + h / 2 + 12, "no cover", 11, 400, (222, 226, 232), "mm")
    else:
        c.shadow(b, 8, blur=4, dy=2, alpha=34)
        c.d.rounded_rectangle(scb(b), radius=sc(8), fill=(0, 0, 0))
        c.scene(b, seed)
    c.rrect(b, 8, outline=(224, 224, 228))
    if kind == "shot":
        # the player's own screenshot from Recent, until downloaded art arrives.
        pw = c.tw("Recent", 9.5, 700) + 14
        c.rrect((x + 6, y + 6, x + 6 + pw, y + 22), 5, fill=CARD)
        c.text(x + 6 + pw / 2, y + 14, "Recent", 9.5, 700, TEXT2, "mm")
    if focus:
        c.rrect((x - 4, y - 4, x + w + 4, y + h + 4), 11, outline=TINT["play"], width=3)
        c.rrect((x + w - 26, y + 8, x + w - 8, y + 26), 5, fill=CARD)
        c.icon("play", x + w - 17, y + 17, 11, TINT["play"])
    label = title
    while c.tw(label, 12.5, 590) > w and len(label) > 4:
        label = label[:-2]
    c.text(x, y + h + 15, label if label == title else label + "…", 12.5, 590,
           TEXT if focus else TEXT, "lm")
    c.text(x, y + h + 31, console, 11, 400, TEXT2, "lm")


def library_sheet(c, subtitle, tiles, focus=None, query=None):
    """The Play Open a game sheet as a flat library (ADR-0264 Decision 1, W-P19)."""
    b = c.sheet(1100, 620, dim=False)
    x0, y0, x1, y1 = b
    c.text(x0 + 24, y0 + 36, "Your library", 20, 700, TEXT, "lm")
    c.text(x0 + 24 + c.tw("Your library", 20, 700) + 12, y0 + 37, subtitle, 13, 400, TEXT2, "lm")
    bb = c.button(x1 - 24, y0 + 20, "Browse a file…", "secondary", h=30, anchor="r", icon="folder")
    c.button(bb[0] - 10, y0 + 20, "Library folders…", "secondary", h=30, anchor="r", icon="folder")
    # search field (Y on a pad opens ADR-0262's on-screen keyboard over it)
    c.field(x0 + 24, y0 + 68, 400, query or "Search games", placeholder=query is None,
            h=32, focused=query is not None)
    if query:
        qx = x0 + 24 + 10 + c.tw(query, 13, 400) + 1
        c.line([(qx, y0 + 76), (qx, y0 + 92)], TEXT, 1.5)
        c.text(x0 + 392, y0 + 84, "×", 15, 500, TEXT3, "mm")
    # console filter: only the consoles actually present (ADR-0264 Decision 5)
    c.segmented(x0 + 444, y0 + 71, ["All", "NES", "Game Boy", "Game Boy Advance"], 0)
    # the grid: vertical ~3:4 cover tiles, row-major, one focus ring
    cols, gap = 8, 14
    tw_ = (x1 - 24 - (x0 + 24) - (cols - 1) * gap) / cols
    th_ = tw_ * 4 / 3
    for i, (title, console, kind) in enumerate(tiles):
        tx = x0 + 24 + (i % cols) * (tw_ + gap)
        ty = y0 + 124 + (i // cols) * (th_ + 46)
        library_tile(c, tx, ty, tw_, th_, title, console, i + 1, kind, focus == i)
    c.button(x0 + 24, y1 - 56, "Back", "secondary", h=32, w=92)
    c.text(x0 + 132, y1 - 40, "A  Play      B  Back      Y  Search      LB / RB  Console      "
           "D-pad  Move", 12, 500, TEXT2, "lm")
    return b


def w_p19():
    tiles = [("Castlevania", "NES", "art"), ("The Legend of Zelda", "NES", "art"),
             ("Super Mario Bros. 3", "NES", "art"), ("Metroid", "NES", "shot"),
             ("Mega Man 2", "NES", "art"), ("Contra", "NES", "shot"),
             ("Kirby's Adventure", "NES", "art"), ("Excitebike", "NES", "art"),
             ("Tetris", "Game Boy", "art"), ("Super Mario Land", "Game Boy", "shot"),
             ("Pokémon Red", "Game Boy", "art"), ("Donkey Kong", "Game Boy", "shot"),
             ("Metroid Fusion", "Game Boy Advance", "art"),
             ("The Legend of Zelda: Oracle of Ages", "Game Boy Color", "generic"),
             ("Sonic the Hedgehog", "Master System", "generic"),
             ("Alex Kidd in Miracle World", "Master System", "shot")]
    c = base("play", "No game loaded")
    library_sheet(c, "· 128 games in 4 folders", tiles, focus=1)
    c.caption("W-P19", "Play — your library: every game under the library folders, at once", 6)
    return c


def w_p19b():
    tiles = [("The Legend of Zelda", "NES", "art"),
             ("Zelda II: The Adventure of Link", "NES", "shot"),
             ("The Legend of Zelda: Oracle of Ages", "Game Boy Color", "generic")]
    c = base("play", "No game loaded")
    library_sheet(c, "· 3 games match “zel”", tiles, focus=0, query="zel")
    c.caption("W-P19b", "Play — search typing narrows the library live (Y on a pad)", 6)
    return c


def w_r0():
    c = base("remaster", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    remaster_start(c)
    c.caption("W-R0", "Remaster — no project yet", 2)
    return c


def w_r0b():
    c = base("remaster", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    remaster_start(c, banner=True)
    c.caption("W-R0b", "Remaster — Python not found (a banner, never a dialog)", 4)
    return c


def w_r1():
    c = base("remaster", "Contra (USA) · playing your project · 412 cells painted", TINT["remaster"])
    remaster_project(c)
    c.caption("W-R1", "Remaster — the project screen (Record → Paint → See it)", 6)
    return c


def w_r2():
    c = Canvas()
    c.window(bg=(0, 0, 0))
    x0, y0, x1, y1 = WIN
    gw = (y1 - y0) * 4 / 3
    gx = (x0 + x1) / 2 - gw / 2
    c.scene((gx, y0 + 1, gx + gw, y1 - 1), 0)
    c.lights()
    hb = (x0 + 110, y0 + 18, x1 - 24, y0 + 66)
    c.hud(hb)
    c.circle(hb[0] + 22, hb[1] + 24, 6, fill=RED)
    c.text(hb[0] + 36, hb[1] + 24, "Recording 01:42", 14, 650, CARD, "lm")
    c.text(hb[0] + 168, hb[1] + 24, "318 new shapes · 2 screens captured", 12.5, 400, (200, 200, 205), "lm")
    c.badge("brush", hb[0] + 440, hb[1] + 12, 24, TINT["remaster"])
    c.text(hb[0] + 472, hb[1] + 24, "Remaster", 12.5, 600, (214, 170, 245), "lm")
    c.button(hb[2] - 12, hb[1] + 10, "Stop", "primary", (90, 90, 95), anchor="r", h=28, icon="stop")
    tb = ((x0 + x1) / 2 - 220, y1 - 64, (x0 + x1) / 2 + 220, y1 - 28)
    c.hud(tb, 10)
    c.text((tb[0] + tb[2]) / 2, (tb[1] + tb[3]) / 2, "Play through what you want to repaint. Esc stops.", 12.5, 500, CARD, "mm")
    c.caption("W-R2", "Remaster — recording: the game fills the window, one pill, Stop", 1)
    return c


def w_r3():
    c = base("remaster", "Building · Contra (USA)", TINT["remaster"])
    remaster_project(c, see_state="job", record_enabled=False)
    c.caption("W-R3", "Remaster — a job runs as a card, with Stop", 6)
    return c


def w_r4():
    c = base("remaster", "Build stopped · 2 problems", ORANGE)
    remaster_project(c, see_state="problems")
    c.caption("W-R4", "Remaster — build problems, in words, with the file to open", 6)
    return c


def popover(c, anchor_x, anchor_y, w, h, below=True):
    b = (anchor_x - w / 2, anchor_y + 10, anchor_x + w / 2, anchor_y + 10 + h) if below else \
        (anchor_x - w / 2, anchor_y - 10 - h, anchor_x + w / 2, anchor_y - 10)
    c.shadow(b, 12, blur=14, dy=6, alpha=80)
    c.rrect(b, 12, fill=(252, 252, 253), outline=(215, 215, 220))
    if below:
        c.poly([(anchor_x - 9, b[1] + 1), (anchor_x, anchor_y + 1), (anchor_x + 9, b[1] + 1)], (252, 252, 253))
    else:
        c.poly([(anchor_x - 9, b[3] - 1), (anchor_x, anchor_y - 1), (anchor_x + 9, b[3] - 1)], (252, 252, 253))
    return b


def w_r5():
    c = base("remaster", "Contra (USA) · playing your project · 412 cells painted", TINT["remaster"])
    tiles = remaster_project(c)
    t = tiles[0]
    b = popover(c, (t[0] + t[2]) / 2, t[3] - 4, 300, 150)
    c.text(b[0] + 16, b[1] + 22, "run", 15, 700, TEXT, "lm")
    c.text(b[0] + 16 + c.tw("run", 15, 700) + 8, b[1] + 23, "6 phases · from recording 2", 12, 400, TEXT2, "lm")
    c.icon("check", b[0] + 22, b[1] + 50, 11, (36, 138, 61))
    c.text(b[0] + 36, b[1] + 50, "Seen in the game", 12.5, 500, TEXT, "lm")
    c.icon("pencil", b[0] + 22, b[1] + 72, 11, TINT["remaster"])
    c.text(b[0] + 36, b[1] + 72, "Painted: 2 of 6 phases", 12.5, 500, TEXT, "lm")
    c.button(b[0] + 16, b[1] + 102, "Open", "primary", TINT["remaster"], h=28)
    # second popover: a pattern page (the only surface that carries fill cells)
    x0, y0, x1, y1 = c.content()
    pb = (x1 - 380, y0 + 452, x1 - 60, y0 + 636)
    c.text(pb[0] + 2, pb[1] - 14, "On the Pattern Pages strip", 12, 650, TEXT2, "lm")
    c.shadow(pb, 12, blur=14, dy=6, alpha=80)
    c.rrect(pb, 12, fill=(252, 252, 253), outline=(215, 215, 220))
    c.text(pb[0] + 16, pb[1] + 22, "page 17", 15, 700, TEXT, "lm")
    c.text(pb[0] + 16 + c.tw("page 17", 15, 700) + 8, pb[1] + 23, "Pattern Pages", 12, 400, TEXT2, "lm")
    c.icon("warn", pb[0] + 22, pb[1] + 50, 11, ORANGE)
    c.text(pb[0] + 36, pb[1] + 50, "12 of 64 cells not seen in the game", 12.5, 500, TEXT, "lm")
    c.para(pb[0] + 16, pb[1] + 68, "Filled from the game's own data. Play further while recording to see them.",
           290, 12, 400, TEXT2)
    c.button(pb[0] + 16, pb[3] - 44, "Open", "primary", TINT["remaster"], h=28)
    c.caption("W-R5", "Remaster — what was seen, what was filled, what is painted")
    return c


def w_r6():
    c = base("remaster", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    remaster_start(c)
    b = c.sheet(480, 330)
    x0, y0, x1, y1 = b
    c.badge("brush", x0 + 24, y0 + 24, 44, TINT["remaster"])
    c.text(x0 + 82, y0 + 46, "This is a finished pack", 18, 700, TEXT, "lm")
    c.para(x0 + 24, y0 + 90, "Make it editable? MesenAI cuts its images into figures and pages you can paint. "
           "The original pack is not changed.", 430, 13.5, 400, TEXT)
    wb = (x0 + 24, y0 + 150, x1 - 24, y0 + 222)
    c.rrect(wb, 10, fill=(255, 244, 225))
    c.icon("warn", wb[0] + 20, wb[1] + 22, 14, ORANGE)
    c.para(wb[0] + 38, wb[1] + 14, "This pack is made for a patched version of the game. You can paint it, but new "
           "recordings will not connect to it.", 380, 12.5, 400, (150, 85, 0))
    c.button(x1 - 24, y1 - 54, "Make Editable", "primary", TINT["remaster"], anchor="r", h=32)
    c.button(x1 - 156, y1 - 54, "Cancel", "secondary", anchor="r", h=32)
    c.caption("W-R6", "Remaster — turn an existing pack into a project", 2)
    return c


def w_r7():
    c = base("remaster", "Contra (USA) · playing your project", TINT["remaster"])
    remaster_project(c)
    b = c.sheet(460, 270)
    x0, y0, x1, y1 = b
    c.text(x0 + 24, y0 + 36, "Compose a scene", 18, 700, TEXT, "lm")
    c.para(x0 + 24, y0 + 66, "The scene composer is a separate tool. It opens in its own window and saves into this project.",
           410, 13.5, 400, TEXT)
    c.icon("check", x0 + 30, y0 + 136, 11, (36, 138, 61))
    c.text(x0 + 44, y0 + 136, "This project has the layout data the composer needs", 12.5, 500, TEXT2, "lm")
    c.button(x1 - 24, y1 - 54, "Open Composer", "primary", TINT["remaster"], anchor="r", h=32, icon="arrow_ur")
    c.button(x1 - 172, y1 - 54, "Cancel", "secondary", anchor="r", h=32)
    c.caption("W-R7", "Remaster — hand-off to the external composer", 2)
    return c


def w_r8():
    c = base("remaster", "Contra (USA) · playing your project", TINT["remaster"])
    remaster_project(c)
    b = c.sheet(500, 470)
    x0, y0, x1, y1 = b
    tint = TINT["remaster"]
    c.badge("sparkle", x0 + 24, y0 + 22, 34, tint)
    c.text(x0 + 70, y0 + 39, "Let the AI play", 18, 700, TEXT, "lm")
    c.para(x0 + 24, y0 + 74, "The AI plays the stage for you and records it. It only steps in where the game "
           "gets stuck, so it runs slower than real time.", 450, 13.5, 400, TEXT)
    rows = [("Start from", "Stage 1 — your recording"), ("Goal", "End of the stage")]
    yy = y0 + 136
    for label, val in rows:
        c.text(x0 + 24, yy + 12, label, 13, 500, TEXT, "lm")
        c.popup(x0 + 150, yy, 250, val)
        yy += 38
    yy += 8
    c.line([(x0 + 24, yy), (x1 - 24, yy)], SEP)
    yy += 16
    c.text(x0 + 24, yy + 13, "OpenRouter key", 13, 500, TEXT, "lm")
    c.field(x0 + 150, yy, 200, "•••••••••••• 4f2a", h=26)
    c.button(x1 - 24, yy, "Change…", "secondary", anchor="r", h=26)
    c.icon("lock", x0 + 156, yy + 40, 10, TEXT3)
    c.text(x0 + 166, yy + 40, "Stored in your Keychain, never in files", 11.5, 400, TEXT2, "lm")
    yy += 62
    c.text(x0 + 24, yy + 12, "Spend limit", 13, 500, TEXT, "lm")
    c.popup(x0 + 150, yy, 110, "US$ 0.25")
    c.text(x0 + 272, yy + 12, "about 10 000 moves", 12, 400, TEXT2, "lm")
    yy += 46
    nb = (x0 + 24, yy, x1 - 24, yy + 64)
    c.rrect(nb, 10, fill=(246, 241, 251))
    c.icon("lock", nb[0] + 18, nb[1] + 20, 12, tint)
    c.para(nb[0] + 34, nb[1] + 12, "The AI sees numbers read from the game's memory — never the picture or the "
           "game file. You pay OpenRouter with your own key.", 400, 12.5, 400, TEXT2)
    c.button(x1 - 24, y1 - 50, "Start", "primary", tint, anchor="r", h=32)
    c.button(x1 - 110, y1 - 50, "Cancel", "secondary", anchor="r", h=32)
    c.caption("W-R8", "Remaster — let the AI play (your own key, ADR-0242)", 6)
    return c


def share_home(c):
    x0, y0, x1, y1 = c.content()
    c.text(x0 + 40, y0 + 54, "Share with the community", 26, 700)
    cw = (x1 - x0 - 100) / 2
    cards = [("box", TINT["share"], "A pack", "Made one, or found one you love? Paste one link. A bot checks it and lists it in the catalog.", "Share a Pack"),
             ("film", (88, 86, 214), "A replay", "Record a run from power-on and share it. Others can watch it in MesenAI.", "Record and Share")]
    for i, (ic, tint, title, desc, btn) in enumerate(cards):
        cb = (x0 + 40 + i * (cw + 20), y0 + 96, x0 + 40 + i * (cw + 20) + cw, y0 + 380)
        c.card(cb, 16)
        c.badge(ic, cb[0] + 24, cb[1] + 24, 56, tint)
        c.text(cb[0] + 24, cb[1] + 112, title, 20, 700)
        c.para(cb[0] + 24, cb[1] + 140, desc, cw - 48, 13.5)
        c.button(cb[0] + 24, cb[3] - 60, btn, "primary", TINT["share"] if i == 0 else tint, h=36, size=14)
    c.icon("lock", x0 + 48, y0 + 418, 13, TEXT2)
    c.text(x0 + 62, y0 + 419, "Submissions open on GitHub in your browser. MesenAI never uploads anything or signs in for you.",
           12.5, 400, TEXT2, "lm")


def w_h1():
    c = base("share", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    share_home(c)
    c.caption("W-H1", "Share — home", 2)
    return c


def w_h2():
    c = base("share", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    x0, y0, x1, y1 = c.content()
    tint = TINT_TEXT["share"]
    c.button(x0 + 32, y0 + 20, "Share", "plain", tint, icon="chev_left")
    fx = x0 + 180
    fw = 600
    c.text(fx, y0 + 80, "Share a pack", 26, 700)
    c.text(fx, y0 + 110, "Three fields. A bot downloads it, checks it, and replies on GitHub in a few minutes.", 13.5, 400, TEXT2)
    c.text(fx, y0 + 156, "Pack link", 13, 600)
    c.field(fx, y0 + 168, fw, "https://github.com/you/your-pack/releases/download/…", placeholder=True, focused=True)
    c.text(fx, y0 + 214, "GitHub release, gist, raw file, Google Drive, MediaFire, Dropbox or MEGA", 12, 400, TEXT2)
    c.text(fx, y0 + 256, "Game", 13, 600)
    c.field(fx, y0 + 268, 420, "Contra (USA)")
    c.text(fx + 440, y0 + 256, "Console", 13, 600)
    c.popup(fx + 440, y0 + 271, 160, "NES")
    c.text(fx, y0 + 314, "Filled in from the game you are playing. Change it if the pack is for another one.", 12, 400, TEXT2)
    c.text(fx, y0 + 370, "Want to share your own remaster?", 13, 400, TEXT2, "lm")
    c.button(fx + c.tw("Want to share your own remaster?", 13) + 6, y0 + 356, "Package a Project…", "plain", tint)
    c.button(fx + fw, y0 + 420, "Continue on GitHub", "primary", TINT["share"], h=36, size=14, anchor="r", icon="arrow_ur")
    c.caption("W-H2", "Share — a pack link, three fields", 6)
    return c


def w_h3():
    c = base("share", "Contra (USA) · your project", TINT["share"])
    x0, y0, x1, y1 = c.content()
    tint = TINT_TEXT["share"]
    c.button(x0 + 32, y0 + 20, "Share", "plain", tint, icon="chev_left")
    fx, fw = x0 + 180, 640
    c.text(fx, y0 + 80, "Share your Contra (USA) project", 26, 700)
    steps = [("Package it", None), ("Put it somewhere public", None), ("Submit the link", None)]
    yy = y0 + 124
    for i, (t, _) in enumerate(steps):
        c.circle(fx + 14, yy + 14, 14, fill=TINT["share"])
        c.text(fx + 14, yy + 14, str(i + 1), 13, 700, CARD, "mm")
        c.text(fx + 40, yy + 14, t, 16, 650, TEXT, "lm")
        if i < 2:
            c.line([(fx + 14, yy + 32), (fx + 14, yy + (100 if i == 0 else 136))], SEP, 2)
        if i == 0:
            c.button(fx + 40, yy + 34, "Build Pack .zip", "secondary", h=28)
            c.icon("check", fx + 186, yy + 48, 12, (36, 138, 61))
            c.text(fx + 200, yy + 48, "contra-usa-mep.zip · 38 MB · no problems", 12.5, 500, TEXT, "lm")
            c.button(fx + fw, yy + 34, "Show in Finder", "plain", tint, h=28, anchor="r", icon="folder")
            yy += 104
        elif i == 1:
            c.para(fx + 40, yy + 34, "Upload the .zip to a GitHub release, Google Drive, Dropbox, MediaFire or MEGA, "
                   "and copy its download link. MesenAI does not host files.", fw - 40, 13)
            c.button(fx + 40, yy + 84, "Open Google Drive", "secondary", h=28, icon="arrow_ur")
            c.text(fx + 210, yy + 98, "then drag the .zip from Finder into it", 12, 400, TEXT2, "lm")
            yy += 140
        else:
            c.field(fx + 40, yy + 34, fw - 40, "https://", placeholder=True, focused=True)
    c.button(fx + fw, yy + 90, "Continue on GitHub", "primary", TINT["share"], h=36, size=14, anchor="r", icon="arrow_ur")
    c.caption("W-H3", "Share — package your project, host it, submit the link", 6)
    return c


def w_h4():
    c = base("share", "Contra (USA) · pack Contra 80s 1.2", (52, 199, 89))
    share_home(c)
    x0, y0, x1, y1 = WIN
    c.overlay((x0, y0 + 53, x1, y1), (0, 0, 0, 70))
    w, h = 400, 270
    for i, title in enumerate(["Before", "After you stop"]):
        bx = (x0 + x1) / 2 - w - 12 + i * (w + 24)
        b = (bx, y0 + 120, bx + w, y0 + 120 + h)
        c.text(bx + 4, b[1] - 16, title, 12.5, 650, CARD, "lm")
        c.shadow(b, 14, blur=22, dy=10, alpha=90)
        c.rrect(b, 14, fill=CARD)
        if i == 0:
            c.badge("film", bx + 22, b[1] + 22, 40, (88, 86, 214))
            c.text(bx + 74, b[1] + 42, "Record and share", 17, 700, TEXT, "lm")
            c.para(bx + 22, b[1] + 84, "Contra (USA) restarts from power-on and records until you stop. Loading a save "
                   "state ends the recording; cheats you have on are recorded with it.", w - 44, 13.5, 400, TEXT)
            c.button(bx + w - 22, b[3] - 54, "Start Recording", "primary", RED, anchor="r", h=32, icon="record")
            c.button(bx + w - 172, b[3] - 54, "Cancel", "secondary", anchor="r", h=32)
        else:
            c.badge("check", bx + 22, b[1] + 22, 40, TINT["share"])
            c.text(bx + 74, b[1] + 42, "Replay saved", 17, 700, TEXT, "lm")
            c.text(bx + 22, b[1] + 86, "contra-usa-2026-10-02.mmo", 13.5, 600, TEXT, "lm")
            c.para(bx + 22, b[1] + 108, "Drag the file into the GitHub form that opens next.", w - 44, 13.5)
            c.button(bx + w - 22, b[3] - 54, "Continue on GitHub", "primary", TINT["share"], anchor="r", h=32, icon="arrow_ur")
            c.button(bx + w - 200, b[3] - 54, "Show in Finder", "secondary", anchor="r", h=32)
    c.caption("W-H4", "Share — record and share a replay", 2)
    return c


def inline_alert(c, y, x0, x1, icon, tint, text, buttons, bg, profile=None):
    b = (x0, y, x1, y + 56)
    c.rrect(b, 12, fill=bg)
    tx = x0
    if profile:
        c.badge({"play": "play", "remaster": "brush", "share": "box"}[profile], x0 - 104, y + 16, 24, TINT[profile])
        c.text(x0 - 72, y + 28, NAME[profile], 12.5, 650, TEXT2, "lm")
    c.icon(icon, tx + 24, y + 28, 15, tint)
    c.text(tx + 44, y + 28, text, 13.5, 500, TEXT, "lm")
    bx = x1 - 14
    for label, kind, t in reversed(buttons):
        r = c.button(bx, y + 13, label, kind, t, anchor="r", h=30)
        bx = r[0] - 8


def w_x1():
    c = Canvas()
    c.caption("W-X1", "Confirmations — one shape everywhere (a pattern sheet, not a screen)")
    c.card((50, 70, 1150, 470), 16)
    c.text(90, 112, "Confirmations", 22, 700)
    c.text(90, 140, "Shown in place, in the profile that asks. Navigation never asks.", 13.5, 400, TEXT2)
    ex = [("play", "warn", ORANGE, "Restore original files? Your edits to this pack will be lost.",
           [("Keep Edits", "secondary", TINT["play"]), ("Restore", "destructive", TINT["play"])], (255, 248, 236)),
          ("play", "box", TINT["play"], "Use Contra HD Remix instead? The game restarts.",
           [("Cancel", "secondary", TINT["play"]), ("Switch Pack", "primary", TINT["play"])], (238, 245, 255)),
          ("remaster", "stop", RED, "Stop recording? Your figures are made from what you played so far.",
           [("Keep Going", "secondary", TINT["play"]), ("Stop", "primary", TEXT)], (250, 240, 240))]
    for i, (pr, ic, t, txt, btns, bg) in enumerate(ex):
        inline_alert(c, 180 + i * 80, 200, 1120, ic, t, txt, btns, bg, profile=pr)
    return c


def w_x2():
    c = Canvas()
    c.caption("W-X2", "Errors — words and the next step, never codes (a pattern sheet)")
    c.card((50, 70, 1150, 470), 16)
    c.text(90, 112, "Errors", 22, 700)
    c.text(90, 140, "A sentence and the next step. Codes and logs stay behind “Show Log”.", 13.5, 400, TEXT2)
    inline_alert(c, 180, 200, 1120, "warn", ORANGE,
                 "This pack could not be downloaded (the host did not answer). Playing without it.",
                 [("Try Again", "secondary", TINT["play"])], (255, 248, 236), profile="play")
    inline_alert(c, 260, 200, 1120, "warn", ORANGE,
                 "This is not the game the project was recorded from.",
                 [("Open the Right Game…", "primary", TINT["remaster"])], (255, 248, 236), profile="remaster")
    inline_alert(c, 340, 200, 1120, "warn", ORANGE,
                 "This host is not accepted. Use a GitHub release, Google Drive, MediaFire, Dropbox or MEGA.",
                 [], (255, 248, 236), profile="share")
    return c


def w_x3():
    c = Canvas()
    c.caption("W-X3", "Interruptions — quitting or changing game while work runs (a pattern sheet)")
    c.card((50, 70, 1150, 560), 16)
    c.text(90, 112, "Interruptions", 22, 700)
    c.text(90, 140, "Only lost work asks. Switching profile never asks: the work keeps running where it started.",
           13.5, 400, TEXT2)
    ex = [("remaster", "stop", RED, "Quit while recording? What you recorded so far is kept as recording 3.",
           [("Keep Recording", "secondary", TINT["play"]), ("Stop and Quit", "primary", TEXT)], (250, 240, 240)),
          ("remaster", "warn", ORANGE, "A build is running. Quit anyway? It stops, and nothing you painted is lost.",
           [("Keep Running", "secondary", TINT["play"]), ("Quit", "primary", TEXT)], (255, 248, 236)),
          ("remaster", "stop", RED, "Open Castlevania? This recording stops and is kept as recording 3.",
           [("Cancel", "secondary", TINT["play"]), ("Stop and Open", "primary", TINT["remaster"])], (250, 240, 240)),
          ("play", "warn", ORANGE, "Open Castlevania? HD Pack Builder (classic) stops; what it wrote is kept.",
           [("Cancel", "secondary", TINT["play"]), ("Stop and Open", "primary", TINT["play"])], (255, 248, 236))]
    for i, (pr, ic, t, txt, btns, bg) in enumerate(ex):
        inline_alert(c, 180 + i * 80, 200, 1120, ic, t, txt, btns, bg, profile=pr)
    c.text(90, 506, "Builds and AI runs are separate processes: opening a game never stops them. "
           "In Play or Share the status line says one runs:", 12.5, 500, TEXT2, "lm")
    c.circle(96, 532, 3.5, fill=TINT["remaster"])
    c.text(106, 532, "Remaster: building Contra (USA) · 40 %", 12, 400, TEXT2, "lm")
    return c


SCREENS = [
    ("W-S1", w_s1), ("W-S2", w_s2), ("W-S3", w_s3),
    ("W-P1", w_p1), ("W-P2", w_p2), ("W-P3", w_p3), ("W-P4", w_p4), ("W-P5", w_p5), ("W-P6", w_p6),
    ("W-P7", w_p7), ("W-P8", w_p8), ("W-P8b", w_p8b), ("W-P8c", w_p8c), ("W-P9", w_p9), ("W-P10", w_p10), ("W-P11", w_p11),
    ("W-P12", w_p12), ("W-P13", w_p13), ("W-P14", w_p14), ("W-P15", w_p15), ("W-P16", w_p16),
    ("W-P19", w_p19), ("W-P19b", w_p19b),
    ("W-R0", w_r0), ("W-R0b", w_r0b), ("W-R1", w_r1), ("W-R2", w_r2), ("W-R3", w_r3), ("W-R4", w_r4),
    ("W-R5", w_r5), ("W-R6", w_r6), ("W-R7", w_r7), ("W-R8", w_r8),
    ("W-H1", w_h1), ("W-H2", w_h2), ("W-H3", w_h3), ("W-H4", w_h4),
    ("W-X1", w_x1), ("W-X2", w_x2), ("W-X3", w_x3),
]


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    here = os.path.dirname(os.path.abspath(__file__))
    ap.add_argument("--out", default=os.path.join(here, "..", "docs", "media", "gui-redesign"))
    ap.add_argument("--only", default="", help="comma-separated ids, e.g. W-R1,W-P4")
    args = ap.parse_args()
    if not os.path.exists(FONT_PATH):
        print(f"render_gui_wireframes: {FONT_PATH} not found (macOS system font required)", file=sys.stderr)
        return 2
    os.makedirs(args.out, exist_ok=True)
    only = {s.strip() for s in args.only.split(",") if s.strip()}
    unknown = only - {wid for wid, _ in SCREENS}
    if unknown:
        print(f"render_gui_wireframes: unknown ids: {', '.join(sorted(unknown))}", file=sys.stderr)
        return 2
    for wid, fn in SCREENS:
        if only and wid not in only:
            continue
        path = os.path.join(args.out, f"{wid}.png")
        fn().save(path)
        print(path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
