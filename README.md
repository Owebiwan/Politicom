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

The game opens on a start menu: continue the latest save, choose a historical revolution, or
start in the open world. Esc brings the menu back during a game.

**Tutorial** (start menu): a guided first game in Revolutionary Catalonia. A panel at the
top left takes you through ten steps (the movement, communes, hierarchy drift, schools and
the expert trap, running the clock, the front and defensive campaigns, trust and mediation,
technologies, the ledger), each moving on when you've done what it asks. You can leave it at
any time and keep playing (`Game/scripts/Main.Tutorial.cs`).

Controls:
- Click a commune to select it; shift-click a second commune for mediation or railways.
- Space pauses, 1–3 set the speed, and `.` advances one week.
- Scroll (mouse wheel, Magic Mouse or trackpad) or pinch to zoom at the pointer. Drag with any button to move the map; a click without moving selects. Double-click empty space to reset the view.
- The game pauses itself when a decision comes up.

If your Godot version differs from 4.7.2, Godot will offer to update the `Godot.NET.Sdk`
version in `Game/Politicom.Game.csproj`. Accept it.

## Sandbox

Sandbox games are no longer offered in the game (old sandbox saves still load). The generator
stays: open-world starts use its event templates and its difficulty calibration, and the
terminal and tests can still play `sandbox:SEED` maps.

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
passive            0%      100%     0% │    51    53      5    82    48 │    34
educator           1%       99%     0% │    52    54      6    85    49 │    28
centralizer        0%        0%   100% │    51    53      4    51    40 │   100
balanced          65%       35%     0% │    74    53      8    77    53 │    38
trader            65%       34%     1% │    75    53      8    72    52 │    47
```

## Open world

You can start the movement in any country, in any of 16 eras from 500 BC to AD 2000:
500 BC, AD 1, 500, 800, 1000, 1200, 1400, 1500, 1650, 1783, 1815, 1871, 1918, 1936, 1960 and
2000. This is alternate history, with an anarchist movement rising wherever and whenever you
choose.

- **Maps:** 1871, 1918 and 1936 are hand-made from today's countries (Natural Earth). The
  other eras come from **historical-basemaps (GPL-3.0)**, so a build that ships them is under
  GPL terms; see `Data/geo/ATTRIBUTION.md`.
- **Cities:** they carry modern names everywhere, so the communes of Rome in AD 1 are Milan,
  Lyon and Lisbon.
- **Earlier regimes:** the early eras add three: **empires** (they conquer, and fight each
  other), **theocracies** (most hostile to the movement, their faith the strongest rival
  culture) and **stateless peoples** (they neither blockade nor send armies, and are
  sometimes sympathetic).
- **Dates:** years before Christ count down ("July 500 BC"). Technology eras follow the
  calendar, so in 500 BC the tree is the ancient one.

**Each age plays like itself:**
- **The enemy:** it's named for its age: the imperial army of the Roman Empire, the royal
  host of France, the holy army of a theocracy, the war bands of a stateless people. From 1789
  on, it's government forces. It's sized to the rising it faces, so a handful of small towns
  don't meet a whole country's army.
- **The rising:** it's told in the age's terms: slaves and craftsmen in antiquity, a rising
  of the commons against the lords in the Middle Ages, the commons against the crown in the
  early modern age.
- **Dilemmas:** event templates carry an era range, and ones made for the map's age come up
  twice as often:
  - **Antiquity:** temple granaries, freeing the slaves, the debt tablets, an oracle, a
    mutinous cohort.
  - **Middle Ages:** a crusade, enclosure of the commons, heretics, the guild masters, plague.
  - **Early modern:** the Inquisition, colonial levies, a printing press, mercenaries.
  - **After 1945:** the television station, the welfare state's offer, the bomb.
  - **Modern dilemmas:** ministries, the bank and committees of public safety appear only
    from their own times.
- **Revolutions abroad:** before 1500 an authoritarian revolution makes a Brotherhood of the
  elect; before 1789, a Commonwealth of saints; after that, a Workers' Republic.
- **Cultural works:** storytellers before print, broadsheets before newspapers.
- **Communication:** ideas travel at 60% speed before the printing press (1450) and 80%
  before 1800, slowing both contagion and the spread of techniques.

- **Communes:** the country's largest cities on its home territory, up to 26. The largest are
  industrial and the rest are mixed or agrarian.
- **Commune values:** seeded from the regime. Education is lower in earlier eras.
- **The enemy:** the country's own government, holding the capital. The communes nearest the
  capital start on the front.
- **Backers:** hostile neighbours can back the government.
- **The rest of the world:** plays on as usual.
- **Difficulty:** calibrated like a sandbox map.
- **Which countries:** any with at least 6 cities on the map besides the capital. There are
  dozens per era.
- **Whole country or a regional rising:** both are offered.
  - **Regional rising** (the default in the game): the movement holds a stronghold, usually far from the capital, and
    the 9 cities nearest it; the government holds the rest of the country.
  - **Joining a regional rising:** the country's other towns (up to 30) watch, and may rise
    and join the federation, one at a time. That's likelier near the federation
    (`RisingReachKm`), when the enemy's own workers are radicalized, and where the movement's
    culture runs deep, and less likely as drift grows. A risen town gets its own territory
    on the map.
  - **Whole country:** the movement holds the country's largest cities, and the government
    holds only the capital.
  - **Terminal:** add `:region` to the id, e.g. `open:1500:FRANCE:2:region`.
- **Revolutions abroad** are separate. Other countries' workers can rise too; a libertarian
  victory makes a sister federation that may offer to federate, and its cities then join yours.
- **Godot:** use the Open world row in the scenario picker. Choose an era, a country, a seed and a
  length, or press **On the map…** to choose the country on a world map of the era:
  - **Hover** a country to see its regime, capital and the cities the movement would hold.
  - **Click** a country in colour to choose it, then **Play**. Grey countries have too few
    cities to play.
  - **Change era** from the same screen; zoom and drag as on the main map.
- **Terminal:** `--scenario open:1936:ARG` (or `open:1936:ARG:7` for seed 7), and
  `open:-500:ACHAEMENID` or `open:1:ROMAN` for the early eras. A state's id is in
  `Data/world/<era>.json`.
- **Calibration:** the enemy's strength is set to the strongest level at which the balanced AI
  survives at least 55% of trial games: 20 games for open starts, because real maps vary more
  than generated ones, and 12 for sandbox maps. Generation takes about 4 seconds.
  Individual maps still vary, roughly 30–90%.

Balanced AI over 100 games each. The centralizer becomes a state in (almost) every game.

| Start                    | survived | destroyed |
|--------------------------|---------:|----------:|
| Greek city-states 500 BC |      32% |       68% |
| Roman Empire AD 1        |      80% |       20% |
| Han AD 1                 |      46% |       54% |
| France 1200              |      50% |       50% |
| Inca Empire 1500         |      89% |       11% |
| France 1650              |      58% |       42% |
| Russian Empire 1815      |      76% |       24% |
| Argentina 1936           |      70% |       30% |
| Brazil 2000              |      65% |       35% |

The sandbox (fresh maps) averages 70%.

## Water, terrain and the paper chart

The map is modelled on Victoria 3's:
- **The sea:** an animated shader (`Game/shaders/sea.gdshader`) draws ripples that drift and
  catch the light, shallow teal near the coast deepening to navy offshore, and broken foam
  where it meets the land. The water is anchored to longitude and latitude, so it stays put
  as you pan and zoom.
- **The ground:** the land is the real Earth, from NASA's Blue Marble (July 2004, about
  1.85 km a pixel), graded toward a painted map and hill-shaded from elevation, so forests,
  steppe, desert and mountains show. Countries and communes are a tint over it that keeps
  the ground's light and shade (`Game/shaders/land.gdshader`), drawn by a land layer behind
  the map. Around the region the full-resolution tiles are joined into one texture; elsewhere
  a world overview is used. It switches off with Relief; generated and city maps keep flat
  colours.
- **Borders:** a broad soft shadow under a firm dark line with a light edge.
- **Grade, vignette and clouds:** a light colour grade and vignette over the map, and drifting
  clouds with their shadows as the view widens, clearing for the paper chart
  (`Game/shaders/grade.gdshader`).
- **The paper chart:** zoomed out toward the whole world, the sea turns to parchment with
  engraved lines following the coasts, and the countries become watercolour on paper.
- **Inland maps:** Paris sits on land, not sea.
- **Country names:** names run across their territory in spaced capitals on a straight line,
  as on Victoria 3's map, along the longest line that stays inside the country (level where
  there's room, steeper for tall, thin countries such as Chile or Norway), sized to fit it;
  a name with no room is left out rather than spilling over the borders. They're cream over
  the sea and brown ink on the paper chart.
- **Close-up ground:** zoomed in, the land gains detail the satellite picture lacks: a
  patchwork of fields in strips with furrows and hedgerows on open farmland, a dappled canopy
  in woods, grain everywhere. Commune zones tint the ground lightly enough to show it.
- **Data:** `tools/geo/build_sea.py` builds the coast-distance field and the land-cover texture
  in `Game/map/` from Natural Earth (public domain); `tools/geo/build_land.py` builds the
  ground (`Data/land/`, mirrored into the game) from NASA's Blue Marble and its elevation (public domain).

## The 3D map

The map is three-dimensional, as Victoria 3's is (`Game/scripts/Map3D.cs`,
`Game/shaders/terrain3d.gdshader`):
- **Ground:** the flat map (`MapView`) is drawn off-screen and laid over a grid raised by the
  real elevation (NASA's GEBCO-derived elevation, `Data/land/height.land`), mountains
  exaggerated about sevenfold and lit from the north-west.
- **Camera:** a perspective camera looks straight down over the world and tilts toward the
  horizon (up to 38°) as you zoom in, the distance fading into haze. Settings → "Tilt the map
  as you zoom in" turns the tilt off.
- **Resolution:** the 3D view is rendered at the screen's own pixels (up to twice the
  1600 x 900 layout, about 170 MB of memory; three times was barely sharper for 400 MB more),
  with 2x anti-aliasing, and the flat map draped over it is drawn to match.
- **How it fits together:** the flat map keeps its size and behaviour; it sits at the foot of
  a larger canvas (the area the tilted camera can see) and draws beyond its own edges into
  it. Pointer input is cast onto the ground and handed to the flat map in its coordinates, so
  clicking, dragging, scrolling and pinching work as before. The legend, banner and tooltips
  are drawn flat on an overlay, and the colour grade, vignette and falling snow lie over the
  screen.

### Models on the 3D map

Close in, towns, armies and militia are 3D models standing on the raised ground
(`Game/scripts/Map3D.Objects.cs`, models in `Game/models/`):
- **Towns** are built from the flat map's plan for each commune, out of buildings made for
  Politicom in Blender by `tools/models/build_buildings.py` (`Game/models/buildings/`), four
  houses for each age, two smaller and two taller:
  - **antiquity:** Roman houses with low hipped tile roofs, a portico or a pergola of vines;
    insulae over shops with wooden balconies; a temple on a podium ringed with columns.
  - **middle ages:** thatched cottages; timber-framed houses with jettied upper floors and steep
    gables; stone houses with crow-stepped gables; a church with aisles, buttresses, lancets, an
    apse and a spired west tower.
  - **early modern:** plastered houses with quoins, string courses and dormers; brick houses with
    curved gables to the street; tall houses with iron balconies; a domed baroque church
    between bell towers.
  - **industrial age:** brick terraces with chimney pots; workers' cottages; Haussmann
    apartments with mansard roofs and wrought-iron balconies; tenements over shops with
    awnings; a town hall with a clock tower; brick works with saw-tooth roof lights, a water
    tower and a tall chimney trailing smoke.
  - **twentieth century:** suburban houses with bay windows; white flat-roofed villas with
    ribbon windows; blocks of flats with concrete balconies; brick blocks over shops; office
    and department-store blocks; a people's house with a clock tower; concrete works with a
    silo.
  - **occupied towns** are ruins: broken walls, fallen beams and rubble, in stone or brick.
  Walls and roofs take the town's colours; everything else keeps its own, baked into the vertex
  colours with ambient occlusion, so a building draws in three calls. Sunlight from the
  north-west casts real shadows from buildings, figures, trees and ships onto each other and
  onto the terrain (the ground is lit only to receive them, so its colours in sunlight are
  exactly the flat map's). In 3D, towns keep growing as you zoom in (up to 2.4 times), so their
  buildings can be read close up, and their names are drawn on plates on the overlay below
  each town, never covered by the buildings.
- **Armies** are companies of soldiers of their era, made for Politicom in Blender by
  `tools/models/build_soldiers.py` (`Game/models/figures/`): a legionary with crested helmet,
  scutum and pilum; a medieval soldier in kettle helmet and tabard with kite shield and spear; an
  18th-century fusilier in tricorn, long coat and crossbelts; a 19th-century soldier in kepi and
  red trousers; a 20th-century one in steel helmet, greatcoat and puttees. Their uniform takes
  the army's colour (muted toward field drab). The **militia** wear their own clothes, three
  variants per era (tunic and round shield; hood and bill; slouch hat; cap and smock; blue
  overalls with a red-and-black scarf). Every figure stands at ease, marches, fights (fires and
  reloads, or thrusts with the spear) and stands guard (aiming, or shield raised); companies
  face their enemy and move out of step. Each figure is one smooth skinned body on a skeleton
  (no gaps at the joints), with a face (eyes, brows, nose, ears; beards and moustaches on
  some), closed hands, the kit of its era (packs and bedrolls, canteens, pouches, cartridge
  boxes, scabbards, bandoliers, gaiters and puttees), detailed weapons (musket locks, barrel
  bands, ramrods, slings, rifle bolts, spear sockets, shield rims and bosses) and ambient
  occlusion baked into its vertex colours; three builds and faces of each, 3,200 to 4,700
  triangles. Run the script in Blender to rebuild or change them.
- **Forests:** trees stand on the land wherever Natural Earth's land cover marks woodland
  (not where the summer satellite picture is merely green, so steppe and cropland stay open),
  in clumps, kept clear of towns (`Game/scripts/Map3D.Trees.cs`). They are made in Blender by
  `tools/models/build_trees.py` (`Game/models/trees/`) and grow by climate: spruce, pine and
  birch in the north and on high ground; oak, elm, birch and pine in temperate lands; oak,
  umbrella pine and cypress round the Mediterranean; palms and evergreen broadleaf in the
  tropics. They follow the seasons of the region's hemisphere: fresh green in spring, gold and
  brown in October and November, the grey-brown of bare twigs in winter, snow on the conifers
  where it lies on the ground; each tree a little lighter or darker than its neighbours. Soldiers cast soft contact shadows.
- **Railways and harbours** (`Game/scripts/Map3D.Transport.cs`): track laid over the terrain
  between linked communes (ballast, sleepers and rails, from `tools/models/build_trains.py`), with a train running out and back on
  every line that isn't cut. The trains are made for Politicom in Blender by
  `tools/models/build_trains.py` (`Game/models/trains/`): before 1880 a 2-4-0 locomotive in
  Brunswick green with red frames, brass dome and tall chimney, a four-wheeled tender, teak
  three-compartment carriages, grey goods vans and wooden coal wagons; from 1880 a black 4-6-0
  lined in red with smoke deflectors and a closed cab, a bogie tender, maroon bogie carriages,
  oxide-red vans and steel coal wagons. Industrial towns run goods trains, others passenger
  trains. The wheels turn, the coupling rods go round, smoke rises from the chimney, and
  everything stops while the train waits at the station. At each coastal town a quay and ships
  of its age riding the swell, made in Blender by `tools/models/build_ships.py`
  (`Game/models/ships/`): a galley with banks of oars, a bronze ram and a striped sail, and a
  fishing boat under a lateen sail in antiquity; a cog with castles fore and aft in the middle
  ages; a three-masted galleon with gun ports until 1840; then an iron screw steamer with a tall
  funnel; in the twentieth century a cargo ship with derricks and, off the larger ports, an
  ocean liner with three funnels and lifeboats (a motor trawler off the smaller ones). Funnels
  smoke. The quay is a wooden pier with a treadwheel crane before 1840, a stone quay with an iron
  crane and a warehouse after.
- Zoomed out, the flat map's emblems take over again.

## Towns, armies and seasons on the map

- **Settlements:** zoomed in, each commune is a small town of buildings in oblique view, lit
  from the north-west like the terrain, more of them the more people live there. The age sets
  the style: flat roofs and a temple in antiquity; steep roofs, a church spire and town walls
  in the middle ages; domes after; factory sheds with smoking chimneys once industry comes;
  blocks of flats with rows of windows in the twentieth century. Farming communes sit among
  their fields; an occupied town is a smoking ruin. Zoomed out, towns are round emblems again.
  (`Game/scripts/MapView.Settlements.cs`)
- **Armies:** a company of soldiers of the age in the enemy's colour (spearmen with round
  shields before 1700, riflemen after), more of them the stronger the army, under a standard
  rippling in the wind. The militia at a front stand under the black-and-red flag.
- **Seasons:** in winter, snow lies across the far north (or south) and its line creeps toward
  the equator as the season deepens; close in on a snowy region, snow falls.

## Motion

Windows and pop-ups ease in (fading up from slightly larger) with a soft shadow, over the
map and dashboard blurred and darkened behind them (`Game/shaders/backdrop.gdshader`), and
fade away when closed. Notices grow in from the right and fade out; tab pages fade in; buttons
glow under the pointer. All of it takes about a fifth of a second (`Game/scripts/Motion.cs`).

## Music and sound

- **Music:** the start menu plays songs of the movement (Hijos del pueblo, A las barricadas,
  the black Internationale, Warszawianka). A game plays music of its age, shuffled and
  crossfaded, changing when a long game reaches a new age:
  - ancient: the Epitaph of Seikilos, guqin, Kevin MacLeod;
  - medieval: Machaut, chant, the Cantigas de Santa Maria, an estampie;
  - early modern: Dowland, Bach, Handel, Vivaldi, Purcell;
  - industrial: Beethoven, Chopin, Dvořák;
  - modern: Satie, Debussy, Dvořák.
  Each piece is credited as it starts ("Now playing").
- **Interface sounds:** clicks, a book opening and closing for windows, a page for tabs and
  news, coins for spending influence, a bell for decisions, a drum for bad news and defeat,
  a sword when a commune falls.
- **Settings** (start menu): music and sound volumes and mute, kept in `user://settings.cfg`.
- **Data:** `tools/audio/build_audio.py` fetches the music from Wikimedia Commons (public
  domain, CC0, CC BY and CC BY-SA only, checked per file), evens its loudness, trims it to four
  minutes and encodes it to `Game/audio/music/`, with credits in `Data/audio/music.json`.
  Interface sounds are from Kenney's RPG Audio, UI Audio and Interface Sounds packs (CC0),
  plus a bell and a drum synthesised by the tool.

