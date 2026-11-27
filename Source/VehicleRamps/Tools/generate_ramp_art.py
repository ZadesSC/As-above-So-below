#!/usr/bin/env python3
"""Generate grayscale (stuff-tintable) vehicle ramp textures for AASB II.

Usage:  <venv>/bin/python generate_ramp_art.py [output_dir]
Needs:  pillow, numpy, scipy

Outputs (RGBA, transparent background, R=G=B everywhere):
  AB_VehicleRampDown_{north,east,south}.png   1280x1280 (5x5 cells, 256 px/cell)
  AB_VehicleRampUp_{north,east,south}.png     1280x1280
  AB_VehicleRampDown.png / AB_VehicleRampUp.png   512x512 menu icons (south view)
  contact_sheet.png                           review sheet (plain + tinted)

Rotation convention (same as AB_GrandStairs*): for rotation R the LOW end of the
flight is on the R side and the HIGH end on the opposite side.
  Down (seen from the upper floor): mouth = high end = side OPPOSITE R,
                                    darkens toward R.
  Up   (seen from the lower floor): mouth = low end = R side, rises away from R.
West is RimWorld's horizontal flip of east.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import maximum_filter

SIZE = 1280          # px per rotation texture (5 cells * 256)
SS = 3               # supersampling factor
MARGIN = 40          # transparent margin outside the outline
OUTLINE = 16         # outline weight at 256 px/cell (reference is ~15)

# palette (grayscale values)
CAP_HI, CAP_LO = 252, 236      # curb / wall top caps
FACE = 162                     # south-facing vertical wall faces
FACE_DK = 132
SLOT = 24                      # recessed slots in wall faces


class Canvas:
    """Float grayscale + alpha canvas, drawn in 1x coordinates, supersampled."""

    def __init__(self, outline=OUTLINE):
        n = SIZE * SS
        self.n = n
        self.g = np.zeros((n, n), np.float32)
        self.a = np.zeros((n, n), np.float32)
        self.outline = outline
        self.shear = None  # (x_origin, k): y' = y + k * (x - x_origin)
        # coordinate ramps in 1x units, broadcastable
        self.X = ((np.arange(n, dtype=np.float32) + 0.5) / SS)[None, :]
        self.Y = ((np.arange(n, dtype=np.float32) + 0.5) / SS)[:, None]

    # -- geometry -----------------------------------------------------------
    def _tf(self, pts):
        out = []
        for x, y in pts:
            if self.shear is not None:
                x0, k = self.shear
                y = y + k * (x - x0)
            out.append((x * SS, y * SS))
        return out

    def mask(self, pts):
        m = Image.new("L", (self.n, self.n), 0)
        ImageDraw.Draw(m).polygon(self._tf(pts), fill=255)
        return np.asarray(m, np.float32) / 255.0

    # -- painting -----------------------------------------------------------
    def paint(self, pts, value, mode="normal", opacity=1.0):
        """value: scalar or array broadcastable to the canvas (use self.X/self.Y)."""
        m = self.mask(pts) * opacity
        if mode == "normal":
            self.g = self.g * (1 - m) + np.broadcast_to(value, self.g.shape) * m
            self.a = np.maximum(self.a, m)
        elif mode == "mul":      # multiply existing gray by `value` (0..1+)
            self.g = self.g * (1 - m) + self.g * value * m
        elif mode == "screen":   # blend toward white by `value` (0..1)
            self.g = self.g + (255 - self.g) * value * m
        self.g = np.clip(self.g, 0, 255)

    def rect(self, x0, y0, x1, y1, value, **kw):
        self.paint([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], value, **kw)

    def vgrad(self, y0, y1, v0, v1, gamma=1.0):
        t = np.clip((self.Y - y0) / (y1 - y0), 0, 1) ** gamma
        return v0 + (v1 - v0) * t

    def hgrad(self, x0, x1, v0, v1, gamma=1.0):
        t = np.clip((self.X - x0) / (x1 - x0), 0, 1) ** gamma
        return v0 + (v1 - v0) * t

    # -- output -------------------------------------------------------------
    def render(self):
        r = self.outline * SS
        solid = (self.a > 0.5).astype(np.float32)
        dil = maximum_filter(solid, size=2 * r + 1, mode="constant")
        alpha = np.maximum(dil, solid)
        gray = self.g * solid          # outline ring is black
        g8 = np.clip(gray, 0, 255).astype(np.uint8)
        a8 = (alpha * 255).astype(np.uint8)
        im = Image.merge("RGBA", [Image.fromarray(g8)] * 3 + [Image.fromarray(a8)])
        # premultiplied-safe downsample: colour under alpha==0 is black, which is
        # also the outline colour, so no fringe appears.
        return im.resize((SIZE, SIZE), Image.LANCZOS)


# ---------------------------------------------------------------------------
# shared detail helpers.  Everything is described for a ramp whose travel axis
# is vertical or horizontal; `axis` = 'y' or 'x'.
# ---------------------------------------------------------------------------
def grooves(c, lo, hi, a0, a1, axis, step=58, dark=0.80, light=0.10, inset=None):
    """Traction grooves across the slope: thin dark line + thin highlight."""
    pos = lo + step * 0.75
    while pos < hi - step * 0.4:
        if axis == "y":
            c.rect(a0, pos, a1, pos + 7, dark, mode="mul")
            c.rect(a0, pos + 7, a1, pos + 10, light, mode="screen")
        else:
            c.rect(pos, a0, pos + 7, a1, dark, mode="mul")
            c.rect(pos + 7, a0, pos + 10, a1, light, mode="screen")
        pos += step


def chevron(c, cx, cy, direction, w=300, h=150, t=46, **kw):
    """V-shaped arrow head centred on (cx, cy) pointing in `direction`."""
    hw = w / 2
    # pointing up (-y): apex at top
    pts = [(-hw, h / 2), (0, -h / 2), (hw, h / 2),
           (hw, h / 2 + t), (0, -h / 2 + t), (-hw, h / 2 + t)]
    pts = [(x, y - t / 2) for x, y in pts]
    rot = {"up": lambda x, y: (x, y), "down": lambda x, y: (-x, -y),
           "left": lambda x, y: (y, -x), "right": lambda x, y: (-y, x)}[direction]
    c.paint([(cx + rot(x, y)[0], cy + rot(x, y)[1]) for x, y in pts], **kw)


def chevrons(c, centres, direction, **kw):
    for cx, cy in centres:
        # dark body with a faint light edge so it survives on light and dark slope
        chevron(c, cx, cy, direction, value=0.84, mode="mul", **kw)


def slots(c, x0, x1, y0, y1, n, horizontal=True):
    """Row of recessed dark slots in a wall face (reads as a guard rail)."""
    span = (x1 - x0) if horizontal else (y1 - y0)
    pitch = span / n
    w = pitch * 0.62
    for i in range(n):
        s = (x0 if horizontal else y0) + pitch * (i + 0.5) - w / 2
        if horizontal:
            c.rect(s, y0, s + w, y1, c.vgrad(y0, y1, SLOT, SLOT + 50))
            c.rect(s, y1 - 5, s + w, y1, 205)
        else:
            c.rect(x0, s, x1, s + w, c.hgrad(x0, x1, SLOT, SLOT + 50))


def box(c):
    b = MARGIN + c.outline
    return b, SIZE - b


# ---------------------------------------------------------------------------
# DOWN ramps: sunk into the floor, darkening toward the deep end.
# ---------------------------------------------------------------------------
def down_vertical(c, mouth):
    """mouth='bottom' -> north rotation; mouth='top' -> south rotation."""
    lo, hi = box(c)
    cw = 92                               # curb width
    sx0, sx1 = lo + cw, hi - cw
    if mouth == "bottom":                 # north: deep end at top
        cap_h = 86
        sy0, sy1 = lo + cap_h, hi
        deep, shallow = sy0, sy1
        face_h = 62
        # curbs: top cap + small south end-face
        for x0, x1 in ((lo, sx0), (sx1, hi)):
            c.rect(x0, lo, x1, hi - face_h, c.vgrad(lo, hi, CAP_HI, CAP_LO))
            c.rect(x0, hi - face_h, x1, hi, c.vgrad(hi - face_h, hi, FACE, FACE_DK))
        c.rect(lo, lo, hi, lo + cap_h, c.vgrad(lo, lo + cap_h, CAP_HI, 240))
    else:                                 # south: deep end at bottom
        cap_h, face_h = 78, 150
        sy0, sy1 = lo, hi - cap_h - face_h
        deep, shallow = sy1, sy0
        for x0, x1 in ((lo, sx0), (sx1, hi)):
            c.rect(x0, lo, x1, sy1 + cap_h, c.vgrad(lo, hi, CAP_HI, CAP_LO))

    # driveway: light at the mouth, falling into black
    t = np.clip((c.Y - shallow) / (deep - shallow), 0, 1)
    surf = 6 + 236 * (1 - t) ** 1.3
    c.rect(sx0, sy0, sx1, sy1, surf)

    # entry apron (flat threshold at floor level)
    ap = 70
    if mouth == "bottom":
        c.rect(sx0, sy1 - ap, sx1, sy1, 224)
        c.rect(sx0, sy1 - ap - 5, sx1, sy1 - ap, 0.72, mode="mul")
        g0, g1 = sy0 + 40, sy1 - ap - 5
    else:
        c.rect(sx0, sy0, sx1, sy0 + ap, 224)
        c.rect(sx0, sy0 + ap, sx1, sy0 + ap + 5, 0.72, mode="mul")
        g0, g1 = sy0 + ap + 5, sy1 - 40

    grooves(c, g0, g1, sx0, sx1, "y")
    cx = SIZE / 2
    span = g1 - g0
    if mouth == "bottom":
        chevrons(c, [(cx, g0 + span * f) for f in (0.52, 0.76)], "up")
    else:
        chevrons(c, [(cx, g0 + span * f) for f in (0.24, 0.48)], "down")

    # retaining-wall faces: wedges that grow with depth (the slope tell-tale)
    wd = 54
    if mouth == "bottom":
        c.paint([(sx0, sy1 - ap), (sx0, sy0), (sx0 + wd, sy0)], 0.62, mode="mul")
        c.paint([(sx1, sy1 - ap), (sx1, sy0), (sx1 - wd, sy0)], 0.62, mode="mul")
        # head wall inner face dropping into the dark, with a portal lintel
        c.rect(sx0, sy0, sx1, sy0 + 46, c.vgrad(sy0, sy0 + 46, 120, 14))
        c.rect(sx0, sy0, sx1, sy0 + 6, 70)
    else:
        c.paint([(sx0, sy0 + ap), (sx0, sy1), (sx0 + wd, sy1)], 0.62, mode="mul")
        c.paint([(sx1, sy0 + ap), (sx1, sy1), (sx1 - wd, sy1)], 0.62, mode="mul")
        # near wall: cap + south face with guard slots
        c.rect(lo, sy1, hi, sy1 + cap_h, c.vgrad(sy1, sy1 + cap_h, CAP_HI, 240))
        fy0 = sy1 + cap_h
        c.rect(lo, fy0, hi, hi, c.vgrad(fy0, hi, FACE + 4, FACE_DK + 6))
        slots(c, lo + 30, hi - 30, fy0 + 30, hi - 34, 7)


def down_east(c):
    """Mouth on the west (left), descending toward the east (right)."""
    lo, hi = box(c)
    cw, cap_w, face_h = 92, 86, 62
    sy0, sy1 = lo + cw, hi - cw - face_h
    sx0, sx1 = lo, hi - cap_w
    # north curb, south curb (+ south face), east head wall (+ south face)
    c.rect(lo, lo, hi, sy0, c.hgrad(lo, hi, CAP_HI, CAP_LO))
    c.rect(lo, sy1, hi, sy1 + cw, c.hgrad(lo, hi, CAP_HI, CAP_LO))
    c.rect(sx1, lo, hi, sy1 + cw, c.hgrad(sx1, hi, 242, CAP_HI))
    c.rect(lo, sy1 + cw, hi, hi, c.vgrad(sy1 + cw, hi, FACE, FACE_DK))

    t = np.clip((c.X - sx0) / (sx1 - sx0), 0, 1)
    c.rect(sx0, sy0, sx1, sy1, 6 + 236 * (1 - t) ** 1.3)

    ap = 70
    c.rect(sx0, sy0, sx0 + ap, sy1, 224)
    c.rect(sx0 + ap, sy0, sx0 + ap + 5, sy1, 0.72, mode="mul")
    g0, g1 = sx0 + ap + 5, sx1 - 40
    grooves(c, g0, g1, sy0, sy1, "x")
    cy = (sy0 + sy1) / 2 + 40
    span = g1 - g0
    chevrons(c, [(g0 + span * f, cy) for f in (0.24, 0.48)], "right")

    # north retaining wall's south face is visible: wedge growing with depth
    wd = 190
    c.paint([(sx0 + ap, sy0), (sx1, sy0), (sx1, sy0 + wd)],
            c.hgrad(sx0, sx1, 150, 10, 0.8))
    c.paint([(sx0 + ap, sy0), (sx1, sy0 + wd), (sx1, sy0 + wd + 8), (sx0 + ap, sy0 + 3)],
            0.6, mode="mul")
    # head wall inner (west) face, falling into the dark
    c.rect(sx1 - 46, sy0, sx1, sy1, c.hgrad(sx1 - 46, sx1, 10, 60))
    c.rect(sx1 - 6, sy0, sx1, sy1, 70)


# ---------------------------------------------------------------------------
# UP ramps: raised slab, lighter toward the high end, shadowed lip at the mouth.
# ---------------------------------------------------------------------------
def up_south(c):
    """Mouth at the bottom (south), rising toward the top (north)."""
    lo, hi = box(c)
    cw = 92
    sx0, sx1 = lo + cw, hi - cw
    land = 96                              # flat landing at the high end
    lip = 64
    # side walls: rise with the slope -> lighter and casting more shadow uphill
    for x0, x1 in ((lo, sx0), (sx1, hi)):
        c.rect(x0, lo, x1, hi, c.vgrad(lo, hi, CAP_HI, 226))
        c.rect(x0, hi - lip, x1, hi, c.vgrad(hi - lip, hi, 190, FACE))
    c.rect(sx0, lo, sx1, hi, c.vgrad(lo, hi, 250, 196))
    # landing
    c.rect(sx0, lo, sx1, lo + land, 252)
    c.rect(sx0, lo + land, sx1, lo + land + 5, 0.74, mode="mul")
    # shadowed toe / lip where the slab meets the floor
    c.rect(sx0, hi - lip, sx1, hi, c.vgrad(hi - lip, hi, 176, 128))
    c.rect(sx0, hi - lip - 5, sx1, hi - lip, 0.70, mode="mul")
    g0, g1 = lo + land + 5, hi - lip - 5
    grooves(c, g0, g1, sx0, sx1, "y", dark=0.76, light=0.5)
    span = g1 - g0
    chevrons(c, [(SIZE / 2, g0 + span * f) for f in (0.30, 0.56, 0.82)], "up")
    # wall shadows on the deck, widening uphill (walls get taller)
    wd = 46
    c.paint([(sx0, hi - lip), (sx0, lo), (sx0 + wd, lo)], 0.74, mode="mul")
    c.paint([(sx1, hi - lip), (sx1, lo), (sx1 - wd, lo)], 0.74, mode="mul")


def up_north(c):
    """Mouth at the top (north); the tall back of the slab faces the viewer."""
    lo, hi = box(c)
    cw = 92
    face_h = 300
    sx0, sx1 = lo + cw, hi - cw
    sy1 = hi - face_h
    lip = 56
    for x0, x1 in ((lo, sx0), (sx1, hi)):
        c.rect(x0, lo, x1, sy1, c.vgrad(lo, sy1, 222, CAP_HI))
    c.rect(sx0, lo, sx1, sy1, c.vgrad(lo, sy1, 150, 248))
    c.rect(sx0, lo, sx1, lo + lip, c.vgrad(lo, lo + lip, 112, 150))
    c.rect(sx0, lo + lip, sx1, lo + lip + 5, 0.70, mode="mul")
    land = 90
    c.rect(sx0, sy1 - land, sx1, sy1, 252)
    c.rect(sx0, sy1 - land - 5, sx1, sy1 - land, 0.74, mode="mul")
    g0, g1 = lo + lip + 5, sy1 - land - 5
    grooves(c, g0, g1, sx0, sx1, "y", dark=0.76, light=0.5)
    span = g1 - g0
    chevrons(c, [(SIZE / 2, g0 + span * f) for f in (0.26, 0.60)], "down", h=130)
    wd = 46
    c.paint([(sx0, lo + lip), (sx0, sy1), (sx0 + wd, sy1)], 0.74, mode="mul")
    c.paint([(sx1, lo + lip), (sx1, sy1), (sx1 - wd, sy1)], 0.74, mode="mul")
    # back face of the raised slab
    c.rect(lo, sy1, hi, hi, c.vgrad(sy1, hi, FACE + 6, FACE_DK))
    c.rect(lo, sy1, hi, sy1 + 6, 0.8, mode="mul")
    # buttress pilasters under the side walls + recessed panels
    for x0, x1 in ((lo, sx0), (sx1, hi)):
        c.rect(x0, sy1, x1, hi, c.vgrad(sy1, hi, 205, 176))
    slots(c, sx0 + 30, sx1 - 30, sy1 + 44, hi - 44, 5)


def up_east(c):
    """Mouth on the east (right), rising toward the west (left).

    Drawn like the reference AB_GrandStairsUp_east: the deck is a parallelogram
    that climbs to the left, with the wedge-shaped south side wall below it.
    """
    lo, hi = box(c)
    rise = 250                         # screen lift of the high (west) end
    toe = 44                           # slab thickness at the mouth
    k = rise / (hi - lo)               # shear slope
    depth = (hi - toe - rise) - lo     # deck depth (north-south) on screen
    cw = 92

    # side face first (unsheared): wedge between the deck's south edge and ground
    c.paint([(lo, hi - toe - rise), (hi, hi - toe), (hi, hi), (lo, hi)],
            c.vgrad(hi - toe - rise, hi, FACE + 10, FACE_DK))
    # vertical ribs on the side face
    n = 6
    for i in range(1, n):
        x = lo + (hi - lo) * i / n
        ytop = hi - toe - rise + k * (x - lo)
        c.rect(x - 5, ytop, x + 5, hi, 0.78, mode="mul")
        c.rect(x + 5, ytop, x + 9, hi, 0.25, mode="screen")
    c.rect(lo, hi - 26, hi, hi, 0.82, mode="mul")      # footing line

    # deck, sheared: local frame has y=lo at the north edge on the LEFT side
    c.shear = (lo, k)
    y0, y1 = lo, lo + depth
    sy0, sy1 = y0 + cw, y1 - cw
    c.rect(lo, y0, hi, sy0, c.hgrad(lo, hi, CAP_HI, 226))
    c.rect(lo, sy1, hi, y1, c.hgrad(lo, hi, CAP_HI, 226))
    c.rect(lo, sy0, hi, sy1, c.hgrad(lo, hi, 250, 196))
    land, lip = 96, 64
    c.rect(lo, sy0, lo + land, sy1, 252)
    c.rect(lo + land, sy0, lo + land + 5, sy1, 0.74, mode="mul")
    c.rect(hi - lip, sy0, hi, sy1, c.hgrad(hi - lip, hi, 176, 128))
    c.rect(hi - lip, y0, hi, sy0, c.hgrad(hi - lip, hi, 190, FACE))
    c.rect(hi - lip, sy1, hi, y1, c.hgrad(hi - lip, hi, 190, FACE))
    c.rect(hi - lip - 5, sy0, hi - lip, sy1, 0.70, mode="mul")
    g0, g1 = lo + land + 5, hi - lip - 5
    grooves(c, g0, g1, sy0, sy1, "x", dark=0.76, light=0.5)
    span = g1 - g0
    cy = (sy0 + sy1) / 2
    chevrons(c, [(g0 + span * f, cy) for f in (0.20, 0.47, 0.74)], "left")
    # north wall's shadow on the deck, widening uphill
    c.paint([(hi - lip, sy0), (lo, sy0), (lo, sy0 + 60)], 0.74, mode="mul")
    # crisp edge between deck and side face
    c.rect(lo, y1 - 5, hi, y1, 0.80, mode="mul")
    c.shear = None


BUILDERS = {
    ("Down", "north"): lambda c: down_vertical(c, "bottom"),
    ("Down", "south"): lambda c: down_vertical(c, "top"),
    ("Down", "east"): down_east,
    ("Up", "north"): up_north,
    ("Up", "south"): up_south,
    ("Up", "east"): up_east,
}


def build(kind, rot, outline=OUTLINE):
    c = Canvas(outline)
    BUILDERS[(kind, rot)](c)
    return c.render()


def icon(kind):
    # Thicker outline so the weight survives the 1280 -> 512 reduction.
    return build(kind, "south", outline=30).resize((512, 512), Image.LANCZOS)


def assert_gray(im, name):
    a = np.asarray(im)
    assert im.mode == "RGBA", name
    assert (a[..., 0] == a[..., 1]).all() and (a[..., 1] == a[..., 2]).all(), name
    assert a[0, 0, 3] == 0 and a[-1, -1, 3] == 0, name + ": corners not transparent"


def contact_sheet(images, path):
    """images: ordered {filename: RGBA}, 4 per kind. Left block plain on mid-gray,
    right block multiplied by a wood-like tint, the way RimWorld's stuff colour
    is applied. Rows: Down, Up."""
    from PIL import ImageFont
    cell, pad, label = 400, 14, 26
    names = list(images)
    per_row = 4
    rows = len(names) // per_row
    block_w = pad + per_row * (cell + pad)
    w = 2 * block_w
    h = pad + rows * (label + cell + pad)
    sheet = Image.new("RGB", (w, h), (128, 128, 128))
    wood = (133, 97, 67)
    d = ImageDraw.Draw(sheet)
    d.rectangle([block_w, 0, w, h], fill=(96, 110, 88))
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 18)
    except OSError:
        font = ImageFont.load_default()
    for blk, tint in enumerate((None, wood)):
        for i, name in enumerate(names):
            im = images[name].resize((cell, cell), Image.LANCZOS)
            if tint:
                arr = np.asarray(im).astype(np.float32)
                arr[..., :3] *= np.array(tint, np.float32) / 255.0
                im = Image.fromarray(arr.astype(np.uint8), "RGBA")
            x = blk * block_w + pad + (i % per_row) * (cell + pad)
            y = pad + (i // per_row) * (label + cell + pad) + label
            d.rectangle([x, y, x + cell - 1, y + cell - 1], outline=(70, 70, 70))
            sheet.paste(im, (x, y), im)
            d.text((x, y - label + 2),
                   name.replace("AB_VehicleRamp", "").replace(".png", "")
                   + (" (wood tint)" if tint else ""), fill=(255, 255, 255), font=font)
    sheet.save(path)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
    os.makedirs(out, exist_ok=True)
    images = {}
    for kind in ("Down", "Up"):
        for rot in ("north", "east", "south"):
            images[f"AB_VehicleRamp{kind}_{rot}.png"] = build(kind, rot)
        images[f"AB_VehicleRamp{kind}.png"] = icon(kind)
    for name, im in images.items():
        assert_gray(im, name)
        im.save(os.path.join(out, name), optimize=True)
        print(f"{name}: {im.size[0]}x{im.size[1]} bbox={im.getbbox()}")
    contact_sheet(images, os.path.join(out, "contact_sheet.png"))


if __name__ == "__main__":
    main()
