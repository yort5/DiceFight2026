// /dice-kingdom/how-to-play (2026-10-16): the basic rules on one page - its
// own route, not a modal, so it works for a first game, a physical game at
// a table, or a link sent to a friend. Written against the engine as it
// stands (TurnEngine, CombatEngine, ChampionPowers, laneRule.ts); when a
// rule changes there, change it here too.
import { useEffect, useMemo, useState } from "react";
import { KEYWORD_RULES } from "../keywordRules";
import { navigate } from "../router";
import { backPath } from "./AccountPage";
import { DieAnatomy, LaneFightExample, StackingExamples, TardigradeFaces, TurnStrip, ZoneMap } from "./HowToPlayFigures";
import { api } from "./api";
import { CHAMPION_ICONS, EnergyBadge, TardigradeIcon } from "./icons";
import { ThemeToggle, useTheme } from "./ThemeToggle";
import type { CardDef } from "./types";

const SECTIONS = [
  ["goal", "The goal"],
  ["team", "Your team"],
  ["dice", "The dice"],
  ["turn", "A turn"],
  ["combat", "Attacking and blocking"],
  ["champions", "Champions"],
  ["zones", "Where dice go"],
  ["keywords", "Keywords"],
  ["packs", "Packs and your collection"],
] as const;

const CHAMPIONS = [
  { id: "Wolf", name: "Wolf", energy: "Claw", power: "Once during your turn, give one of your creatures +3 ATK." },
  { id: "Armadillo", name: "Armadillo", energy: "Shell", power: "Once a turn, after blockers are chosen, one of your creatures in the fight takes no combat damage." },
  { id: "GoldenEagle", name: "Golden Eagle", energy: "Wing", power: "Once during your turn, field one creature without paying its fielding cost." },
  { id: "GreatHornedOwl", name: "Great Horned Owl", energy: "Eye", power: "Once during your turn, spin one creature one level - yours up, or your opponent's down." },
];

function Section({ id, title, children }: { id: string; title: string; children: React.ReactNode }) {
  return (
    <section id={id} className="panel dk-howto-section">
      <h2>{title}</h2>
      {children}
    </section>
  );
}