## Relief

Real maps (the historical scenarios, open starts and the world view) are shaded with
**topographic relief** from Natural Earth (public domain).
- **What it shows:** the Pyrenees, the Andes and the Alps stand out under the political and
  map-mode colours, while flat ground and sea are untouched.
- **Detail:** a world overview is used when zoomed out. Closer in, 30° tiles at about 1.85 km a
  pixel (full resolution) load as needed.
- **Turning it off:** use the **Relief** switch next to Trust links, or press **R**.
- **Building it:** `tools/geo/build_relief.py` builds the tiles (about 32 MB); see
  `tools/geo/README.md`.

## Communes' proposals, scarcity prices and the ledger

Three features that bring the game closer to Victoria 3, keeping the anarchist premise:

- **Communes propose their own projects.** Every few weeks a commune puts a plan before the
  movement, and you can back it (influence; the project starts with that commune at its
  heart and its neighbours invited) or let it lapse (its people are disappointed). Plans
  come from need:
  - **Famine relief:** where a commune is starving.
  - **A defensive campaign:** where a front is giving way.
  - **An athenaeum:** where political education is thin.
  - **A railway:** between industrial towns at least 15 km apart.
  - **Collectivizing the works:** once no front is losing.

  The same kind of plan isn't proposed twice running. The balanced AI backs proposals, except
  the planners' collectivization.
