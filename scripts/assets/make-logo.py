#!/usr/bin/env python3
# Draws the logo (needs Pillow): python3 scripts/assets/make-logo.py 1024 assets/logo/groovefin-1024.png [--full-bleed]
"""Draws the Groovefin logo at any size: white notes on a blue-to-green tile, as in the app's Square150x150Logo."""
import sys
from PIL import Image, ImageChops, ImageDraw

size, out = int(sys.argv[1]), sys.argv[2]
full_bleed = "--full-bleed" in sys.argv[3:]

# Design units: the 300 px Square150x150Logo.scale-200.png the values were measured from.
TILE, RADIUS = (11.6, 288.4), 64.5
TOP_LEFT, BOTTOM_RIGHT = (58, 91, 218), (17, 184, 133)  # gradient from canvas corner to canvas corner
HEADS = [(99.75, 208.2), (199.35, 190.6)]  # centres of the two ellipses
HEAD_RX, HEAD_RY = 32.25, 26.5
STEM_WIDTH = 14.75
BEAM = [(117.25, 76.4), (231.6, 58.5), (231.6, 88.0), (117.25, 105.5)]  # top left, top right, bottom right, bottom left

SS = 4  # drawn this many times larger, then scaled down for smooth edges
n = size * SS
k = n / 300


def beam_top(x):
    (x0, y0), (x1, y1) = BEAM[0], BEAM[1]
    return y0 + (y1 - y0) * (x - x0) / (x1 - x0)


# Diagonal gradient: the mean of a horizontal and a vertical ramp, mapped onto the two colours.
ramp = Image.linear_gradient("L").resize((n, n), Image.BILINEAR)
t = ImageChops.add(ramp, ramp.transpose(Image.Transpose.ROTATE_90).transpose(Image.Transpose.FLIP_TOP_BOTTOM), scale=2)
tile = Image.merge("RGB", [t.point(lambda i, a=a, b=b: round(a + (b - a) * i / 255)) for a, b in zip(TOP_LEFT, BOTTOM_RIGHT)])

notes = Image.new("L", (n, n), 0)
d = ImageDraw.Draw(notes)
for cx, cy in HEADS:
    d.ellipse(((cx - HEAD_RX) * k, (cy - HEAD_RY) * k, (cx + HEAD_RX) * k, (cy + HEAD_RY) * k), fill=255)
    # The stem shares the head's right edge, runs from its middle up to the beam's top edge.
    right = cx + HEAD_RX
    left = right - STEM_WIDTH
    d.polygon([(left * k, beam_top(left) * k), (right * k, beam_top(right) * k), (right * k, cy * k), (left * k, cy * k)], fill=255)
d.polygon([(x * k, y * k) for x, y in BEAM], fill=255)
tile.paste((255, 255, 255), mask=notes)

mask = Image.new("L", (n, n), 0)
if full_bleed:
    mask.paste(255, (0, 0, n, n))
else:
    ImageDraw.Draw(mask).rounded_rectangle((TILE[0] * k, TILE[0] * k, TILE[1] * k - 1, TILE[1] * k - 1), radius=RADIUS * k, fill=255)
tile.putalpha(mask)
tile.resize((size, size), Image.LANCZOS).save(out, optimize=True)
print(f"{out}: {size}x{size}")
