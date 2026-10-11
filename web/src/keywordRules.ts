// Display reference only. Based on RULES_ENGINE_DESIGN.md's keyword index,
// KeywordAbilities.cs, and the catalog's printed reminder text.
// Keep effects that belong to a particular card in that card's ability text.
export const KEYWORD_RULES: Record<string, string> = {
  "Back for More": "Use this die's listed Back for More ability when you roll it from the Prep Area. The card states the effect and any cost.",
  Boomerang: "After using this action die, roll it. If it lands on an action face, add it to your Prep Area.",
  Cleave: "If this attacking character KOs a blocker, deal half its attack damage, rounded down, to another target opposing character die.",
  "Common Ground": "Use the listed ability when this die attacks together with the kind of character stated on its card. The card specifies its partner requirement and the bonus.",
  Crossover: "A Crossover character requires more than one energy type to purchase. Pay its printed requirements. Abilities referring to Crossover affect characters of this kind.",
  Crosspulse: "Use the listed Crosspulse ability only if you purchased this character using energy entirely of its printed energy types. The card states the purchase effect.",
  Enlistment: "The listed effect applies unless the opponent chooses to KO one of their own character dice as described on the card. Follow its timing and any target restrictions.",
  Equip: "Attach this equipment die to an eligible character die with Equip. The equipped character receives the bonuses or abilities printed on the equipment card.",
  Fabricate: "Fabricate X-Y lets you KO X of your character dice with a combined purchase cost of at least Y to purchase this die for free. Follow any additional restrictions printed on the card.",
  Flip: "At the beginning of your turn, you may flip this card to its other side. Use the ability text on its current side.",
  Frag: "When this attacker KOs one or more blockers with combat damage, deal 1 damage to each opposing character die that did not block this turn, before damage is cleared.",
  Fusion: "Before attacking, move the two different Fusion materials listed on the card from the Field to Prep to move this unpurchased Fusion die from its card to Prep.",
  Gadgeteer: "When this character attacks, you may roll an action die from your Used Pile. If it lands on an action face, you may field it.",
  Gate: "When fielded, you may roll another unpurchased die of the character named by Gate. Put a character result in Prep; otherwise return it to its card.",
  Heroic: "When fielded, this character may pair with a different Heroic character until the start of your next turn. The card describes the benefits for the pair.",
  Immortal: "When this die would go to the Used Pile, put it into your bag instead. This does not apply when the die is first purchased.",
  Impulse: "Use the card's Impulse ability when you purchase this die. The card states the effect and any optional cost.",
  "Iron Will": "This character cannot be KO'd unless it was already damaged earlier this turn. A later damaging effect may KO it once that condition is met.",
  Recruit: "When this die damages the opponent with combat damage, move an eligible die described on the card from your Used Pile, Reserve Pool, or Prep Area to the Field on its level 1 character face.",
  Resistance: "Resistance abilities depend on one of your character dice being KO'd. Follow the card's text for the trigger, timing, and effect.",
  Ritual: "You may move a monster die from your Reserve Pool to your Used Pile to pay this monster's summoning cost, as described on the card.",
  Rush: "When drawn during Clear and Draw, this die lets you draw and roll extra dice for dice matching the energy type listed by Rush. Follow the card's printed condition.",
  Spark: "Use the card's Spark ability when you roll a die with a burst (* or **) face. The card specifies the effect.",
  "Suit Up": "When purchasing this die, you may KO a character die of the name listed by Suit Up to field the purchased die at level 2.",
  Synergy: "While this character is active, you may pay the stated cost and use its Synergy ability any time you could use a Global ability. Follow the card's effect and timing restrictions.",
  Teamwork: "Teamwork adds or repeats the listed effect when its affiliation requirement is met. The card describes the required affiliated characters and any additional targets.",
  Trap: "When used, place this action die in the Field. When its printed trigger happens, resolve the effect and send the die to the Used Pile.",
  "Turtle Power": "While this character is active, other Teenage Mutant Ninja Turtle character dice cost 1 less to purchase, to a minimum of 1.",
  Underdog: "Use the listed Underdog effect while your opponent has more character dice in their Field Zone than you do. Follow the card's timing and effect.",
  Vengeance: "Use the card's Vengeance ability when you take damage. The card describes the resulting effect and any required KO or other cost.",
  Aftershock: "Use the card's Aftershock ability when an opponent causes this creature to leave the Field, including by KO, damage, or removal. The card states the effect.",
  Ally: "This character die also counts as a Sidekick while in the Field or Attack Zone. It keeps its other attributes.",
  Amplify: "While this character is active, each time you use an action die, spin its dice up one level if possible. Using a Global does not count as using an action die.",
  Attune: "While active, each time you use an action die, this creature deals 1 damage to the opponent or a target character die. Attune N deals N damage instead. A Global does not trigger Attune.",
  Awaken: "Use the card's Awaken ability each time this die spins up one or more levels. Each die triggers separately. Rolling a higher level is not spinning up.",
  "Breath Weapon": "When this creature attacks, you may pay N energy to deal N damage to the opponent and every opposing creature. Without a number, N is 1. Use it once per turn for each character, regardless of how many of its dice attack; it does not target.",
  "Call Out": "When this die attacks, choose a target opposing character die. Only that die may block this attacker, but it is not required to block.",
  Continuous: "Use this action die by moving it from the Reserve Pool to the Field Zone. It stays there until its rules let you remove it to resolve its effect. Resolving it later does not count as using another action die.",
  Corrupt: "Corrupt N makes the target player draw N dice from their bag, choose one to put in the Used Pile, and return the remaining dice to the bag. Follow the card's text for when to use it.",
  Deadly: "At Clean Up, character dice that were engaged with a Deadly die are KO'd if still in the Field. This applies even if the Deadly die has already left play.",
  Energize: "Use the card's Energize ability when this die rolls a double-energy face. During Roll and Reroll, check only the final result at the end of the step. The creature does not need to be active. Later qualifying rerolls can trigger it again.",
  "Energy Drain": "After blockers are assigned, each opposing creature engaged with this die spins down N levels, to a minimum of level 1. Without a number, N is 1. Multiple Energy Drain values use the highest value, rather than adding together.",
  Experience: "At Clean Up, if you KO'd an opposing Monster this turn, each of your active Experience cards gains an Experience token. Each token permanently gives that card's character dice +1 attack and +1 defense.",
  Fast: "This die deals combat damage before dice without Fast. A die KO'd by that early damage does not deal its normal combat damage back. Fast dice fighting each other deal their damage simultaneously.",
  Founder: "Founder identifies this character for abilities that refer to Founder dice. It does not add an effect by itself; follow the ability text on the cards that refer to it.",
  Global: "A Global ability is available to either player, even if they do not own the card or have one of its dice active. Pay its stated cost in an allowed Global window, then resolve its text.",
  Infiltrate: "When this attacking die is unblocked, you may return it to the Field Zone and deal 1 damage to the opponent instead of its normal unblocked combat damage.",
  Intimidate: "When fielded, remove a target opposing creature from the Field until the end of the turn. While removed, it is not active, cannot block, and cannot be targeted. It returns on the same face at Clean Up.",
  Loyalty: "A Loyalty counter belongs to a card and gives all of its applicable character dice +1 attack and +1 defense per counter. Counters persist across turns. Follow the card's text for how it gains counters; it need not be active to receive one.",
  Obscure: "While this creature is active, each time you use an action die, it cannot be blocked for the rest of the turn. Using a Global does not trigger Obscure. It does not undo blocks already assigned.",
  "On Attack": "Use this die's listed ability when it is declared as an attacker. Resolve it before proceeding through the attack sequence.",
  "On Block": "Use this die's listed ability when it is declared as a blocker, before combat damage is dealt.",
  "On Field": "Use this die's listed ability when you field it. Each die fielded triggers separately. Returning a die from combat is not fielding it.",
  Overcrush: "If this attacker KOs or removes all of its blockers, excess attack damage is dealt to the opponent. In Dice Kingdom, a lane with two or more attackers also gets this effect. Blockers that survive prevent Overcrush damage.",
  Range: "When one or more creatures with Range attack, every active Range creature on both sides deals N damage to a target opposing creature, simultaneously. Without a number, N is 1. Multiple Range values on a die use the highest value.",
  Regenerate: "If this character die would be KO'd, roll it instead. A character face returns it to the Field Zone on that face without being KO'd. An energy face means it is KO'd and goes to Prep.",
  Retaliation: "While this character is active, when one of your character dice sharing an affiliation with it is KO'd, use its Retaliation ability. The card states how much damage or which effect to resolve.",
  Sacrifice: "Move the chosen creature from the Field to Out of Play on its owner's turn, or to the Used Pile otherwise. This is not a KO and does not trigger KO abilities. Resolve the effect that required the sacrifice.",
  Strike: "This character gets +2 attack, +2 defense, and Overcrush while it is the only character die you have fielded this turn. Fielding another character die removes that bonus.",
  Swarm: "While a character with Swarm is active, drawing another die of that character during Clear and Draw lets you draw an extra die. Multiple active copies do not multiply the bonus. Extra Swarm draws do not trigger further Swarm draws.",
  "Tag Out": "After blockers are declared, you may move this die from the Field to Prep to give a target creature in the Field +2 attack and +2 defense until end of turn. Moving it to Prep this way is not a KO.",
  Teamwatch: "While this character is active, fielding a different character die that shares an affiliation with it triggers the listed Teamwatch ability. Follow the card's text for the effect.",
};