- **Scarcity prices.** Food and goods each have a price: 1 means about four weeks of stores
  in hand, and the price rises as stores run down (0.4 to 3), moving over weeks rather than
  overnight. No money changes hands between communes; prices are what scarcity costs:
  - **Trade:** food sold abroad buys goods at the ratio of the two prices.
  - **Gold:** gold buys less of whatever the federation is short of, because merchants know.
  - **Wellbeing:** dear food weighs on industrial towns, which don't grow their own.

  Prices show in the Resources section.
- **The ledger (L).** A Victoria-style grid of charts, from the first week to now: food and
  goods in store, both prices, wellbeing, enthusiasm, militia, gold, science, culture, drift,
  communes, trust, influence and enemy strength. Each chart shows its current value and its
  range.

## Civilisations: lore and pictures

Every civilisation on the world maps (634 across the 16 eras) has a short history from
**Wikipedia** (CC BY-SA 4.0, credited), with a freely licensed picture from **Wikimedia
Commons** where its article has one. Flags are skipped.
- **Where it shows:** beside the open-start country map, and in the World tab for the
  selected country.
- **Era line:** a line built from the map data says what the state is in that era: its
  regime, capital, power and neighbours.
- **Building it:** `tools/ui/build_lore.py` fetches the text in one fast pass and the
  pictures in a slow, resumable one, and only accepts an article that describes a people,
  polity, culture or period. Descriptive names ("Amazon hunter-gatherers") are matched to
  the indigenous peoples of that region.

