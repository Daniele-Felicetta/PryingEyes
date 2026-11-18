# The cathedral and the catacombs

Request of 17 November 2026: the game has two zones. **Your cathedral**, a ruin to rebuild, with a market of supplies and resources
in the square outside; and **the dungeons**, which are the procedural floors built so far, reached no longer by lift (it is not a tower
any more) but down into **catacombs**. The cathedral is built with resources taken from the enemies and the bosses of the catacombs:
simple ones (bones, wood, steel), rarer ones (embers, hearts, blood) and one exclusive to each boss (relics, darkness-imbued crystals,
faith-imbued crystals, gems). The use of points and what the cathedral gives back were left to design; the answers are below.

This page says how it plays, then the numbers, then the code and how it was verified. The rules of each catacomb level (waves, paid
areas, perks, shops) are those of [tower.md](tower.md); this page only lists what changes.

## How it plays (alone)

1. *Gioca* starts in the **cathedral square** (*il sagrato*), at night, in front of the façade. Nothing attacks there: no waves, no enemies.
2. Inside, each **work** (*opera*) of the cathedral has its place. Until it is built, the place is a **construction site**: scaffolding,
   stones and a sign. Looking at the sign shows the cost against what is in the store (`Legno 32/50 · Acciaio 30/30`) and the effect;
   `F` builds it when the store covers it and its prerequisites stand. The cathedral is rebuilt at once with the work in place (a
   half-second fade; you stay where you were).
3. The **market** stalls in the square sell supplies for the next expedition and buy and sell resources, for **oboli** (coins).
4. Behind the apse, **the descent to the catacombs** has five gates, one per branch. An open gate takes you to the first level of its
   branch. A closed one is the construction site of the work that opens it.
5. In the catacombs every level plays like a floor of the Tower: waves, areas bought with points, perks, the mystery chest, the altar.
   Enemies drop **resources** on the floor; walking close pulls them into your **satchel** (*zaino*, top right).
6. You come back up in one of three ways, and everything in the satchel goes to the cathedral's store while your unspent points become
   oboli:
   - by the **stair you came down**, between waves (during a wave it is haunted and refuses);
   - after the **boss** of the third level of the branch: its gate leads up, not down;
   - with the **return bell** (a work of the cathedral), once per expedition, from the pause menu, even in the middle of a wave.
