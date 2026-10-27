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
// with no painting yet falls back to its silhouette everywhere.

const ART: Record<string, string> = {
  "AI013": "koala",
  "DK-WING-03": "hummingbird",
  "BFF033": "snow-leopard",
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
