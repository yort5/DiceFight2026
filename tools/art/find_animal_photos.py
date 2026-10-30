"""Find the animals in a folder of your own photos (2026-10-27).

    python find_animal_photos.py PHOTOS_DIR OUT_DIR [--only "manatee,bearded dragon"] [--copy]

The user, on card art: "it's quite possible I have a bearded dragon, plenty
of animals from our honeymoon in australia, and we're going to Florida in a
couple of months and will definitely take a picture of a manatee." A family
photo can stand in where no public-domain painting exists (like the Barn
Swallow's duck drawing).

Every photo under PHOTOS_DIR (JPEG, PNG, WebP, HEIC; folders searched
recursively, originals never touched) is scored by OpenCLIP against a
list of animal names - wild, farm, pets, Australian and Florida wildlife and
every Dice Kingdom animal - plus "not an animal" scenes (people,
landscapes, food, screenshots, toys, statues). A photo counts when most of
its score is on animals. People in the photo are fine, as long as an animal
is too.

Writes to OUT_DIR:
  animals.csv         every match: file, date taken, best animal, score, runners-up
  sheets/<animal>.jpg numbered contact sheets, best match first
  by-animal/<animal>/ copies of the matching files (with --copy)
  embeddings.npz      caches, so a re-run (new photos, new --only or
  labels.npz          --extra names) only reads what it hasn't seen

Options:
  --only NAMES   report just these animals (comma-separated); every photo
                 is still scored against the whole list, so a dolphin
                 isn't called a manatee for want of a better word
  --extra NAMES  add animals the built-in list doesn't have
  --min P        how much of a photo's score must be on animals (0.5)
  --copy         copy the matching originals into OUT_DIR/by-animal
  --threads N    CPU cores to use (2)
  --rest R       after each batch of photos, rest R times as long as the
                 batch took (1 = work half the time, 2 = a third, 0 = none)
  --fast         every core, no rests - the old full-speed behaviour

It runs gently by default (2 cores, resting half the time, low priority),
so an overnight run keeps the computer cool and quiet - after the dog
photos ran the fan at full tilt (2026-10-30). Progress is saved every 200
photos: stop it any time and run the same command again to carry on.

Setup: Python 3.10+, then
    pip install open_clip_torch pillow pillow-heif numpy
(the ~/.devtools/imgtools venv has them). The first run downloads the model
(~600 MB). It runs on a CPU, about a photo a second by default (a few a
second with --fast); nothing is uploaded.
Photos are matched on what they show, so the date and file name don't
matter, but the CSV lists the date taken to help find a trip's photos.
"""
import argparse
import csv
import os
import re
import shutil
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageOps

try:
    import pillow_heif  # iPhone photos

    pillow_heif.register_heif_opener()
except ImportError:
    pass

MODEL, PRETRAINED = "ViT-B-32", "laion2b_s34b_b79k"
EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif"}

# Every Dice Kingdom animal (Set 1, 2026-10-27).
DICE_KINGDOM = """honey badger, wolverine, grizzly bear, orca, peregrine falcon, silverback gorilla, stoat,
cape buffalo, mongoose, electric eel, army ant, komodo dragon, hippopotamus, musk ox, pangolin, hermit crab,
opossum, termite, rhinoceros, box turtle, leech, meerkat, bombardier beetle, osprey, mountain goat,
monarch butterfly, pigeon, greyhound, swift, flying squirrel, firefly, archerfish, barn owl, hyena,
anglerfish, cowbird, magpie, basilisk lizard, elephant, fox, cuttlefish, frilled lizard, chameleon,
pufferfish, scorpion, woodpecker, frigatebird, secretary bird, gibbon, kangaroo rat, wild boar,
orb weaver spider, centipede, dung beetle, lion, saiga antelope, mandrill, wombat, bison, sugar glider,
stag beetle, puffin, cobra, prairie dog, flatworm, hercules beetle, marmoset, fire ant, electric ray,
velvet worm, leaf insect, orchid mantis, bowerbird, fire salamander, filefish, hawk, pistol shrimp,
red panda, manatee, platypus, bearded dragon, box jellyfish, raccoon, praying mantis, polar bear,
cassowary, skunk, penguin, raven, chipmunk, hare, swordfish, cone snail, crocodile, bull shark,
bottlenose dolphin, barn swallow, koala, hummingbird, snow leopard, tardigrade"""

