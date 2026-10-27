// Card paintings (2026-10-27). The user picked the vintage-painting style
// from the art test, with one rule: "for the die icon I think the
// silhouette still works best. Anywhere we expand to see the bigger
// picture we can use the vintage painting, but the dice on the field and
// attack zones and such should just get the outline." So: the card
// inspector, the mobile inspect panel and the Packs page show a painting
// when the card has one; dice, roster chips and lists keep CHARACTER_ICONS.
//
// Files: public/dice-kingdom/art/<slug>.webp (480x544, the whole picture)
// and <slug>-icon.webp (256 square, a tighter crop for round plates), made
// by tools/art/scene.py; sources and licences in CREDITS.txt there. A card
// with no painting yet falls back to its silhouette everywhere (Barn Swallow
// keeps the family duck drawing on purpose).

const ART: Record<string, string> = {
  "AI013": "koala", // Koala
  "DK-WING-03": "hummingbird", // Hummingbird
  "BFF033": "snow-leopard", // Snow Leopard
  "DK-EYE-03": "anglerfish", // Anglerfish
  "DK-WING-12": "archerfish", // Archerfish
  "DK-EYE-01": "barn-owl", // Barn Owl
  "DK-EYE-06": "basilisk", // Basilisk
  "DPS148": "bison", // Bison
  "SW001": "box-jellyfish", // Box Jellyfish
  "DK-SHELL-08": "box-turtle", // Box Turtle
  "DK-CLAW-08": "cape-buffalo", // Cape Buffalo
  "DOOM013": "cassowary", // Cassowary
  "GOTG021": "chipmunk", // Chipmunk
  "FUS004": "cone-snail", // Cone Snail
  "DK-EYE-04": "cowbird", // Cowbird
  "DK-EYE-09": "cuttlefish", // Cuttlefish
  "DPS029": "deathstalker-scorpion", // Deathstalker Scorpion
  "DPS099": "dung-beetle", // Dung Beetle
  "DK-CLAW-10": "electric-eel", // Electric Eel
  "IG058": "electric-ray", // Electric Ray
  "WWE033": "emperor-penguin", // Emperor Penguin
  "XFC017": "fire-salamander", // Fire Salamander
  "DK-WING-10": "flying-squirrel", // Flying Squirrel
  "DK-EYE-08": "fox", // Fox
  "DPS033": "frigatebird", // Frigatebird
  "DK-EYE-10": "frilled-lizard", // Frilled Lizard
  "DPS093": "giant-centipede", // Giant Centipede
  "DPS057": "gibbon", // Gibbon
  "DK-WING-07": "greyhound", // Greyhound
  "DK-CLAW-03": "grizzly-bear", // Grizzly Bear
  "SW018": "harris-s-hawk", // Harris's Hawk
  "DK-SHELL-04": "hermit-crab", // Hermit Crab
  "DK-SHELL-01": "hippopotamus", // Hippopotamus
  "DK-WING-06": "homing-pigeon", // Homing Pigeon
  "DK-CLAW-01": "honey-badger", // Honey Badger
  "DK-EYE-02": "hyena", // Hyena
  "DK-CLAW-12": "komodo-dragon", // Komodo Dragon
  "AVX019": "leaf-insect", // Leaf Insect
  "DPS102": "lioness", // Lioness
  "DPS114": "mandrill", // Mandrill
  "DRS022": "marmoset", // Marmoset
  "XFC110": "mimic-filefish", // Mimic Filefish
  "DK-WING-05": "monarch-butterfly", // Monarch Butterfly
  "DK-CLAW-09": "mongoose", // Mongoose
  "DK-WING-04": "mountain-goat", // Mountain Goat
  "DK-SHELL-02": "musk-ox", // Musk Ox
  "DK-SHELL-05": "opossum", // Opossum
  "DPS071": "orb-weaver", // Orb Weaver
  "DK-WING-01": "osprey", // Osprey
  "DK-SHELL-03": "pangolin", // Pangolin
  "DK-CLAW-05": "peregrine-falcon", // Peregrine Falcon
  "DOOM017": "platypus", // Platypus
  "WOL071": "polar-bear", // Polar Bear
  "TAG003": "prairie-dog", // Prairie Dog
  "WWE037": "praying-mantis", // Praying Mantis
  "CW014": "pufferfish", // Pufferfish
  "MSW020": "puffin", // Puffin
  "CW054": "red-panda", // Red Panda
  "DK-SHELL-07": "rhinoceros", // Rhinoceros
  "DPS038": "secretary-bird", // Secretary Bird
  "SKC090": "spitting-cobra", // Spitting Cobra
  "JLL002": "stag-beetle", // Stag Beetle
  "DK-CLAW-07": "stoat", // Stoat
  "GOTG105": "sugar-glider", // Sugar Glider
  "DK-WING-09": "swift", // Swift
  "DPS061": "wild-boar", // Wild Boar
  "DK-CLAW-02": "wolverine", // Wolverine
  "DPS142": "wombat", // Wombat
  "DPS031": "woodpecker", // Woodpecker
};

export function hasPainting(cardId: string | null | undefined): boolean {
  return !!cardId && cardId in ART;
}

export function paintingSrc(cardId: string, crop: "full" | "icon" = "full"): string | null {
  const slug = ART[cardId];
  if (!slug) return null;
  return `/dice-kingdom/art/${slug}${crop === "icon" ? "-icon" : ""}.webp`;
}

// The painting filling its box (round plate, banner, thumbnail - the
// container sets the size and shape), or null when there isn't one.
export function Painting({ cardId, crop = "full", alt, className }: {
  cardId: string | null | undefined;
  crop?: "full" | "icon";
  alt: string;
  className?: string;
}) {
  const src = cardId ? paintingSrc(cardId, crop) : null;
  if (!src) return null;
  return <img className={`dk-painting${className ? ` ${className}` : ""}`} src={src} alt={alt} loading="lazy" decoding="async" />;
}
