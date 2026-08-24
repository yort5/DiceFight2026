namespace DiceFight.V2.Model;

// V2_PLAN.md Phase 1 task 3. Returns a flat list of error strings rather
// than throwing - a config can be invalid in more than one way at once,
// and the caller (a future card-editor UI, or a config-authoring test)
// wants all of them, not just the first.
public static class GameConfigValidation
{
    public static IReadOnlyList<string> Validate(this GameConfig config)
    {
        var errors = new List<string>();
        var declaredSymbols = new HashSet<string>(config.EnergySymbols.Select(s => s.Id));

        foreach (var entry in config.BasicDicePool)
        {
            ValidateDie(entry.Die, declaredSymbols, errors);
        }

        // Finding 4: energy symbols and keywords now share the tag
        // namespace (a die's tag set includes both its keywords and its
        // printed energy symbol id) - a collision would make a TargetFilter
        // tag query ambiguous about which one it meant.
        var keywordIds = new HashSet<string>(config.Keywords.Select(k => k.Id));
        foreach (var symbolId in declaredSymbols)
        {
            if (keywordIds.Contains(symbolId))
                errors.Add($"Energy symbol id \"{symbolId}\" collides with a declared keyword id - both would be tags on the same die.");
        }

        return errors;
    }

    // A second entry point, separate from Validate(), because a card
    // catalog is not part of GameConfig itself (Appendix B) - it's loaded
    // alongside a config, so this can only run once both are in hand.
    // Extends the same "symbols/keywords share the tag namespace" check
    // (Finding 4) to each card's own affiliations, and checks each card's
    // own die the same way Validate() checks the basic dice pool.
    public static IReadOnlyList<string> ValidateCatalog(this GameConfig config, IReadOnlyList<CardDef> cards)
    {
        var errors = new List<string>();
        var declaredSymbols = new HashSet<string>(config.EnergySymbols.Select(s => s.Id));
        var keywordIds = new HashSet<string>(config.Keywords.Select(k => k.Id));
        var symbolOrKeywordIds = new HashSet<string>(declaredSymbols);
        symbolOrKeywordIds.UnionWith(keywordIds);

        // A die's tag set is its affiliations + keywords + CARD NAME +
        // "sidekick" + energy symbol ids (Part 1's tag-unification note).
        // Every one of those shares a namespace, so a collision anywhere
        // in it makes a TagQuery silently ambiguous - a filter for the
        // affiliation "X" would also match a card merely NAMED "X".
        // Card names were previously unchecked; they are the likeliest
        // collision in practice, since names are the one part of the tag
        // set nobody chooses with the namespace in mind.
        var cardNames = new HashSet<string>(cards.Select(c => c.Name));

        foreach (var card in cards)
        {
            ValidateDie(card.Die, declaredSymbols, errors, card.Id);

            foreach (var symbolId in card.EnergySymbolIds)
            {
                if (!declaredSymbols.Contains(symbolId))
                    errors.Add($"Card \"{card.Id}\": energy symbol \"{symbolId}\" is not declared in GameConfig.EnergySymbols.");
            }

            foreach (var affiliation in card.Affiliations)
            {
                if (symbolOrKeywordIds.Contains(affiliation))
                    errors.Add($"Card \"{card.Id}\": affiliation \"{affiliation}\" collides with a declared energy symbol or keyword id - both would be tags on the same die.");
                if (cardNames.Contains(affiliation))
                    errors.Add($"Card \"{card.Id}\": affiliation \"{affiliation}\" collides with a card name - both would be tags on the same die.");
            }

            if (symbolOrKeywordIds.Contains(card.Name))
                errors.Add($"Card \"{card.Id}\": card name \"{card.Name}\" collides with a declared energy symbol or keyword id - both would be tags on the same die.");
        }

        return errors;
    }

    private static void ValidateDie(DieDefinition die, HashSet<string> declaredSymbols, List<string> errors, string? context = null)
    {
        var label = context is null ? $"Die \"{die.Id}\"" : $"Card \"{context}\"'s die \"{die.Id}\"";

        if (die.Faces.Count == 0)
        {
            errors.Add($"{label} has no faces.");
            return;
        }

        foreach (var face in die.Faces)
        {
            foreach (var symbol in face.Symbols)
            {
                if (!declaredSymbols.Contains(symbol.SymbolId))
                    errors.Add($"{label} has a face referencing undeclared energy symbol \"{symbol.SymbolId}\".");
            }
        }
    }
}
