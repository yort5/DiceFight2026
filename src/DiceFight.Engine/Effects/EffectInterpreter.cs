using DiceFight.Engine.Model;
using DiceFight.Engine;

namespace DiceFight.Engine.Effects;

// Executes an EffectNode tree against a GameState (see RULES_ENGINE_DESIGN.md
// - "Ability representation"). Intentionally a plain dispatch over a small,
// closed set of primitives rather than a general expression evaluator -
// that's the point of keeping the DSL small.
public static class EffectInterpreter
{
    // Rule 3.2.5 - an ability reacts to the game state as it existed when
    // it entered the queue, not a moving target as its own clauses
    // resolve. Casket of Ancient Winters is the case that makes this
    // concrete: its own first clause KOs 3 dice (which land in the Prep
    // Area, rule 1.5.3.2) before its third clause asks for "3 dice from
    // their Prep Area" - if that were resolved live, clause 1's own KOs
    // would inflate clause 3's candidate pool. So every TargetSpec in the
    // tree is resolved (legal set computed, caller's choice validated)
    // ONCE, upfront, against the pre-execution state, and cached; clauses
    // then just look up their already-resolved targets while running.
    // This also naturally covers a spec referenced twice within one
    // ability (Shocking Grasp's damage clause and its "if that character
    // is KO'd" check) - same spec, same cache entry, resolved once.
    // Cache key is TargetSpec's structural equality, so two calls building
    // an equivalent spec (e.g. two `TargetSpec.CharacterDie("target
    // character die")` invocations, which share the same default
    // EligibleZones array) hit the same entry. Caveat: two *array-literal*
    // EligibleZones for what's meant to be the same repeated target would
    // NOT share an entry (List/array equality is reference-based) - not
    // hit by anything in SampleCards today, but worth knowing.
    public static void Execute(EffectNode node, EffectContext ctx)
    {
        var cache = new Dictionary<TargetSpec, IReadOnlyList<string>>();
        foreach (var spec in CollectTargetSpecs(node).Distinct())
            Resolve(ctx, spec, cache);

        Execute(node, ctx, cache);
    }

    // Whether this effect tree has any real (non-Self) target for a
    // caller to choose - reuses the same tree walk Execute itself relies
    // on to find them, so this can never drift out of sync with what
    // Execute actually needs. Used by the API to tell the client upfront
    // whether a Global ability's "targets" stage is meaningful at all,
    // rather than always showing a "click a target, or Skip" prompt
    // regardless (e.g. Falcon's Global genuinely has none).
    public static bool NeedsTarget(EffectNode node) => CollectTargetSpecs(node).Any();

    private static IEnumerable<TargetSpec> CollectTargetSpecs(EffectNode node)
    {
        switch (node)
        {
            case Sequence seq:
                foreach (var step in seq.Steps)
                foreach (var spec in CollectTargetSpecs(step))
                    yield return spec;
                break;
            case DealDamage n: if (!n.Target.IsSelf) yield return n.Target; break;
            case DealDamagePerActiveAffiliate n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Ko n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Sacrifice n: if (!n.Target.IsSelf) yield return n.Target; break;
            case ForceBlock n: if (!n.Target.IsSelf) yield return n.Target; break;
            case ForceAttack n: if (!n.Target.IsSelf) yield return n.Target; break;
            case CantBlock n: if (!n.Target.IsSelf) yield return n.Target; break;
            case SetCallOutTarget n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Corrupt n: yield return n.PlayerTarget; break; // never Self - see TargetSpec.Player
            // RedrawFromBag's own choice is deliberately NOT collected
            // here - see its case in the mutating Execute below for why
            // (same "can't be answered upfront" reasoning as Corrupt,
            // just for a different reason).
            case MoveDie n: if (!n.Target.IsSelf) yield return n.Target; break;
            case ModifyStat n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Reroll n: if (!n.Target.IsSelf) yield return n.Target; break;
            case RerollAndMoveUnlessCharacter n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Spin n: if (!n.Target.IsSelf) yield return n.Target; break;
            case PrepDie n: if (!n.Source.IsSelf) yield return n.Source; break;
            case FieldDie n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Conditional n:
                if (!n.CheckTarget.IsSelf) yield return n.CheckTarget;
                foreach (var spec in CollectTargetSpecs(n.Then))
                    yield return spec;
                if (n.Else is not null)
                    foreach (var spec in CollectTargetSpecs(n.Else))
                        yield return spec;
                break;
        }
    }

