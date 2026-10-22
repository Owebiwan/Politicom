# Map data

`build_geo.py` turns public map data into the small per-scenario files in `Data/geo/`.
Run it again if you change a scenario's area or add a scenario.

```sh
mkdir -p /tmp/geo-src && cd /tmp/geo-src
base=https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson
for f in ne_10m_land ne_10m_lakes ne_10m_rivers_lake_centerlines ne_10m_admin_0_boundary_lines_land \
         ne_10m_admin_1_states_provinces; do
  curl -fLO "$base/$f.geojson"
done
curl -fL -o paris_arrondissements.geojson \
  "https://opendata.paris.fr/api/explore/v2.1/catalog/datasets/arrondissements/exports/geojson"
for f in ne_50m_admin_0_countries ne_50m_land ne_10m_populated_places_simple; do curl -fLO "$base/$f.geojson"; done
cd - && python3 tools/geo/build_geo.py /tmp/geo-src Data/geo
python3 tools/geo/build_world.py /tmp/geo-src Data/world
```

Each scenario's area, detail level and options are set in the `REGIONS` table at the top of
the script.

**Provinces** shape the federation's outer edge, so it follows real borders instead of a
smoothed outline. A *core* province is held entire, so territories fill it out to its borders.
A *contested* province is held only near the communes, so the side facing the enemy stays a
smooth front. Catalonia's four provinces are core and Aragon's three are contested. All the
Free Territory's oblasts are contested: it never had fixed borders, only the land the
insurgents held.
- **Catalonia:** Spain's 1833 provinces, which are still right for 1936. They're also drawn faintly.
- **Free Territory:** today's oblasts stand in for the 1918 governorates. They're used for the
  edge but never drawn.

Some layers are left out where they'd be anachronistic:
- **Free Territory:** today's borders aren't drawn, and the Dnipro reservoirs (built from the
  1930s on) are dropped.

## World maps

`build_world.py` builds `Data/world/<era>.json` from the era mappings in `eras/`: today's
countries (by Natural Earth `ADM0_A3` code) grouped into the states of the time, with colonies
folded into their empires. Countries not listed are drawn as neutral land.

Borders are traced from the edges neighbouring countries share, so borders inside one state
(India within the British Empire) disappear without any polygon union. Each state's cities
list its home country first, then its colonies.

## Historical eras

`build_history.py` builds the eras before and after the hand-made ones (500 BC to AD 1815,
1960, 2000) from **historical-basemaps** snapshots:

```sh
mkdir -p /tmp/hb && cd /tmp/hb
for f in bc500 bc1 500 800 1000 1200 1400 1500 1650 1783 1815 1960 2000; do
  curl -fLO "https://raw.githubusercontent.com/aourednik/historical-basemaps/master/geojson/world_$f.geojson"
done
cd - && python3 tools/geo/build_history.py /tmp/hb /tmp/geo-src Data/world
```

- **States:** features grouped by the state that rules them (`SUBJECTO`), so colonies belong
  to their empire. The largest 150 polities are kept (plus a few famous small ones such as Rome
  and Venice); the rest are drawn as neutral land.
- **Cities:** Natural Earth populated places inside each outline, or within about 35 km of it
  (the source's coasts are coarse). They carry **modern names**: Istanbul, not Byzantion.
- **Capitals:** from a table of seats of power where the largest modern city is wrong (Lisbon
  for Portugal, not São Paulo); otherwise the largest city.
- **Power:** from area, scaled down for stateless peoples (land is not armies).
- **Ideology:** from the era and the name: empires, caliphates and khanates are `empire`;
  papal, caliphal and monastic states are `theocracy`; hunter-gatherers, pastoralists and
  peoples are `tribal` (also by region before contact); republics are `conservative` before
  1776; modern states are liberal unless listed.
- **Fixes:** a small table corrects known slips in the source (China in 1400 is the Ming).

## Relief

`build_relief.py` turns Natural Earth's high-resolution shaded relief into the shade tiles in
`Data/relief/`: grey and alpha, so mountains darken and catch the light while flat ground and
sea stay clear. It needs numpy.

```sh
curl -fLO https://naciscdn.org/naturalearth/10m/raster/SR_HR.zip && unzip SR_HR.zip
python3 -m venv /tmp/venv && /tmp/venv/bin/pip install numpy
/tmp/venv/bin/python tools/geo/build_relief.py SR_HR.tif Data/relief
```

- **The world overview** (5400 × 2700) is drawn when zoomed out.
- **The 30° tiles** (900 px, about 3.7 km a pixel, each with a one-pixel border from its
  neighbours so they join without seams) are loaded as the view needs them. Tiles of open sea
  are left out.
- **Smoothing:** the source is averaged down and lightly blurred, so it shades smoothly when a
  region map stretches it.
- **Generated sandbox maps** have invented coasts, so they get no relief.

## Sources and licences

- **historical-basemaps** (github.com/aourednik/historical-basemaps): historical borders.
  GNU GPL v3.0. See `Data/geo/ATTRIBUTION.md`.

- **Natural Earth** (naturalearthdata.com): land, lakes, rivers, land borders, provinces. Public domain.
- **City of Paris, "Arrondissements"** (opendata.paris.fr): arrondissement boundaries.
  Open Database License (ODbL). See `Data/geo/ATTRIBUTION.md`.
