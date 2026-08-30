import { CardText } from "./CardText";
import { DieIcon } from "./DieIcon";
import { isCommunityCard } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { CardDef, Die } from "./types";

// The Basic Action cards in the centre of the table.
//
// Rule 2.1.2 - these are COMMUNITY PROPERTY: either player may buy from
// them (which TurnEngine.Purchase has always allowed). They used to be
// drawn inside whichever player's Unpurchased roster happened to have
// brought them, which read as if they were that player's to buy. Here
// they sit on their own, between the two mats.
//
// They are still one card PER PLAYER WHO BROUGHT ONE, though, which is
// why these are keyed by bringer and card rather than collapsed by card.
// If both players bring the same Basic Action there are two cards in the
// centre, each with its own 3 dice (rule 2.1.2's "apiece", rule 1.2.11's
// fixed count) - and, more importantly, its own Global: rule 3.4.2.4
// makes two identical cards two separate abilities, so a "once per turn"
// Global on a card both players brought can be paid for twice a turn
// (rule 2.6.5.3). Showing one merged row would hide that second use.

interface CommunityCard {
  key: string;
  card: CardDef;
  broughtBy: string;
  dieIds: string[];
}

export function CommunityCards(props: {
  dice: Die[];
  cardsById: Map<string, CardDef>;
  /** Whose copies are labelled "yours". */
  nearPlayerId: string;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
}) {
  const byCard = new Map<string, CommunityCard>();
  for (const die of props.dice) {
    if (die.zone !== "Unpurchased" || !die.cardId) continue;
    const card = props.cardsById.get(die.cardId);
    if (!isCommunityCard(card)) continue;
    // Keyed by who brought it as well as which card - see above.
    const key = `${die.ownerId}|${die.cardId}`;
    const entry = byCard.get(key) ?? { key, card: card!, broughtBy: die.ownerId, dieIds: [] };
    entry.dieIds.push(die.id);
    byCard.set(key, entry);
  }
  const cards = [...byCard.values()].sort(
    (a, b) => a.card.name.localeCompare(b.card.name) || a.broughtBy.localeCompare(b.broughtBy),
  );
  const duplicated = new Set(
    cards.filter((c, _, all) => all.filter((o) => o.card.id === c.card.id).length > 1).map((c) => c.card.id),
  );

  return (
    <section className="community-cards" aria-label="Basic Action cards">
      <h3>
        Basic Actions
        <span className="community-sub">shared · either player may buy</span>
      </h3>
      {cards.length === 0 ? (
        <p className="empty-hint">No Basic Action cards in this game.</p>
      ) : (
        <ul className="community-list">
          {cards.map(({ key, card, broughtBy, dieIds }) => {
            const selected = dieIds.some(
              (id) => id === props.selection.primary || props.selection.secondary.includes(id),
            );
            return (
              <li key={key}>
                <button
                  className={`community-card${selected ? " selected" : ""}`}
                  onClick={() => props.onGroupClick(dieIds)}
                  disabled={dieIds.length === 0}
                  title={`${card.name} - costs ${card.purchaseCost}`}
                >
                  <span className="community-head">
                    <span className="community-cost">{card.purchaseCost}</span>
                    <DieIcon kind="Action" size={15} />
                    {/* Two Basic Actions can share a name across
                        printings (both Cosmic Cubes, say), so the
                        subtitle is what tells them apart. */}
                    <span className="community-name">
                      {card.name}
                      {card.subtitle && <span className="hint"> — {card.subtitle}</span>}
                    </span>
                    {/* Only worth saying whose copy this is when both
                        players brought the same card - otherwise it is
                        noise, since either of them can buy from it. */}
                    {duplicated.has(card.id) && (
                      <span className="community-owner">
                        {broughtBy === props.nearPlayerId ? "yours" : "theirs"}
                      </span>
                    )}
                    <span className="community-left">{dieIds.length} left</span>
                  </span>
                  <span className="community-text">
                    <CardText text={card.rawText} />
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
