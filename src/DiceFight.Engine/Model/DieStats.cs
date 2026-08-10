using DiceFight.Engine;
using DiceFight.Engine.Effects;

namespace DiceFight.Engine.Model;

// Rule 1.3 key - the physical Sidekick die face: fielding cost 0, 1A/1D.
// Sidekicks have no card (rule 1.3.9), so this isn't card-driven data.
public static class DieStats
{
    // The single lookup point for "which CardDef currently backs this
    // die's own ABILITY text" - Mister Sinister ("Mutant Supremacist",
    // DPS083)/Vulcan ("Power Suppression", DPS095) both "ignore text,"
    // enforced here by returning null (as if the die had no card at all)
    // whenever GameState.BlankedDieIds/BlankedControllerIds says so.
    // Every keyword/triggered-ability/static-grant lookup that asks "what
    // does THIS die's own card do" goes through this now, rather than
    // reimplementing its own VirtualCardId-or-CardId lookup. Deliberately
    // NOT used for GetFace/GetMaxLevel/EffectiveAttack's face lookup, the
    // physical roll-face composition (PlaceholderDiceRoller), affiliation
    // (DieStats.HasAffiliation), or energy type - "ignore text" reads as
    // the rules-text box specifically (keywords, triggered/static
    // abilities), not fixed printed attributes a blanked card still
    // physically has. Also not used when a DIFFERENT die's card is being
    // consulted just for board-presence/identity (e.g. "is a die named X
    // currently active" for someone ELSE's condition) - only for asking
    // whether THIS card's own text grants something.
    internal static CardDef? GetCard(GameState state, DieInstance die)
    {
        if (state.BlankedDieIds.Contains(die.Id) || state.BlankedControllerIds.Contains(die.ControllerId))
            return null;
        var cardId = die.VirtualCardId ?? die.CardId;
        return cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
    }

    // Whether the die currently has the named keyword - either printed on
    // its own card (Overcrush, Regenerate, etc. - see CardDef.Keywords/
    // KeywordInstance), or granted live by some other active die's
    // "while active, your Sidekicks gain [keyword]" text (e.g. Darkseid:
    // "your Sidekicks gain Swarm" - see CardDef.GrantsToSidekicks). The
    // grant is re-evaluated every call, not cached, since it depends on
    // the current board (the granting die must still be active) exactly
    // like any other "while active" effect. Guarded against `keyword ==
    // "Ally"` to avoid a cycle: CountsAsSidekick is how an Ally die gets
    // into the grant-eligible set in the first place, so asking "is Ally
    // granted" would recurse into itself; granting Ally via "your
    // Sidekicks gain Ally" isn't a real card pattern anyway.
    public static bool HasKeyword(GameState state, DieInstance die, string keyword)
    {
        if (HasPrintedKeyword(state, die, keyword)) return true;
        if (keyword == "Overcrush" && HasStrikeBonus(state, die)) return true;
        if (HasConditionalSelfGrant(state, die, keyword)) return true;
        if (die.AppliedKeywords.Contains(keyword)) return true;

        if (keyword == "Ally" || !CountsAsSidekick(state, die)) return false;

        var granters = state.DiceIn(die.ControllerId, Zone.FieldZone)
            .Concat(state.DiceIn(die.ControllerId, Zone.AttackZone));
        return granters.Any(granter => GetCard(state, granter)?.GrantsToSidekicks.Contains(keyword) ?? false);
    }

    private static bool HasPrintedKeyword(GameState state, DieInstance die, string keyword) =>
        GetCard(state, die)?.Keywords.Any(k => k.Name == keyword) ?? false;

    // See CardDef.GrantsSelfKeywordWhileNamedCardActive's remarks -
    // Psylocke's own "gains Deadly while Wolverine is active." "Active"
    // means the named card anywhere on the board, either player's (the
    // text doesn't say "your"), not just die's own controller's side.
    private static bool HasConditionalSelfGrant(GameState state, DieInstance die, string keyword)
    {
        var grant = GetCard(state, die)?.GrantsSelfKeywordWhileNamedCardActive;
        if (grant is null || grant.Keyword != keyword) return false;

        // "Active" here just means board presence/identity for a
        // DIFFERENT die - not this die's own text, so it's the raw
        // lookup, not GetCard (a blanked die's existence still counts
        // for someone ELSE's "while [Name] is active" condition).
        return state.Dice.Any(d =>
            d.Zone is Zone.FieldZone or Zone.AttackZone &&
            (d.VirtualCardId ?? d.CardId) is { } otherCardId &&
            state.CardCatalog.TryGetValue(otherCardId, out var otherCard) &&
            otherCard.Name == grant.WhileCardNamed);
    }