    private static void Execute(EffectNode node, EffectContext ctx, Dictionary<TargetSpec, IReadOnlyList<string>> cache)
    {
        switch (node)
        {
            case Sequence seq:
                // Rule 3.1.7 - multiple effects in one ability resolve
                // sequentially, in the order the card text lists them.
                foreach (var step in seq.Steps)
                {
                    Execute(step, ctx, cache);
                    // A step that just raised a PendingChoice (Corrupt/
                    // RedrawFromBag) hasn't actually finished yet - later
                    // steps must wait for it to be answered, not run
                    // ahead of a choice that's still open. Not exercised
                    // by any currently-authored card (Corrupt/
                    // RedrawFromBag are always a whole ability by
                    // themselves today, never one step of a longer
                    // Sequence), but cheap to get right now.
                    if (ctx.State.PendingChoice is not null) break;
                }
                break;

            case DealDamage dealDamage:
            {
                var koIds = new List<string>();
                foreach (var id in Resolve(ctx, dealDamage.Target, cache))
                {
                    // A TargetSpec.CharacterDieOrPlayer resolution can be
                    // either kind of id (e.g. Attune's "target player or
                    // Character die") - a real player id means "deal
                    // damage to that player's life," not a die at all.
                    if (ctx.State.IsPlayerId(id))
                    {
                        ctx.State.GetPlayer(id).Life -= dealDamage.Amount;
                        continue;
                    }

                    var die = FindDie(ctx, id);
                    die.Damage += dealDamage.Amount;
                    // Ability damage KOs immediately rather than waiting
                    // for a simultaneous batch check - abilities resolve
                    // one at a time (rule 3.2.2), unlike combat damage.
                    if (DieStats.TryResolveKO(ctx.State, die, ctx.Roller)) koIds.Add(id);
                }
                TurnEngine.ResolveKOReactions(ctx.State, ctx.Queue, koIds);
                break;
            }

            case DealDamagePerActiveAffiliate perAffiliate:
            {
                var amount = ActiveAffiliateCount(ctx);
                var koIds = new List<string>();
                foreach (var id in Resolve(ctx, perAffiliate.Target, cache))
                {
                    if (ctx.State.IsPlayerId(id))
                    {
                        ctx.State.GetPlayer(id).Life -= amount;
                        continue;
                    }

                    var die = FindDie(ctx, id);
                    die.Damage += amount;
                    if (DieStats.TryResolveKO(ctx.State, die, ctx.Roller)) koIds.Add(id);
                }
                TurnEngine.ResolveKOReactions(ctx.State, ctx.Queue, koIds);
                break;
            }

            case Ko ko:
            {
                var koIds = new List<string>();
                foreach (var id in Resolve(ctx, ko.Target, cache))
                {
                    if (DieStats.ForceKO(ctx.State, FindDie(ctx, id), ctx.Roller)) koIds.Add(id);
                }
                TurnEngine.ResolveKOReactions(ctx.State, ctx.Queue, koIds);
                break;
            }

            case Sacrifice sacrifice:
                foreach (var id in Resolve(ctx, sacrifice.Target, cache))
                {
                    var die = FindDie(ctx, id);
                    // Appendix 1 clarification 1 - Out of Play only on the
                    // sacrificed die's own OWNER's turn (until end of
                    // turn); otherwise straight to the Used Pile, since
                    // Out of Play doesn't meaningfully exist outside the
                    // active player's own turn (same reasoning already
                    // used for Global ability energy payment).
                    die.Zone = die.OwnerId == ctx.State.ActivePlayerId ? Zone.OutOfPlay : Zone.UsedPile;
                    die.ResetToUnrolled();
                }
                break;

            case MoveDie move:
                foreach (var id in Resolve(ctx, move.Target, cache))
                    FindDie(ctx, id).Zone = move.ToZone;
                break;

            case ModifyStat modify:
                foreach (var id in Resolve(ctx, modify.Target, cache))
                {
                    FindDie(ctx, id).AppliedModifiers.Add(
                        new Modifier(modify.AttackDelta ?? 0, modify.DefenseDelta ?? 0, ctx.SourceDieId ?? "ability"));
                }
                break;

            case Spin spin:
                foreach (var id in Resolve(ctx, spin.Target, cache))
                {
                    var die = FindDie(ctx, id);
                    var actualDelta = DieStats.SpinLevel(ctx.State, die, spin.LevelDelta);

                    // Keyword Awaken fires for ANY spin-up of 1+ levels,
                    // whatever caused it - not just Amplify's own trigger
                    // point in TurnEngine.UseActionDie.
                    if (ctx.Queue is not null)
                        TurnEngine.CheckAwaken(ctx.State, ctx.Queue, die, actualDelta);
                }
                break;

            case PrepDie prep:
                foreach (var id in Resolve(ctx, prep.Source, cache))
                {
                    var die = FindDie(ctx, id);
                    die.Zone = Zone.PrepArea;
                    die.ResetToUnrolled();
                }
                break;

            case FieldDie field:
                foreach (var id in Resolve(ctx, field.Target, cache))
                {
                    // Rule 2.6.3 note - dice fielded by an ability are
                    // fielded for free on level 1 unless stated otherwise.
                    // Paying for a non-free ability-driven field isn't
                    // modeled - no currently-authored card needs it. Also
                    // assumes Status is already Character/SidekickCharacter
                    // (true for every currently-authored user, e.g.
                    // Colossus's Energize target - a die already sitting
                    // in the Reserve Pool on a character face) - doesn't
                    // transition an Energy-status die (Jubilee, DPS036,
                    // deliberately left vanilla for exactly this reason:
                    // an Energize ability fires while its own die is
                    // still Status: Energy, and "field this die" would
                    // need a real Status change this doesn't do).
                    var die = FindDie(ctx, id);
                    die.Level = 1;
                    die.Zone = Zone.FieldZone;
                }
                break;

            case DrawDice draw:
                // Rule 2.3.13 - an ability that says "draw/roll a die"
                // outside Clear and Draw rolls the die once (whatever face
                // it lands on, not necessarily Energy) and places it into
                // the Reserve Pool on that face. Doesn't refill the Bag
                // from the Used Pile the way TurnEngine's own Clear and
                // Draw does - nothing currently authored draws down the Bag
                // far enough for that to matter here.
                for (var i = 0; i < draw.Count; i++)
                {
                    var bag = ctx.State.DiceIn(ctx.ControllerId, Zone.Bag).ToList();
                    if (bag.Count == 0) break;
                    var picked = ctx.Random is not null ? bag[ctx.Random.Next(bag.Count)] : bag[0];
                    picked.Zone = Zone.ReservePool;

                    if (ctx.Roller is not null)
                        ApplyRoll(ctx, picked);
                    else
                        picked.Status = DieStatus.Energy;
                }
                break;

            // Lab Test (DPS005, a Continuous Action die - see its own
            // AbilityDef remarks): "reroll one of the character dice in
            // your Reserve Pool" - unlike DrawDice, the die doesn't move
            // zones, it's re-rolled in place. Silently a no-op if no
            // Roller is available (same "can't do anything meaningful"
            // fallback as DrawDice's `else` branch effectively is for its
            // own Roller-less case, just without a placeholder face to
            // fall back to here).
            case Reroll reroll:
                foreach (var id in Resolve(ctx, reroll.Target, cache))
                {
                    if (ctx.Roller is null) continue;
                    ApplyRoll(ctx, FindDie(ctx, id));
                }
                break;

            case RerollAndMoveUnlessCharacter rerollAndMove:
            {
                var movedCount = 0;
                foreach (var id in Resolve(ctx, rerollAndMove.Target, cache))
                {
                    if (ctx.Roller is null) continue;
                    var die = FindDie(ctx, id);
                    ApplyRoll(ctx, die);
                    if (die.Status is DieStatus.Character or DieStatus.SidekickCharacter) continue;
                    die.Zone = rerollAndMove.ToZone;
                    movedCount++;
                }
                if (rerollAndMove.DamagePerMovedToOpponent > 0 && movedCount > 0)
                {
                    var damagedOpponent = ctx.State.GetPlayer(ctx.State.OpponentOf(ctx.ControllerId));
                    damagedOpponent.Life -= rerollAndMove.DamagePerMovedToOpponent * movedCount;
                }
                break;
            }

            case GainLife gain:
                var gainingPlayer = ctx.State.GetPlayer(ctx.ControllerId);
                gainingPlayer.Life = Math.Min(Player.StartingLife, gainingPlayer.Life + gain.Amount); // rule 1.1.3
                break;

            case LoseLife lose:
                var loserId = lose.Whose == TargetOwnership.Opposing
                    ? ctx.State.OpponentOf(ctx.ControllerId)
                    : ctx.ControllerId;
                ctx.State.GetPlayer(loserId).Life -= lose.Amount;
                break;

            case SwapLife:
                var controller = ctx.State.GetPlayer(ctx.ControllerId);
                var opponent = ctx.State.GetPlayer(ctx.State.OpponentOf(ctx.ControllerId));
                (controller.Life, opponent.Life) = (opponent.Life, controller.Life);
                break;

            case Conditional conditional:
                if (Resolve(ctx, conditional.CheckTarget, cache).Any(id => CheckCondition(ctx, id, conditional.When)))
                    Execute(conditional.Then, ctx, cache);
                else if (conditional.Else is not null)
                    Execute(conditional.Else, ctx, cache);
                break;

            case FieldSidekickForEachPlayer:
                foreach (var playerId in new[] { ctx.ControllerId, ctx.State.OpponentOf(ctx.ControllerId) })
                {
                    // Rule 1.6.8 - a Sidekick sitting in the Used Pile is
                    // unrolled (not "considered a character die"), so any
                    // one of them is fair game - there's no stale rolled
                    // face to match against once dormant-zone dice are
                    // correctly reset (see DieInstance.ResetToUnrolled).
                    var sidekick = ctx.State.DiceIn(playerId, Zone.UsedPile).FirstOrDefault(d => d.IsSidekick);
                    if (sidekick is null) continue; // rule text's "if able"
                    sidekick.Zone = Zone.FieldZone;
                    sidekick.Status = DieStatus.SidekickCharacter;
                    sidekick.Level = 1;
                }
                break;

            case ForceBlock forceBlock:
                foreach (var id in Resolve(ctx, forceBlock.Target, cache))
                    ctx.State.MustBlockThisTurn.Add(id);
                break;

            case ForceAttack forceAttack:
                foreach (var id in Resolve(ctx, forceAttack.Target, cache))
                    ctx.State.MustAttackThisTurn.Add(id);
                break;

            case CantBlock cantBlock:
                foreach (var id in Resolve(ctx, cantBlock.Target, cache))
                    ctx.State.CantBlockThisTurn.Add(id);
                break;

            case SetCallOutTarget callOut:
                // No legal opposing Character die at all (rule 3.1.10) -
                // nothing recorded, which CombatEngine.ActiveCallOutTargets
                // then treats the same as any other cancellation case: no
                // entry, no restriction.
                var callOutTarget = Resolve(ctx, callOut.Target, cache).FirstOrDefault();
                if (callOutTarget is not null && ctx.SourceDieId is not null)
                    ctx.State.CallOutTargets[ctx.SourceDieId] = callOutTarget;
                break;

            case Corrupt corrupt:
            {
                var targetPlayerId = Resolve(ctx, corrupt.PlayerTarget, cache).FirstOrDefault();
                if (targetPlayerId is null) break; // rule 3.1.10 - no legal player target

                var drawn = TurnEngine.DrawFromBag(ctx.State, targetPlayerId, corrupt.Count, ctx.Random);
                if (drawn.Count == 0) break; // nothing left anywhere, even after refilling

                // The choice only exists among these specific just-drawn
                // dice, which didn't exist in any queryable zone before
                // this exact call - can't go through the normal cached
                // Resolve/LegalTargets path (see the Corrupt record's
                // remarks), and there's no legitimate answer a caller
                // could have supplied upfront either, since the dice to
                // choose among didn't exist yet when the request was
                // made. So this always pauses via PendingChoice - see
                // its own remarks and GameState.PendingChoice - rather
                // than ever consulting ctx.ResolveTargets for it.
                if (drawn.Count == 1)
                {
                    // No real choice among which - resolves immediately,
                    // same as always.
                    drawn[0].Zone = Zone.UsedPile;
                    drawn[0].ResetToUnrolled();
                    break;
                }

                ctx.State.PendingChoice = new PendingChoice
                {
                    ControllerId = ctx.ControllerId,
                    Description = "Choose one drawn die to place in the Used Pile - the rest return to the bag.",
                    CandidateDieIds = drawn.Select(d => d.Id).ToList(),
                    AllowMultiple = false,
                    Resolve = chosenIds =>
                    {
                        var chosen = drawn.First(d => d.Id == chosenIds[0]);
                        chosen.Zone = Zone.UsedPile;
                        chosen.ResetToUnrolled();
                        foreach (var d in drawn.Where(d => d != chosen))
                            d.Zone = Zone.Bag; // "the rest are returned to the bag"
                    }
                };
                break;
            }

            case DrawAndChooseOneToRoll drawChoose:
            {
                var drawn = TurnEngine.DrawFromBag(ctx.State, ctx.ControllerId, drawChoose.DrawCount, ctx.Random);
                if (drawn.Count == 0) break; // nothing left anywhere, even after refilling

                void RollAndKeep(DieInstance die)
                {
                    die.Zone = Zone.ReservePool;
                    if (ctx.Roller is not null) ApplyRoll(ctx, die);
                    else die.Status = DieStatus.Energy; // no roller available - same DrawDice fallback
                }

                if (drawn.Count == 1)
                {
                    // No real choice among which - resolves immediately,
                    // same as Corrupt's own single-draw case above.
                    RollAndKeep(drawn[0]);
                    break;
                }

                ctx.State.PendingChoice = new PendingChoice
                {
                    ControllerId = ctx.ControllerId,
                    Description = "Choose one drawn die to roll - the rest return to the bag.",
                    CandidateDieIds = drawn.Select(d => d.Id).ToList(),
                    AllowMultiple = false,
                    Resolve = chosenIds =>
                    {
                        var chosen = drawn.First(d => d.Id == chosenIds[0]);
                        RollAndKeep(chosen);
                        foreach (var d in drawn.Where(d => d != chosen))
                            d.Zone = Zone.Bag; // "return the other to your bag"
                    }
                };
                break;
            }

            case RedrawFromBag redraw:
            {
                // Unlike most targets, this one's candidates DO already
                // exist in a real, queryable zone before this call even
                // starts (Cosmic Cube/Rip Hunter's dice were drawn by
                // TurnEngine.ClearAndDraw itself, earlier, before the
                // ability queue even began draining) - but the player
                // still can't answer this in the same request as the
                // trigger, since there's no round-trip for them to see
                // what was actually drawn first. So this bypasses the
                // normal cached Resolve() pipeline too (computing legal
                // targets directly, the same call Resolve() itself makes
                // internally) and always pauses via PendingChoice when
                // there's anything to redraw at all - "you may send any
                // number of them" means even a single candidate is a
                // real yes/no decision, not something to skip like
                // Corrupt's single-candidate case above.
                var legal = LegalTargets.Query(ctx.State, ctx.ControllerId, redraw.Target);
                if (legal.Count == 0) break; // nothing eligible to redraw

                ctx.State.PendingChoice = new PendingChoice
                {
                    ControllerId = ctx.ControllerId,
                    Description = redraw.Target.Description,
                    CandidateDieIds = legal,
                    AllowMultiple = true,
                    Resolve = chosenIds =>
                    {
                        foreach (var id in chosenIds)
                        {
                            var die = FindDie(ctx, id);
                            die.Zone = redraw.ToZone;
                            // Rule - Out of Play is deliberately not
                            // treated as a dormant zone (see
                            // DieInstance.ResetToUnrolled's own
                            // remarks); everything else this can target
                            // (Used Pile) is.
                            if (redraw.ToZone != Zone.OutOfPlay) die.ResetToUnrolled();
                        }

                        // "For each die sent [to ToZone], draw a die" -
                        // lands in DiceFromBag (see the record's
                        // remarks), to be rolled together with the rest
                        // of this turn's draw once Roll runs, not
                        // immediately.
                        if (chosenIds.Count > 0)
                            TurnEngine.DrawFromBag(ctx.State, ctx.ControllerId, chosenIds.Count, ctx.Random);
                    }
                };
                break;
            }

            case PrepFromBagIfPurchasedThisTurn purchasedThisTurn:
                var purchaser = ctx.State.GetPlayer(ctx.ControllerId);
                if (purchasedThisTurn.CharacterOnly ? purchaser.PurchasedCharacterDieThisTurn : purchaser.PurchasedDieThisTurn)
                {
                    // TurnEngine.DrawFromBag already handles refilling an
                    // empty Bag from the Used Pile (rule 2.1.8-adjacent
                    // "when the bag is empty, shuffle the used pile back
                    // in") - picking straight from Zone.Bag here missed
                    // that refill entirely, so a die sitting in the Used
                    // Pile could never be reached by this ability.
                    var drawn = TurnEngine.DrawFromBag(ctx.State, ctx.ControllerId, 1, ctx.Random);
                    if (drawn.Count > 0) drawn[0].Zone = Zone.PrepArea;
                }
                break;

            case PrepFromBag:
            {
                var drawn = TurnEngine.DrawFromBag(ctx.State, ctx.ControllerId, 1, ctx.Random);
                if (drawn.Count > 0) drawn[0].Zone = Zone.PrepArea;
                break;
            }

            case GrantLoyaltyCounter:
                if (ctx.SourceDieId is not null)
                {
                    var grantee = FindDie(ctx, ctx.SourceDieId);
                    var granteeCardId = grantee.VirtualCardId ?? grantee.CardId;
                    if (granteeCardId is not null)
                        ctx.State.LoyaltyCounters[granteeCardId] =
                            ctx.State.LoyaltyCounters.GetValueOrDefault(granteeCardId) + 1;
                }
                break;

            default:
                throw new NotSupportedException($"Unhandled effect node: {node.GetType().Name}");
        }
    }

