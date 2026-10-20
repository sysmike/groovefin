#!/usr/bin/env python3
# Regenerates src/Groovefin.App/Assets/Taskbar/*.ico (needs Pillow): python3 scripts/assets/make-taskbar-icons.py src/Groovefin.App/Assets/Taskbar
"""Draws the taskbar thumbnail button icons: previous, play, pause, next, for light and dark taskbars."""
import sys
from PIL import Image, ImageDraw
out = sys.argv[1]
S = 256
shapes = {
    "play": [("poly", [(80, 44), (80, 212), (212, 128)])],
    "pause": [("rect", (64, 48, 112, 208)), ("rect", (144, 48, 192, 208))],
    "next": [("poly", [(52, 48), (52, 208), (168, 128)]), ("rect", (176, 48, 208, 208))],
    "previous": [("rect", (48, 48, 80, 208)), ("poly", [(204, 48), (204, 208), (88, 128)])],
}
# The suffix is the taskbar theme: light taskbars get dark glyphs and vice versa.
colours = {"light": (26, 26, 26, 255), "dark": (255, 255, 255, 255)}
for name, parts in shapes.items():
    for theme, colour in colours.items():
        img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        for kind, geo in parts:
            if kind == "poly":
                d.polygon(geo, fill=colour)
            else:
                d.rectangle(geo, fill=colour)
        img.save(f"{out}/{name}-{theme}.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32)], bitmap_format="bmp")
print("ok")
