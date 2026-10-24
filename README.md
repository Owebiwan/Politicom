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

The game opens on a start menu: continue the quick-save, choose a historical revolution (or a
random map), or start in the open world. Esc brings the menu back during a game.

Controls:
- Click a commune to select it; shift-click a second commune for mediation or railways.
- Space pauses, 1–3 set the speed, and `.` advances one week.
- Scroll (mouse wheel, Magic Mouse or trackpad) or pinch to zoom at the pointer. Drag with any button to move the map; a click without moving selects. Double-click empty space to reset the view.
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
  - **Whole country** (the default): the movement holds the country's largest cities, and the
    government holds only the capital.
  - **Regional rising:** the movement holds a stronghold, usually far from the capital, and
    the 9 cities nearest it; the government holds the rest of the country.
  - **Joining a regional rising:** the country's other towns (up to 30) watch, and may rise
    and join the federation, one at a time. That's likelier near the federation
    (`RisingReachKm`), when the enemy's own workers are radicalized, and where the movement's
    culture runs deep, and less likely as drift grows. A risen town gets its own territory
    on the map.
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
- **Country names:** names run across their territory in spaced capitals, along the
  country's main axis with a slight bow and sized to fit it, as on Victoria 3's map. They're
  cream over the sea and brown ink on the paper chart.
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
- **How it fits together:** the flat map keeps its size and behaviour; it sits at the foot of
  a larger canvas (the area the tilted camera can see) and draws beyond its own edges into
  it. Pointer input is cast onto the ground and handed to the flat map in its coordinates, so
  clicking, dragging, scrolling and pinching work as before. The legend, banner and tooltips
  are drawn flat on an overlay, and the colour grade, vignette and falling snow lie over the
  screen.

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

## Random events and pop-ups

Besides each scenario's scripted dilemmas, **random events** happen: about one every couple of
months of game time, each kind at most once a game. There are 37 of them, in
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
    its commissars: arms and militarization, plus drift and more party cells. This is separate
    from the domestic ally's offers (the Republican Government, the Red Army command).
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
passive            6%       94%     0% │    58    49      8    66    45 │    64
educator          85%       15%     0% │    64    51     15    78    52 │    38
centralizer        0%        0%   100% │    26    50      2    50    32 │   100
balanced          94%        0%     6% │    87    51     13    71    56 │    50
trader            87%        0%    13% │    85    52     13    65    54 │    64
```

The war follows the historical shape: with nobody playing, the front mostly holds through
1937 and collapses in the Aragon (March 1938) and Catalonia (December 1938) offensives.
A passive player also lets every historical event take its historical course, which is
why it fares so badly.

The Free Territory (`--scenario free_territory_1918`) is harsher and plays differently:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            0%      100%     0% │    65    49     11    83    52 │    38
educator           2%       98%     0% │    70    51     12    90    56 │    20
centralizer        0%        0%   100% │    28    48      2    50    32 │   100
balanced          80%        1%    19% │    87    50     12    68    54 │    72
trader            31%        1%    68% │    80    49     11    60    50 │    87
```

There, refusing to centralize at all means being crushed by the final Red offensive, but
with the highest freedom. The educator's total score nearly ties the balanced player's.

Paris (`--scenario paris_commune_1871`) is the tragic one:

```
policy       survived destroyed  state │  surv   QoL spread  free total │ drift
passive            0%      100%     0% │    82    67      7    91    62 │    27
educator           0%      100%     0% │    80    67      7    96    62 │     7
centralizer        0%        0%   100% │    78    63      4    48    49 │   100
balanced          31%       69%     0% │    96    68      7    92    65 │    18
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
