"""Draws the RDPLauncher app icon: a row of displays, the middle one in front, on a gradient tile.

Usage (needs Pillow): python tools/make_icon.py src/RDPLauncher/RDPLauncher.ico preview.png
"""
import sys
from PIL import Image, ImageDraw

TOP = (45, 212, 191)      # teal
BOTTOM = (14, 116, 144)   # deep cyan
WHITE = (255, 255, 255, 255)
SHADE = (255, 255, 255, 150)

def monitor(d, cx, top, width, height, stroke, fill, screen_fill=None, with_stand=True):
    """A monitor outline centered on cx, its screen's top edge at `top`; returns the screen box."""
    left, right = cx - width / 2, cx + width / 2
    box = [left, top, right, top + height]
    d.rounded_rectangle(box, radius=width * 0.08, outline=fill, width=int(stroke), fill=screen_fill)
    if with_stand:
        neck_bottom = top + height + height * 0.22
        d.line([(cx, top + height), (cx, neck_bottom)], fill=fill, width=int(stroke))
        d.line([(cx - width * 0.2, neck_bottom), (cx + width * 0.2, neck_bottom)], fill=fill, width=int(stroke))
        for x in (cx - width * 0.2, cx + width * 0.2):
            d.ellipse([x - stroke / 2, neck_bottom - stroke / 2, x + stroke / 2, neck_bottom + stroke / 2], fill=fill)
    return box

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
    cx = s / 2
    if small:
        # Two displays side by side read better than three at tiny sizes.
        stroke = s * 0.07
        w, h = s * 0.36, s * 0.27
        top = s * 0.3
        for x in (cx - w * 0.56, cx + w * 0.56):
            monitor(d, x, top, w, h, stroke, WHITE)
    else:
        stroke = s * 0.042
        # Side displays, a little smaller and set back. Drawn on their own layer and blended in, since
        # drawing translucent white straight onto the tile would make those pixels see-through.
        w_side, h_side = s * 0.24, s * 0.18
        layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        layer_draw = ImageDraw.Draw(layer)
        for x in (cx - s * 0.255, cx + s * 0.255):
            monitor(layer_draw, x, s * 0.32, w_side, h_side, stroke, WHITE, with_stand=False)
        layer.putalpha(layer.getchannel("A").point(lambda a: a * SHADE[3] // 255))
        img.alpha_composite(layer)
        d = ImageDraw.Draw(img)
        # The middle display, in front, with the remote session's arrow on its screen.
        w, h = s * 0.4, s * 0.29
        top = s * 0.27
        bg = tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * 0.4) for i in range(3)) + (255,)
        box = monitor(d, cx, top, w, h, stroke, WHITE, screen_fill=bg)
        # Arrow pointing up and away: connecting to another computer.
        x0, y0, x1, y1 = box
        left, right = x0 + (x1 - x0) * 0.36, x0 + (x1 - x0) * 0.64
        upper, lower = y0 + (y1 - y0) * 0.3, y0 + (y1 - y0) * 0.7
        head = (x1 - x0) * 0.16
        line = int(stroke * 0.9)
        d.line([(left, lower), (right, upper)], fill=WHITE, width=line)
        d.line([(right - head, upper), (right, upper), (right, upper + head)], fill=WHITE, width=line, joint="curve")
        for x, y in ((left, lower), (right - head, upper), (right, upper + head), (right, upper)):
            d.ellipse([x - line / 2, y - line / 2, x + line / 2, y + line / 2], fill=WHITE)

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