AUSTRALIA = """kangaroo, wallaby, quokka, echidna, emu, kookaburra, cockatoo, galah, rainbow lorikeet,
possum, tasmanian devil, dingo, goanna, blue-tongued skink, flying fox, black swan, ibis, pelican,
sea turtle, manta ray, reef shark, clownfish, coral reef fish, sea star, octopus, saltwater crocodile,
little penguin, fur seal, whale, huntsman spider, magpie-lark"""

FLORIDA = """alligator, american crocodile, heron, great egret, roseate spoonbill, flamingo, anhinga,
cormorant, brown pelican, sandhill crane, armadillo, iguana, gecko, anole lizard, gopher tortoise,
snapping turtle, sea lion, stingray, horseshoe crab, key deer, white-tailed deer, otter, bald eagle,
vulture, wood stork, frog, toad, snake, seagull, sandpiper"""

EVERYDAY = """dog, puppy, corgi, cat, kitten, horse, pony, donkey, cow, bull, pig, sheep, goat, llama,
alpaca, chicken, rooster, duck, goose, swan, turkey, rabbit, guinea pig, hamster, mouse, rat, squirrel,
bat, deer, moose, elk, bear, wolf, coyote, tiger, leopard, cheetah, jaguar, zebra, giraffe, camel,
monkey, chimpanzee, orangutan, sloth, beaver, badger, hedgehog, porcupine, ferret, seal, walrus,
shark, goldfish, tropical fish, jellyfish, crab, lobster, shrimp, snail, slug, worm, butterfly, moth,
bee, wasp, ant, beetle, ladybug, dragonfly, grasshopper, cricket, spider, lizard, turtle, tortoise,
parrot, owl, eagle, falcon, crow, robin, cardinal, blue jay, sparrow, finch, songbird, peacock,
ostrich, hen, chick"""

# Things a photo can be instead of an animal. Toys, statues and pictures of
# animals are here so they don't count as the animal itself.
NOT_ANIMAL = [
    "a person", "a group of people", "a selfie", "a baby", "a landscape", "a beach", "the ocean",
    "a mountain view", "a city street", "a building", "a room interior", "food on a plate", "a car",
    "a screenshot", "a document or receipt", "a sign", "flowers", "a tree", "a stuffed animal toy",
    "an animal statue or sculpture", "a painting or poster of an animal", "a drawing", "a logo",
    "an empty enclosure at a zoo", "fireworks", "a sunset",
]


def names(text):
    return [n.strip() for n in re.split(r"[,\n]", text) if n.strip()]


def photos_in(folder):
    return sorted(p for p in Path(folder).rglob("*") if p.suffix.lower() in EXTENSIONS and p.is_file())


def load(path, size=224):
    with Image.open(path) as im:
        im.draft("RGB", (size * 2, size * 2))  # JPEGs decode small, fast
        return ImageOps.exif_transpose(im).convert("RGB")


def date_taken(path):
    try:
        with Image.open(path) as im:
            exif = im.getexif()
            value = exif.get_ifd(0x8769).get(36867) or exif.get(306)  # DateTimeOriginal, DateTime
            return str(value or "")[:10].replace(":", "-")
    except Exception:
        return ""


