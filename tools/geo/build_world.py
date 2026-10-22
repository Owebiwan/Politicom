#!/usr/bin/env python3
"""
Builds Data/world/<era>.json: the world map for one era, from Natural Earth (public domain).

Today's countries are grouped into the states of the era by tools/geo/eras/<era>.json. No
polygon union is needed: neighbouring countries in Natural Earth share identical border
vertices, so borders between different period states are traced from shared edges, and
borders inside one state (e.g. India within the British Empire) are simply not drawn.

Usage:
  tools/geo/build_world.py SOURCE_DIR OUTPUT_DIR

SOURCE_DIR must contain ne_50m_admin_0_countries.geojson, ne_50m_land.geojson and
ne_10m_populated_places_simple.geojson (see tools/geo/README.md).
"""
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_geo import simplify, area, rings  # noqa: E402

TOL = 0.03          # degrees: about 3 km, plenty at world scale
MIN_AREA = 0.02     # square degrees: drop specks (but always keep a state's largest piece)
CITIES = 12
PALETTE = ["#7a8b6a", "#8a7a5e", "#6e7f8f", "#8f6e6e", "#6f8a7e", "#857089", "#8a8a62", "#5f7f6a",
           "#7f6a58", "#6a7896", "#93806a", "#6f8f8f", "#8b6f7c", "#76875a"]


def key(p):
    return (round(p[0], 5), round(p[1], 5))


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    src, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    countries = json.load(open(os.path.join(src, "ne_50m_admin_0_countries.geojson"), encoding="utf-8"))["features"]
    land = json.load(open(os.path.join(src, "ne_50m_land.geojson"), encoding="utf-8"))["features"]
    places = json.load(open(os.path.join(src, "ne_10m_populated_places_simple.geojson"), encoding="utf-8"))["features"]
    by_code = collections.defaultdict(list)
    for f in countries:
        by_code[f["properties"]["ADM0_A3"]].extend(rings(f["geometry"]))

    land_rings = []
    for f in land:
        for r in rings(f["geometry"]):
            s = simplify([tuple(p[:2]) for p in r], TOL)
            if max(y for _, y in s) < -55:  # Antarctica: no states, and it would be a box at the bottom
                continue
            if len(s) >= 3 and area(s) >= MIN_AREA:
                land_rings.append([[round(x, 3), round(y, 3)] for x, y in s])

    eras_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "eras")
    for name in sorted(os.listdir(eras_dir)):
        era = json.load(open(os.path.join(eras_dir, name), encoding="utf-8"))
        state_of = {}
        for s in era["states"]:
            for code in s["members"]:
                if code in state_of:
                    sys.exit(f"{name}: {code} is in both {state_of[code]} and {s['id']}")
                if code not in by_code:
                    sys.exit(f"{name}: unknown country code {code} in {s['id']}")
                state_of[code] = s["id"]

        # Borders: edges shared by countries of different period states (or a state and neutral land).
        owners = collections.defaultdict(set)
        for code, rs in by_code.items():
            for r in rs:
                for a, b in zip(r, r[1:]):
                    owners[tuple(sorted([key(a), key(b)]))].add(code)
        border_edges = []
        neighbours = collections.defaultdict(set)
        for edge, codes in owners.items():
            if len(codes) < 2:
                continue
            states = {state_of.get(c) for c in codes}
            if len(states) > 1 and states != {None}:
                border_edges.append(edge)
                named = [s for s in states if s]
                for a in named:
                    neighbours[a].update(b for b in named if b != a)
        borders = [[[round(x, 3), round(y, 3)] for x, y in simplify(line, TOL)] for line in chain(border_edges)]

        out_states = []
        for i, s in enumerate(era["states"]):
            pieces = []
            for code in s["members"]:
                for r in by_code[code]:
                    simp = simplify([tuple(p[:2]) for p in r], TOL)
                    if len(simp) >= 3:
                        pieces.append(simp)
            pieces.sort(key=area, reverse=True)
            pieces = [p for j, p in enumerate(pieces) if j == 0 or area(p) >= MIN_AREA]
            # Home-country cities first (the metropole, not its colonies), then by population.
            cities = sorted((p["properties"] for p in places if p["properties"]["adm0_a3"] in s["members"]),
                            key=lambda p: (p["adm0_a3"] != s["members"][0], -(p.get("pop_max") or 0)))
            capital = next((c for c in cities if c["name"] == s.get("capital") or c.get("nameascii") == s.get("capital")), None)
            if capital is None:
                capital = next((c for c in cities if c.get("adm0cap") == 1 and c["adm0_a3"] == s["members"][0]), cities[0] if cities else None)
            out_states.append({
                "id": s["id"],
                "name": s["name"],
                "ideology": s["ideology"],
                "power": s.get("power", 1),
                "neighbours": sorted(neighbours[s["id"]]),
                "color": s.get("color", PALETTE[i % len(PALETTE)]),
                "capital": {"name": s.get("capital", capital["name"]), "lon": round(capital["longitude"], 3), "lat": round(capital["latitude"], 3)} if capital else None,
                "cities": [{"name": c["name"], "lon": round(c["longitude"], 3), "lat": round(c["latitude"], 3), "pop": int(c.get("pop_max") or 0)} for c in cities[:CITIES]],
                "rings": [[[round(x, 3), round(y, 3)] for x, y in p] for p in pieces],
            })
        out = {"era": era["era"], "note": era.get("note", ""), "land": land_rings, "borders": borders, "states": out_states}
        path = os.path.join(out_dir, era["era"] + ".json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, separators=(",", ":"))
        print(f"{era['era']}: {len(out_states)} states, {sum(len(s['rings']) for s in out_states)} rings, "
              f"{len(borders)} border lines, {os.path.getsize(path) // 1024} KB")


def chain(edges):
    """Joins undirected edges into polylines."""
    adj = collections.defaultdict(list)
    for a, b in edges:
        adj[a].append(b)
        adj[b].append(a)
    seen = set()
    lines = []
    # Start at endpoints (degree != 2) first, then close any remaining loops.
    starts = [p for p in adj if len(adj[p]) != 2] + list(adj)
    for start in starts:
        for nxt in adj[start]:
            e = tuple(sorted([start, nxt]))
            if e in seen:
                continue
            line = [start]
            prev, cur = start, nxt
            seen.add(e)
            while True:
                line.append(cur)
                if len(adj[cur]) != 2:
                    break
                cand = [q for q in adj[cur] if tuple(sorted([cur, q])) not in seen]
                if not cand:
                    break
                prev, cur = cur, cand[0]
                seen.add(tuple(sorted([prev, cur])))
            lines.append(line)
    return lines


if __name__ == "__main__":
    main()
