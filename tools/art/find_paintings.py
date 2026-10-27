"""Find public-domain naturalist paintings for Dice Kingdom animals (2026-10-27).

    python find_paintings.py OUT_DIR "Snow Leopard" "Koala" ...

The user picked the vintage-painting style (art test round 2), so each
card wants an old natural-history plate or painting. For each animal this
searches Wikimedia Commons for illustrations, keeps the public-domain and
CC0 ones, and writes OUT_DIR/<slug>/candidates.json (title, licence,
author, date, source and thumbnail URLs) plus OUT_DIR/<slug>/sheet.jpg, a
numbered contact sheet to pick from by eye. Download the picked one's
`url` and frame it with scene.py.

Needs Pillow (the ~/.devtools/imgtools venv) and network access.
"""
import io
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

from PIL import Image, ImageDraw

API = "https://commons.wikimedia.org/w/api.php"
UA = "DiceFight2026-art/1.0 (https://github.com/yort5/DiceFight2026)"
STYLE = "(lithograph OR chromolithograph OR plate OR illustration OR watercolour OR painting OR engraving)"
FREE = re.compile(r"public domain|^pd|cc0", re.I)
NOT_ART = re.compile(r"\b(photo|photograph|dsc|img_|jpg_|stamp|coin|logo|map)\b", re.I)


def get(params):
    url = API + "?" + urllib.parse.urlencode({**params, "format": "json"})
    request = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def search(name, limit=40):
    data = get({
        "action": "query", "generator": "search", "gsrnamespace": 6, "gsrlimit": limit,
        "gsrsearch": f'"{name}" {STYLE} filetype:bitmap',
        "prop": "imageinfo", "iiprop": "url|size|extmetadata", "iiurlwidth": 360,
    })
    found = []
    for page in sorted(data.get("query", {}).get("pages", {}).values(), key=lambda p: p.get("index", 0)):
        info = page["imageinfo"][0]
        meta = {k: re.sub("<[^>]+>", "", str(v.get("value", ""))) for k, v in info.get("extmetadata", {}).items()}
        licence = meta.get("LicenseShortName", "")
        if not FREE.search(licence) or NOT_ART.search(page["title"]):
            continue
        if min(info["width"], info["height"]) < 500:
            continue
        found.append({
            "title": page["title"], "licence": licence, "author": meta.get("Artist", "")[:120],
            "date": meta.get("DateTimeOriginal", "")[:40], "url": info["url"],
            "thumb": info["thumburl"], "page": info["descriptionurl"],
        })
    return found


def sheet(candidates, path, cell=240):
    cols = 4
    rows = max(1, (len(candidates) + cols - 1) // cols)
    out = Image.new("RGB", (cols * cell, rows * (cell + 16)), "white")
    draw = ImageDraw.Draw(out)
    for i, c in enumerate(candidates):
        x, y = (i % cols) * cell, (i // cols) * (cell + 16)
        try:
            im = Image.open(io.BytesIO(fetch(c["thumb"]))).convert("RGB")
            im.thumbnail((cell - 8, cell - 8))
            out.paste(im, (x + 4, y + 4))
        except Exception as error:  # a dead thumbnail shouldn't sink the sheet
            draw.text((x + 8, y + 40), str(error)[:30], fill="red")
        draw.text((x + 6, y + cell - 2), f"{i}  {c['title'][5:40]}", fill="black")
        time.sleep(0.2)
    out.save(path, quality=85)


def main():
    out_dir = Path(sys.argv[1])
    for name in sys.argv[2:]:
        slug = re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")
        folder = out_dir / slug
        folder.mkdir(parents=True, exist_ok=True)
        candidates = search(name)[:12]
        (folder / "candidates.json").write_text(json.dumps(candidates, indent=1))
        sheet(candidates, folder / "sheet.jpg")
        print(f"{name}: {len(candidates)}")


if __name__ == "__main__":
    main()
