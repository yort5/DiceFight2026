# Card symbols

The symbols printed on Dice Masters cards, taken from the old Teambuilder
(`~/DiceMasters/Teambuilder`, hosted at tb.dicecoalition.com), which is
the same icon set players have been reading for a decade. Its `iconid`
map in `cards.php` is the key to the original filenames.

They are 17x17 PNGs, all comfortably under Vite's 4KB inline limit, so
they ship as data URIs inside the bundle - nothing extra to deploy.

`src/gameIcons.ts` is the registry (rendered by `src/GameIcon.tsx`). Files listed here but not imported
there are copied in and ready to use, just not rendered anywhere yet.

| File | Was | Meaning |
| --- | --- | --- |
| `energy-mask.png` | e1 | Mask energy |
| `energy-fist.png` | e2 | Fist energy |
| `energy-bolt.png` | e3 | Bolt energy |
| `energy-shield.png` | e4 | Shield energy |
| `energy-bolt-or-fist.png` | e5 | either Bolt **or** Fist - one split symbol, not two |
| `energy-bolt-or-mask.png` | e6 | either Bolt or Mask |
| `energy-bolt-or-shield.png` | e7 | either Bolt or Shield |
| `energy-fist-or-mask.png` | e8 | either Fist or Mask |
| `energy-fist-or-shield.png` | e9 | either Fist or Shield |
| `energy-mask-or-shield.png` | eA | either Mask or Shield |
| `energy-generic-0.png` … `-9.png` | eg0-eg9 | generic energy: any type, that many |
| `energy-generic-x.png` | egx | generic energy, variable amount |
| `energy-wild.png` | qu | the `?` wild face, which counts as any type |
| `sidekick.png` | pawn | a Sidekick die |

Copied in but not wired up yet:

| File | Was | Meaning |
| --- | --- | --- |
| `energy-bolt-x2.png` | e33 | the double-Bolt face (and `-fist-`/`-mask-`/`-shield-x2`) |
| `action.png` | action | an action face |
| `flip.png` | flip | the "flip this die" symbol |
| `burst.png` | burst | the burst star the `*`/`**` stat marks stand for |
| `equip.png` | eq | Equip |
| `alignment-good.png` | dg | D&D alignment (and `-evil`, `-neutral`) |

The doubles are deliberately unused: the reference sheet writes both the
double-energy face and two separate symbols as the same words ("Bolt
Bolt"), so we cannot tell them apart and render two single symbols, which
costs the same either way.

The affiliation logos live in `public/affiliations` instead, not here:
there are 97 of them at ~154KB, which is more than half the JS bundle
again if inlined, and only the few actually on screen ever get fetched.
See `src/AffiliationIcons.tsx`.
