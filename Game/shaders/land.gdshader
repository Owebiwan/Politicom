// The land under the map: the ground itself (NASA's Blue Marble,
// graded and hill-shaded by tools/geo/build_land.py) with each country's colour laid over it
// as a tint that keeps the terrain's light and shade. Vertex colour is the country's colour;
// its alpha is how strongly it tints (0 = bare ground). Zoomed out to the world it turns into
// a paper chart of flat colours. With terrain off it draws plain vertex colours.
shader_type canvas_item;

uniform sampler2D overview : filter_linear_mipmap, repeat_enable; // the whole world, 4096 x 2048
uniform sampler2D detail : filter_linear_mipmap;                  // full resolution around the region
uniform vec4 detail_rect;     // west, north, east, south (degrees)
uniform bool has_detail = false;
uniform bool terrain = true;
uniform vec2 ll_origin;       // lon/lat of the layout's origin
uniform vec2 ll_per_unit;     // lon/lat per layout unit
uniform float paper = 0.0;    // 0 = terrain, 1 = paper chart
uniform vec3 parchment = vec3(0.84, 0.78, 0.64);
uniform float winter_north = 0.0; // 0..1: how deep in winter each hemisphere is
uniform float winter_south = 0.0;

varying vec2 ll;

// ── Close-up detail: fields, woods and rough ground, in kilometres on the ground ──

vec2 hash2(vec2 p) {
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return fract(sin(p) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash2(i).x, hash2(i + vec2(1, 0)).x, u.x), mix(hash2(i + vec2(0, 1)).x, hash2(i + vec2(1, 1)).x, u.x), u.y);
}

// Voronoi cells: the nearest cell's random id, and the distance to its edge.
vec3 cells(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    float best = 8.0, second = 8.0;
    vec2 id = vec2(0);
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++) {
            vec2 g = vec2(float(x), float(y));
            vec2 o = hash2(i + g);
            vec2 r = g + o * 0.85 - f;
            float d = dot(r, r);
            if (d < best) { second = best; best = d; id = i + g; }
            else if (d < second) { second = d; }
        }
    return vec3(hash2(id), sqrt(second) - sqrt(best));
}

// The ground close up, from what the satellite picture says it is: a patchwork of fields with
// furrows and hedgerows on open farmland, a dappled canopy in forests, grain on rough high
// ground. `fade` is 0 far out (where it would only shimmer) and 1 close in.
vec3 close_up(vec3 ground, vec2 km, float fade) {
    if (fade <= 0.0) return ground;
    float lum = dot(ground, vec3(0.299, 0.587, 0.114));
    float sat = max(ground.r, max(ground.g, ground.b)) - min(ground.r, min(ground.g, ground.b));
    float green = ground.g - max(ground.r, ground.b);
    float wood = smoothstep(0.0, 0.05, green) * (1.0 - smoothstep(0.28, 0.42, lum));
    float farm = (1.0 - wood) * smoothstep(0.12, 0.2, lum) * (1.0 - smoothstep(0.62, 0.75, lum)) * (1.0 - smoothstep(0.25, 0.4, sat));
    // Fields: strips of a few hundred metres to a kilometre or two, laid out along each district's
    // own lie of the land (districts of ~10 km), their edges a little wavering; each sown
    // differently and ploughed along its length.
    vec3 district = cells(km / 10.0);
    float lie = district.x * 3.14159;
    mat2 turn = mat2(vec2(cos(lie), sin(lie)), vec2(-sin(lie), cos(lie)));
    vec2 q = turn * km + vec2(noise(km * 1.7), noise(km * 1.7 + 9.0)) * 0.35;
    float scale = mix(0.7, 1.6, district.y);
    vec3 f = cells(q / vec2(scale * 2.0, scale * 0.8));
    float kind = f.x;
    vec3 crop = kind < 0.3 ? vec3(1.06, 1.03, 0.92)    // ripe grain
              : kind < 0.55 ? vec3(0.95, 1.03, 0.93)   // green crops, pasture
              : kind < 0.72 ? vec3(0.93, 0.89, 0.85)   // ploughed earth
              : vec3(1.0);                             // fallow
    crop = mix(vec3(1.0), crop, 0.75 + 0.25 * f.y);
    float furrow = 0.975 + 0.025 * sin(q.x * 45.0 / scale);
    float hedge = 1.0 - 0.1 * (1.0 - smoothstep(0.015, 0.05, f.z)) * step(0.35, f.y);
    vec3 fields = ground * crop * furrow * hedge;
    // Woods: crowns and gaps.
    float canopy = noise(km * 9.0) * 0.6 + noise(km * 23.0) * 0.4;
    vec3 woods = ground * (0.8 + 0.42 * canopy);
    // Everywhere a little grain, so no ground is flat.
    float grain = 0.94 + 0.12 * (noise(km * 4.0) * 0.5 + noise(km * 15.0) * 0.5);
    vec3 detailed = mix(mix(ground * grain, woods, wood), fields, farm);
    return mix(ground, detailed, fade);
}

void vertex() {
    ll = ll_origin + VERTEX * ll_per_unit;
}

void fragment() {
    vec4 c = COLOR;
    if (!terrain) {
        COLOR = vec4(c.rgb, 1.0);
    } else {
        vec3 ground = texture(overview, vec2((ll.x + 180.0) / 360.0, clamp((90.0 - ll.y) / 180.0, 0.0, 1.0))).rgb;
        if (has_detail) {
            vec2 duv = vec2((ll.x - detail_rect.x) / (detail_rect.z - detail_rect.x), (detail_rect.y - ll.y) / (detail_rect.y - detail_rect.w));
            if (duv.x >= 0.0 && duv.x <= 1.0 && duv.y >= 0.0 && duv.y <= 1.0) {
                vec4 d = texture(detail, duv);
                float edge = min(min(duv.x, 1.0 - duv.x), min(duv.y, 1.0 - duv.y));
                ground = mix(ground, d.rgb, d.a * smoothstep(0.0, 0.03, edge));
            }
        }
        // Winter snow: down from the far north (or south) as the season deepens.
        float w = ll.y >= 0.0 ? winter_north : winter_south;
        float line = 66.0 - 12.0 * w;
        float snow = w * smoothstep(line - 3.0, line + 3.0, abs(ll.y));
        float glum = dot(ground, vec3(0.299, 0.587, 0.114));
        ground = mix(ground, vec3(0.92, 0.93, 0.95) * (0.82 + 0.3 * glum), snow * 0.65);
        // Close in, the ground gains fields, woods and grain (in kilometres, fading out as a
        // kilometre shrinks below a few pixels).
        vec2 km = vec2(ll.x * cos(radians(ll.y)), ll.y) * 111.0;
        float px_per_km = 1.0 / max(1e-6, length(fwidth(km)));
        ground = close_up(ground, km, smoothstep(6.0, 16.0, px_per_km) * (1.0 - paper));
        float lum = dot(ground, vec3(0.299, 0.587, 0.114));
        // The colour carries the ground's light and some of its own colour: bright plains, dark
        // forests, fields and mountain shadow show through.
        vec3 tinted = c.rgb * (0.42 + 1.15 * lum) + (ground - lum) * 0.45;
        vec3 col = mix(ground, tinted, c.a);
        // The paper chart: flat colours (bare land in parchment) with a trace of the relief.
        vec3 chart = mix(parchment, c.rgb, c.a > 0.01 ? 0.88 : 0.0) * (0.88 + 0.24 * lum);
        COLOR = vec4(mix(col, chart, paper), 1.0);
    }
}