    // TargetSpec.Self bypasses legal-target filtering entirely and
    // resolves straight to the ability's own source die (rule 3.1.15-style
    // self-reference). Everything else goes through LegalTargets (rule
    // 3.3) - the caller still picks WHICH legal die(s) to actually use
    // (that's a real player/AI decision this system doesn't make), but the
    // choice is validated against the real legal set rather than trusted
    // blindly, and rule 3.3.11's "as many as available, otherwise all of
    // them" count requirement is enforced here. Results are cached per
    // TargetSpec for the lifetime of one top-level Execute call - see the
    // class-level remarks on why a repeated reference to "the target"
    // shouldn't be re-validated against a board its own earlier clause
    // may have just changed.
    private static IReadOnlyList<string> Resolve(
        EffectContext ctx, TargetSpec spec, Dictionary<TargetSpec, IReadOnlyList<string>> cache)
    {
        if (spec.IsSelf)
            return ctx.SourceDieId is not null ? [ctx.SourceDieId] : [];

        if (cache.TryGetValue(spec, out var cached))
            return cached;

        var legal = LegalTargets.Query(ctx.State, ctx.ControllerId, spec);
        var chosen = ctx.ResolveTargets(spec);

        var illegal = chosen.Where(id => !legal.Contains(id)).ToList();
        if (illegal.Count > 0)
        {
            throw new InvalidOperationException(
                $"Chosen target(s) [{string.Join(", ", illegal)}] are not legal for '{spec.Description}'.");
        }

        IReadOnlyList<string> result;
        if (legal.Count == 0)
        {
            result = []; // rule 3.1.10 - no legal targets, nothing to apply
        }
        else
        {
            var required = spec.Optional ? 0 : Math.Min(spec.Count, legal.Count);
            if (chosen.Count < required)
            {
                throw new InvalidOperationException(
                    $"'{spec.Description}' needs {required} target(s) but only {chosen.Count} were chosen.");
            }

            result = chosen.Count > spec.Count ? chosen.Take(spec.Count).ToList() : chosen;
        }

        cache[spec] = result;
        return result;
    }

