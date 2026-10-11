import { api as classicApi } from "./api";
import { api as kingdomApi } from "./dicekingdom/api";
import type { CardDef as KingdomCardDef } from "./dicekingdom/types";
import type { CardDef } from "./types";

/** Internal catalog switch. Set to false to restore the original Dice Masters catalog. */
export const USE_DICE_KINGDOM_CATALOG: boolean = true;

/**
 * The builder retains its existing search, sort, team, and print workflows.
 * The V2 catalog uses a different DTO, so adapt it in one place rather than
 * create another team builder or maintain a duplicate list of cards.
 *
 * V2's /cards includes *all* registered Dice Kingdom cards, including those
 * not assigned to any premade Champion team.
 */
export function adaptKingdomCard(card: KingdomCardDef): CardDef {
  return {
    id: card.id,
    name: card.name,
    subtitle: card.subtitle,
    type: card.isAction ? "BasicAction" : "Character",
    purchaseCost: card.purchaseCost,
    energyTypes: card.energyTypes,
    affiliations: [],
    alignment: null,
    rarity: null,
    oldTeamBuilderCode: null,
    affiliationIcons: [],
    dieLimit: card.dieLimit,
    levels: card.levels.map((level) => ({ ...level, burstStars: null })),
    rawText: card.rawText || card.actionText || "",
    keywords: card.keywords,
    abilityTriggers: [],
    globalAbilityCost: card.global
      ? { amount: card.global.cost, requiredType: card.global.energyType }
      : null,
    globalAbilityNeedsTarget: false,
    isImplemented: true,
    whenFieldedNeedsTarget: false,
    set: "DK",
  };
}

export async function loadTeamBuilderCards(): Promise<CardDef[]> {
  if (!USE_DICE_KINGDOM_CATALOG) return classicApi.getCards();
  return (await kingdomApi.getCards()).map(adaptKingdomCard);
}