## Needs attention

At the top of the Overview tab, **Needs attention** lists up to six communes that most need the
movement this turn (`Sim/Attention.cs`), most urgent first, each with what's wrong and one
button for what would help, at its influence cost:

| Problem | Button |
|---|---|
| Voting to leave the federation | Mediate with its least-trusted neighbour (or hold it by force, if it has none) |
| Losing ground at the front | Fortify; or back a commune's proposed defensive campaign; or propose one |
| Going hungry | Propose famine relief |
| Discontented (above 0.5) | Mediate with its least-trusted neighbour |
| Party cells in over 40% of its committees | Expose the cells |
| Experts taking charge (the expert trap) | Fund a political school |
| Thin political education | Fund a political school |

A defensive campaign helps the whole front, so it is offered once, on the worst town. A
commune's name selects it and brings it into view. In a large federation it saves searching
the map town by town; the decisions are still yours.

## Folding the dashboard

The dashboard on the right folds away: press the tab on the map's right edge (or **P**) and the
map takes the whole window, with a small clock at the top (date, turn, pause and speeds). The
tab, P, or clicking a commune brings it back. The game remembers whether it was folded.

## Battles

Every turn each front commune under attack fights a battle, and the map shows it: where a
commune was fought over last turn, crossed swords stand between its militia and the army
attacking it, ringed green if the attack was repulsed, amber if the line held, red if ground
was lost or the town fell. Click them to open the battle (`Game/scripts/Main.Battle.cs`):

- **A 3D scene** of that turn's fighting, built from the map's own models
  (`Game/scripts/Map3D.Battle.cs`), the camera sweeping slowly over the field from the flank.
  The town and its works stand behind the militia's line, the enemy's companies come from the
  west, and how they stand tells how it went. Repulsed, the attackers fall back and leave their
  dead before the line; held, the lines trade volleys; ground lost, the attackers are at the
  works, shells burst among the militia and houses burn; fallen, the enemy's flag stands in the
  burning ruins. Muskets and rifles flash and smoke from 1450, guns shell the line from 1820;
  earlier, spears and shields only. Companies, figures and kit are of the age.
