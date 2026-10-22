#!/usr/bin/env python3
"""
Builds Data/geo/<scenario>.json from public map data: land, lakes, rivers and land borders
from Natural Earth (public domain), and Paris's arrondissement boundaries from the City of
Paris open-data portal (ODbL). Each scenario gets only its own area, simplified to the detail
its scale needs, so the game ships a few kilobytes instead of tens of megabytes.

Usage:
  tools/geo/build_geo.py SOURCE_DIR OUTPUT_DIR

SOURCE_DIR must contain (download with tools/geo/README.md):
  ne_10m_land.geojson, ne_10m_lakes.geojson, ne_10m_rivers_lake_centerlines.geojson,
  ne_10m_admin_0_boundary_lines_land.geojson, ne_10m_admin_1_states_provinces.geojson,
  paris_arrondissements.geojson
"""
import json
import os
import re
import sys

# Area (lon_min, lat_min, lon_max, lat_max), simplification tolerance in degrees,
# whether to draw today's land borders (only where they match the period), and whether
# commune territories come from real boundaries rather than being computed.
REGIONS = {
    # Spain's provinces date from 1833, so their boundaries are right for 1936 and can be drawn.
    "catalonia_1936": dict(bbox=(-1.6, 40.1, 3.7, 43.0), tol=0.006, borders=True,
                           provinces=("ESP", ["Barcelona", "Girona", "Lleida", "Tarragona", "Huesca", "Zaragoza", "Teruel"]),
                           # Catalonia was held entire; in Aragon only the east, behind the front.
                           contested=["Huesca", "Zaragoza", "Teruel"], draw_provinces=True),
    # 1918: today's borders didn't exist, and the Dnipro "lakes" are Soviet-era reservoirs.
    # Today's oblasts roughly cover the old Katerynoslav and Taurida governorates: good enough to
    # shape the federation's edge, but not drawn, since the lines themselves are anachronistic.
    "free_territory_1918": dict(bbox=(33.2, 45.3, 39.0, 49.4), tol=0.006, borders=False, lakes=False,
                                provinces=("UKR", ["Dnipropetrovsk", "Zaporizhzhia", "Donetsk"]), draw_provinces=False,
                                # The Free Territory never had fixed borders: it was wherever the insurgents held.
                                contested=["Dnipropetrovsk", "Zaporizhzhia", "Donetsk"]),
    "paris_commune_1871": dict(bbox=(2.20, 48.79, 2.48, 48.93), tol=0.0002, borders=False, territories="paris"),
}


def load(src, name):
    with open(os.path.join(src, name), encoding="utf-8") as f:
        return json.load(f)["features"]


def rings(geometry):
    """Outer rings of a Polygon or MultiPolygon (holes are ignored: none matter at these scales)."""
    t, c = geometry["type"], geometry["coordinates"]
    if t == "Polygon":
        return [c[0]]
    if t == "MultiPolygon":
        return [p[0] for p in c]
    return []


def lines(geometry):
    t, c = geometry["type"], geometry["coordinates"]
    if t == "LineString":
        return [c]
    if t == "MultiLineString":
        return c
    return []


def clip_polygon(ring, bbox):
    """Sutherland–Hodgman against the four edges of the box."""
    x0, y0, x1, y1 = bbox
    edges = [
        (lambda p: p[0] >= x0, lambda a, b: (x0, a[1] + (b[1] - a[1]) * (x0 - a[0]) / (b[0] - a[0]))),
        (lambda p: p[0] <= x1, lambda a, b: (x1, a[1] + (b[1] - a[1]) * (x1 - a[0]) / (b[0] - a[0]))),
        (lambda p: p[1] >= y0, lambda a, b: (a[0] + (b[0] - a[0]) * (y0 - a[1]) / (b[1] - a[1]), y0)),
        (lambda p: p[1] <= y1, lambda a, b: (a[0] + (b[0] - a[0]) * (y1 - a[1]) / (b[1] - a[1]), y1)),
    ]
    pts = [tuple(p[:2]) for p in ring]
    for inside, cross in edges:
        if not pts:
            break
        out = []
        for i, cur in enumerate(pts):
            prev = pts[i - 1]
            if inside(cur):
                if not inside(prev):
                    out.append(cross(prev, cur))
                out.append(cur)
            elif inside(prev):
                out.append(cross(prev, cur))
        pts = out
    return pts


def clip_line(line, bbox):
    """Splits a polyline into the pieces inside the box (vertex-level: good enough once simplified)."""
    x0, y0, x1, y1 = bbox
    inside = lambda p: x0 <= p[0] <= x1 and y0 <= p[1] <= y1
    pieces, cur = [], []
    for p in line:
        if inside(p):
            cur.append(tuple(p[:2]))
        elif cur:
            pieces.append(cur)
            cur = []
    if cur:
        pieces.append(cur)
    return [p for p in pieces if len(p) >= 2]