class Clip:
    def __init__(self, threads):
        import open_clip
        import torch

        torch.set_num_threads(threads)
        self.torch = torch
        self.model, _, self.preprocess = open_clip.create_model_and_transforms(MODEL, pretrained=PRETRAINED)
        self.model.eval()
        self.tokenizer = open_clip.get_tokenizer(MODEL)

    def images(self, paths, batch=16, rest=0.0):
        """Yields (path, vector) per photo, vector None if unreadable. After each
        batch, sleeps `rest` times as long as the batch took, so the CPU cools."""
        for i in range(0, len(paths), batch):
            started = time.monotonic()
            tensors, good = [], []
            for p in paths[i:i + batch]:
                try:
                    tensors.append(self.preprocess(load(p)))
                    good.append(True)
                except Exception as error:  # a broken file shouldn't stop the run
                    print(f"  skipped {p}: {error}", file=sys.stderr)
                    good.append(False)
            vectors = iter(self._encode_images(tensors) if tensors else [])
            for p, ok in zip(paths[i:i + batch], good):
                yield p, next(vectors) if ok else None
            print(f"  {min(i + batch, len(paths))}/{len(paths)} photos read", end="\r", flush=True)
            if rest:
                time.sleep(rest * (time.monotonic() - started))
        print()

    def _encode_images(self, tensors):
        with self.torch.no_grad():
            v = self.model.encode_image(self.torch.stack(tensors)).float()
        return (v / v.norm(dim=-1, keepdim=True)).numpy()

    def texts(self, labels, animal):
        # A few phrasings per label, averaged (CLIP's usual prompt ensemble).
        templates = (["a photo of a {}.", "a close-up photo of a {}.", "a photo of a {} in the wild."]
                     if animal else ["a photo of {}."])
        prompts = [t.format(label) for label in labels for t in templates]
        with self.torch.no_grad():
            chunks = [self.model.encode_text(self.tokenizer(prompts[i:i + 256])).float()
                      for i in range(0, len(prompts), 256)]
            v = self.torch.cat(chunks)
        v = (v / v.norm(dim=-1, keepdim=True)).numpy().reshape(len(labels), len(templates), -1).mean(1)
        return v / np.linalg.norm(v, axis=1, keepdims=True)


def cached_texts(clip, labels, animal, cache_path):
    """Label vectors, kept in OUT_DIR so a re-run doesn't encode them again."""
    cache = {}
    if cache_path.exists():
        data = np.load(cache_path, allow_pickle=False)
        cache = dict(zip(data["labels"], data["vectors"]))
    key = lambda label: f"{'animal' if animal else 'scene'}:{label}"
    todo = [label for label in labels if key(label) not in cache]
    if todo:
        cache.update({key(label): v for label, v in zip(todo, clip.texts(todo, animal))})
        np.savez(cache_path, labels=list(cache), vectors=np.stack(list(cache.values())))
    return np.stack([cache[key(label)] for label in labels])


def cached_embeddings(clip, paths, cache_path, rest=0.0, save_every=200):
    """Image vectors, reusing the cache for files that haven't changed. The
    cache is saved every `save_every` photos, so a stopped overnight run
    (Ctrl+C, laptop asleep) picks up where it left off."""
    cache = {}
    if cache_path.exists():
        data = np.load(cache_path, allow_pickle=False)
        cache = {(k, s): v for k, s, v in zip(data["keys"], data["stamps"], data["vectors"])}
    stamp = lambda p: f"{p.stat().st_size}-{int(p.stat().st_mtime)}"
    save = lambda: np.savez(cache_path, keys=[k for k, _ in cache], stamps=[s for _, s in cache],
                            vectors=np.stack(list(cache.values())))
    todo = [p for p in paths if (str(p), stamp(p)) not in cache]
    if todo:
        print(f"Reading {len(todo)} new photos ({len(paths) - len(todo)} cached)")
        for n, (p, v) in enumerate(clip.images(todo, rest=rest), 1):
            if v is not None:
                cache[(str(p), stamp(p))] = v
            if n % save_every == 0:
                save()
    keep = {(str(p), stamp(p)) for p in paths}
    cache = {k: v for k, v in cache.items() if k in keep}
    if cache:
        save()
    return {Path(k): v for (k, _), v in cache.items()}