- **A report:** attack against defence as two bars, the ground held before and after, the works,
  the people under arms, a defensive campaign's help, and every part of the defence and the
  attack (the same breakdown the simulation computes with).
- **Earlier battles there**, a strip of dates coloured by how each went: click one to see it.

The simulation records each battle (`BattleReport`, kept for about six months and saved with
the game). There are no casualty figures: the simulation doesn't count the dead, so the report
doesn't invent them.

## Goals

Every five or six months, from about ten weeks in, the movement is offered a **goal** that fits
the moment (`Sim/Systems/GoalSystem.cs`): hold a threatened town at the front for twelve weeks;
bring hierarchy drift down by six points; end a hunger within eight weeks; raise trust between
communes, political education or the radicalization of the enemy's workers by a set amount.
It comes as a decision. Taken on, it shows in the Overview with its progress and the weeks
left; met, the assemblies take heart (+8% enthusiasm, +8 influence); missed, they lose a little
(-4% enthusiasm). Not taken up, it lapses with no cost. The AI players take every goal on, and
the balance tables above include them.

## Random events and pop-ups

Besides each scenario's scripted dilemmas, **random events** happen: about one every couple of
months of game time, each kind at most once a game. There is never a long stretch of just
waiting: after eight weeks with nothing to decide, one comes for certain (and if every kind
has been used, one not seen for a year may come round again). There are 37 of them, in
`Data/events/random_events.json`, written for different societies. Each fits by era, by the
regime the movement rises against, and by the communes it needs:

- **Any age:** a bumper harvest, a drought, a fire, refugees, a quarrel over water, a
  festival, a spy, a defecting officer, hoarders, a flood.
- **Travellers bringing a technique of their age:** a millwright, a printer, an engineer,
  hackers.
- **Antiquity and stateless peoples:** a comet, gladiators, raiders, kinship with the clans, a
  great gift-feast, the high priest's offer.
- **Feudal and theocratic regimes:** the tithe, a relic, excommunication, burning the manor
  rolls, witch trials, privateers.
- **Empires:** the imperial levy, a governor who wants to switch sides.
- **Modern regimes:** elections called to split the movement, a king's constitution,
  blackshirt squads, the party's commissars, a general strike, broken machines.
- **The contemporary world:** sanctions, a foreign journalist, a rumour online.

How they play:
- **If nobody answers,** the default is what happens when nobody acts, so a passive movement
  gains little from them.
- **Blows to the enemy** are scaled to its strength, so no single event decides a war.
- **Pictures:** event pop-ups open with a picture in a gilt frame. These are
  freely licensed period art from Wikimedia Commons (public domain, CC0, CC BY or CC BY-SA): medieval chronicles, Bruegel, engravings
  from illustrated papers, and photographs of 1871, 1917, 1919 and 1946.
  - **One style:** each picture is toned to the same sepia engraving
    (`tools/ui/process_event_art.py`), so paintings, woodcuts and photographs read as one
    series. A credit line sits under each frame.
  - **Matching:** an event's words set its theme (war, uprising, assembly, hunger, harvest,
    work, faith, plague, fire, flood, refugees, festival, trade or press), and the game's
    year picks the age. The nearest age with a picture is used.
  - **Variety:** the common themes (war, uprisings, assemblies, towns, fire, festivals, faith)
    have several pictures. Each event always shows the same one, so a long game doesn't
    repeat a single image.
  - **Scenario pictures:** the historical scenarios show their own history first: the
    Durruti Column and the barricades of 19 July 1936 in Catalonia; the cannons of
    Montmartre, the fallen Vendôme Column and the proclamation of the Commune in Paris; the
    Makhnovists' black banner and Makhno with his comrades in Ukraine.
  - **Adding a picture:** add an entry with its Commons file title to
    `tools/ui/event_art.json` and rerun the script. `tools/ui/find_event_art.py` lists
    public-domain candidates for a slot.
- **Pop-ups:** every decision, scripted or random, and every big piece of news (a revolution
  abroad, a town rising, hierarchy hardening) opens in the middle of the screen with its
  choices. Each choice shows its effects under it. News items that concern a commune have a
  **Show me** button. Several queue up, and a pop-up closes by itself if its decision was
  answered in the side panel or ran out of time.

## Endings and peace

- **Endings** (`Sim/Systems/Endings.cs`): a finished game ends with one of several endings,
  from the outcome and how it was reached: *The revolution spreads* (a sister federation
  abroad), *A recognized autonomy* (peace with every enemy), *A free society* (freedom
  75+ and quality of life 50+), *The federation endured, in name* (drift past 50), *The
  federation endured*, *You won the war and lost the revolution* (a state formed), *The
  federation has dissolved*, *Crushed, but remembered* (destroyed, but after holding out for
  60% of the game, or with its ideas spread abroad), *The federation has been destroyed*. An epilogue follows: what became of each
  figure, how many communes ended free, which fortifications held, whether its culture and any
  sister federations outlived the war. The leaderboard records the ending.
- **Peace** (open-world starts only; the historical scenarios keep their history,
  `Sim/Systems/PeaceSystem.cs`): an enemy offers terms once it is ready: a long stalemate (40
  weeks without taking a commune), a long war, or exhaustion (raids and occupation wear it).
  Making peace closes its front (it keeps what it took) for +3 drift; fighting on lifts
  enthusiasm. Peace with every enemy ends the game early. The balanced AI accepts when drift
  allows. Offers come only when the federation really holds: in about a third of France 1936
  regional games, rarely when an enemy keeps taking communes.

## Decisions at the front

