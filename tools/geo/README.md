# Map data

`build_geo.py` turns public map data into the small per-scenario files in `Data/geo/`.
Run it again if you change a scenario's area or add a scenario.

```sh
mkdir -p /tmp/geo-src && cd /tmp/geo-src
base=https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson
for f in ne_10m_land ne_10m_lakes ne_10m_rivers_lake_centerlines ne_10m_admin_0_boundary_lines_land; do
  curl -fLO "$base/$f.geojson"
done
curl -fL -o paris_arrondissements.geojson \
  "https://opendata.paris.fr/api/explore/v2.1/catalog/datasets/arrondissements/exports/geojson"
cd - && python3 tools/geo/build_geo.py /tmp/geo-src Data/geo
```

Each scenario's area, detail level and options are set in the `REGIONS` table at the top of
the script. Some layers are left out where they'd be anachronistic:
- **Free Territory:** today's borders aren't drawn, and the Dnipro reservoirs (built from the
  1930s on) are dropped.

## Sources and licences

- **Natural Earth** (naturalearthdata.com): land, lakes, rivers, land borders. Public domain.
- **City of Paris, "Arrondissements"** (opendata.paris.fr): arrondissement boundaries.
  Open Database License (ODbL). See `Data/geo/ATTRIBUTION.md`.
