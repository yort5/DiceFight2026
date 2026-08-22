namespace DiceFight.V2.Model;

// Turn/team constants, as data (Appendix B). The current game is just one
// RulesConfig - a Direction-C variant (draw 6 instead of 4, etc.) is a
// different value here, never an engine-code change. FieldZoneCap is
// nullable (unlimited by default); extend this record only with user
// sign-off (V2_PLAN.md ground rule 2 - RulesConfig is part of the closed,
// frozen v2 vocabulary just like the effect templates are).
public sealed record RulesConfig(
    int StartingLife,
    int DrawCount,
    int MaxTeamCards,
    int MaxTeamDice,
    int BasicActionCount,
    int? FieldZoneCap = null);
