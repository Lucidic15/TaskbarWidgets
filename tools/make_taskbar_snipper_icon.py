"""Draws the Taskbar Snipper app icon: the corners of a frame with a crosshair in them (the taskbar
button's screenshot glyph) on a gradient tile, in the style of the other two widgets' icons.

Usage (needs Pillow): python tools/make_taskbar_snipper_icon.py icons/taskbar-snipper.ico preview.png
"""
import sys
from PIL import Image, ImageDraw

TOP = (251, 146, 60)      # orange
BOTTOM = (225, 29, 72)    # rose
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
    # The frame's corners: thicker at small sizes, so they stay clear.
    half = s * (0.27 if small else 0.25)
    stroke = s * (0.085 if small else 0.06)
    arm = half * 0.62
    left, top, right, bottom = cx - half, cy - half, cx + half, cy + half
    corners = [
        [(left, top + arm), (left, top), (left + arm, top)],
        [(right - arm, top), (right, top), (right, top + arm)],
        [(right, bottom - arm), (right, bottom), (right - arm, bottom)],
        [(left + arm, bottom), (left, bottom), (left, bottom - arm)],
    ]
    for corner in corners:
        d.line(corner, fill=WHITE, width=int(stroke), joint="curve")
        for x, y in (corner[0], corner[2]):
            d.ellipse([x - stroke / 2, y - stroke / 2, x + stroke / 2, y + stroke / 2], fill=WHITE)
    # The crosshair.
    cross = half * 0.36
    d.line([(cx - cross, cy), (cx + cross, cy)], fill=WHITE, width=int(stroke))
    d.line([(cx, cy - cross), (cx, cy + cross)], fill=WHITE, width=int(stroke))
    for x, y in ((cx - cross, cy), (cx + cross, cy), (cx, cy - cross), (cx, cy + cross)):
        d.ellipse([x - stroke / 2, y - stroke / 2, x + stroke / 2, y + stroke / 2], fill=WHITE)

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