7. **Dying** ends the expedition: you wake in the cathedral with half of the common resources (rounded down) and none of the rare ones.
   The rest stays as **your remains** on the first level of that branch, marked by a column of light; reach them on the next descent into
   the same branch and they go back into the satchel. Dying again before that loses them for good. Returning to the main menu during an
   expedition counts as dying, unless a player alone chooses **Salva e torna al menu** between two waves: the expedition is then suspended in a
   file of the character and resumed from *Gioca* ([saves-and-characters.md](saves-and-characters.md#a-suspended-expedition)).

## The branches

Each branch is three levels; the third holds its boss. The global floor number that scales difficulty is `branch × 3 + level`, so the
first branch has the difficulty of floors 1-3 of the old Tower, the second of floors 4-6, and so on (`Cattedrale/Catalogo.cs`, `Rami`).

| Branch | Theme | Boss (third level) | Its resource | Opened by |
| --- | --- | --- | --- | --- |
| I. L'Ossario | Hotel and church | Il Cardinale | Reliquie | open from the start |
| II. Il Teatro sepolto | Theatre | Il Direttore | Cristalli d'oscurità | Il Cancello del Teatro (costs 1 Reliquia) |
| III. Il Sanatorio | Sanatorium | Il Primario | Cristalli di fede | Il Cancello del Sanatorio (1 Cristallo d'oscurità) |
| IV. Il Guardaroba del Conte | Guardaroba | Il Conte (the Cardinale in crimson and black) | Gemme | Il Cancello del Conte (1 Cristallo di fede) |
| L'Abisso | any, drawn per level | the four in turn, one every three levels | that boss's | Il Rosone (the last work) |

The Guardaroba uses its own props (`gu_*`). They reach the game through the kit saved in `Torre.unity`, which lists all 17 since the
scene was regenerated with `BuildPlayer.Torre` (how that works, and why the scene is committed again whenever a prop is added, is in
[guardaroba.md](guardaroba.md#how-the-props-reach-the-game)). `FloorDirector.ThemeOfFloor` keeps a safety net: if a scene without them is
loaded, the fourth branch draws one of the three complete themes instead of showing grey boxes.

**The boss.** On a third level, after the three waves that would open the gate, the next wave opens with the boss and half the usual
escort. The boss has at least the hit points of the Cardinale of the eighth waves, plus 25% per branch, and its name on the top bar. The gate
opens only when that wave is cleared; at that moment the boss's trophy is secured in the store (three of its resource the first time,
two after, plus 12-18 common and 3 rare resources), so it is kept even if you die before reaching the stair. In the Abyss the gate goes
on down.

**What else changed on a level.** The lift cabins are stone landings closed by an iron portcullis that rises (`ProcLift.BuildGate`):
the arrival one has the steps you came down, the exit one the dark shaft where the stair goes on, with a torch on the wall and an iron
lantern over the arch that lights when it opens. The banner names the branch and the level (*L'Ossario · livello II*), the guide arrow
says *cancello*, the HUD says *LIVELLO*. A **poor box** (*cassetta delle elemosine*) next to the arrival turns 500 points into 33 oboli
at once, safe even if you die.

## Resources

| Grade | Resources | Where they come from |
| --- | --- | --- |
| Common | Ossa (bones), Legno (wood), Acciaio (steel) | most enemies: monks and Veglie bones and wood, abbots and the Portachiavi steel |
| Rare | Brace (embers), Cuori (hearts), Sangue (blood) | 2.5-15% of kills: Veglie embers, abbots and chief physicians hearts, flying things and nurses blood |
| Boss | Reliquie, Cristalli d'oscurità, Cristalli di fede, Gemme | only the boss of their branch; never sold or bought |

Each enemy type has a table of common and one of rare drops (`Bottino`): for example a monk drops 1-2 bones or wood with 22% chance and a
rare resource with 2.5%; a statue drops 3-5 steel or bones with 75% and a rare one with 15%; the eighth-wave Cardinale always drops 6-10
common and 1-2 rare. Amounts grow by 15% per branch. **A knife kill doubles the chance of common drops.** In co-op every machine draws
its own drops for every kill and each player picks up their own.

## Points and oboli

Points stay what they were inside the catacombs (doors, wall weapons, the chest, perks, the altar, traps); every expedition starts again
from 500 plus the bonuses below. What changes is that **points are worth something when you come back**:

- climbing back alive, unspent points become **oboli** (Charon's coin), one every 10 points (every 6 with the Treasury);
- dying, they are lost (with the Confessional, one obolo every 20 points survives);
- the poor box on every level turns 500 points into 33 oboli at once, safe whatever happens next.

So every purchase down there is a choice between surviving the next wave and bringing something home. Oboli are spent at the market.

## The market

Four stalls in the square; each offer is an object on the counter, chosen by standing in front of it and pressing `F`, like a wall
weapon. **Stock refills every time you come back from the catacombs.** Merchants never trade boss resources, and nothing sells for more
than it costs to buy (the lab checks it).

| Stall | Offers (oboli; stock per return) |
| --- | --- |
| La bottega della speziale | Ampolla d'acqua santa 30 (3), Cero votivo 120 (1): start with the Rinascita book, Mappa del cartografo 20 (1): every level of the expedition fully mapped |
| Il banco dell'armaiolo | Mina del reliquiario 45 (2), Carillon 25 (2), Lettera di credito 60 (2): +500 points at the start |
| Il carro dell'ossaio | buys 10 Ossa / Legno / Acciaio for 25 / 30 / 40 (3 each); sells 10 of them for 8 / 10 / 12 |
| La tenda del cerusico | buys 1 Brace / Cuori / Sangue for 35 / 40 / 30 (2 each); sells 1 for 10 / 12 / 9 |

Supplies are spent automatically at the start of the next expedition.

## The works

Twenty works, each with a place in the cathedral (`Opere` in `Catalogo.cs`; the cathedral is `Proc/HubPlan.cs` and its furnishing
`Proc/RoomKits.Hub.cs`). The first four need only common resources (plus a little blood for the font); the rest need rare resources and
the trophies of the bosses, so the cathedral grows as you go deeper.

| Work | Place | Cost | Needs | What it gives |
| --- | --- | --- | --- | --- |
| Le Volte della navata | nave | 40 wood, 20 steel | | the nave gets its roof back (before: open to the night sky); +250 points at the start of every expedition |
| La Fonte battesimale | baptistery | 15 bones, 15 steel, 2 blood | | at least three holy waters on every level |
| La Sacrestia | sacristy | 30 wood, 25 steel | | a rack of three free weapons better than the pistol (grade 2; one of grade 3 with the Forge) to take down |
| L'Ossario | crypt | 60 bones, 10 wood | | dying keeps all common resources and half of the rare ones |
| L'Altare maggiore | presbytery | 30 bones, 20 wood, 3 hearts | Volte | before each expedition, light one of three candles: a blessing for the whole descent |
| L'Organo | nave | 50 wood, 30 steel, 4 embers | Volte | every wave cleared heals you fully and raises the shield to 100 |
| Il Confessionale | west aisle | 25 wood, 4 blood | Volte | dying, points still give one obolo every 20 |
| La Forgia | sacristy | 50 steel, 15 wood, 6 embers | Sacrestia | +15% damage with every weapon |
| Il Tesoro | sacristy | 30 steel, 30 bones, 2 hearts | Sacrestia | one obolo every 6 points instead of 10 |
| Il Cancello del Teatro | crypt | 30 steel, 30 bones, 1 relic | | opens branch II |
| Il Cancello del Sanatorio | crypt | 45 steel, 30 wood, 3 embers, 1 darkness crystal | Cancello del Teatro | opens branch III |
| Il Cancello del Conte | crypt | 60 steel, 5 blood, 1 faith crystal | Cancello del Sanatorio | opens branch IV |
| Il Campanile | bell tower | 40 steel, 30 wood, 5 embers, 1 relic | Volte | the return bell: once per expedition, from the pause menu |
| La Cappella della Pietra | east aisle | 40 bones, 4 hearts, 2 relics | Altare | start every expedition with Pelle di pietra (it takes a perk slot) |
| La Cappella delle Mani | east aisle | 40 wood, 4 embers, 2 darkness crystals | Altare | start with Mani svelte |
| La Vetrata del Cardinale | nave clerestory | 3 relics, 20 wood | Volte | +25% damage to bosses |
| La Vetrata del Direttore | nave clerestory | 3 darkness crystals, 20 steel | Volte | area doors cost 25% less |
| La Vetrata del Primario | nave clerestory | 3 faith crystals, 20 bones | Volte | +25 maximum health |
| La Vetrata del Conte | nave clerestory | 3 gems, 5 blood | Volte | perk books cost 40% less |
| Il Rosone | façade | 3 of each boss resource, 100 bones, 100 wood, 100 steel | the four windows | the cathedral is consecrated: the Abyss opens under the crypt |

The cathedral changes as it is built: the nave is a roofless ruin with a broken crown on its walls until the vaults; the four stained
glass windows of the bosses and the rose window exist as windows (with their coloured light) only once made; the bell tower is a stump;
pews lie overturned until the altar brings the congregation back, then stand in rows under two chandeliers. The **register** on the
lectern in the narthex lists every work with its state, cost and effect.

**Blessings** (the altar): three of six are offered after every return; the one lit lasts the next expedition. *Furia* +20% damage,
*Grazia* +30 maximum health, *Raccolto* resources ×1.5, *Provvidenza* power-ups drop twice as often, *Decima* +25% points from kills,
*Porte d'oro* doors cost 30% less.

**Balance.** The economy lab simulates a methodical player who clears nine waves and the boss of the deepest useful branch, extracts,
then builds whatever the store covers and buys missing commons at the market: with the current tables the first works are affordable
after one expedition and the whole cathedral after about 15 (boss kills 5, 4, 3 and 3). Nobody has played it yet: these numbers are a
starting point, not a tuning.

## Co-op and split screen

The cathedral belongs to a character (one each, [saves-and-characters.md](saves-and-characters.md)), and works, the market and blessings are used when
playing alone. In online co-op and in split screen the game starts in the catacombs as before and goes down level after level across the
branches (floors 1-3 are the Ossario, 4-6 the Teatro, ...). Everything picked up, and the boss trophies, go straight into the store of the
character that picked them (each machine's own online, each seat's own in split screen), with no risk.
Doors keep everybody's price in co-op (the host checks purchases). The boss of a branch shows its health bar only on the host and on
clients for the Cardinale; resources are drawn per machine, so no new network messages were needed.

## Code map

| Piece | Where |
| --- | --- |
| Engine-independent rules (ledger, constructions, market, loot, expedition) | `unity/PryingEyes/Packages/com.pryingeyes.economy` ([README](../../unity/PryingEyes/Packages/com.pryingeyes.economy/README.md)) |
| The catalog: resources, branches, works, blessings, supplies, market, drop tables | `Scripts/Cattedrale/Catalogo.cs` (no `UnityEngine`: the lab compiles it) |
| State, one per character, saved in the character's file (`persistentDataPath/characters/<id>.json`; the old `cattedrale.json` is imported once as the first character; `-profilo NAME` for another folder; automated runs use a test profile); suspending and resuming an expedition (`Riprendi`, `Sospendi`) | `Scripts/Cattedrale/Fabbrica.cs`, `Scripts/Characters/` |
| Modes: catacombs (default in the game; tests keep the Tower unless `-catacombe`/`-cattedrale`; `-torre` forces the Tower) and cathedral (alone) | `Scripts/Cattedrale/Catacombe.cs` |
| Drops on the floor, the satchel HUD | `Scripts/Cattedrale/RisorsaPickup.cs`, `HUD.Risorse.cs` |
| Construction sites, market objects, candles, gates, register | `Scripts/Cattedrale/HubInteract.cs` |
| The cathedral plan and furnishing | `Scripts/Proc/HubPlan.cs`, `Scripts/Proc/RoomKits.Hub.cs` |
| Hub, expeditions, return, death, remains, poor box | `Scripts/Proc/FloorDirector.Catacombe.cs` |
| Gates instead of lifts | `Scripts/Proc/ProcLift.cs` (`BuildGate`) |
| The boss of a branch | `Scripts/Proc/RoundDirector.Survival.cs`, `Enemy.MakeBranchBoss` |
| Bonuses: damage, health, prices, kill points, drop chance, the organ | `Weapon.cs`, `PlayerPerks.cs`, `RoomAccessState.cs`, `Perks.cs`, `GameManager.cs`, `Drops.cs`, `RoundDirector.Survival.cs` |

## Verification

- `dotnet run -c Release --project tools/economylab` (also `python tools/studio.py test economy` and the CI gate): 75 checks, every
  guarantee of the package (atomic payments, overflow, cycles, stock, deterministic loot within 2% of the expected mean over 200,000
  rolls, extraction and loss policy) and the catalog (every cost in known materials, every material obtainable, boss resources only from
  bosses, each branch gated by the previous boss, no market arbitrage, the death policy, the balance simulation above). Passed on
  17 November 2026.
- `PryingEyes.exe -batchmode -autotest -cattedraletest -nowaves -seed 1000 [-cattedraleshots]` (`Cattedrale/CattedraleTest.cs`):
  49 checks with the real controller and interactions on a test profile, passed on 17 November 2026 on a fresh build, with and without
  graphics. It starts in the cathedral (plan valid, ten rooms, square, roofless nave, 18 market offers, 5 gates, the sites, the register,
  no waves), buys holy water, builds the font and the vaults (the cathedral is rebuilt: the font in place, the nave roofed, the altar and
  organ sites appear), checks the Teatro gate is walled, goes down into the Ossario (catacomb plan, hotel theme, gates instead of lifts,
  750 starting points, holy water from the supply and the font), kills an enemy and picks up its drop into the satchel, uses the poor box,
  climbs the stair (satchel banked, points to oboli, pistol and no perks again), dies on a second descent (half the commons kept, the rest
  in the remains), picks the remains up on a third, then plays the third level: three waves, the Cardinale as the branch boss, the gate,
  the trophy already in the store, the gate leading up, the return. Finally the save is reloaded and compared. With `-cattedraleshots`
  it writes photos to `hotel_build/cattedrale/` (git-ignored).
- `python tools/check_suite.py` and the Python unit tests pass with the new package, lab and suite.
- With the characters (18 November 2026, 0.9): `-cattedraletest` is 72 checks, 72 passed. The 23 new ones are the suspended expedition (equipment, points,
  satchel, an area bought, the file consumed, no extra descent) and the abandoned one; what they check is in
  [saves-and-characters.md](saves-and-characters.md#verification).
- The fourth branch with the regenerated scene kit (17 November 2026, a full `BuildPlayer.Torre` build): `-catacombe -floor 10` builds the
  first level of the branch with the Guardaroba theme and the 17 `gu_*` props as models (`-perfprops`, seed 3068: all 17 photographed, none a
  grey box; photos in [guardaroba.md](guardaroba.md#verification)). A control player built from the old scene shows grey boxes when the
  theme is forced and, in the catacombs, falls back to another theme (the theatre on the first level). The strict navigation bot,
  `python tools/check_navigation.py --extra=-catacombe --floor 10 --floors 2`, walks the first two levels of the branch: 6 of 6 seeds
  (1000, 2961, 3046, 777, 114, 4242), 2 of 2 levels each, every level `tema=Guardaroba`, 0 warnings, 8 doors bought per level.
  `-cattedraletest` stayed 49/49 on that build. `-survivaltest` failed most runs (10 of 14), on the old scene as much as on the new
  one (a random Prova and the burst of an Appestato, not the kit); it is deterministic since, see
  [../engineering/testing.md](../engineering/testing.md).

Not verified: a human playthrough (pace, prices, the balance), the cathedral with a controller, the LayoutDemo consumer
(`EconomyDemo.cs` and its five checks in `DistrictDemo.cs` were written but that project was not opened), and co-op and split screen with
the catacombs (the code paths are shared with the waves and were not run).
