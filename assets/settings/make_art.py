"""Settings artwork generator for Deskmate.

Writes one SVG per category icon (icon-<id>.svg, 128 viewBox) and per banner scene
(scene-<id>.svg, 480x192 viewBox) into ./svg, plus preview.html.
render_art.ps1 rasterises them with headless Edge and packs the two atlases that
build.cmd embeds (src/SettingsIcons.png, src/SettingsScenes.png).

Palette comes from DESIGN.md: tile colours are the paper twins of Ink.Labels,
ink is Deskmate ivory, accents are the calm Fx tones (mint, gold, rose, sky).
"""
import colorsys
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "svg")

# Order is the atlas order; SettingsArt.cs uses the same list.
IDS = ["general", "look", "island", "dock", "messages", "work", "remind",
       "ai", "agents", "ingat", "files", "system", "game", "keys"]

LABEL = {  # Ink.Labels paper twins
    "slate": "#5B5770", "violet": "#6650B8", "sky": "#2D6C8C", "emerald": "#2C7552",
    "coral": "#AE4639", "amber": "#86621A", "rose": "#A3456E",
}
TILE = {
    "general": "slate", "look": "violet", "island": "sky", "dock": "emerald",
    "messages": "coral", "work": "amber", "remind": "emerald", "ai": "violet",
    "agents": "slate", "ingat": "sky", "files": "rose", "system": "sky",
    "game": "amber", "keys": "slate",
}
# Per-category hue nudge so two tiles that share a label colour still differ.
HUE = {
    "look": -10, "ai": 14, "island": -8, "ingat": 10, "system": 0,
    "dock": -6, "remind": 10, "general": 0, "agents": -12, "keys": 12,
    "work": 0, "game": -8,
}

IVORY = "#FBF9F5"
MINT = "#8FE3B8"
MINT_DEEP = "#2E9C6C"
GOLD = "#F4CD5C"
ROSE = "#F49AB8"
SKY = "#8CC9EC"
CORAL = "#F59384"


def hx(c):
    c = c.lstrip("#")
    return tuple(int(c[i:i + 2], 16) / 255 for i in (0, 2, 4))


def tohex(rgb):
    return "#" + "".join("%02X" % max(0, min(255, round(v * 255))) for v in rgb)


def shade(c, light=0.0, hue=0.0, sat=1.0):
    r, g, b = hx(c)
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    h = (h + hue / 360.0) % 1.0
    l = max(0, min(1, l + light))
    s = max(0, min(1, s * sat))
    return tohex(colorsys.hls_to_rgb(h, l, s))


def palette(cid):
    base = LABEL[TILE[cid]]
    base = shade(base, hue=HUE.get(cid, 0))
    return {
        "top": shade(base, light=0.13, hue=-6, sat=1.08),
        "base": base,
        "bottom": shade(base, light=-0.07, hue=6, sat=1.05),
        "deep": shade(base, light=-0.16, sat=1.1),
        "soft": shade(base, light=0.30, sat=0.9),
    }


def squircle(cx, cy, size, n=4.6, steps=96):
    a = size / 2.0
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        ct, st = math.cos(t), math.sin(t)
        x = a * math.copysign(abs(ct) ** (2.0 / n), ct)
        y = a * math.copysign(abs(st) ** (2.0 / n), st)
        pts.append("%.2f %.2f" % (cx + x, cy + y))
    return "M" + " L".join(pts) + " Z"


def gear(cx, cy, r_out, r_in, teeth, hole):
    pts = []
    n = teeth * 4
    for i in range(n):
        a = 2 * math.pi * (i + 0.5) / n - math.pi / 2
        r = r_out if (i % 4) in (0, 1) else r_in
        pts.append("%.2f %.2f" % (cx + r * math.cos(a), cy + r * math.sin(a)))
    ring = "M" + " L".join(pts) + " Z"
    hole_p = ("M%.2f %.2f a%.2f %.2f 0 1 0 %.2f 0 a%.2f %.2f 0 1 0 %.2f 0 Z"
              % (cx - hole, cy, hole, hole, 2 * hole, hole, hole, -2 * hole))
    return ring + " " + hole_p


