import { CardText } from "./CardText";
import { DieIcon } from "./DieIcon";
import { isCommunityCard } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { CardDef, Die } from "./types";

// The Basic Action cards in the centre of the table.
//
// Rule 2.1.2 - these are COMMUNITY PROPERTY: they belong to neither
// roster, and either player may buy from them (which TurnEngine.Purchase
// has always allowed). They used to be drawn inside whichever player's
// Unpurchased roster happened to have brought them, which read as if they
// were that player's to buy. Here they sit on their own, between the two
// mats, with the dice left on each card.
//
// Rule 1.2.11 gives every Basic Action card exactly 3 dice, so "N left"
// counts down from 3 and is shared: a die your opponent buys is one you
// cannot.

interface CommunityCard {
  card: CardDef;
  dieIds: string[];
}

export function CommunityCards(props: {
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
}) {
  const byCard = new Map<string, CommunityCard>();
  for (const die of props.dice) {
    if (die.zone !== "Unpurchased" || !die.cardId) continue;
    const card = props.cardsById.get(die.cardId);
    if (!isCommunityCard(card)) continue;
    const entry = byCard.get(die.cardId) ?? { card: card!, dieIds: [] };
    entry.dieIds.push(die.id);
    byCard.set(die.cardId, entry);
  }
  const cards = [...byCard.values()].sort((a, b) => a.card.name.localeCompare(b.card.name));

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
          {cards.map(({ card, dieIds }) => {
            const selected = dieIds.some(
              (id) => id === props.selection.primary || props.selection.secondary.includes(id),
            );
            return (
              <li key={card.id}>
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