    // See CardDef.GrantsSelfStatBonusWhileNamedCardActive's remarks - the
    // stat-bonus counterpart to HasConditionalSelfGrant just above, same
    // "named card active anywhere on the board, either player's" check.
    private static ConditionalSelfStatBonus? SelfStatBonusWhileNamedCardActive(GameState state, DieInstance die)
    {
        var grant = GetCard(state, die)?.GrantsSelfStatBonusWhileNamedCardActive;
        if (grant is null) return null;

        var active = state.Dice.Any(d =>
            d.Zone is Zone.FieldZone or Zone.AttackZone &&
            (d.VirtualCardId ?? d.CardId) is { } otherCardId &&
            state.CardCatalog.TryGetValue(otherCardId, out var otherCard) &&
            otherCard.Name == grant.WhileCardNamed);
        return active ? grant : null;
    }

    // See CardDef.CannotBeTargetedByOpponentWhileNamedCardActive's
    // remarks - checked by LegalTargets.Query against whoever is doing
    // the targeting (requestingControllerId), not just the die's own
    // controller, since this protection only blocks the OPPONENT.
    public static bool IsProtectedFromOpponentTargeting(GameState state, DieInstance die, string requestingControllerId)
    {
        if (die.ControllerId == requestingControllerId) return false;

        var namedCard = GetCard(state, die)?.CannotBeTargetedByOpponentWhileNamedCardActive;
        if (namedCard is null) return false;

        return state.Dice.Any(d =>
            d.Zone is Zone.FieldZone or Zone.AttackZone &&
            (d.VirtualCardId ?? d.CardId) is { } otherCardId &&
            state.CardCatalog.TryGetValue(otherCardId, out var otherCard) &&
            otherCard.Name == namedCard);
    }

    // See CardDef.GrantsSelfAttackBonusPerMatchingDie's remarks -
    // LegalTargets.Query does the actual counting, reusing whichever
    // TargetSpec dimensions (ownership, zone, RequiredAffiliations,
    // MaxDefense, ...) the card's own CountFilter needs; Count/
    // Description/Optional on that spec are irrelevant here since
    // nothing is ever "chosen" from the result, just counted.
    private static int SelfAttackBonusPerMatchingDie(GameState state, DieInstance die)
    {
        var grant = GetCard(state, die)?.GrantsSelfAttackBonusPerMatchingDie;
        if (grant is null) return 0;

        var matchCount = LegalTargets.Query(state, die.ControllerId, grant.CountFilter).Count;
        return matchCount * grant.AttackPerMatch;
    }

    // See CardDef.GrantsOpponentStatDebuff's remarks - scans `die`'s
    // OWN OPPONENT's active dice for a granter (the cross-player mirror
    // of StaticTeamBonusFor's same-controller scan), accumulating across
    // multiple granters the same way that method does.
    private static OpponentStatDebuff TotalOpponentStatDebuff(GameState state, DieInstance die)
    {
        if (die.Zone is not (Zone.FieldZone or Zone.AttackZone)) return new OpponentStatDebuff(0, 0);

        // Energy type is a fixed printed attribute, not "text" - the raw
        // lookup, not GetCard (see GetCard's own remarks on the line
        // between the two).
        var dieCardId = die.VirtualCardId ?? die.CardId;
        var dieCard = dieCardId is not null ? state.CardCatalog.GetValueOrDefault(dieCardId) : null;

        var granterCards = state.DiceIn(state.OpponentOf(die.ControllerId), Zone.FieldZone)
            .Concat(state.DiceIn(state.OpponentOf(die.ControllerId), Zone.AttackZone))
            .Select(d => GetCard(state, d))
            .Where(card => card is not null)
            .Distinct();

        var attack = 0;
        var defense = 0;
        foreach (var card in granterCards)
        {
            if (card!.GrantsOpponentStatDebuff is not { } debuff) continue;
            if (debuff.ExcludedEnergyType is { } excluded && (dieCard?.EnergyTypes.Contains(excluded) ?? false)) continue;
            attack += debuff.AttackDelta;
            defense += debuff.DefenseDelta;
        }
        return new OpponentStatDebuff(attack, defense);
    }