def circle(cx, cy, r, fill, extra=""):
    return '<circle cx="%g" cy="%g" r="%g" fill="%s" %s/>' % (cx, cy, r, fill, extra)


def rect(x, y, w, h, r, fill, extra=""):
    return ('<rect x="%g" y="%g" width="%g" height="%g" rx="%g" fill="%s" %s/>'
            % (x, y, w, h, r, fill, extra))


def sparkle(cx, cy, s, fill, extra=""):
    k = s * 0.16
    d = ("M{cx} {t} C{a} {b} {c} {d} {r} {cy} C{c} {e} {a} {f} {cx} {bt} "
         "C{g} {f} {h} {e} {l} {cy} C{h} {d} {g} {b} {cx} {t} Z").format(
        cx=cx, cy=cy, t=cy - s, bt=cy + s, r=cx + s, l=cx - s,
        a=cx + k * 0.5, b=cy - s * 0.45, c=cx + s * 0.45, d=cy - k * 0.5,
        e=cy + k * 0.5, f=cy + s * 0.45, g=cx - k * 0.5, h=cx - s * 0.45)
    return '<path d="%s" fill="%s" %s/>' % (d, fill, extra)


def motif(cid, p):
    """Ivory artwork for one category inside a 128 box (no tile)."""
    I = IVORY
    ghost = 'fill-opacity="0.38"'
    deep = p["deep"]
    if cid == "general":
        return "".join([
            '<path d="%s" fill="%s" fill-rule="evenodd" %s/>' % (gear(92, 38, 19, 15, 7, 6), I, ghost),
            '<path d="%s" fill="%s" fill-rule="evenodd"/>' % (gear(58, 70, 36, 28.5, 8, 11.5), I),
            circle(58, 70, 5.5, MINT),
        ])
    if cid == "look":
        body = ("M64 24 C90 24 108 42 108 62 C108 76 98 82 88 80 C80 79 76 84 79 91 "
                "C82 99 76 106 64 106 C40 106 20 88 20 64 C20 42 40 24 64 24 Z")
        return "".join([
            '<path d="%s" fill="%s"/>' % (body, I),
            circle(44, 58, 8, ROSE), circle(60, 42, 8, GOLD), circle(82, 48, 8, MINT),
            circle(42, 82, 8, SKY), circle(62, 86, 5, deep, 'fill-opacity="0.55"'),
        ])
    if cid == "island":
        return "".join([
            rect(20, 24, 88, 80, 17, I),
            rect(36, 33, 56, 22, 11, "#1C1A22"),
            circle(47, 44, 5, MINT),
            rect(57, 41.5, 26, 5, 2.5, I, 'fill-opacity="0.85"'),
            rect(32, 68, 64, 8, 4, deep, 'fill-opacity="0.30"'),
            rect(32, 82, 40, 8, 4, deep, 'fill-opacity="0.30"'),
        ])
    if cid == "dock":
        return "".join([
            rect(22, 22, 84, 62, 14, I, ghost),
            rect(34, 34, 34, 6, 3, I, 'fill-opacity="0.7"'),
            rect(34, 46, 22, 6, 3, I, 'fill-opacity="0.7"'),
            rect(20, 74, 88, 32, 16, I),
            rect(32, 82, 16, 16, 5, MINT_DEEP),
            rect(56, 82, 16, 16, 5, deep),
            rect(80, 82, 16, 16, 5, "#E0A93A"),
        ])
    if cid == "messages":
        back = "M50 26 H96 A14 14 0 0 1 110 40 V58 A14 14 0 0 1 96 72 H50 A14 14 0 0 1 36 58 V40 A14 14 0 0 1 50 26 Z"
        front = ("M34 46 H80 A15 15 0 0 1 95 61 V81 A15 15 0 0 1 80 96 H46 L32 106 "
                 "L34 95 A15 15 0 0 1 19 81 V61 A15 15 0 0 1 34 46 Z")
        return "".join([
            '<path d="%s" fill="%s" %s/>' % (back, I, ghost),
            '<path d="%s" fill="%s"/>' % (front, I),
            circle(41, 71, 5, deep), circle(57, 71, 5, deep), circle(73, 71, 5, deep),
            circle(100, 30, 11, GOLD, 'stroke="%s" stroke-width="0"' % I),
        ])
    if cid == "work":
        return "".join([
            '<clipPath id="wc"><rect x="24" y="30" width="80" height="74" rx="15"/></clipPath>',
            rect(24, 30, 80, 74, 15, I),
            '<rect x="24" y="30" width="80" height="22" fill="%s" clip-path="url(#wc)"/>' % deep,
            rect(42, 22, 9, 17, 4.5, I), rect(77, 22, 9, 17, 4.5, I),
            '<path d="M46 77 L58 89 L83 64" fill="none" stroke="%s" stroke-width="9" '
            'stroke-linecap="round" stroke-linejoin="round"/>' % MINT_DEEP,
        ])
    if cid == "remind":
        rays = []
        for i in range(8):
            a = 2 * math.pi * i / 8
            x1, y1 = 78 + 27 * math.cos(a), 46 + 27 * math.sin(a)
            x2, y2 = 78 + 35 * math.cos(a), 46 + 35 * math.sin(a)
            rays.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" '
                        'stroke-width="6" stroke-linecap="round"/>' % (x1, y1, x2, y2, GOLD))
        cloud = "".join([
            circle(50, 76, 19, I), circle(72, 68, 23, I), circle(93, 80, 15, I),
            rect(31, 76, 77, 24, 12, I),
        ])
        return "".join(rays) + circle(78, 46, 20, GOLD) + '<g>%s</g>' % cloud
    if cid == "ai":
        return "".join([
            sparkle(56, 70, 40, I),
            sparkle(96, 32, 14, MINT),
            circle(98, 96, 6, GOLD),
        ])
    if cid == "agents":
        return "".join([
            rect(18, 26, 92, 76, 15, I),
            circle(32, 39, 4, CORAL), circle(44, 39, 4, "#E0A93A"), circle(56, 39, 4, MINT_DEEP),
            rect(18, 50, 92, 2.5, 0, deep, 'fill-opacity="0.18"'),
            '<path d="M36 64 L50 76 L36 88" fill="none" stroke="%s" stroke-width="8" '
            'stroke-linecap="round" stroke-linejoin="round"/>' % deep,
            rect(58, 82, 26, 8, 4, MINT_DEEP),
        ])
    if cid == "ingat":
        nodes = [(38, 42, 12), (90, 36, 10), (60, 96, 13), (98, 84, 8)]
        hub = (64, 64)
        edges = "".join('<line x1="%g" y1="%g" x2="%g" y2="%g" stroke="%s" stroke-opacity="0.6" '
                        'stroke-width="5" stroke-linecap="round"/>' % (hub[0], hub[1], x, y, I)
                        for x, y, _ in nodes)
        edges += ('<line x1="38" y1="42" x2="90" y2="36" stroke="%s" stroke-opacity="0.35" '
                  'stroke-width="4" stroke-linecap="round"/>' % I)
        dots = "".join(circle(x, y, r, I, 'fill-opacity="%s"' % ("0.55" if r < 9 else "1"))
                       for x, y, r in nodes)
        return edges + dots + circle(64, 64, 12, MINT) + circle(64, 64, 5, IVORY)
    if cid == "files":
        return "".join([
            '<path d="M32 30 H52 L60 38 H96 A12 12 0 0 1 108 50 V60 H20 V42 A12 12 0 0 1 32 30 Z" '
            'fill="%s" %s/>' % (I, ghost),
            rect(20, 46, 88, 58, 13, I),
            '<path d="M58 62 L88 56 V64 L58 70 Z" fill="%s"/>' % deep,
            rect(58, 64, 5.5, 26, 2, deep), rect(82.5, 58, 5.5, 26, 2, deep),
            '<ellipse cx="53" cy="90" rx="9" ry="7" fill="%s" transform="rotate(-18 53 90)"/>' % deep,
            '<ellipse cx="78" cy="84" rx="9" ry="7" fill="%s" transform="rotate(-18 78 84)"/>' % deep,
        ])
    if cid == "system":
        return "".join([
            rect(16, 26, 76, 54, 11, I, ghost),
            rect(40, 82, 28, 7, 3.5, I, ghost),
            rect(68, 42, 42, 68, 11, I),
            rect(74, 52, 30, 46, 6, deep, 'fill-opacity="0.22"'),
            rect(82, 46, 14, 3.5, 1.75, deep, 'fill-opacity="0.55"'),
            circle(89, 75, 8, MINT_DEEP),
            '<path d="M85 75 L88 78 L93.5 72" fill="none" stroke="%s" stroke-width="2.6" '
            'stroke-linecap="round" stroke-linejoin="round"/>' % I,
        ])
    if cid == "game":
        body = ("M40 44 H88 C102 44 110 56 112 70 L114 84 C116 98 100 104 92 94 L84 84 H44 "
                "L36 94 C28 104 12 98 14 84 L16 70 C18 56 26 44 40 44 Z")
        return "".join([
            '<path d="%s" fill="%s"/>' % (body, I),
            rect(30, 61, 22, 8, 2.5, deep), rect(37, 54, 8, 22, 2.5, deep),
            circle(86, 58, 6, MINT_DEEP), circle(98, 68, 6, "#D9557F"),
            circle(74, 68, 6, "#E0A93A"), circle(86, 78, 6, deep, 'fill-opacity="0.5"'),
        ])
    if cid == "keys":
        keys = []
        for row, y in enumerate((48, 63)):
            for k in range(6):
                x = 24 + k * 14 + (5 if row else 0)
                if row and k == 5:
                    continue
                fill = MINT_DEEP if (row == 0 and k == 4) else deep
                op = "1" if fill == MINT_DEEP else "0.42"
                keys.append(rect(x, y, 10, 10, 3, fill, 'fill-opacity="%s"' % op))
        return "".join([
            rect(12, 36, 104, 60, 14, I),
            "".join(keys),
            rect(38, 78, 52, 9, 4.5, deep, 'fill-opacity="0.65"'),
        ])
    raise KeyError(cid)


