"""Dock icon generator for Deskmate.

Writes one SVG per Dock system button (dock-<id>.svg, 128 viewBox) into ./svg, plus preview.html.
render_icons.ps1 rasterises them with headless Edge into the atlas build.cmd embeds (src/DockIcons.png).

The tiles share the Settings art language (assets/settings/make_art.py): a gradient squircle,
one bold ivory motif, at most one calm accent. Dock icons are drawn small (about 24-40 px), so
each motif is two or three heavy shapes and nothing thinner than 8 units.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "settings"))
from make_art import (IVORY, MINT, MINT_DEEP, GOLD, circle, rect, shade, sparkle,  # noqa: E402
                     squircle, tile_defs)

OUT = os.path.join(HERE, "svg")

# Order is the atlas order; DockArt.cs uses the same list.
IDS = ["start", "taskview", "work", "suruh", "shelf"]

BASE = {
    "start": "#4B2766",     # Deskmate plum, the brand tile
    "taskview": "#5B5770",  # slate
    "work": "#8A6418",      # amber
    "suruh": "#6A4FC4",     # violet
    "shelf": "#2C7552",     # emerald
}


def palette(cid):
    base = BASE[cid]
    return {
        "top": shade(base, light=0.13, hue=-6, sat=1.08),
        "base": base,
        "bottom": shade(base, light=-0.07, hue=6, sat=1.05),
        "deep": shade(base, light=-0.16, sat=1.1),
        "soft": shade(base, light=0.30, sat=0.9),
    }


def motif(cid, p):
    I = IVORY
    ghost = 'fill-opacity="0.40"'
    deep = p["deep"]
    if cid == "start":
        # The launcher grid; the last cell is the brand's mint pill
        return "".join([
            rect(26, 26, 34, 34, 11, I),
            rect(68, 26, 34, 34, 11, I),
            rect(26, 68, 34, 34, 11, I),
            rect(68, 74, 34, 22, 11, MINT),
        ])
    if cid == "taskview":
        return "".join([
            rect(40, 20, 70, 54, 13, I, ghost),
            '<clipPath id="tv"><rect x="18" y="40" width="80" height="66" rx="14"/></clipPath>',
            rect(18, 40, 80, 66, 14, I),
            '<rect x="18" y="40" width="80" height="17" fill="%s" fill-opacity="0.85" clip-path="url(#tv)"/>' % deep,
            rect(30, 68, 36, 9, 4.5, deep, 'fill-opacity="0.32"'),
            rect(30, 84, 24, 9, 4.5, deep, 'fill-opacity="0.32"'),
            circle(80, 88, 8, MINT_DEEP),
        ])
    if cid == "work":
        # Clipboard with one ticked task
        return "".join([
            rect(26, 24, 76, 86, 15, I),
            rect(46, 15, 36, 20, 9, deep),
            rect(56, 20, 16, 7, 3.5, I, 'fill-opacity="0.55"'),
            '<path d="M42 64 L55 77 L84 48" fill="none" stroke="%s" stroke-width="11" '
            'stroke-linecap="round" stroke-linejoin="round"/>' % MINT_DEEP,
            rect(42, 89, 44, 9, 4.5, deep, 'fill-opacity="0.38"'),
        ])
    if cid == "suruh":
        bubble = ("M34 22 H94 A16 16 0 0 1 110 38 V76 A16 16 0 0 1 94 92 H52 L30 108 "
                  "L33 91 A16 16 0 0 1 18 76 V38 A16 16 0 0 1 34 22 Z")
        return "".join([
            '<path d="%s" fill="%s"/>' % (bubble, I),
            sparkle(64, 57, 23, deep),
            sparkle(98, 26, 13, MINT),
        ])
    if cid == "shelf":
        # A tray holding a sheet: drop files here, drag them out later
        return "".join([
            rect(50, 14, 48, 62, 10, I, ghost + ' transform="rotate(12 74 45)"'),
            rect(32, 18, 50, 64, 10, I),
            rect(42, 32, 30, 8, 4, deep, 'fill-opacity="0.35"'),
            rect(42, 46, 20, 8, 4, deep, 'fill-opacity="0.35"'),
            rect(14, 68, 100, 42, 15, I),
            rect(26, 68, 76, 7, 3.5, deep, 'fill-opacity="0.40"'),
            rect(50, 86, 28, 9, 4.5, deep, 'fill-opacity="0.55"'),
            circle(98, 32, 8, GOLD),
        ])
    raise KeyError(cid)


def icon_svg(cid):
    p = palette(cid)
    sq = squircle(64, 64, 126)
    inner = squircle(64, 64, 123.5)
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" width="128" height="128">'
        '<defs>{defs}</defs>'
        '<path d="{sq}" fill="url(#g)"/>'
        '<path d="{sq}" fill="url(#sh)"/>'
        '<path d="{inner}" fill="none" stroke="url(#rim)" stroke-width="2"/>'
        '<g filter="url(#ds)">{m}</g></svg>'
    ).format(defs=tile_defs(cid, p), sq=sq, inner=inner, m=motif(cid, p))


def main():
    os.makedirs(OUT, exist_ok=True)
    for cid in IDS:
        with open(os.path.join(OUT, "dock-%s.svg" % cid), "w", encoding="utf-8") as f:
            f.write(icon_svg(cid))
    cells = "".join('<div class="i"><img src="svg/dock-{c}.svg" width="{w}"></div>'.format(c=c, w=w)
                    for w in (128,) for c in IDS)
    small = "".join('<div class="i"><img src="svg/dock-{c}.svg" width="{w}"></div>'.format(c=c, w=w)
                    for w in (40, 28, 22) for c in IDS)
    bar = '<div class="bar">%s</div>' % "".join('<span><img src="svg/dock-%s.svg" width="28"></span>' % c for c in IDS)
    html = ('<!doctype html><meta charset="utf-8"><title>Deskmate Dock icons</title><style>'
            'body{margin:0;font:13px Segoe UI;display:grid;grid-template-columns:1fr 1fr}'
            '.col{padding:20px;display:flex;flex-wrap:wrap;gap:14px;align-items:center}'
            '.dark{background:#2B2930}.light{background:#FDFDFC}'
            '.bar{display:flex;gap:6px;padding:6px 10px;border-radius:20px;background:rgba(127,120,140,.25)}'
            '.bar span{display:grid;place-items:center;width:44px;height:44px;border-radius:12px;background:rgba(255,255,255,.08)}'
            '</style><div class="col dark">{a}</div><div class="col light">{a}</div>'
            .replace("{a}", cells + small + bar))
    with open(os.path.join(HERE, "preview.html"), "w", encoding="utf-8") as f:
        f.write(html)
    print("wrote", len(IDS), "svgs")


if __name__ == "__main__":
    main()
