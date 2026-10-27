"""Full-picture card art for Dice Kingdom (2026-10-27 art test, round 2).

The user, on round 1 (stylize.py's cut-outs): "the stylized photos could
work if we didn't crop and kept the whole picture, but ... a bit more
colorful. The vintage painting could also work if we either didn't crop or
did something more interesting with the background." So these keep a scene
behind the animal instead of cutting it out.

    python scene.py paint    PHOTO OUT_PREFIX --frame x0,y0,x1,y1 --face x0,y0,x1,y1
    python scene.py plate    PLATE OUT_PREFIX --frame ... --face ... [--hand-colour]
    python scene.py backdrop PLATE OUT_PREFIX --crop ... --energy Shell --style rays|wash [--erase 'x,y x,y ...']

paint: the photo as a vivid painting (OpenCV's edge-preserving stylization,
then extra colour and a warm/cool grade). plate: a vintage painting as it
is; --hand-colour washes colour into a black-and-white engraving.
backdrop: the animal cut out of a plate (rembg) and set on a new
background in its energy's colours - "rays" (a trading-card sunburst) or
"wash" (soft watercolour blobs).

Each writes OUT_PREFIX-full.webp (480x544, the full-art card's art window)
and OUT_PREFIX-icon.webp (256 square, the tighter --face crop for the small
round avatars and dice; backdrop centres it on the animal).

Needs Pillow, numpy, opencv-python-headless and rembg: the
~/.devtools/imgtools venv has them.
"""
import argparse

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ENERGY = {"Claw": "#b8451f", "Shell": "#2f6b52", "Wing": "#2f6690", "Eye": "#6a4189"}
CREAM = (255, 248, 236)
FULL = (480, 544)
ICON = 256


def box(text):
    return tuple(int(v) for v in text.split(","))


def points(text):
    return [tuple(int(v) for v in pair.split(",")) for pair in text.split()]


def hex_rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def fit(im, frame, size):
    """Crop to `frame`, widened to the target's aspect ratio, and scale."""
    x0, y0, x1, y1 = frame
    w, h = x1 - x0, y1 - y0
    want = size[0] / size[1]
    if w / h > want:
        grow = w / want - h
        y0, y1 = y0 - grow / 2, y1 + grow / 2
    else:
        grow = h * want - w
        x0, x1 = x0 - grow / 2, x1 + grow / 2
    # Slide back inside the picture where it can.
    dx = max(0, -x0) - max(0, x1 - im.width)
    dy = max(0, -y0) - max(0, y1 - im.height)
    return im.crop((round(x0 + dx), round(y0 + dy), round(x1 + dx), round(y1 + dy))).resize(size, Image.LANCZOS)


def vibrance(bgr, amount):
    """More colour where there's little, so greys warm up but reds don't clip."""
    hsv = cv2.cvtColor(bgr, cv2.COLOR_BGR2HSV).astype(np.float32)
    s = hsv[..., 1] / 255
    hsv[..., 1] = np.clip((s + amount * s * (1 - s) * 2) * 255, 0, 255)
    return cv2.cvtColor(hsv.astype(np.uint8), cv2.COLOR_HSV2BGR)


def grade(bgr):
    """A little contrast, warm highlights and cool shadows (storybook light)."""
    f = bgr.astype(np.float32) / 255
    f = np.clip((f - 0.5) * 1.15 + 0.5, 0, 1)
    lum = f.mean(axis=2, keepdims=True)
    warm, cool = np.array([0.9, 0.98, 1.1]), np.array([1.04, 1.0, 0.96])  # BGR
    return (np.clip(f * (lum * warm + (1 - lum) * cool), 0, 1) * 255).astype(np.uint8)


def painted(im):
    bgr = cv2.cvtColor(np.array(im.convert("RGB")), cv2.COLOR_RGB2BGR)
    bgr = cv2.stylization(bgr, sigma_s=80, sigma_r=0.35)
    bgr = grade(vibrance(bgr, 1.1))
    return Image.fromarray(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))


def hand_coloured(im):
    """An engraving's ink kept, its paper washed: sky at the top, earth below."""
    grey = np.array(im.convert("L")).astype(np.float32) / 255
    h, w = grey.shape
    t = np.linspace(0, 1, h)[:, None, None]
    sky, earth = np.array([178, 208, 228]), np.array([226, 184, 120])
    wash = sky * (1 - t) + earth * t
    # Soft blotches so it reads as brushwork, not a gradient.
    rng = np.random.default_rng(7)
    noise = cv2.GaussianBlur(rng.random((h, w)).astype(np.float32), (0, 0), 18)
    noise = (noise - noise.mean()) / (noise.std() + 1e-6)
    wash = wash * (1 + 0.07 * noise[..., None])
    ink = np.array([52, 38, 30])
    out = ink + (wash - ink) * grey[..., None]
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))


def cut_out(im, erase=()):
    from rembg import new_session, remove

    cut = remove(im.convert("RGB"), session=new_session("isnet-general-use"), post_process_mask=True)
    alpha = cut.getchannel("A").point(lambda a: 255 if a > 128 else 0).filter(ImageFilter.MedianFilter(5))
    for polygon in erase:  # bits of neighbouring animals the cut-out kept
        ImageDraw.Draw(alpha).polygon(polygon, fill=0)
    # Drop specks: anything under 2% of the biggest piece.
    n, labels, stats, _ = cv2.connectedComponentsWithStats(np.array(alpha), connectivity=8)
    if n > 2:
        areas = stats[1:, cv2.CC_STAT_AREA]
        keep = np.isin(labels, 1 + np.flatnonzero(areas >= areas.max() * 0.02))
        alpha = Image.fromarray((keep * 255).astype(np.uint8))
    cut.putalpha(alpha)
    return cut.crop(alpha.getbbox())