export function HowToPlayPage() {
  const [theme, setTheme] = useTheme();
  const [cards, setCards] = useState<CardDef[] | null>(null);
  const [cardsFailed, setCardsFailed] = useState(false);
  useEffect(() => {
    api.getCards().then(setCards, () => setCardsFailed(true));
  }, []);
  // Only the keywords some card in the game actually has, so the list grows
  // with the catalog instead of listing every Dice Masters keyword.
  const keywords = useMemo(() => {
    if (!cards) return [];
    const used = new Set(cards.flatMap((c) => c.keywords));
    return Object.keys(KEYWORD_RULES).filter((k) => used.has(k)).sort((a, b) => a.localeCompare(b));
  }, [cards]);
  const back = backPath();

  return (
    <div className="dicekingdom dk-account dk-howto">
      {/* Stays on screen while you scroll (2026-10-19): the installed app has
          no browser back button or address bar, so this is the only way out. */}
      <header className="dk-howto-bar">
        <a
          href={back}
          onClick={(e) => {
            e.preventDefault();
            navigate(back);
          }}
        >
          ← Dice Kingdom
        </a>
        <select
          aria-label="Jump to a section"
          value=""
          onChange={(e) => {
            const id = e.currentTarget.value;
            if (id === "top") window.scrollTo({ top: 0, behavior: "smooth" });
            else document.getElementById(id)?.scrollIntoView({ behavior: "smooth" });
          }}
        >
          <option value="" disabled>
            Contents
          </option>
          <option value="top">Top</option>
          {SECTIONS.map(([id, title]) => (
            <option key={id} value={id}>
              {title}
            </option>
          ))}
        </select>
        <ThemeToggle theme={theme} setTheme={setTheme} />
      </header>
      <h1>How to Play</h1>
      <p className="dk-howto-lede">
        Dice Kingdom is a dice-building game for two players. You start with a bag of small dice, buy stronger
        creature dice as you go, and send them into battle.
      </p>
      <nav className="dk-howto-toc" aria-label="Contents">
        {SECTIONS.map(([id, title]) => (
          <a key={id} href={`#${id}`}>
            {title}
          </a>
        ))}
      </nav>

      <Section id="goal" title="The goal">
        <p>
          Both players start with <b>20 life</b>. Bring your opponent to 0 and you win.
        </p>
      </Section>

      <Section id="team" title="Your team">
        <ul>
          <li>
            <b>A Champion.</b> It has no die - it gives you one special power you can use once a turn (see{" "}
            <a href="#champions">Champions</a>).
          </li>
          <li>
            <b>Eight creature cards.</b> Each card has dice you can buy during the game. You can see both teams'
            cards at any time.
          </li>
          <li>
            <b>A Basic Action card.</b> Its dice can be bought by either player.
          </li>
          <li>
            <b>Eight Tardigrade dice</b> <TardigradeIcon size={16} /> of your Champion's energy type. These start in
            your bag - everything else you buy.
          </li>
        </ul>
        <p className="dk-acct-dim">
          Custom teams from the Team Builder pick their own cards and Tardigrades, and may pick a Champion too - its
          Basic Action must then be of the Champion's energy.
        </p>
      </Section>

      <Section id="dice" title="The dice">
        <p>Every face of a die is one of three kinds:</p>
        <ul>
          <li>
            <b>Energy</b> - one or two energy of a type: Claw <EnergyBadge type="Claw" size={14} />, Shell{" "}
            <EnergyBadge type="Shell" size={14} />, Wing <EnergyBadge type="Wing" size={14} /> or Eye{" "}
            <EnergyBadge type="Eye" size={14} />. Wild <EnergyBadge type="Wild" size={14} /> counts as any type.
            Energy pays for everything.
          </li>
          <li>
            <b>Creature</b> - a level (1-3), a fielding cost, and ATK / DEF. Higher levels are stronger.
          </li>
          <li>
            <b>Action</b> - a one-time effect, written on the action's card.
          </li>
        </ul>
        <DieAnatomy cards={cards} />
        <p>
          <b>Tardigrade faces:</b> two faces of 2 energy, one of 1 energy, two creature faces worth a Wild energy
          (spend them as energy, or field them as a level 2 1/1), and a level 3 1/2. Tardigrade creatures are free to
          field.
        </p>
        <TardigradeFaces type="Claw" />
        <p>
          <b>Spinning</b> turns a die to another level without rolling it.
        </p>
      </Section>

      <Section id="turn" title="A turn">
        <TurnStrip />
        <ol className="dk-howto-steps">
          <li>
            <b>Clear and Draw.</b> Leftover dice in your Reserve Pool go to your Used Pile. Draw 4 dice from your bag.
            When the bag runs out, your Used Pile goes back into it. If even that can't make 4, you take 1 damage for
            each die you're short - and get 1 energy for each to spend this turn.
            <span className="dk-acct-dim"> The first player draws 4 but sets one aside on turn one.</span>
          </li>
          <li>
            <b>Roll and Reroll.</b> Roll the dice you drew, plus any waiting in your Prep Area. Then you may reroll any
            of them, once.
          </li>
          <li>
            <b>Main.</b> In any order:
            <ul>
              <li>
                <b>Buy</b> a die from a card: pay its cost, with at least one energy of the card's type. Bought dice go
                to your Used Pile, so they reach your bag later.
              </li>
              <li>
                <b>Field</b> a creature you rolled: pay its fielding cost with any energy. Fielded creatures stay until
                they're knocked out.
              </li>
              <li>
                <b>Use</b> an action you rolled, a card's Global ability, or your Champion's power.
              </li>
            </ul>
            Creature faces you didn't field go to your Used Pile. Unspent energy stays in your Reserve Pool through
            your opponent's turn - you can spend it on Global abilities then.
          </li>
          <li>
            <b>Attack</b> (optional) - see below.
          </li>
          <li>
            <b>Clean Up.</b> Damage on creatures heals, and "until end of turn" effects end.
          </li>
        </ol>
      </Section>

      <Section id="combat" title="Attacking and blocking">
        <ol className="dk-howto-steps">
          <li>
            <b>Choose attackers</b> from your fielded creatures and put each into one of the <b>four lanes</b>.
            <div className="dk-howto-rule">
              <b>Stacking rule:</b> a lane can take another attacker only if another lane already has as many. A
              second attacker in a lane needs another lane with one, a third needs another lane with two. Empty lanes
              are always open.
            </div>
            <StackingExamples />
          </li>
          <li>
            <b>Your opponent blocks</b> with their fielded creatures. Each blocker goes into one lane; several can
            block the same lane.
          </li>
          <li>
            <b>Each lane is one fight.</b> The lane's attackers add up their ATK against its blockers, and the
            blockers add up theirs against the attackers - first-chosen dice take damage first. A creature with damage
            equal to its DEF is knocked out.
          </li>
          <li>
            <b>Unblocked lanes</b> hit your opponent for their total ATK. Blocked lanes don't - unless the lane has{" "}
            <b>Overcrush</b>: if every blocker is knocked out, the leftover damage goes through. Any lane with two or
            more attackers has Overcrush.
          </li>
        </ol>
        <LaneFightExample />
        <p>
          After the fight, attackers that went unblocked leave the Field (they're back in your Used Pile at the end of
          the turn). Attackers that were blocked and survived go back to the Field. Knocked-out dice go to their
          owner's Prep Area, so they're rolled again next turn.
        </p>
        <p className="dk-acct-dim">
          The trade-off: a big hit uses up your attackers, and stacking two strong creatures in a lane means sending a
          smaller one alone somewhere else.
        </p>
      </Section>

      <Section id="champions" title="Champions">
        <div className="dk-howto-champs">
          {CHAMPIONS.map((c) => {
            const Icon = CHAMPION_ICONS[c.id];
            return (
              <div key={c.id} className="dk-howto-champ">
                <span className="dk-howto-champ-icon">{Icon && <Icon size={36} />}</span>
                <span>
                  <b>{c.name}</b> <EnergyBadge type={c.energy} size={14} />
                  <br />
                  {c.power}
                </span>
              </div>
            );
          })}
        </div>
      </Section>

      <Section id="zones" title="Where dice go">
        <ZoneMap />
        <dl className="dk-howto-zones">
          <dt>Bag</dt>
          <dd>Dice waiting to be drawn.</dd>
          <dt>Prep Area</dt>
          <dd>Rolled next turn along with your draw - knocked-out creatures land here.</dd>
          <dt>Reserve Pool</dt>
          <dd>This turn's rolled dice: energy to spend, creatures to field, actions to use.</dd>
          <dt>Field</dt>
          <dd>Your creatures in play. They can attack and block.</dd>
          <dt>Used Pile</dt>
          <dd>Spent and finished dice. It refills your bag when the bag is empty.</dd>
          <dt>Out of Play</dt>
          <dd>Energy you spent and unblocked attackers, until your turn ends.</dd>
        </dl>
      </Section>

      <Section id="keywords" title="Keywords">
        <p className="dk-acct-dim">
          Some cards list a keyword instead of spelling out a rule. Tap a keyword on a card in a game to see it too.
        </p>
        {cardsFailed && <p className="dk-acct-dim">Couldn't load the card list - try again later.</p>}
        {!cards && !cardsFailed && <p className="dk-acct-dim">Loading…</p>}
        <dl className="dk-howto-keywords">
          {keywords.map((k) => (
            <div key={k}>
              <dt>{k}</dt>
              <dd>{KEYWORD_RULES[k]}</dd>
            </div>
          ))}
        </dl>
      </Section>

      <Section id="packs" title="Packs and your collection">
        <p>
          Finishing a game with a Champion adds its starter cards to your collection. Coins buy random packs of more cards (you get
          coins when you start, and more each day you play), and spare dice can be traded in for rarer cards. Rarer
          doesn't mean stronger - a team of common cards can win.
        </p>
      </Section>
    </div>
  );
}