def contact_sheet(rows, path, cell=220, cols=6, limit=48):
    rows = rows[:limit]
    sheet = Image.new("RGB", (cols * cell, ((len(rows) + cols - 1) // cols) * (cell + 18)), "white")
    draw = ImageDraw.Draw(sheet)
    for i, row in enumerate(rows):
        x, y = (i % cols) * cell, (i // cols) * (cell + 18)
        try:
            im = load(row["file"], cell)
            im.thumbnail((cell - 6, cell - 6))
            sheet.paste(im, (x + 3, y + 3))
        except Exception:
            draw.text((x + 8, y + 40), "unreadable", fill="red")
        draw.text((x + 4, y + cell), f"{i}  {row['score']:.2f}  {Path(row['file']).name[:22]}", fill="black")
    sheet.save(path, quality=85)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("photos")
    parser.add_argument("out")
    parser.add_argument("--only", default="")
    parser.add_argument("--extra", default="")
    parser.add_argument("--min", type=float, default=0.5)
    parser.add_argument("--copy", action="store_true")
    parser.add_argument("--threads", type=int, default=2)
    parser.add_argument("--rest", type=float, default=1.0)
    parser.add_argument("--fast", action="store_true")
    args = parser.parse_args()
    if args.fast:
        args.threads, args.rest = os.cpu_count() or 4, 0.0
    else:
        try:
            os.nice(10)  # anything else on the computer goes first
        except (AttributeError, OSError):  # Windows has no nice
            pass

    animals = list(dict.fromkeys(names(DICE_KINGDOM) + names(AUSTRALIA) + names(FLORIDA) + names(EVERYDAY) + names(args.extra)))
    only = {n.lower() for n in names(args.only)}
    missing = only - set(animals)
    if missing:
        animals += sorted(missing)

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    paths = photos_in(args.photos)
    if not paths:
        sys.exit(f"No photos found under {args.photos}")
    print(f"{len(paths)} photos under {args.photos}; loading {MODEL}")
    clip = Clip(args.threads)
    vectors = cached_embeddings(clip, paths, out / "embeddings.npz", rest=args.rest)
    text = np.concatenate([cached_texts(clip, animals, True, out / "labels.npz"),
                           cached_texts(clip, NOT_ANIMAL, False, out / "labels.npz")])

    files = list(vectors)
    logits = 100 * np.stack([vectors[f] for f in files]) @ text.T
    probs = np.exp(logits - logits.max(1, keepdims=True))
    probs /= probs.sum(1, keepdims=True)
    n = len(animals)

    matches = []
    for f, p in zip(files, probs):
        animal_share = p[:n].sum()
        order = np.argsort(-p[:n])
        best = animals[order[0]]
        if animal_share < args.min or (only and best.lower() not in only):
            continue
        matches.append({
            "file": str(f), "date": date_taken(f), "animal": best,
            "score": float(p[order[0]] / animal_share), "animal_share": float(animal_share),
            "runners_up": "; ".join(f"{animals[j]} {p[j] / animal_share:.2f}" for j in order[1:3]),
        })
    matches.sort(key=lambda m: (m["animal"], -m["score"]))

    with open(out / "animals.csv", "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["animal", "score", "file", "date", "animal_share", "runners_up"])
        writer.writeheader()
        for m in matches:
            writer.writerow({**m, "score": f"{m['score']:.2f}", "animal_share": f"{m['animal_share']:.2f}"})

    (out / "sheets").mkdir(exist_ok=True)
    by_animal = {}
    for m in matches:
        by_animal.setdefault(m["animal"], []).append(m)
    for animal, rows in sorted(by_animal.items(), key=lambda kv: -len(kv[1])):
        slug = re.sub(r"[^a-z0-9]+", "-", animal.lower()).strip("-")
        contact_sheet(rows, out / "sheets" / f"{slug}.jpg")
        if args.copy:
            folder = out / "by-animal" / slug
            folder.mkdir(parents=True, exist_ok=True)
            for i, row in enumerate(rows):
                shutil.copy2(row["file"], folder / f"{i:03d}-{Path(row['file']).name}")
        print(f"{len(rows):5d}  {animal}")
    print(f"{len(matches)} of {len(files)} photos show an animal; see {out / 'animals.csv'} and {out / 'sheets'}")


if __name__ == "__main__":
    main()