    // Whether this die's own card carries the named affiliation (e.g.
    // "Monster" for keyword Experience's own earning condition). Printed
    // only - nothing grants an affiliation the way Darkseid grants Swarm.
    public static bool HasAffiliation(GameState state, DieInstance die, string affiliation)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        return cardId is not null
            && state.CardCatalog.TryGetValue(cardId, out var card)
            && card.Affiliations.Contains(affiliation);
    }

    // Keyword Strike - "On the turn you field a Character die with
    // Strike, at the end of the Main Step, if you fielded no other
    // Character dice this turn, this Character die gets +2A, +2D, and
    // Overcrush." The printed reminder text (Bizarro: "...so long as it
    // is the only character die you fielded this turn") phrases this as
    // a live, continuously-true condition rather than a one-time
    // snapshot, so it's recomputed on demand from GameState.
    // FieldedThisTurn (populated by TurnEngine.Field, cleared each turn
    // in CleanUp) instead of being applied once at a fixed instant - the
    // two only differ if something inspects this die's stats mid-Main-
    // Step before any other die could possibly have been fielded yet,
    // which changes nothing observable either way. "Fielded," not
    // "purchased" or "active" - a die fielded this turn that was later
    // KO'd still counts against a DIFFERENT Strike die's own check
    // (fielding is a historical fact about the turn, not current board
    // state), and Sidekicks fielded onto a character face count too,
    // same as TurnEngine.Field's own validation already treats them.
    public static bool HasStrikeBonus(GameState state, DieInstance die)
    {
        if (die.Zone is not (Zone.FieldZone or Zone.AttackZone)) return false;
        if (!HasPrintedKeyword(state, die, "Strike")) return false;
        if (!state.FieldedThisTurn.Contains(die.Id)) return false;

        return state.Dice.Count(d => state.FieldedThisTurn.Contains(d.Id) && d.ControllerId == die.ControllerId) == 1;
    }

    // Keyword Energy Drain X - "spin each Character die engaged with a
    // Character die with Energy Drain down [X] level(s)." Returns 0 if
    // this die doesn't have the keyword at all (checked via the printed
    // card only - no card grants Energy Drain the way Darkseid grants
    // Swarm yet, so there's no numeric amount to look up on a grant),
    // otherwise the X in "Energy Drain X" (KeywordInstance.Params[0]),
    // defaulting to 1 for the bare "Energy Drain" wording.
    public static int EnergyDrainAmount(GameState state, DieInstance die)
    {
        var keyword = GetCard(state, die)?.Keywords.FirstOrDefault(k => k.Name == "Energy Drain");
        if (keyword is null) return 0;
        return keyword.Params is { Count: > 0 } ? keyword.Params[0] : 1;
    }

    // Keyword Range X - the X in "Range X" (KeywordInstance.Params[0]).
    // Every printed instance of this keyword names an explicit X (unlike
    // Energy Drain's bare wording), but default to 1 anyway for the same
    // "never throw over missing data" reason EnergyDrainAmount does.
    public static int RangeAmount(GameState state, DieInstance die)
    {
        var keyword = GetCard(state, die)?.Keywords.FirstOrDefault(k => k.Name == "Range");
        if (keyword is null) return 0;
        return keyword.Params is { Count: > 0 } ? keyword.Params[0] : 1;
    }

    // Keyword Ally - Appendix 1: "Character dice with the Ally keyword
    // ability are considered Sidekick Character dice while in the Field
    // Zone... in addition to their other attributes. They don't count as
    // Sidekick dice while in the bag, Prep Area, Used Pile, or Reserve
    // Pool." This is the zone-gated superset of DieInstance.IsSidekick
    // (which only knows about real, cardless physical Sidekicks) - use
    // this one for any "is this die a legal Sidekick target/effect
    // subject right now" question; use the raw property only when the
    // zone-independent physical fact is what's actually being asked (e.g.
    // "how many physical Sidekick dice does this player own").
    // AttackZone counts too, since it's a subset of the Field Zone
    // (rule 1.5.6.1), not a separate play area.
    public static bool CountsAsSidekick(GameState state, DieInstance die) =>
        die.IsSidekick || (die.Zone is Zone.FieldZone or Zone.AttackZone && HasKeyword(state, die, "Ally"));

    // Rule 3.7.4/3.7.5 - "spin" a Character die's level, clamped to its
    // card's real level range ("if able" - spinning past either end is
    // just absorbed, not an error). Returns how many levels it actually
    // moved (0 if already at the clamped end, or if the die isn't
    // currently on a character face at all - Level isn't meaningful
    // otherwise), so callers that care whether a spin *really* moved the
    // die up (Awaken) can tell a no-op from a real spin without
    // re-deriving the before/after levels themselves.
    public static int SpinLevel(GameState state, DieInstance die, int delta)
    {
        if (die.Status is not (DieStatus.Character or DieStatus.SidekickCharacter)) return 0;
        var maxLevel = GetMaxLevel(state, die);
        var oldLevel = die.Level;
        die.Level = Math.Clamp(die.Level + delta, 1, maxLevel);
        return die.Level - oldLevel;
    }

    // Same VirtualCardId-vs-CardId fix as GetFace just above, for the
    // same reason.
    private static int GetMaxLevel(GameState state, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        return cardId is null ? 1 : Math.Max(1, state.CardCatalog[cardId].Levels.Count);
    }

    public static readonly CharacterFace SidekickFace = new(FieldingCost: 0, Attack: 1, Defense: 1);

    // Rule 1.6.8 - a rolled Sidekick Character die is always level 1.
    // Real bug found authoring Master Mold's own Sentinel token (the
    // first die anywhere with CardId null but a real VirtualCardId -
    // Copying, rule 3.10, was "left as a stub" per VirtualCardId's own
    // remarks and evidently never actually exercised): this used to
    // check `die.CardId is null` alone, so a token die (CardId null by
    // design - see PlaceToken) always fell through to the bare
    // SidekickFace (1A/1D) instead of ever consulting its VirtualCardId,
    // silently discarding its real printed stats.
    public static CharacterFace GetFace(GameState state, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        if (cardId is null)
            return SidekickFace;

        var card = state.CardCatalog[cardId];
        var index = Math.Clamp(die.Level - 1, 0, Math.Max(0, card.Levels.Count - 1));
        return card.Levels.Count > 0 ? card.Levels[index] : SidekickFace;
    }

    // Rule 3.4.5.7/3.4.5.2/3.4.5.3 - "While Captain Marvel is active, your
    // Character dice get +1 attack and +1 defense" is the textbook Static
    // team-wide stat ability: it applies to every one of the controller's
    // active Character dice (including the granting die itself - the
    // text says "your Character dice," no "other" qualifier), only while
    // at least one die of that specific card is active, and only once per
    // unique granting card even if multiple copies of it are active
    // (clarification 3.4.5.3 - "does not stack"). Computed fresh on every
    // call rather than stored, matching rule 3.6.9 - a Static modifier
    // "always applies" to the stat it names and "cannot be manipulated,"
    // unlike an AppliedModifiers entry.
    public static StaticTeamBonus StaticTeamBonusFor(GameState state, DieInstance die)
    {
        if (die.Zone is not (Zone.FieldZone or Zone.AttackZone)) return new StaticTeamBonus(0, 0);

        var granterCards = state.DiceIn(die.ControllerId, Zone.FieldZone)
            .Concat(state.DiceIn(die.ControllerId, Zone.AttackZone))
            .Select(d => GetCard(state, d))
            .Where(card => card is not null)
            .Distinct();

        var attack = 0;
        var defense = 0;
        foreach (var card in granterCards)
        {
            if (card!.GrantsStaticTeamBonus is not { } bonus) continue;
            if (bonus.RequiredAffiliation is { } affiliation && !HasAffiliation(state, die, affiliation)) continue;
            attack += bonus.AttackDelta;
            defense += bonus.DefenseDelta;
        }
        return new StaticTeamBonus(attack, defense);
    }

    // Keyword Experience - "Each token grants +1A and +1D to all
    // Character dice belonging to that card." Unlike Strike/Static team
    // bonuses, tokens apply unconditionally - "permanent modifiers,"
    // not a "while active" effect - so this doesn't check zone at all,
    // matching every die of the card whether it's on the field, in the
    // Prep Area, or still sitting unpurchased. Looked up by CardId
    // directly against GameState.ExperienceTokens (see TurnEngine.
    // CleanUp for how tokens are actually granted).
    public static int ExperienceBonus(GameState state, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        return cardId is not null ? state.ExperienceTokens.GetValueOrDefault(cardId) : 0;
    }

    // Keyword Loyalty - "give their applicable dice +1A and +1D
    // modifiers for each counter. A Character die from that card does
    // not need to be active to get a Loyalty Counter" (Appendix 1) -
    // same unconditional, zone-independent shape as ExperienceBonus
    // above, just a separate GameState dictionary (see its own remarks
    // for why they're not merged). Looked up by CardId against
    // GameState.LoyaltyCounters (see TurnEngine.CleanUp/EffectNode.
    // GrantLoyaltyCounter for how counters are actually granted).
    public static int LoyaltyBonus(GameState state, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        return cardId is not null ? state.LoyaltyCounters.GetValueOrDefault(cardId) : 0;
    }

    // Rule 3.6.1/3.6.4 - combine all Applied/Static modifiers, clamp at zero.
    public static int EffectiveAttack(GameState state, DieInstance die)
    {
        var face = GetFace(state, die);
        var total = face.Attack + die.AppliedModifiers.Sum(m => m.AttackDelta);
        if (HasStrikeBonus(state, die)) total += 2;
        total += StaticTeamBonusFor(state, die).AttackDelta;
        total += ExperienceBonus(state, die);
        total += LoyaltyBonus(state, die);
        total += SelfStatBonusWhileNamedCardActive(state, die)?.AttackDelta ?? 0;
        total += SelfAttackBonusPerMatchingDie(state, die);
        total += TotalOpponentStatDebuff(state, die).AttackDelta;
        return Math.Max(0, total);
    }

    public static int EffectiveDefense(GameState state, DieInstance die)
    {
        var face = GetFace(state, die);
        var total = face.Defense + die.AppliedModifiers.Sum(m => m.DefenseDelta);
        if (HasStrikeBonus(state, die)) total += 2;
        total += StaticTeamBonusFor(state, die).DefenseDelta;
        total += ExperienceBonus(state, die);
        total += LoyaltyBonus(state, die);
        total += SelfStatBonusWhileNamedCardActive(state, die)?.DefenseDelta ?? 0;
        total += TotalOpponentStatDebuff(state, die).DefenseDelta;
        return Math.Max(0, total);
    }

    // Colossus ("Organic Steel", DPS063) - the single choke point every
    // damage-application site (combat fast/slow waves, Range, ability
    // DealDamage/DealDamagePerActiveAffiliate) now funnels through,
    // exactly like TurnEngine.ResolveKOReactions already is for "what
    // happens after a KO." Returns the die that actually ends up holding
    // the damage - not necessarily `die` itself, since a redirect target
    // can sit in a completely different zone than the original recipient
    // (Colossus in the Field Zone intercepting damage meant for a
    // blocker in the Attack Zone) - or null if the damage was fully
    // prevented. Callers that run their own KO check afterward must use
    // the RETURNED die, and fold it into whatever zone-scoped scan
    // they'd otherwise run, since a redirect target isn't guaranteed to
    // already be in that zone.
    public static DieInstance? ApplyDamage(GameState state, DieInstance die, int amount)
    {
        if (amount <= 0) return die;

        var redirector = FindDamageRedirector(state, die);
        if (redirector is null)
        {
            die.Damage += amount;
            return die;
        }

        state.UsedDamageRedirectThisTurn.Add(die.ControllerId);

        // "*Instead, prevent that damage" - the redirecting die's OWN
        // current face decides this, same "the ability's own source die,
        // not the original target" convention every other burst check in
        // this engine already follows.
        if (GetFace(state, redirector).BurstStars == 1)
            return null;

        redirector.Damage += amount;
        return redirector;
    }

    private static DieInstance? FindDamageRedirector(GameState state, DieInstance die)
    {
        if (die.Status is not (DieStatus.Character or DieStatus.SidekickCharacter)) return null;
        if (state.UsedDamageRedirectThisTurn.Contains(die.ControllerId)) return null;

        return state.DiceIn(die.ControllerId, Zone.FieldZone)
            .Concat(state.DiceIn(die.ControllerId, Zone.AttackZone))
            .FirstOrDefault(d => d.Id != die.Id && (GetCard(state, d)?.GrantsFirstDamageRedirectToSelf ?? false));
    }

    // Rule 2.7.6.1 - KO once damage reaches/exceeds defense. Shared by
    // CombatEngine (simultaneous batch check after combat damage) and
    // EffectInterpreter (ability damage KOs immediately, since abilities
    // resolve one at a time rather than in a simultaneous batch - rule
    // 3.2.2). roller is optional (null in the handful of test call sites
    // that don't care about Regenerate) - without one, Regenerate simply
    // doesn't trigger and the die is KO'd normally.
    public static bool TryResolveKO(GameState state, DieInstance die, IDiceRoller? roller = null)
    {
        if (die.Damage < EffectiveDefense(state, die)) return false;

        return ForceKO(state, die, roller);
    }

    // Unconditionally KO's a die, bypassing the defense check
    // TryResolveKO does first - shared with direct ability-driven KO
    // effects (e.g. Casket of Ancient Winters' Ko node), which don't go
    // through a damage/defense comparison at all.
    //
    // Glossary - Regenerate: "If this character would be KO'd, roll it.
    // If you roll a character face, return it to the field on the rolled
    // face (but not the Attack Zone). Otherwise, move the die to your
    // Prep Area." That's an interception, not a KO that happens and then
    // gets undone - a die that regenerates was never actually KO'd, so
    // this doesn't set Zone/Prep Area at all in that case; the return value
    // (true = really KO'd, false = intercepted by Regenerate) is what lets
    // callers that care about an actual KO (a "when KO'd" trigger firing)
    // tell the difference, rather than just having called this method.
    //
    // Note this is a narrower question than "is this die still blocking" -
    // a die that Regenerates is alive (false here), but it also explicitly
    // does not return to the Attack Zone, so it stops blocking either way.
    // CombatEngine's Overcrush check is zone-based for exactly this reason
    // and treats a Regenerated blocker as removed, even though this method
    // reports it as not-KO'd.
    public static bool ForceKO(GameState state, DieInstance die, IDiceRoller? roller = null)
    {
        if (roller is not null && HasKeyword(state, die, "Regenerate"))
        {
            var cardId = die.VirtualCardId ?? die.CardId;
            var card = cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
            var result = roller.Roll(die, card);
            if (result.Status is DieStatus.Character or DieStatus.SidekickCharacter)
            {
                die.Zone = Zone.FieldZone; // "back to the field... but not the Attack Zone"
                die.Status = result.Status;
                die.Level = result.Level;
                die.Damage = 0;
                die.EnergyKind = EnergyKind.None;
                die.ProvidedEnergyType = null;
                die.EnergyAmount = 1;
                die.AppliedModifiers.Clear(); // rule 3.4.5.4 - lifetime ends when it leaves the Field Zone (it never did here, but it's a fresh face)
                die.AppliedKeywords.Clear();
                return false;
            }
            // Rolled a non-character face - falls through to a normal KO,
            // matching "otherwise, move the die to your Prep Area."
        }

        // Keyword Experience - "if you KO'd an opposing Monster during
        // your turn" (see GameState.OpposingMonsterKOdThisTurn's own
        // remarks for why only the controller/affiliation matter here,
        // not which specific die or Monster). This is the single choke
        // point every real KO already funnels through - combat, ability
        // damage, Range, Deadly's Clean Up KO - so it's recorded once
        // here rather than at each individual call site. Only reached
        // for a die that's actually being KO'd (not a Regenerate
        // interception above, which returns before this point).
        if (die.ControllerId == state.OpponentOf(state.ActivePlayerId) && HasAffiliation(state, die, "Monster"))
            state.OpposingMonsterKOdThisTurn = true;

        // Keyword Loyalty (Jean Grey, DPS035) - "if no character dice
        // were KO'd that turn" - any character or Sidekick die, either
        // player's, same choke point as the Experience check above.
        if (die.Status is DieStatus.Character or DieStatus.SidekickCharacter)
            state.AnyCharacterKOdThisTurn = true;

        die.Zone = Zone.PrepArea; // rule 1.5.3.2
        die.ResetToUnrolled(); // also covers rule 3.4.5.4 - modifier lifetime ends when the die leaves the Field Zone
        return true;
    }
}
