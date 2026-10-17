"""Draws the AudioFlipper app icon: a speaker inside two circular 'switch' arrows on a gradient tile.

Usage (needs Pillow): python tools/make_audio_flipper_icon.py icons/audio-flipper.ico preview.png
"""
import math, sys
from PIL import Image, ImageDraw

TOP = (56, 189, 248)      # sky blue
BOTTOM = (79, 70, 229)    # indigo
WHITE = (255, 255, 255, 255)

def render(size):
    s = size * 8  # supersample, then downscale for smooth edges
    small = size <= 32
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Gradient tile with rounded corners.
    margin = s * (0.04 if small else 0.06)
    radius = s * 0.22
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([margin, margin, s - margin, s - margin], radius=radius, fill=255)
    gradient = Image.linear_gradient("L").resize((s, s))
    tile = Image.composite(Image.new("RGBA", (s, s), BOTTOM + (255,)), Image.new("RGBA", (s, s), TOP + (255,)), gradient)
    img.paste(tile, (0, 0), mask)

    d = ImageDraw.Draw(img)
    cx = cy = s / 2

    # Speaker: a box and a cone, centered.
    unit = s * (0.15 if small else 0.125)
    sx = cx + unit * 0.2  # The speaker's shape is wider on the left; shift it so it looks centered.
    box = [sx - unit * 1.15, cy - unit * 0.55, sx - unit * 0.35, cy + unit * 0.55]
    d.rounded_rectangle(box, radius=unit * 0.12, fill=WHITE)
    cone = [(sx - unit * 0.45, cy - unit * 0.55), (sx + unit * 0.75, cy - unit * 1.3),
            (sx + unit * 0.75, cy + unit * 1.3), (sx - unit * 0.45, cy + unit * 0.55)]
    d.polygon(cone, fill=WHITE)

    # Two clockwise arrows forming a circle around the speaker.
    r = s * (0.31 if small else 0.30)
    stroke = s * (0.075 if small else 0.055)
    for start, end in ((200, 325), (20, 145)):
        d.arc([cx - r, cy - r, cx + r, cy + r], start, end, fill=WHITE, width=int(stroke))
        # Round cap at the tail.
        a = math.radians(start)
        px, py = cx + (r - stroke / 2) * math.cos(a), cy + (r - stroke / 2) * math.sin(a)
        d.ellipse([px - stroke / 2, py - stroke / 2, px + stroke / 2, py + stroke / 2], fill=WHITE)
        # Arrowhead at the end, pointing clockwise along the circle.
        a = math.radians(end)
        mid = r - stroke / 2
        px, py = cx + mid * math.cos(a), cy + mid * math.sin(a)
        tx, ty = -math.sin(a), math.cos(a)   # clockwise tangent
        nx, ny = math.cos(a), math.sin(a)    # outward normal
        head = stroke * 1.25
        tip = (px + tx * head * 1.6, py + ty * head * 1.6)
        left = (px + nx * head, py + ny * head)
        right = (px - nx * head, py - ny * head)
        d.polygon([tip, left, right], fill=WHITE)

    return img.resize((size, size), Image.LANCZOS)

def main(out_ico, out_preview):
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    images = [render(n) for n in sizes]
    images[-1].save(out_ico, format="ICO", sizes=[(n, n) for n in sizes], append_images=images[:-1])
    # Preview sheet of every size on light and dark backgrounds.
    width = sum(sizes) + 20 * (len(sizes) + 1)
    sheet = Image.new("RGBA", (width, 2 * 256 + 60), (243, 243, 243, 255))
    ImageDraw.Draw(sheet).rectangle([0, 256 + 30, width, 2 * 256 + 60], fill=(32, 32, 32, 255))
    x = 20
    for img in images:
        sheet.paste(img, (x, 20), img)
        sheet.paste(img, (x, 256 + 40), img)
        x += img.width + 20
    sheet.save(out_preview)

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
