using DiceFight.Engine.Effects;

namespace DiceFight.Engine.Model;

// Rule 1.3.5/1.3.6 - one die face: fielding cost, attack, defense at a given
// level, plus which burst symbol (if any) that level's face carries.
public sealed record CharacterFace(int FieldingCost, int Attack, int Defense, int? BurstStars = null);

// Static card data - the printed card. Never mutates during a game.
// RawText is kept verbatim (rule 1.2.8/1.2.9) for display and as the
// source of truth while Abilities are authored incrementally; a card with
// an empty Abilities list is legal and simply has no engine-executable
// text yet (see RULES_ENGINE_DESIGN.md's data-pipeline section).
public sealed record CardDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Subtitle { get; init; }
    public required CardType Type { get; init; }
    public required int PurchaseCost { get; init; }
    public IReadOnlyList<EnergyType> EnergyTypes { get; init; } = [];
    public IReadOnlyList<string> Affiliations { get; init; } = [];
    public Alignment? Alignment { get; init; }

    // Rule 1.2.10/1.2.11 - Max # on the card; Basic Actions always use 3.
    public required int DieLimit { get; init; }

    // Rule 1.3.5 - Character dice only. Empty for Action/Basic Action cards.
    public IReadOnlyList<CharacterFace> Levels { get; init; } = [];

    public string RawText { get; init; } = string.Empty;
    public IReadOnlyList<KeywordInstance> Keywords { get; init; } = [];
    public IReadOnlyList<AbilityDef> Abilities { get; init; } = [];

    // Bespoke text like Darkseid's "While Darkseid is active, your
    // Sidekicks gain Swarm" - a static, continuously-recomputed grant
    // (not a discrete triggered ability), scoped to "your Sidekicks,"
    // which per DieStats.CountsAsSidekick already includes any active
    // Ally-keyword die too. See DieStats.HasKeyword for how this is
    // actually applied live; empty for every card that doesn't grant
    // anything.
    public IReadOnlyList<string> GrantsToSidekicks { get; init; } = [];

    // Rule 3.4.5.7 - "an attack and/or defense value modifier provided by
    // a Character die with a 'while active' ability is a Static ability."
    // Captain Marvel's "While Captain Marvel is active, your Character
    // dice get +1 attack and +1 defense" is the textbook case: a live,
    // continuously-recomputed team-wide bonus, not a discrete modifier
    // object like AppliedModifiers (see DieStats.StaticTeamBonusFor and
    // its EffectiveAttack/EffectiveDefense callers). Null for every card
    // that doesn't grant one. Deliberately narrow in scope to "your
    // Character dice get +A/+D while I'm active" - not a general static-
    // ability framework (no debuffs, no affiliation-scoped or "while
    // attacking/blocking"-only variants yet - see rule 3.4.5.6).
    public StaticTeamBonus? GrantsStaticTeamBonus { get; init; }
}

// See CardDef.GrantsStaticTeamBonus's remarks.
public sealed record StaticTeamBonus(int AttackDelta, int DefenseDelta);

// Rule Appendix 1 - keyword abilities are a finite, engine-known set
// implemented as plugins (see design doc); Params covers e.g. Range X,
// Fabricate X-Y, Breath Weapon X.
public sealed record KeywordInstance(string Name, IReadOnlyList<int>? Params = null);