Two actions for a front commune (Commune tab):
- **Fortify** (8 influence, and goods from the commune's own store): earthworks and
  barricades go up over three turns, to about 60% after one round (up to 100%), adding up to
  +25% defence. The works wear down by 3% a turn while the front gives way. The map shows them
  as a ring of works round the town, higher as they go up (a palisade on the flat map). In 3D
  they are of the age (`tools/models/build_works.py`): a palisade of stakes on an earth bank
  with a timber gate tower; a battlemented stone wall with round towers; an earthen rampart
  faced with stone, with gabions and cannon; barricades of paving stones, barrels and carts
  under a red flag, with field guns; sandbagged trenches behind barbed wire, with machine-gun
  nests. Strongpoints stand either side of the gate and, as the works rise, round the ring.
- **Raid** (6 influence, once every four turns): a strike on the enemy. The chance (15–85%,
  shown on the button) is better the more firmly the commune holds its line. Success tires
  the enemy (+0.04 exhaustion), brings back captured arms and lifts enthusiasm; failure costs
  15% of the commune's fighters and some ground.
- The balanced AI fortifies communes that are losing ground and raids where the odds are
  good, but only with drift well in hand: influence spent here isn't spent on schools.
  Balance (200 games): Catalonia 96%, Free Territory 73%, Paris 49% (up from 43%: barricades
  and raids help a besieged city), open starts 57–76%.
- Paris was then brought back to its intended difficulty: Bloody Week's strength rose from 26
  to 29, and the balanced player again survives about a third of games (34%; 20% at 32, 50% at
  26). The educator never outlasts it.

## Notable figures

Each game has notable militants (`Sim/Model/Figures.cs`, `Sim/Systems/FigureSystem.cs`):
- **Historical scenarios** name real people: Durruti, García Oliver, Federica Montseny and
  Lucía Sánchez Saornil in Catalonia; Makhno, Maria Nikiforova, Volin and Arshinov in the Free
  Territory; Dąbrowski, Louise Michel, Eugène Varlin and Courbet in Paris. Durruti falls on the
  Madrid front when the historical event comes. Open-world and generated starts get one of
  each role, named for their trade and town ("The smith of Lyon").
- **Roles:** a **commander** makes their commune (and, half as much, its neighbours) defend
  better; an **orator** grows trust around them and the revolution's enthusiasm; an
  **organizer** adds food and goods; an **educator** raises political education there and
  next door.
- **Prestige and ambition:** prestige grows with their work (a commander's faster while the
  front holds); ambition grows with prestige, slower where political education is high.
  Prestige x ambition adds hierarchy drift ("Figures gathering power"), and past about 0.42
  they ask for power: **grant it** (twice as effective, +4 drift now and much more every turn)
  or **rotate them back to the ranks** (prestige and ambition fall, a short rest, their
  followers grumble, drift eases).
- **Commanders** at a losing front may fall, which costs enthusiasm.
- **Where to see them:** the Figures section of the Movement tab (send the selected commune a
  figure, or rotate one), a ★ after the town's name on the map, and the commune's tooltip.
- **Balance** (balanced AI, 200 games): Catalonia 96%, Free Territory 72% (from about 79%:
  ambitious figures are a real pressure), Paris 43%; open starts 55–70%. The AI rotates
  anyone past 0.3; the centralizer grants every request.

## Why a number is what it is

In the Commune tab, underlined numbers explain themselves on hover, part by part, as Victoria
3's tooltips do (`Sim/Explain.cs`):
- **Wellbeing:** what it is moving toward (it closes 15% of the gap each turn): food and goods
  in store, education, hierarchy, ground lost at the front, recalled delegates, dear food in a
  factory town.
- **Discontent:** little trust in its neighbours, low wellbeing, its values, hierarchy,
  starvation; past about 0.6 a commune may vote to leave.
- **Food:** stores in weeks of eating, grown and eaten this turn, recent aid.
- **Defence at the front** (green while holding, red while losing ground): its own militia
  and its neighbours' (added), then every factor that multiplies them: techniques, hierarchy,
  the war committee, arms, stockpiles, recalled delegates, foreign volunteers, commanders,
  fortifications.

On the Overview tab: **last week's drift** (every source and brake), **prices** (weeks of
food and goods in store, and the price each sets), and **each enemy's strength** (its army, any
offensive, intervening states; then its radicalized workers, exhaustion and technology).

The simulation computes these numbers through the same breakdowns, so the tooltip and the
number never disagree.

## Map modes

