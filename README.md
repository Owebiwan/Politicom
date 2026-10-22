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
passive            0%      100%     0% │    52    53      8    82    49 │    35
educator           1%       99%     0% │    53    54      9    85    50 │    29
centralizer        0%        0%   100% │    54    53      6    51    41 │   100
balanced          61%       39%     0% │    74    53     12    77    54 │    37
trader            60%       39%     1% │    76    54     11    72    53 │    50
```

## What the player sees

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
- **Paris:** it helps (39% survival vs 28%). In the short siege the arms come in time and the drift doesn't.
- **Catalonia:** it pays off in a different way. Survival is the same, but final drift is lower
  (54 vs 69), because better-armed fronts mean fewer emergencies and less time with a war committee.
- **Free Territory:** the drift piles up over the long war, and the trader becomes a state 61% of the time.

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
passive           29%       71%     0% │    67    50     17    65    49 │    67
educator          93%        7%     0% │    66    51     22    73    53 │    61
centralizer        0%        0%   100% │    36    50      5    52    36 │   100
balanced          99%        1%     0% │    86    51     19    68    56 │    69
trader            99%        0%     1% │    84    52     19    74    57 │    54
```

The war follows the historical shape: with nobody playing, the front mostly holds through
1937 and collapses in the Aragon (March 1938) and Catalonia (December 1938) offensives.
A passive player also lets every historical event take its historical course, which is
why it fares so badly.

The Free Territory (`--scenario free_territory_1918`) is harsher and plays differently:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            0%      100%     0% │    63    50     17    86    54 │    33
educator           0%      100%     0% │    64    51     17    90    55 │    23
centralizer        0%        0%   100% │    34    48      4    53    35 │   100
balanced          73%       12%    15% │    85    49     19    73    56 │    63
trader            25%       17%    58% │    80    49     16    65    53 │    78
```

There, refusing to centralize at all means being crushed by the final Red offensive, but
with the highest freedom. The educator's total score nearly ties the balanced player's.

Paris (`--scenario paris_commune_1871`) is the tragic one:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            0%      100%     0% │    83    67     11    91    63 │    27
educator           0%      100%     0% │    83    67     11    96    64 │     8
centralizer        0%        0%   100% │    83    64      8    51    51 │   100
balanced          28%       72%     0% │    96    68     11    93    67 │    17
trader            39%       61%     0% │    96    68     11    90    66 │    24
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