    private static bool CheckCondition(EffectContext ctx, string dieId, EffectCondition condition) => condition switch
    {
        EffectCondition.TargetWasKOd => FindDie(ctx, dieId) is { Zone: Zone.PrepArea, Status: DieStatus.Unrolled },
        // dieId is unused here - see EffectCondition.NoCharacterKOdThisTurn's own remarks.
        EffectCondition.NoCharacterKOdThisTurn => !ctx.State.AnyCharacterKOdThisTurn,
        EffectCondition.PrepAreaEmpty => !ctx.State.DiceIn(ctx.ControllerId, Zone.PrepArea).Any(),
        EffectCondition.OnSingleBurstFace => CurrentBurstStars(ctx.State, FindDie(ctx, dieId)) == 1,
        EffectCondition.OnDoubleBurstFace => CurrentBurstStars(ctx.State, FindDie(ctx, dieId)) == 2,
        _ => throw new NotSupportedException($"Unhandled effect condition: {condition}")
    };

    // A Character die's current face is always derivable from Level (no
    // separate storage - DieStats.GetFace). A Basic Action/Action die has
    // no "level," so which of its 3 faces it landed on is instead stored
    // directly on the die itself (DieInstance.BurstStars, set by
    // TurnEngine/EffectInterpreter's own ApplyRoll) - this picks whichever
    // source actually applies to the die being checked.
    private static int? CurrentBurstStars(GameState state, DieInstance die) =>
        die.Status is DieStatus.Character or DieStatus.SidekickCharacter
            ? DieStats.GetFace(state, die).BurstStars
            : die.BurstStars;