Trust, Education, Values, Wellbeing, Culture, Ideology, Diplomacy and Revolution, plus:
- **Food:** weeks of stores in hand in each commune (dark red where it's starving).
- **Militia:** the share of each commune under arms, against the most it can field.
- **Party cells:** where rival parties have a hold (expose them in the Commune tab).
- **Discontent:** restless communes, which free-ride and may secede.

## What the player sees

**The look.** The interface is modelled on Victoria 3:
- **Windows:** pop-ups, the scenario picker, the tech tree and the dashboard are dark grained
  lacquer in engraved gold frames with flourished corners: a bracket with curling ends and a
  palmette.
- **Titles:** window titles sit on a metal-rimmed plate with scroll terminals.
- **Section headings:** each sits on a shaded bar with fine gold rules.
- **Controls:** buttons are embossed bronze plates (lit on hover, red lacquer when on).
  Tabs are plates with the open one lit, and switches are gilt.
- **Medallions:** pop-up and decision emblems sit in beaded gold medallions. Dividers are
  tapered gold rules.
- **Text:** a book serif, **Crimson Pro**, throughout. Titles and headings are semibold, in
  title case and gold; body text is regular, cream, at about 16 px; figures are bold.
- **Replacing the art:** every ornament is a PNG drawn by `tools/ui/build_ornaments.py`
  (Pillow and numpy). Painted art of the same size and margins can replace any of them
  without code changes; the margins are in `Game/scripts/UiTheme.cs`.
- **Emblems:** engraved emblems replace the earlier pixel art: game-icons.net silhouettes
  (CC BY 3.0) struck in gold, steel or enamel, with a dark keyline and shadow
  (`tools/ui/build_emblems.py`). They're vector graphics, so they're crisp at any size.
  - **Settlements:** each commune is a round map pin with a gold rim and an ivory emblem of
    its kind and age: huts, a smithy or a castle before 1750; a barn, a factory or a church
    after. Occupied communes are ruins on a red-rimmed pin.
  - **Armies:** enemy armies are a Roman-style helmet before 1700 and a steel helmet after,
    tinted in the enemy's colour. The militia is a black-and-red flag.
  - **Technologies, resources, section headings and pop-ups** each have their own emblem.
- **Section headings** end in a thin gilt rule.
- **Rebuilding:** `tools/ui/build_emblems.py` rebuilds the emblems. Credits are in
  `Data/geo/ATTRIBUTION.md`.

**Militia, gold and enthusiasm.** Three resources that each come with a dilemma:
- **Militia:** each commune has a share of its people under arms, about a tenth.
  - **Defence** scales with that share. Fighters beyond the usual share don't work, so the
    commune produces less.
  - **Volunteering:** a commune drifts toward what it volunteers. That's more where people
    are militant, and fewer as enthusiasm fades.
  - **Call for volunteers** (Commune tab) raises the share here and in the neighbouring
    communes. **Conscription** (Movement tab) puts at least a fifth of every commune's people
    under arms, but adds drift every turn and wears enthusiasm down.
  - **Losses:** losing front communes lose people.
  - The balanced AI only calls for volunteers; conscription is the centralizer's tool.
- **Gold:** **Seize the treasuries** once a game for gold and silver from the banks and
  churches, or win it in events.
  - **What it buys:** arms for the front, grain for the hungriest communes, or machines for
    the workshops, from a state willing to sell. Prices are worse under embargo; with no
    world, smugglers sell at a third off.
  - **The catch:** seizing adds drift and angers monarchies, empires and churches. While the
    gold sits, the treasury committee that guards it adds drift every turn, so it's
    better spent.
- **Enthusiasm:** the revolution's spirit, starting at 80%.
  - **What lowers it:** a long war, hunger, every commune lost, hierarchy and conscription.
  - **What raises it:** culture, towns rising, and revolutions won abroad.
  - **What it changes:** production, militias and contagion (×0.94 at 40%, ×1.03 at full),
    and how many come forward when volunteers are called.

**Resources.** The Overview's Resources section shows the federation's food and goods:
- **Food and goods:** what's in store, what's made a turn against what's eaten or used
  (green when there's a surplus, red when there's a deficit), and how many turns of food
  are in reserve.
- **From abroad:** arms bought from friendly states, trade, and food imports.
- **Science and culture:** science a turn, and culture on average.
- **Shortages:** which communes are starving, and which front communes are short of arms.

The commune list (C) has **Food** and **Goods** columns: each commune's stores in turns of its
own needs, sortable, with the detail on hover. Industrial towns live on the farms' food and
the farms on the towns' goods, so the columns show where the federation depends on sharing.

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
  - **Your diplomacy:** *send emissaries* (warm a state; fascists barely listen), *agitate
    workers* (radicalize it; its government resents it) and an *international appeal* (double
    the volunteers for a while).
  - **Other countries' cities** are on the map (the era's largest, capitals ringed in gold).
    Click one to send emissaries or agitators there; the map flags cities with a mission in
    the last half year, and saves keep the missions.
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
- **Ideological rivals:** authoritarian communist states compete with you for the same
  workers: the USSR in 1936, Soviet Russia in 1918, and any party that seizes power after a
  revolution.
  - **Party cells** grow in your communes where political education is thin and life is hard.
    They add drift (shown in the drift breakdown and the Commune tab), as the PSUC did in 1937.
    Political education wears them down, and *Expose party cells* breaks them up here and next
    door, at the cost of the rival's goodwill.
  - **Arms for commissars:** now and then the strongest rival offers arms if the militias take
    its commissars: arms and militarization, plus drift and more party cells. Each time it
    comes back its terms are harder (more arms, but more drift and a bigger party), and the
    third is its last. This is separate from the domestic ally's offers (the Republican
    Government, the Red Army command), which also grow dearer each time they're accepted; refuse
    the ally twice and it stops asking.
  - **Revolutions abroad:** a rival nearby makes a winning revolution likelier to end with a
    party in power, so revolutions become contests between you and them.
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

There are 98 technologies in five lines of development: Organizational, Agricultural,
Industrial, Military and Communication. They span seven ages: Ancient, Classical, Medieval,
Early modern, Industrial, Modern and Contemporary.

- **Era (`era`):** the year from which a technique can be discovered. Later ones are shown
  dimmed with their year and stay locked until the calendar reaches them.
- **Common knowledge (`until`):** older techniques become common knowledge by a given year.
  Pre-industrial ones are common knowledge by 1850. They no longer appear in the tree, cost
  nothing and count as known for prerequisites. So a game in 1936 plays the industrial,
  modern and contemporary tree; a start in an earlier age plays its early tree (ancient maps are still to come).
- **Chains:** each line runs from the first techniques to the latest, for example the
  Organizational line: village moots → sortition → guild self-government → open assemblies →
  imperative mandate → rotating delegates → consensus process. Some techniques need two lines
  at once, such as heavy industry (war industry and electrification).

Every line has an **efficient path** that adds drift and a **free path** that brakes it at
some cost, in every age:

| Line | Free path (industrial and modern) |
|---|---|
| Organizational | Imperative mandate, rotating delegates, consensus process |
| Agricultural | Communal land councils, agroecology |
| Industrial | Workers' control, cooperative networks |
| Military | Elected officers, defence committees, civil resistance |
| Communication | Wall newspapers, free radio, free software |

A commune on a line's free path resists that line's centralizing technologies: it discovers
and adopts them at about a third of the usual rate (`PathResistance`). The data is in
`Data/technologies.json`.

### Science

- **Where science comes from:** each commune produces it from its people, its practical
  education and the institutions it holds, such as libraries, scribes, academies and popular
  universities.
- **Who adds to it:** trade with more advanced states and sister federations.
- **Breakthroughs:** science accumulates. When it reaches the cost of the next breakthrough,
  the commune with the most research discovers something. Each breakthrough costs 20% more.
  Breakthroughs need the movement to organize research with a focus. Without one, science
  only speeds the communes' own discoveries, so science is a lever, not a free win.
