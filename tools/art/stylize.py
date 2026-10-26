"""Turn an animal photo into flat "sticker" art for Dice Kingdom (2026-10-26 art test).

    python stylize.py PHOTO OUT_PREFIX --energy Wing [--crop x0,y0,x1,y1]

Writes three 256px squares with a transparent background, a dark outline
and a light rim: OUT_PREFIX-cut.png (just cut out - for a painting or plate),
OUT_PREFIX-flat.png (the animal's own colours, flattened to a few tones) and
OUT_PREFIX-tint.png (shaded in its energy's colours).

Needs Pillow and rembg (background removal, downloads its model on first
run): the ~/.devtools/imgtools venv has both.
"""
import argparse

from PIL import Image, ImageChops, ImageFilter, ImageOps
from rembg import new_session, remove

# web/src/dicekingdom/dicekingdom.css's energy colours.
ENERGY = {"Claw": "#b8451f", "Shell": "#2f6b52", "Wing": "#2f6690", "Eye": "#6a4189"}
INK = (31, 26, 23)
CREAM = (255, 248, 236)
SIZE = 256


def hex_rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def cut_out(photo, crop, session):
    im = Image.open(photo).convert("RGB")
    if crop:
        im = im.crop(crop)
    cut = remove(im, session=session, post_process_mask=True)
    # Keep the biggest solid part of the mask, softened at the edge.
    alpha = cut.getchannel("A").point(lambda a: 255 if a > 128 else 0).filter(ImageFilter.MedianFilter(5))
    cut.putalpha(alpha)
    box = alpha.getbbox()
    cut = cut.crop(box)
    # Square, with room for the outline.
    side = round(max(cut.size) * 1.12)
    square = Image.new("RGBA", (side, side))
    square.paste(cut, ((side - cut.width) // 2, (side - cut.height) // 2))
    return square.resize((SIZE, SIZE), Image.LANCZOS)


def outlined(art, alpha):
    """A dark outline and a thin light rim behind the art (a sticker edge)."""
    rim = alpha.filter(ImageFilter.MaxFilter(5))
    edge = rim.filter(ImageFilter.MaxFilter(5))
    out = Image.new("RGBA", art.size)
    out.paste(Image.new("RGBA", art.size, INK + (255,)), mask=edge)
    out.paste(Image.new("RGBA", art.size, CREAM + (255,)), mask=rim)
    out.alpha_composite(art)
    return out


def smoothed(im, small=96):
    """Big flat shapes: posterize at low resolution, then scale back up."""
    tiny = im.resize((small, small), Image.LANCZOS).filter(ImageFilter.ModeFilter(3))
    return tiny.resize(im.size, Image.LANCZOS).filter(ImageFilter.ModeFilter(9))


def flat(cut, colours=6):
    """The animal's own colours, smoothed and cut down to a few flat tones."""
    alpha = cut.getchannel("A")
    rgb = ImageOps.autocontrast(smoothed(cut.convert("RGB")), cutoff=1)
    poster = rgb.quantize(colors=colours, method=Image.Quantize.MEDIANCUT).convert("RGB")
    art = poster.convert("RGBA")
    art.putalpha(alpha)
    return outlined(art, alpha)


def tint(cut, energy):
    """Four tones from ink through the energy colour to cream."""
    alpha = cut.getchannel("A")
    base = hex_rgb(ENERGY[energy])
    palette = [INK, mix(INK, base, 0.65), base, mix(base, CREAM, 0.55), CREAM]
    lum = ImageOps.autocontrast(smoothed(cut.convert("L")), cutoff=2)
    steps = len(palette)
    banded = lum.point(lambda v: min(steps - 1, v * steps // 256))
    art = Image.new("RGBA", cut.size)
    for i, colour in enumerate(palette):
        mask = ImageChops.multiply(banded.point(lambda v, i=i: 255 if v == i else 0), alpha)
        art.paste(Image.new("RGBA", cut.size, colour + (255,)), mask=mask)
    return outlined(art, alpha)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("photo")
    parser.add_argument("out_prefix")
    parser.add_argument("--energy", choices=ENERGY, required=True)
    parser.add_argument("--crop", help="x0,y0,x1,y1 in the photo, before background removal")
    args = parser.parse_args()
    crop = tuple(int(v) for v in args.crop.split(",")) if args.crop else None
    cut = cut_out(args.photo, crop, new_session("isnet-general-use"))
    outlined(cut, cut.getchannel("A")).save(f"{args.out_prefix}-cut.png", optimize=True)
    flat(cut).save(f"{args.out_prefix}-flat.png", optimize=True)
    tint(cut, args.energy).save(f"{args.out_prefix}-tint.png", optimize=True)


if __name__ == "__main__":
    main()