    private static DieInstance FindDie(EffectContext ctx, string id) =>
        ctx.State.Dice.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException($"No die with id '{id}'.");

    // Shared by DrawDice and Reroll - rolls `die` via ctx.Roller and
    // applies the result face to it in place (Status/Level/Energy* only;
    // the caller is responsible for any zone move, since DrawDice needs
    // one and Reroll doesn't). Also checks Energize immediately, same
    // "outside the Roll and Reroll Step, so no reroll decision to wait
    // for" reasoning DrawDice's own remarks already explain.
    private static void ApplyRoll(EffectContext ctx, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        var card = cardId is not null ? ctx.State.CardCatalog.GetValueOrDefault(cardId) : null;
        var result = ctx.Roller!.Roll(die, card);
        die.Status = result.Status;
        die.Level = result.Level;
        die.EnergyKind = result.Status == DieStatus.Energy ? result.EnergyKind : EnergyKind.None;
        die.ProvidedEnergyType = result.Status == DieStatus.Energy ? result.ProvidedEnergyType : null;
        die.EnergyAmount = result.Status == DieStatus.Energy ? result.EnergyAmount : 1;
        die.BurstStars = result.Status == DieStatus.Action ? result.BurstStars : null;

        if (ctx.Queue is not null)
            TurnEngine.CheckEnergize(ctx.State, ctx.Queue, die);
    }