- **Research focus:** the movement still can't pick a technique, but it can set a focus on
  one line. Breakthroughs then come in that line, and communes lean toward it when they
  discover on their own.
- **Spread:** ideas still spread along trust links, and the movement can circulate one.

### Culture

Each commune has a **culture**: how deeply the movement's ideas have taken root.
- **Growth:** culture grows from political education and from cultural techniques (the press,
  cinema, preachers, popular universities). It spreads to trusted neighbours and fades if
  nothing feeds it.
- **Cultural works:** the player can found three, each costing influence, with a commune
  taking 8 weeks to take one up:
  - **A newspaper:** culture here and next door, and its pages reach the enemy's workers.
  - **A school movement:** culture and political education.
  - **An art movement:** songs, theatre and posters. A lot of culture, and the neighbours warm
    to it.
- **Effects:** culture brakes drift ("a culture of freedom" in the drift breakdown), builds
  trust, keeps rival parties' cells out and makes contagion abroad stronger.
- **Rival cultures:** abroad, every state's own culture resists the movement's ideas among its
  workers, most of all in large fascist, communist and monarchist states.
- **Map:** the **Culture** map mode shows both.

### Technology between states

- **States:** every state has a technology level that rises with its size, faster at peace.
- **The enemy:** it modernizes as the war goes on, faster when better-armed sponsors
  intervene.
- **Falling behind:** if the federation's own technology level rises less, the enemy gains up
  to 20% strength ("+N% from better weapons" in the Overview). Keeping up cancels this, but
  research can't make the enemy weaker than it started.

All slow processes (science, culture, modernization) scale with turn length, so Paris's daily
turns don't run seven times faster.

**In Godot,** the **Technologies** button (or K) opens the tree:
- **Columns:** grouped by age, with the current age marked.
- **Side column:** science, the next breakthrough and the research focus picker.
- **Nodes:** each shows its spread, its drift (▲) or brake (▼), and the education it needs.
- **Details:** hover or click a node to light up its chain and see its effects, its era and
  who has it.

**In the terminal:**
- `techs` lists the live tree.
- `focus military` (or `focus balanced`) sets the research focus.
- `paper`, `schools` and `art <commune>` found cultural works.

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

### Campaigns

Open-world starts can also run as **campaigns** of 10, 25 or 50 years (520, 1,300 or 2,600
weekly turns; anything over 500 turns is a campaign, `--length 2600` in the terminal). Press 4
for the fastest speed. A campaign plays differently:
- **Peace doesn't end it.** Make peace with every enemy and the game goes on in peacetime.
- **Treaties are broken.** After four sure years of peace, an enemy may break the treaty
  (about one chance in five a year): its old front reopens and a new offensive follows.
- **Peacetime brakes drift.** While no commune is on a front, the militias go home and the
  assemblies take back what the war gave away (a drift brake, "Peacetime: the militias go
  home").
- **What grows with the decades levels off.** Drift from double-edged technologies, from their
  brakes and from large projects rises toward a ceiling instead of without end, so a long game
  settles into a balance; renewed offensives stop growing after the eighth.
- Ordinary games (up to 500 turns) are unchanged; in them, peace with every enemy ends the game
  and counts the rest as survived.

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
- **Godot:** Save names a save (or overwrites one); Load lists every save, newest first, to
  load or delete; F5 quick-saves and F9 loads the quick-save. The start menu's Continue loads
  the latest save. Saves live in Godot's user data folder (`saves/`, each with a small
  `.meta.json` for the load screen).

### Leaderboard

Every finished game is ranked on a local leaderboard (`leaderboard.json` in the same folder)
by its total score, the average of survival, quality of life, spread and freedom. The end
screen says where the game placed; the leaderboard, from the start menu or the end screen,
shows the top 100, for all scenarios or one.

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
passive            0%      100%     0% │    44    48      8    70    43 │    54
educator           1%      100%     0% │    50    51     16    80    49 │    30
centralizer        0%        0%   100% │    25    50      2    51    32 │   100
balanced          80%       15%     6% │    77    51     16    67    53 │    57
trader            67%       16%    18% │    78    52     15    62    52 │    68
```

Catalonia can be lost both ways: to the Nationalists, or to the federation's own hardening
into a state. The Nationalist Army starts at strength 1.9 (from 1.4), and the Catalonia
Offensive is a crushing 120 (from 44). In Catalonia an overwhelming offensive takes ground up
to three times faster than one that barely outmatches the defence (`overwhelmCap` in the
scenario; elsewhere offensives keep the old pace), so the last offensive can break a weak
federation in its final weeks. Education alone no longer saves it: a movement that never
organizes its defence is crushed, and one that centralizes to survive becomes a state.

The war follows the historical shape: with nobody playing, the front mostly holds through
1937 and collapses in the Aragon (March 1938) and Catalonia (December 1938) offensives.
A passive player also lets every historical event take its historical course, which is
why it fares so badly.

The Free Territory (`--scenario free_territory_1918`) is harsher and plays differently:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            8%       93%     0% │    70    50     13    79    53 │    44
educator          31%       69%     0% │    77    52     16    87    58 │    22
centralizer        0%        0%   100% │    27    48      2    50    32 │   100
balanced          79%        0%    21% │    87    51     15    71    56 │    61
trader            47%        0%    54% │    83    51     14    60    52 │    85
```

There, refusing to centralize at all usually means being crushed by the final Red offensive,
but with the highest freedom; goals met along the way (see below) now carry the educator
through about a third of games.

Paris (`--scenario paris_commune_1871`) is the tragic one:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            0%      100%     0% │    82    66      7    91    61 │    26
educator           0%      100%     0% │    80    66      7    96    62 │     7
centralizer        0%        0%   100% │    76    62      4    49    48 │   100
balanced          45%       56%     0% │    96    66      7    92    65 │    17
trader            56%       45%     0% │    96    66      7    90    65 │    23
```

Bloody Week's strength is 27 (from 29): Paris is still the tragic scenario, but a good
defence now holds it in nearly half of games rather than a third.

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
