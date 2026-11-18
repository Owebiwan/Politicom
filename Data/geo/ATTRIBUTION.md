# Map data credits

- World maps for 1871, 1918 and 1936 (`Data/world/`): countries, land and populated places
  from **Natural Earth**, public domain, grouped into period states by this project's own era
  mappings.
- World maps for the other eras (`Data/world/-500.json` … `1815.json`, `1960.json`,
  `2000.json`): historical borders from **historical-basemaps** by André Ourednik and
  contributors (github.com/aourednik/historical-basemaps), licensed under the **GNU General
  Public License v3.0**. These files are simplified derivatives (grouped by ruling state,
  simplified, with Natural Earth coasts and modern city names added) and are under the same
  licence. Distributing a build that contains them means distributing it under GPL-3.0 terms,
  or replacing these files.
- Land, lakes, rivers, land borders and provinces: **Natural Earth** (naturalearthdata.com), public domain.
- The ground under the map (`Data/land/`, mirrored into the game): **NASA Earth Observatory**, Blue Marble: Next
  Generation (July 2004, with topography and bathymetry) and the GEBCO-derived elevation
  published with it, public domain. Graded and hill-shaded by `tools/geo/build_land.py`.
- Land cover and coastlines for the sea (`Game/map/`): **Natural Earth I** land cover and
  1:50m land, public domain, processed by `tools/geo/build_sea.py`.
- Shaded relief (`Data/relief/`): **Natural Earth "Shaded Relief, High Resolution"** (SR_HR),
  public domain, converted to transparent shade tiles.
- Paris arrondissement boundaries (`paris_commune_1871.json`, `territories`): **Ville de Paris,
  "Arrondissements"**, opendata.paris.fr, licensed under the Open Database License (ODbL)
  1.0. This file is a simplified derivative of that database and is shared under the same
  licence.

# Interface credits

- Emblems (`Game/emblems/`): silhouettes from **game-icons.net**, licensed under **CC BY 3.0**
  (creativecommons.org/licenses/by/3.0), by Lorc, Delapouite, Carl Olsen, Caro Asercion,
  Cathelineau, Sbed and Skoll; given an engraved metal finish by `tools/ui/build_emblems.py`.
  The militia flag was drawn for this project.
- Font (`Game/fonts/`): **Crimson Pro** by Jacques Le Bailly, under the **SIL Open Font
  License 1.1** (licence text alongside the font).
- The ornaments (`Game/ui/`: frames, plates, buttons, tabs, medallions, dividers) are drawn in
  code by `tools/ui/build_ornaments.py`; the clock emblems and switches were drawn for this project.
- Event pictures (`Game/art/events/`): public-domain works and photographs from **Wikimedia
  Commons**, each under a free licence: public domain, CC0, or CC BY / CC BY-SA (any use with
  attribution; never non-commercial or no-derivatives licences). They are cropped and toned by
  `tools/ui/process_event_art.py`. Each picture's author, date, licence and source page are
  in `Data/events/art_credits.json` and shown under it in the game.
- Civilisation lore (`Data/lore/lore.json`): excerpts from **Wikipedia** articles, licensed
  **CC BY-SA 4.0**. Each names its article and is credited in the game. Pictures
  (`Game/art/lore/`) are from **Wikimedia Commons** under free licences (public domain, CC0,
  CC BY, CC BY-SA), each credited, and toned to the game's style.


## Music and sound

- Music: recordings from **Wikimedia Commons** under public domain, CC0, CC BY or CC BY-SA
  licences; each piece's performer, licence and source page are in `Data/audio/music.json`
  and shown in the game as it plays. CC BY-SA pieces stay under CC BY-SA.
- Interface sounds: **Kenney** (kenney.nl), RPG Audio, UI Audio and Interface Sounds, CC0.
- Ambient sound (sea, wind, birds, cicadas, rain, crowds, workshops, a street, bells, a rooster,
  gulls, a steam whistle, a ship's horn) and the raw material for the distant battles: field
  recordings from **Wikimedia Commons** under public domain, CC0, CC BY or CC BY-SA licences;
  each recording's author, licence and source page are in `Data/audio/ambience.json`. The
  battle loops are mixes made for this project from those recordings (CC BY-SA ones keep their
  licence).

## 3D models

- Trees in `Game/models/trees/`: made for this project by `tools/models/build_trees.py` (Blender).
- Ships and quays in `Game/models/ships/` and fortifications in `Game/models/works/`: made for this
  project by `tools/models/build_ships.py` and `tools/models/build_works.py` (Blender).
- Town buildings in `Game/models/buildings/`: made for this project by
  `tools/models/build_buildings.py` (Blender).
- Soldiers and militia in `Game/models/figures/`: made for this project by
  `tools/models/build_soldiers.py` (Blender).
- Locomotives, carriages, wagons and track in `Game/models/trains/`: made for this project by
  `tools/models/build_trains.py` (Blender).
- Event pictures for the anniversary, mourning and the barricades (`Game/art/events/celebration_*`,
  `mourning_*`, `barricades_*`): rendered for this project by `tools/art/render_event_art.py`
  (Blender, Cycles). The towns are built by that script. Their textures (plaster, stucco, brick,
  stone, roof tiles, slates, cobbles, wood), the props (barrels, crates, tables, chairs,
  benches, baskets, bottles, jugs, lamps) and the skies (HDRIs) are from **Poly Haven**
  (polyhaven.com), **CC0**, fetched by `tools/art/fetch_assets.py`. The people are the game's own
  figures.