const ALIASES: Record<string, string> = {
  continous: "Continuous", // A spelling used by a printed catalog entry.
  "when fielded": "On Field",
  "when attacking": "On Attack",
  "when blocking": "On Block",
};
const names = [...Object.keys(KEYWORD_RULES), ...Object.keys(ALIASES)].sort((a, b) => b.length - a.length);
const canonical = new Map(Object.keys(KEYWORD_RULES).map(name => [name.toLowerCase(), name]));
const escapeRegex = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const pattern = new RegExp(`\\b(${names.map(escapeRegex).join("|")})(?:[ \\t]+(\\d+(?:[-–]\\d+)?|X(?:[-–]Y)?))?\\b`, "gi");

export interface KeywordPart { text: string; keyword?: string; explanation?: string }

/** Preserve the original wording and punctuation, matching whole keywords only. */
export function splitKeywordText(text: string): KeywordPart[] {
  const parts: KeywordPart[] = [];
  let last = 0;
  for (const match of text.matchAll(pattern)) {
    if (match.index > last) parts.push({ text: text.slice(last, match.index) });
    const keyword = canonical.get(match[1].toLowerCase()) ?? ALIASES[match[1].toLowerCase()];
    parts.push({ text: match[0], keyword, explanation: KEYWORD_RULES[keyword] });
    last = match.index + match[0].length;
  }
  if (last < text.length) parts.push({ text: text.slice(last) });
  return parts;
}
