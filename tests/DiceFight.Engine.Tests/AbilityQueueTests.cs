using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// Replicates the worked "Queue Example" from rule 3.2.2 verbatim:
// Die A and Die B (attackers) each have "when attacks, deal 1 damage to
// all opposing dice"; Die C (defender) has "when damaged, KO an opposing
// die". The rulebook traces resolution order as A, B, C, C - with the
// first Die C resolution KO'ing Die A and the second KO'ing Die B.
public class AbilityQueueTests
{
    [Fact]
    public void Drain_MatchesRulebookQueueExample()
    {
        var queue = new AbilityQueue();
        var resolutionOrder = new List<string>();
        var koOrder = new List<string>();
        var koTargets = new Queue<string>(["DieA", "DieB"]);
        var placeholderEffect = new DealDamage(1, TargetSpec.CharacterDie("all opposing dice"));

        // (1) Die A and Die B declared as attackers.
        queue.Enqueue("DieA", "active", TriggerType.WhenAttacks, placeholderEffect);
        queue.Enqueue("DieB", "active", TriggerType.WhenAttacks, placeholderEffect);

        queue.Drain(ability =>
        {
            resolutionOrder.Add(ability.SourceDieId!);

            if (ability.Trigger == TriggerType.WhenAttacks)
            {
                // (2)/(3) damaging Die C initiates its "when damaged" ability.
                queue.Enqueue("DieC", "inactive", TriggerType.WhenDamaged, placeholderEffect);
            }
            else if (ability.Trigger == TriggerType.WhenDamaged)
            {
                // (4)/(5) each Die C resolution KOs one opposing die.
                koOrder.Add(koTargets.Dequeue());
            }
        });

        Assert.Equal(["DieA", "DieB", "DieC", "DieC"], resolutionOrder);
        Assert.Equal(["DieA", "DieB"], koOrder);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void Interrupt_ResolvesBeforeAlreadyQueuedAbilities()
    {
        var queue = new AbilityQueue();
        var order = new List<string>();
        var effect = new Ko(TargetSpec.CharacterDie("target"));

        queue.Enqueue("First", "p1", TriggerType.WhenKOd, effect);
        queue.Enqueue("Second", "p1", TriggerType.WhenKOd, effect);

        queue.Drain(ability =>
        {
            order.Add(ability.SourceDieId!);
            if (ability.SourceDieId == "First")
            {
                // A Prevent/Redirect-type reaction (3.2.8) jumping ahead of
                // whatever is already queued.
                queue.Interrupt("Preventer", "p2", TriggerType.Global, effect);
            }
        });

        Assert.Equal(["First", "Preventer", "Second"], order);
    }

    // `shouldStop` (used by GamesController.Drain for GameState.
    // PendingChoice - see its own remarks) is checked after each
    // ability, not before, so the ability that raised the stop condition
    // still gets to finish its own resolve call first - only anything
    // *after* it is left untouched in the queue.
    [Fact]
    public void Drain_StopsEarly_WhenShouldStopReturnsTrue()
    {
        var queue = new AbilityQueue();
        var order = new List<string>();
        var effect = new Ko(TargetSpec.CharacterDie("target"));

        queue.Enqueue("First", "p1", TriggerType.WhenKOd, effect);
        queue.Enqueue("Second", "p1", TriggerType.WhenKOd, effect);

        var stop = false;
        queue.Drain(
            ability =>
            {
                order.Add(ability.SourceDieId!);
                if (ability.SourceDieId == "First") stop = true;
            },
            shouldStop: () => stop);

        Assert.Equal(["First"], order);
        Assert.False(queue.IsEmpty);
        Assert.Equal("Second", queue.Pending.Single().SourceDieId);
    }

    // Confirms a later Drain call (no shouldStop this time - nothing left
    // to pause for) picks up exactly where the first one left off.
    [Fact]
    public void Drain_ResumingAfterAnEarlyStop_RunsWhateverWasStillQueued()
    {
        var queue = new AbilityQueue();
        var order = new List<string>();
        var effect = new Ko(TargetSpec.CharacterDie("target"));

        queue.Enqueue("First", "p1", TriggerType.WhenKOd, effect);
        queue.Enqueue("Second", "p1", TriggerType.WhenKOd, effect);

        var stop = false;
        queue.Drain(ability => { order.Add(ability.SourceDieId!); stop = true; }, shouldStop: () => stop);
        Assert.Equal(["First"], order);

        queue.Drain(ability => order.Add(ability.SourceDieId!));

        Assert.Equal(["First", "Second"], order);
        Assert.True(queue.IsEmpty);
    }
}