def tile_defs(cid, p, suffix=""):
    return (
        '<linearGradient id="g{s}" x1="0" y1="0" x2="0.35" y2="1">'
        '<stop offset="0" stop-color="{top}"/><stop offset="0.55" stop-color="{base}"/>'
        '<stop offset="1" stop-color="{bottom}"/></linearGradient>'
        '<radialGradient id="sh{s}" cx="0.28" cy="0.12" r="0.75">'
        '<stop offset="0" stop-color="#FFFFFF" stop-opacity="0.26"/>'
        '<stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/></radialGradient>'
        '<linearGradient id="rim{s}" x1="0" y1="0" x2="0" y2="1">'
        '<stop offset="0" stop-color="#FFFFFF" stop-opacity="0.38"/>'
        '<stop offset="0.5" stop-color="#FFFFFF" stop-opacity="0.06"/>'
        '<stop offset="1" stop-color="#000000" stop-opacity="0.10"/></linearGradient>'
        '<filter id="ds{s}" x="-20%" y="-20%" width="140%" height="150%">'
        '<feDropShadow dx="0" dy="2.6" stdDeviation="2.4" flood-color="{deep}" flood-opacity="0.55"/>'
        '</filter>'
    ).format(s=suffix, **p)


def tile_body(cid, p, suffix=""):
    sq = squircle(64, 64, 126)
    inner = squircle(64, 64, 123.5)
    return (
        '<path d="{sq}" fill="url(#g{s})"/>'
        '<path d="{sq}" fill="url(#sh{s})"/>'
        '<path d="{inner}" fill="none" stroke="url(#rim{s})" stroke-width="2"/>'
        '<g filter="url(#ds{s})">{m}</g>'
    ).format(sq=sq, inner=inner, s=suffix, m=motif(cid, p))


