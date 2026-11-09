"""Builds Cooldown's icons from the source artwork (a transparent PNG or WebP of any size).

    python make-icon.py <artwork>

Writes, next to this script:
  Cooldown.png  512x512, for the app's window and title bar
  Cooldown.ico  every size Windows uses, from 16 to 256 pixels, for the exe, taskbar and Start menu
"""
import os
import sys
from PIL import Image

here = os.path.dirname(os.path.abspath(__file__))
art = Image.open(sys.argv[1]).convert("RGBA")

# Trim to the artwork (ignoring the faintest glow), then center it on a square with a small margin.
box = art.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
art = art.crop(box)
side = round(max(art.size) * 1.04)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(art, ((side - art.width) // 2, (side - art.height) // 2))

square.resize((512, 512), Image.LANCZOS).save(os.path.join(here, "Cooldown.png"))
master = square.resize((256, 256), Image.LANCZOS)
master.save(os.path.join(here, "Cooldown.ico"),
            sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)])
print("wrote Cooldown.png and Cooldown.ico from", sys.argv[1], "artwork box", box)
