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
  Commons**, each checked as public domain or CC0. They are cropped and toned by
  `tools/ui/process_event_art.py`. Each picture's author, date, licence and source page are
  in `Data/events/art_credits.json` and shown under it in the game.
