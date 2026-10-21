# Politicom

A prototype of the anarchist grand strategy game: you are the movement, not the state.
It covers the prototype scope from the design doc. That is one region (30 communes in
Catalonia and Aragon, 1936–39), the trust network, voluntary projects, hierarchy drift with
thresholds and brakes, both education tracks, double-edged technologies, and one hostile
state plus an "ally" that offers arms in exchange for centralization.

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
dotnet test                                     # 24 tests
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

## What the player sees

- **Intro screen:** the situation, the dilemma, how you win, and the controls.
- **Drift meter:** split into the four stage zones, with the threshold where a state forms.
  Below it, last week's drift is broken down by cause.
- **History chart ("So far"):** drift (coloured by stage), communes still federated, and
  mean trust, with the enemy offensives shaded.
- **Results screen:** the outcome, the four scores, and the full history chart, with
  options to keep looking or start a new game.

## Historical events

The scenario has six scripted decisions in its `events` list:
- joining the Generalitat (September 1936)
- Durruti's death (November 1936)
- the May Days (May 1937)
- the dissolution of the Council of Aragon (August 1937)
- the SIM's offer of its services (September 1937)
- the Battle of the Ebro (July 1938)

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

`Data/` is mirrored into `Game/data/` on every build and packed into exports. To export:
1. In Godot, go to **Editor → Manage Export Templates** and download the templates for your
   version (about 1 GB).
2. Run **Project → Export → macOS** (or
   `godot --headless --path Game --export-release macOS ../build/Politicom.zip`).

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

The current defaults produce the dilemma the doc asks for:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive           11%       89%     0% │    62    49     15    67    48 │    64
educator          85%       15%     0% │    65    50     22    73    53 │    62
centralizer        0%        0%   100% │    40    49      6    52    37 │   100
balanced          99%        0%     1% │    87    50     19    64    55 │    79
```

The war follows the historical shape: with nobody playing, the front mostly holds through
1937 and collapses in the Aragon (March 1938) and Catalonia (December 1938) offensives.
A passive player also lets every historical event take its historical course, which is
why it fares so badly.

In this table, centralizing wins the war and loses the revolution, doing nothing loses
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