def icon_svg(cid):
    p = palette(cid)
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" width="128" height="128">'
            '<defs>%s</defs>%s</svg>' % (tile_defs(cid, p), tile_body(cid, p)))


def scene_svg(cid):
    """Quiet right-weighted banner art: one faint ring, a small ghost tile low on the left
    and the tilted category tile. The top-right corner stays clear for the status pill."""
    p = palette(cid)
    W, H = 480, 192
    cx, cy = 352, 150
    # Two small dots only, both low and away from the pill row
    dots = (circle(446, 150, 3.5, MINT, 'fill-opacity="0.7"')
            + circle(262, 176, 2.5, GOLD, 'fill-opacity="0.7"'))
    sparks = ""
    rings = ('<circle cx="%g" cy="%g" r="104" fill="none" stroke="%s" stroke-opacity="0.14" '
             'stroke-width="1.4"/>' % (cx, cy, p["base"]))
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">'
        '<defs>{d1}{d2}'
        '<radialGradient id="blob" cx="0.5" cy="0.5" r="0.5">'
        '<stop offset="0" stop-color="{base}" stop-opacity="0.20"/>'
        '<stop offset="1" stop-color="{base}" stop-opacity="0"/></radialGradient>'
        '<linearGradient id="fade" x1="0" y1="0" x2="1" y2="0">'
        '<stop offset="0.30" stop-color="#FFF" stop-opacity="0"/>'
        '<stop offset="0.62" stop-color="#FFF" stop-opacity="1"/></linearGradient>'
        '<mask id="m"><rect width="{W}" height="{H}" fill="url(#fade)"/></mask>'
        '<filter id="lift" x="-30%" y="-30%" width="160%" height="170%">'
        '<feDropShadow dx="0" dy="9" stdDeviation="9" flood-color="{deep}" flood-opacity="0.32"/></filter>'
        '</defs>'
        '<g mask="url(#m)">'
        '<circle cx="{cx}" cy="{cy}" r="190" fill="url(#blob)"/>{rings}'
        '<g transform="translate(274 150) rotate(9) scale(0.46) translate(-64 -64)" opacity="0.26">'
        '<path d="{sq}" fill="url(#gB)"/></g>'
        '<g transform="translate({cx} {cy}) rotate(-11) scale(1.12) translate(-64 -64)" filter="url(#lift)">'
        '{tile}</g>{dots}{sparks}</g></svg>'
    ).format(W=W, H=H, cx=cx, cy=cy, d1=tile_defs(cid, p, "A"), d2=tile_defs(cid, p, "B"),
             rings=rings, sq=squircle(64, 64, 126), tile=tile_body(cid, p, "A"),
             dots=dots, sparks=sparks, **p)


