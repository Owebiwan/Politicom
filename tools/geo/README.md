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

## Sources and licences

- **Natural Earth** (naturalearthdata.com): land, lakes, rivers, land borders, provinces. Public domain.
- **City of Paris, "Arrondissements"** (opendata.paris.fr): arrondissement boundaries.
  Open Database License (ODbL). See `Data/geo/ATTRIBUTION.md`.