def simplify(points, tol):
    """Douglas–Peucker."""
    if len(points) < 3:
        return points
    (ax, ay), (bx, by) = points[0], points[-1]
    dx, dy = bx - ax, by - ay
    norm = (dx * dx + dy * dy) ** 0.5
    best, index = 0.0, 0
    for i in range(1, len(points) - 1):
        px, py = points[i]
        d = abs(dy * px - dx * py + bx * ay - by * ax) / norm if norm else ((px - ax) ** 2 + (py - ay) ** 2) ** 0.5
        if d > best:
            best, index = d, i
    if best <= tol:
        return [points[0], points[-1]]
    return simplify(points[: index + 1], tol)[:-1] + simplify(points[index:], tol)


def bbox_hits(geometry, bbox):
    x0, y0, x1, y1 = bbox
    pts = [p for r in rings(geometry) for p in r] + [p for l in lines(geometry) for p in l]
    return any(x0 <= p[0] <= x1 and y0 <= p[1] <= y1 for p in pts)


def rounded(points, digits):
    return [[round(x, digits), round(y, digits)] for x, y in points]


def area(ring):
    return abs(sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(ring, ring[1:] + ring[:1]))) / 2


def build(src, region_id, spec):
    bbox, tol = spec["bbox"], spec["tol"]
    digits = 5 if tol < 0.001 else 4
    min_area = (tol * 4) ** 2
    out = {"bbox": list(bbox), "land": [], "lakes": [], "rivers": [], "borders": []}

    for name, key in (("ne_10m_land.geojson", "land"), ("ne_10m_lakes.geojson", "lakes")):
        if key == "lakes" and not spec.get("lakes", True):
            continue
        for f in load(src, name):
            if not bbox_hits(f["geometry"], bbox):
                continue
            for ring in rings(f["geometry"]):
                clipped = simplify(clip_polygon(ring, bbox), tol)
                if len(clipped) >= 3 and area(clipped) >= min_area:
                    out[key].append(rounded(clipped, digits))
    if not out["land"]:  # a city map: the whole box is land
        x0, y0, x1, y1 = bbox
        out["land"].append([[x0, y0], [x1, y0], [x1, y1], [x0, y1]])

    for f in load(src, "ne_10m_rivers_lake_centerlines.geojson"):
        if not bbox_hits(f["geometry"], bbox):
            continue
        name = f["properties"].get("name") or ""
        for line in lines(f["geometry"]):
            for piece in clip_line(line, bbox):
                piece = simplify(piece, tol)
                if len(piece) >= 2:
                    out["rivers"].append({"name": name, "points": rounded(piece, digits)})

    if spec.get("borders"):
        for f in load(src, "ne_10m_admin_0_boundary_lines_land.geojson"):
            if not bbox_hits(f["geometry"], bbox):
                continue
            for line in lines(f["geometry"]):
                for piece in clip_line(line, bbox):
                    piece = simplify(piece, tol)
                    if len(piece) >= 2:
                        out["borders"].append(rounded(piece, digits))

    if "provinces" in spec:
        country, wanted = spec["provinces"]
        out["provinces"] = []
        out["drawProvinces"] = bool(spec.get("draw_provinces"))
        for f in load(src, "ne_10m_admin_1_states_provinces.geojson"):
            p = f["properties"]
            if p.get("adm0_a3") != country or p.get("name_en") not in wanted:
                continue
            for ring in rings(f["geometry"]):
                clipped = simplify(clip_polygon(ring, bbox), tol)
                if len(clipped) >= 3 and area(clipped) >= min_area:
                    out["provinces"].append({"name": p["name_en"], "ring": rounded(clipped, digits),
                                             "contested": p["name_en"] in spec.get("contested", [])})
        found = {p["name"] for p in out["provinces"]}
        if found != set(wanted):
            raise SystemExit(f"{region_id}: provinces not found: {sorted(set(wanted) - found)}")

    if spec.get("territories") == "paris":
        out["territories"] = paris_territories(src, region_id, tol, digits)
    return out


def paris_territories(src, region_id, tol, digits):
    """Real arrondissement boundaries, keyed by the scenario's commune names (matched on the number)."""
    names = {}
    scenario_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Data", "scenarios", region_id + ".json")
    text = open(scenario_path, encoding="utf-8").read()
    for name in re.findall(r'"name": "((\d+)(?:st|nd|rd|th)[^"]*)"', text):
        names[int(name[1])] = name[0]
    result = {}
    for f in load(src, "paris_arrondissements.geojson"):
        number = int(f["properties"]["c_ar"])
        if number in names:
            ring = max(rings(f["geometry"]), key=lambda r: area([tuple(p[:2]) for p in r]))
            result[names[number]] = rounded(simplify([tuple(p[:2]) for p in ring], tol), digits)
    missing = sorted(set(names) - {n for n in names if names[n] in result})
    if missing:
        raise SystemExit(f"No boundary found for arrondissements {missing}")
    return result


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    src, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    for region_id, spec in REGIONS.items():
        geo = build(src, region_id, spec)
        path = os.path.join(out_dir, region_id + ".json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(geo, f, ensure_ascii=False, separators=(",", ":"))
        print(f"{region_id}: {len(geo['land'])} land, {len(geo['lakes'])} lakes, {len(geo['rivers'])} river pieces, "
              f"{len(geo['borders'])} borders, {len(geo.get('provinces', []))} province rings, "
              f"{len(geo.get('territories', {}))} territories, {os.path.getsize(path) // 1024} KB")


if __name__ == "__main__":
    main()
