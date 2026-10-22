# Politicom

A prototype of the anarchist grand strategy game: you are the movement, not the state.
It covers the prototype scope from the design doc: the trust network, voluntary projects,
hierarchy drift with thresholds and brakes, both education tracks, double-edged technologies,
hostile states, and an "ally" that offers arms in exchange for centralization.

## Scenarios

| Scenario | Communes | Enemies | What it tests |
|---|---|---|---|
| Revolutionary Catalonia, 1936–39 | 30 | Nationalist Army | Running an industrial economy through collectives while the front slowly gives way |
| Free Territory, Ukraine, 1918–21 | 22 | White Army; Red Army (an ally until it isn't) | Militia war on several fronts, and the pull toward a single command |
| The Paris Commune, 1871 | 20 | Versailles army | A siege with almost no time to organize; each turn is a day |
| Sandbox | 20–40 | 1–2, generated | A new alternate-history region from a seed |

The Godot intro screen lets you choose one. In the terminal, pass `--scenario free_territory_1918`
or `--scenario paris_commune_1871`. Scenarios are pure JSON in `Data/scenarios/`, and can set:
- **Enemies:** any number, each with its own front, offensives and map colour.
- **`daysPerTick`:** the turn length. Paris uses 1, so its turns are days.
- **`foodImports` / `goodsImports`:** supplies from outside each turn. Paris was fed through the
  Prussian lines; Catalonia imported grain it couldn't grow for Barcelona's factories.
- **`warPace`:** how fast front communes are lost or regained. Paris uses 5 for street fighting.

## Layout

| Folder | What it is |
|---|---|
| `Sim/` | The simulation: plain C# (net8.0), with no engine dependency, deterministic per seed |
| `Data/` | Scenario and technologies as JSON (designers edit these, not code) |
| `Sim.Cli/` | `politicom` command: play in the terminal, or run headless batches for balancing |
| `Sim.Tests/` | xUnit tests, including a regression test for the core design dilemma |
| `Data/world/` | The world map of each era (built by `tools/geo/build_world.py` from Natural Earth and `tools/geo/eras/`) |
| `Data/geo/`, `tools/geo/` | Real map data per scenario, and the script that builds it (Natural Earth; Paris boundaries ODbL, see `Data/geo/ATTRIBUTION.md`) |
| `Game/` | Godot 4 (.NET) front end: map, trust graph, drift meter, actions |

## Quick start

```sh
dotnet test                                     # 42 tests
dotnet run --project Sim.Cli -- play            # play in the terminal (type `help`)
dotnet run --project Sim.Cli -c Release -- batch --runs 300
```

### Godot front end

1. Install **Godot 4.7+ .NET edition** (`brew install --cask godot-mono`, or from godotengine.org).
2. Open `Game/project.godot`, press **Build**, then **Run**.

Controls:
- Click a commune to select it; shift-click a second commune for mediation or railways.
- Space pauses, 1–3 set the speed, and `.` advances one week.
- Scroll or pinch to zoom, drag or two-finger scroll to pan, and double-click empty space to reset the view.
- The game pauses itself when a decision comes up.

If your Godot version differs from 4.7.2, Godot will offer to update the `Godot.NET.Sdk`
version in `Game/Politicom.Game.csproj`. Accept it.

## Sandbox

`Sim/Sandbox/SandboxGenerator.cs` builds an alternate-history region from a seed:
- **The map:** fictional communes on a map, with one or two cities, towns around them and
  farming villages elsewhere.
- **Values:** spread out from a few ideological centres.
- **Enemies:** one or two, each with its own front, colour and offensives. Sometimes the
  second starts as an ally and turns on you.
- **The rest:** maybe an ally offering arms, and five events drawn from the templates in
  `Data/sandbox/event_templates.json`.

It returns an ordinary scenario, so play, batch runs, the AI players and saves all work
unchanged. A sandbox save carries its own map.

**Calibration.** Generated maps differ wildly in how defensible they are. So after building
one, the generator plays a few quick games with the built-in balanced player, and bisects a
strength multiplier for the enemies until that player survives about 70% of the time. This
takes about half a second and is deterministic. Without it, the balanced player's survival
ranged from 0% to 100% across maps. With it, every map lands between about 30% and 70%.

- **Godot:** the scenario picker has a Sandbox row with a seed, a Random button and a size
  (20–40 communes). The header and results screen show the seed, so a good map can be shared.
- **Terminal:** `--scenario sandbox:42` (or `sandbox:42:32` for 32 communes). A bare
  `--scenario sandbox` uses `--seed`, and in `batch` generates a fresh map for every game:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            0%      100%     0% │    51    53      5    82    48 │    33
educator           1%       99%     0% │    52    54      6    85    49 │    28
centralizer        0%        0%   100% │    53    53      4    50    40 │   100
balanced          66%       34%     0% │    74    53      8    77    53 │    37
trader            65%       35%     1% │    75    53      8    72    52 │    47
```

## What the player sees

- **The map:** each historical scenario is drawn on real geography: sea, coastlines, rivers
  and (for Catalonia) the French border. Communes sit at their true positions. Each commune's
  territory is its share of the land, clipped to the coast and to the provinces the
  federation holds. Catalonia fills its real provinces out to the Pyrenees, while the Aragon
  front and the Free Territory's edges are drawn as a smooth front line. Paris
  uses the real arrondissement boundaries. The territories are coloured by the map mode over a
  tint for their land: farmland, mixed or industry. Generated sandbox maps get invented geography of the
  same kind: a wandering coastline away from the enemy (or none, for a landlocked region),
  rivers (one through the largest city), lakes and provinces. Dense clusters such as Barcelona show their biggest
  labels first; zoom in for the rest.
  - Occupied land takes the occupier's colour, and front borders are drawn in the enemy's colour.
  - A small bar under each front commune's name shows how much of it is still held.
  - Seceded communes are greyed with a dashed border, and the selection is outlined in gold.
  - Trust links are an optional overlay (T).
  - The view fits itself to the territories, and you can zoom and pan.
- **Railways:** Catalonia and the Free Territory start with their historical main lines
  (approximate routes), and sandbox maps generate a network. A finished railway project adds
  a new line. Communes joined by rail send each other more aid (×1.15) and militia support
  (×1.25), and ideas pass between them faster (×1.3). A line into occupied land is shown cut.
- **Armies:** the war is simulated as pressure, not units, so the map draws that pressure as
  armies.
  - Each enemy's army (a box in its colour, sized and labelled by its strength) stands on its
    side of each front commune.
  - When a commune falls, the armies come out of it and slide forward, so the front visibly
    advances. During an offensive they pulse and point at their target.
  - The federation's militia appear as black-and-red flags sized by their defense, and
    defensive campaigns send columns marching from the communes taking part to the front.
- **A daily clock:** turns are a week long, but while the clock runs the date ticks forward a
  day at a time at the same overall pace, and the turn resolves on its last day.
- **The world:** the historical scenarios are played on a world map of their era (1871, 1918
  or 1936).
  - **Zooming:** from your communes out to the whole planet; the World button or W jumps
    between the two.
  - **At world zoom:** every period state is drawn with its borders and name, and a pulsing
    ring marks your federation. The Ideology map mode colours states by regime.
  - **Accuracy:** the borders are approximate. They're today's countries grouped into the
    states of the time, with colonies folded into their empires (`tools/geo/eras/`). For
    example, interwar Poland is drawn with today's borders.
  - **Coming next:** rival revolutionary states.
- **The outside world:** every period state has a stance toward the federation, from −1
  (hostile) to +1 (friendly). It starts from its ideology and the scenario's history, for
  example Germany and Italy hostile and Mexico friendly in 1936.
  - **What moves it:** order in the federation reassures governments and disappoints
    sympathizers; foreign trade warms liberal states; a state whose own workers radicalize
    fears your example.
  - **What it leads to:**
    - below −0.30: an embargo, which sets the trade embargo
    - above +0.35: arms sales to the front
    - above +0.50: volunteers
    - below −0.60: intervention, adding to the enemy it backs (up to +60% of that enemy's strength)
  - **Weight:** each state's weight is its power, scaled down by distance.
  - **Volunteers** also come from radicalized workers anywhere, like the International Brigades.
  - **Your diplomacy:** *send delegates* (warm a state; fascists barely listen), *agitate
    workers* (radicalize it; its government resents it) and an *international appeal* (double
    the volunteers for a while).
  - **Where to see it:** the World tab, the Diplomacy and Revolution map modes, clicking any
    country, or `world` / `delegates` / `agitate` / `appeal` in the terminal.
- **Revolutions abroad:** your contagion radicalizes every state's workers, more strongly
  next door and next to a sister federation.
  - **Risings:** above 55% a rising can break out, and the clock stops. It's decided over 8
    turns: radical workers win; powerful, repressive states hold. You can send support from
    the World tab.
  - **If it's crushed:** radicalization collapses and repression hardens.
  - **If it wins:** a party may seize power instead. That's likelier the more centralized
    your own federation has become, because revolutions copy what they see.
  - **A libertarian victory** makes a sister federation, which sends arms and volunteers and
    later offers to **federate**. Its largest cities then join your federation as communes,
    on the map in their own country, so the federation spreads across the world.
  - **Spread** now counts the world's workers and revolutions won.
  - **How often:** these happen in long games. In 500-week Catalonia, an education-focused
    player sees about two revolutions win per game.
- **Wars between states:** history starts some wars, and others break out in play.
  - **Scripted:** the Sino-Japanese War (1937) and the Second World War (September 1939) in
    long Catalonia games; the Polish–Soviet, Hungarian–Romanian and Greco-Turkish wars after 1918.
  - **Random:** wars break out between hostile neighbours, more often between opposed regimes,
    and like-minded powers may join. Free federations never start them.
  - **Effects:** borders don't change, but a state at war can spare only 40% of its strength
    for intervening against you, and war weariness radicalizes its workers, so revolutions
    follow wars.
  - **Display:** wars appear as red dashed lines between capitals on the world map, in
    country tooltips and in the World tab.
  - **Generated maps** get 3–5 neighbouring countries, and a neighbour on an enemy's side can
    back that enemy.
- **The sidebar:** the clock, influence, the drift meter and any decision waiting on you are
  always visible. Everything else is in four tabs: Overview, Movement, Commune and Log.
  Clicking a commune opens its tab.
- **The theme:** one dark theme throughout (`Game/scripts/UiTheme.cs`), with a red accent for
  the movement.
- **Intro screen:** the situation, the dilemma, how you win, and the controls.
- **Drift meter:** split into the four stage zones, with the threshold where a state forms.
  Below it, last week's drift is broken down by cause.
- **History chart ("So far"):** drift (coloured by stage), communes still federated, and
  mean trust, with the enemy offensives shaded.
- **Results screen:** the outcome, the four scores, and the full history chart, with
  options to keep looking or start a new game.
- **Commune list** (the Communes button, or C): a sortable table of every commune's trust,
  wellbeing, education, discontent and status. Click a row to select it and centre the map on it.
- **Toasts:** brief pop-ups on the map for falls, offensives and decisions.
- **Tips:** one-time hints the first time something important happens, such as the front
  losing ground, famine, the expert trap, a commune voting to leave, the first drift stage,
  or influence full and unspent.
- **Pauses:** the clock stops for decisions, for a warning 3 points before each drift
  threshold (once per threshold), and when a new stage forms, so the key moments aren't missed.
- **Project pace:** each project shows when it will finish at the current pace, marked LATE
  if that misses its deadline. A project at 75% or more when its deadline arrives gets one
  extension instead of collapsing. Failures say why: too few communes committed, or too much
  effort lost to coordination.
- **Tooltips:** hover the drift meter, influence or scores for an explanation. Esc closes the
  results screen and the commune list.

## Foreign trade and embargoes

From the design doc's perks table: internal exchange runs on mutual aid, but trading with
capitalist states needs currency, which brings markets and accounts back in.

- **Opening trade:** use the **Foreign trade** toggle (`trade on` in the terminal). Each turn,
  part of every commune's grain surplus is sold abroad, and the goods bought go to the front first.
- **The cost:** drift every turn from the trade bureau and its accounts.
- **Embargoes:** as the idea spreads behind enemy lines, more of the world refuses to trade.
  The embargo is 1.5 × mean radicalization, capped at 90%. Spread and trade pull against each other.
- **What arms are worth:** a front commune's defense rises with its arms stockpile, up to +15%,
  so goods from trade, the ally or events actually help hold the line.

Whether trade pays depends on the scenario (compare `balanced` and `trader` in the tables
below):
- **Paris:** it's roughly even (30% survival vs 29%). The arms arrive in time and the drift has little time to build.
- **Catalonia:** it pays off in a different way. Survival is the same, but final drift is lower
  (50 vs 59), because better-armed fronts mean fewer emergencies and less time with a war committee.
- **Free Territory:** the drift piles up over the long war, and the trader becomes a state 61% of the time.

## The tech tree

28 technologies sit in five lines of development (Organizational, Agricultural, Industrial,
Military, Communication), in chains of two to four steps. Some need two lines at once:
- **Heavy industry:** war industry and electrification.
- **General staff:** mobile columns and war industry.

Every line has an **efficient path** that adds drift and a **free path** that brakes it at
some cost:

| Line | Free path |
|---|---|
| Organizational | Imperative mandate, rotating delegates |
| Agricultural | Communal land councils |
| Industrial | Workers' control |
| Military | Elected officers |
| Communication | Wall newspapers |

A commune on a line's free path resists that line's centralizing technologies: it discovers
and adopts them at about a third of the usual rate (`PathResistance`). So circulating a brake
early decides which way a line develops.

Almost every technology cuts both ways, adding drift or slowing decisions. The data is in
`Data/technologies.json`.

The movement can't choose research:
- **Discovery:** communes discover ideas from their own education, needs and values.
- **Spread:** ideas pass along trust links to neighbours with half the education needed.
- **Circulate:** the only lever is to circulate an idea so it spreads faster.

Communes with little education stay on the first step, so the deeper tree opens up through
schooling.

**In Godot,** the **Technologies** button (or K) opens the tree:
- **Nodes:** each shows how widely the idea has spread, whether it adds drift (▲) or brakes
  it (▼), and the education it needs. Locked nodes are ideas no commune can reach yet.
- **Chains:** hover or click a node to light up the chain leading to it.
- **Details:** pointing at a node describes it in the side column: perk, risk, effects in
  plain language, who has it and what it leads to. Clicking keeps it there, with a
  **Circulate** button.

**In the terminal,** `techs` lists the tree by category and depth.

## Game length

Each scenario runs to its historical end by default, but the start screen (or `--length N` in
the terminal) lets you play up to 500 turns. After the scripted history the war goes on:
- **Renewed offensives:** every enemy launches them, starting from its average offensive and
  growing slowly. Its steady growth stops, so it rebuilds only through these offensives.
- **Drift keeps building:** over a long game ideas spread everywhere, so the free paths matter.

Long games are hard:
- **Catalonia, 500 weeks:** the balanced player survives about 59% of games, the educator 76%.
- **Free Territory:** under 10%.
- **Paris:** no AI player has outlasted Bloody Week, which in the historical game ends on its
  final day. A longer game asks whether you can.
- **Sandbox:** maps are calibrated at the chosen length.

## Historical events

Catalonia has six scripted decisions in its `events` list:
- joining the Generalitat (September 1936)
- Durruti's death (November 1936)
- the May Days (May 1937)
- the dissolution of the Council of Aragon (August 1937)
- the SIM's offer of its services (September 1937)
- the Battle of the Ebro (July 1938)

Paris has six, ending in the Committee of Public Safety vote and Bloody Week. The Free Territory has its own six: the Second Regional Congress, Trotsky's Order No. 1824,
Peregonovka, typhus, the Starobilsk agreement, and the final betrayal.

Each has options with effects such as drift, influence, wellbeing, trust, enemy strength,
arms and secession. If the movement doesn't answer in time, the `defaultOption` happens;
it's usually what happened historically. New events are pure JSON (see `Sim/Model/Events.cs`
for the effect fields).

## Saving and loading

- **Terminal:** `save [file]` and `load [file]` while playing, or `politicom play --load FILE`.
- **Godot:** the Save and Load buttons use one quick-save slot in Godot's user data folder.

A save holds the full state, including the random-number generator, so a loaded game plays
out exactly as it would have. Tests check this at three points in the game.

## Exporting a standalone build

`Data/` is mirrored into `Game/data/` on every build and packed into exports, so the app
needs nothing outside itself.

1. Install the macOS export template once. In Godot, use **Editor → Manage Export Templates**.
   Or copy `templates/macos.zip` and `templates/version.txt` from the official
   `Godot_v4.7.2-stable_mono_export_templates.tpz` into
   `~/Library/Application Support/Godot/export_templates/4.7.2.stable.mono/`. Only the macOS
   template (124 MB) is needed, not the whole 1.2 GB bundle.
2. Export:
   `godot --headless --path Game --export-release macOS ../build/Politicom.zip`,
   then unzip it to get `Politicom.app`. It's a universal build for Apple Silicon and Intel.

The app is unsigned. It runs normally on the Mac that built it. Anyone you send it to must
right-click it and choose **Open** the first time, or macOS will refuse to start it.

Two project settings the export needs, already in place:
- `Game/Politicom.Game.sln`, so Godot can build the C# code.
- ETC2/ASTC texture import, required for Apple Silicon.

## Balancing with batches

Every number lives in `Sim/Balance.cs`. Scripted players (`Sim/Ai/Policies.cs`) play
hundreds of games in seconds:

```sh
politicom batch --runs 300                                   # compare all policies
politicom batch --sweep CoordinationExponent=1.0,1.3,1.6     # answer an open question with data
politicom batch --set PoliticalBrake=0.3 --out results.csv   # try a change and export the runs
politicom run --policy balanced --seed 7 --timeseries ts.csv # one game, week by week
politicom params                                             # list every parameter
```

The four policies:
- **passive**: does nothing.
- **educator**: political education, mediation and recall; never centralizes.
- **centralizer**: war committee, government arms, the statistics bureau.
- **balanced**: centralizes only in emergencies and recalls delegates before thresholds.
- **trader**: the balanced player, but it opens foreign trade whenever drift has room.

The current defaults produce the dilemma the doc asks for:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive           25%       75%     0% │    62    49      9    67    47 │    59
educator          96%        4%     0% │    65    51     13    77    52 │    39
centralizer        0%        0%   100% │    36    50      3    52    35 │   100
balanced         100%        0%     0% │    83    52     12    80    57 │    31
trader            99%        0%     0% │    85    52     12    74    56 │    47
```

The war follows the historical shape: with nobody playing, the front mostly holds through
1937 and collapses in the Aragon (March 1938) and Catalonia (December 1938) offensives.
A passive player also lets every historical event take its historical course, which is
why it fares so badly.

The Free Territory (`--scenario free_territory_1918`) is harsher and plays differently:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            0%      100%     0% │    67    49     10    86    53 │    33
educator           0%      100%     0% │    70    50     11    90    55 │    21
centralizer        0%        0%   100% │    34    48      2    54    34 │   100
balanced          79%        3%    18% │    86    49     11    70    54 │    67
trader            42%        8%    50% │    81    49     10    64    51 │    77
```

There, refusing to centralize at all means being crushed by the final Red offensive, but
with the highest freedom. The educator's total score nearly ties the balanced player's.

Paris (`--scenario paris_commune_1871`) is the tragic one:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            0%      100%     0% │    82    67      7    91    62 │    26
educator           0%      100%     0% │    80    67      7    96    62 │     7
centralizer        0%        0%   100% │    83    64      5    51    51 │   100
balanced          30%       70%     0% │    96    68      7    93    66 │    16
trader            48%       52%     0% │    96    68      7    90    65 │    23
```

The front holds through April and early May, and Bloody Week (from 21 May) breaks it in
days. A war committee at the right moment gives about a 30% chance of outlasting history. Winning
the war outright means becoming a state.

In the Catalonia table, centralizing wins the war and loses the revolution, doing nothing loses
the war, and playing the dilemma well scores best while living close to the edge.

## How the open design questions are answered (for now)

| Question | Prototype answer | Where |
|---|---|---|
| How is influence earned? | Base income plus mean political education plus wellbeing plus press and ateneo technologies; capped at 100 | `InfluenceSystem` |
| Coordination cost formula | `f = k·(n−1)^e`, then multiplied by political education, communication technology, drift stage and war committee modifiers; cost = `f/(1+f)` | `Coordination.Cost` |
| Drift thresholds, reversible? | 4 stages (25/50/75/100). Each one reverts only after drift falls 10 below its threshold; the State stage never reverts | `DriftTracker` |
| How communes join projects | A utility score: value fit, trust in partners, own benefit, education, hunger, inertia, persuasion | `ProjectSystem.Utility` |
| Free-riding without coercion | Decaying aid ledgers. Free riders lose neighbours' trust; labor-time accounting and political education reduce it. Police reduce it too, at a price | `EconomySystem.Ledgers` |
| Making the trust graph readable | Edge width and colour show trust, rings show front, secession and selection, and map modes switch between trust, education, values and wellbeing | `Game/scripts/MapView.cs` |

All of these are first guesses, built so they can be tuned with `--sweep`.