    // Counts the ability's own source die's controller's active
    // (Field/Attack Zone) dice that share an affiliation with the source
    // die's own card - Black Manta's "for each of your active Villains"
    // idiom. Counts dice, not unique characters (the standard Dice
    // Masters convention for "for each active X" scaling text, distinct
    // from Retaliation's own "trigger once per unique character" rule -
    // see CombatEngine.ResolveRetaliation) - and includes the source
    // die's own other copies, since the card text doesn't say "other."
    private static int ActiveAffiliateCount(EffectContext ctx)
    {
        var source = ctx.SourceDieId is not null ? FindDie(ctx, ctx.SourceDieId) : null;
        var sourceCardId = source?.VirtualCardId ?? source?.CardId;
        if (sourceCardId is null || !ctx.State.CardCatalog.TryGetValue(sourceCardId, out var sourceCard)) return 0;

        return ctx.State.DiceIn(source!.ControllerId, Zone.FieldZone)
            .Concat(ctx.State.DiceIn(source.ControllerId, Zone.AttackZone))
            .Count(d =>
                (d.VirtualCardId ?? d.CardId) is { } id
                && ctx.State.CardCatalog.TryGetValue(id, out var card)
                && card.Affiliations.Any(sourceCard.Affiliations.Contains));
    }
}