def main():
    os.makedirs(OUT, exist_ok=True)
    cells = []
    for cid in IDS:
        with open(os.path.join(OUT, "icon-%s.svg" % cid), "w", encoding="utf-8") as f:
            f.write(icon_svg(cid))
        with open(os.path.join(OUT, "scene-%s.svg" % cid), "w", encoding="utf-8") as f:
            f.write(scene_svg(cid))
        cells.append(cid)
    rows = "".join(
        '<div class="row"><img src="svg/icon-{c}.svg" width="128"><img src="svg/icon-{c}.svg" width="44">'
        '<img src="svg/icon-{c}.svg" width="20"><img class="sc" src="svg/scene-{c}.svg" width="480"><span>{c}</span></div>'
        .format(c=c) for c in cells)
    html = ('<!doctype html><meta charset="utf-8"><title>Deskmate Settings art</title><style>'
            'body{margin:0;font:13px Segoe UI;display:grid;grid-template-columns:1fr 1fr}'
            '.col{padding:20px}.dark{background:#2B2930;color:#F5F2EC}.light{background:#FDFDFC;color:#211C2A}'
            '.row{display:flex;align-items:center;gap:16px;margin-bottom:10px}</style>'
            '<div class="col dark">%s</div><div class="col light">%s</div>' % (rows, rows))
    with open(os.path.join(HERE, "preview.html"), "w", encoding="utf-8") as f:
        f.write(html)
    print("wrote", len(cells) * 2, "svgs")


if __name__ == "__main__":
    main()