def rays(size, energy):
    w, h = size
    base = hex_rgb(ENERGY[energy])
    light, dark = mix(base, CREAM, 0.55), mix(base, CREAM, 0.3)
    bg = Image.new("RGB", size, dark)
    draw = ImageDraw.Draw(bg)
    cx, cy, r = w / 2, h * 0.45, max(w, h) * 1.5
    for i in range(0, 24, 2):
        a0, a1 = np.radians(i * 15), np.radians((i + 1) * 15)
        draw.polygon([(cx, cy), (cx + r * np.cos(a0), cy + r * np.sin(a0)), (cx + r * np.cos(a1), cy + r * np.sin(a1))], fill=light)
    glow = Image.new("L", size, 0)
    ImageDraw.Draw(glow).ellipse((cx - w * 0.45, cy - w * 0.45, cx + w * 0.45, cy + w * 0.45), fill=255)
    bg = Image.composite(Image.new("RGB", size, mix(base, CREAM, 0.85)), bg, glow.filter(ImageFilter.GaussianBlur(w * 0.12)))
    return vignette(bg, base)


def wash(size, energy):
    w, h = size
    base = hex_rgb(ENERGY[energy])
    bg = Image.new("RGB", size, mix(base, CREAM, 0.8))
    rng = np.random.default_rng(3)
    tones = [mix(base, CREAM, t) for t in (0.15, 0.35, 0.55)] + [(236, 196, 110), (150, 196, 170), (214, 140, 120)]
    for i in range(18):
        layer = Image.new("L", size, 0)
        x, y, r = rng.uniform(0, w), rng.uniform(0, h), rng.uniform(w * 0.15, w * 0.4)
        ImageDraw.Draw(layer).ellipse((x - r, y - r * 0.8, x + r, y + r * 0.8), fill=int(rng.uniform(120, 200)))
        bg = Image.composite(Image.new("RGB", size, tones[i % len(tones)]), bg, layer.filter(ImageFilter.GaussianBlur(r * 0.35)))
    return vignette(bg, base)


def vignette(bg, base):
    w, h = bg.size
    mask = Image.new("L", bg.size, 255)
    ImageDraw.Draw(mask).ellipse((-w * 0.15, -h * 0.1, w * 1.15, h * 1.1), fill=0)
    edge = Image.new("RGB", bg.size, mix(base, (20, 16, 14), 0.4))
    return Image.composite(edge, bg, mask.filter(ImageFilter.GaussianBlur(w * 0.15)).point(lambda v: v * 0.7))


def on_backdrop(cut, bg, scale=0.86):
    """The cut-out with a cream sticker edge and a soft shadow, low and centred."""
    w, h = bg.size
    s = min(w * scale / cut.width, h * scale / cut.height)
    cut = cut.resize((round(cut.width * s), round(cut.height * s)), Image.LANCZOS)
    pad = 16
    sheet = Image.new("RGBA", (cut.width + pad * 2, cut.height + pad * 2))
    sheet.paste(cut, (pad, pad))
    alpha = sheet.getchannel("A")
    rim = alpha.filter(ImageFilter.MaxFilter(7))
    shadow = Image.new("RGBA", sheet.size, (20, 16, 14, 0))
    shadow.putalpha(rim.filter(ImageFilter.GaussianBlur(8)).point(lambda v: v * 0.55))
    sticker = Image.new("RGBA", sheet.size)
    sticker.paste(Image.new("RGBA", sheet.size, CREAM + (255,)), mask=rim)
    sticker.alpha_composite(sheet)
    out = bg.convert("RGBA")
    x, y = (w - sheet.width) // 2, round((h - sheet.height) * 0.62)
    out.alpha_composite(shadow, (x + 5, y + 8))
    out.alpha_composite(sticker, (x, y))
    return out.convert("RGB")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["paint", "plate", "backdrop"])
    parser.add_argument("source")
    parser.add_argument("out_prefix")
    parser.add_argument("--frame", type=box, help="the scene to keep (x0,y0,x1,y1); widened to fit")
    parser.add_argument("--face", type=box, help="the tighter crop for the small icon")
    parser.add_argument("--crop", type=box, help="backdrop: the part of the plate holding the animal")
    parser.add_argument("--erase", action="append", default=[], type=points,
                        help="backdrop: a polygon to drop from the cut-out, 'x,y x,y ...' within --crop (repeatable)")
    parser.add_argument("--hand-colour", action="store_true")
    parser.add_argument("--energy", choices=ENERGY)
    parser.add_argument("--style", choices=["rays", "wash"], default="rays")
    args = parser.parse_args()
    im = Image.open(args.source).convert("RGB")
    if args.mode == "backdrop":
        cut = cut_out(im.crop(args.crop) if args.crop else im, args.erase)
        make = rays if args.style == "rays" else wash
        full = on_backdrop(cut, make(FULL, args.energy))
        icon = on_backdrop(cut, make((ICON, ICON), args.energy), scale=0.95)
    else:
        look = painted if args.mode == "paint" else hand_coloured if args.hand_colour else (lambda x: x)
        full = look(fit(im, args.frame, FULL))
        icon = look(fit(im, args.face or args.frame, (ICON, ICON)))
    full.save(f"{args.out_prefix}-full.webp", quality=85)
    icon.save(f"{args.out_prefix}-icon.webp", quality=85)


if __name__ == "__main__":
    main()
