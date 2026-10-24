// The sea under the map: lit ripples that drift, deeper colour away from the coast, foam where
// it meets the land, and at world zoom a paper chart with engraved lines that follow the coast.
// Positions come in as longitude/latitude so the water stays put as the map pans and zooms.
shader_type canvas_item;

uniform sampler2D coast : filter_linear, repeat_enable; // sqrt(distance from coast / 800 km)
uniform bool has_coast = true;
uniform vec2 origin;      // lon/lat at the control's top-left
uniform vec2 deg_per_px;  // degrees per screen pixel (y negative: latitude falls downward)
uniform vec2 size;
uniform float paper = 0.0; // 0 = water, 1 = paper chart
uniform vec3 shallow = vec3(0.13, 0.25, 0.30);
uniform vec3 deep = vec3(0.045, 0.085, 0.14);
uniform vec3 foam_col = vec3(0.80, 0.86, 0.85);
uniform vec3 parchment = vec3(0.80, 0.74, 0.60);
uniform vec3 ink = vec3(0.34, 0.27, 0.19);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

float waves(vec2 p, float t) {
    float h = 0.0, a = 0.55;
    mat2 rot = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
    for (int i = 0; i < 4; i++) {
        h += a * noise(p + vec2(t, t * 0.7));
        p = rot * p * 2.03;
        a *= 0.5;
        t *= -1.3;
    }
    return h;
}

void fragment() {
    vec2 px = UV * size;
    vec2 ll = origin + px * deg_per_px;
    vec2 tuv = vec2(fract((ll.x + 180.0) / 360.0), clamp((90.0 - ll.y) / 180.0, 0.0, 1.0));
    // Past the poles there is no data (the lookup would smear its edge row): open sea.
    float c = has_coast && abs(ll.y) < 89.5 ? texture(coast, tuv).r : 1.0;
    float dkm = c * c * 800.0;
    float km_px = abs(deg_per_px.x) * 111.0;

    // Ripples anchored to the world, about 60 screen pixels long at any zoom.
    vec2 p = ll / abs(deg_per_px) / 60.0 * vec2(1.0, -1.0);
    float t = TIME * 0.18;
    float e = 0.06;
    float h = waves(p, t);
    float hx = waves(p + vec2(e, 0.0), t) - waves(p - vec2(e, 0.0), t);
    float hy = waves(p + vec2(0.0, e), t) - waves(p - vec2(0.0, e), t);
    vec3 n = normalize(vec3(-hx * 1.6, -hy * 1.6, 1.0));
    vec3 l = normalize(vec3(-0.45, -0.55, 0.7));
    float diffuse = dot(n, l);
    float spec = pow(max(dot(n, normalize(l + vec3(0.0, 0.0, 1.0))), 0.0), 70.0);

    vec3 col = mix(shallow, deep, smoothstep(0.0, 0.85, c));
    col *= 0.82 + 0.3 * diffuse;
    col += spec * 0.28 * vec3(1.0, 0.95, 0.82);

    // Foam: a broken band a few screen pixels wide along the coast.
    float band = clamp(km_px * 6.0, 1.5, 60.0); // a few pixels close in, never a halo zoomed out
    float foam = (1.0 - smoothstep(band * 0.25, band, dkm)) * (0.45 + 0.55 * noise(p * 3.0 + vec2(t * 3.0, -t * 2.0)));
    col = mix(col, foam_col, has_coast ? clamp(foam, 0.0, 1.0) * 0.6 : 0.0);

    // The paper chart: parchment, with engraved lines following the coast, closer near the land.
    float spacing = km_px * 7.0 * (1.0 + dkm / 400.0);
    float line = abs(fract(dkm / spacing) - 0.5) * 2.0;
    float engraved = (1.0 - smoothstep(0.0, 0.22, line)) * exp(-dkm / 450.0) * (has_coast ? 1.0 : 0.0);
    vec3 chart = parchment * (0.94 + 0.06 * noise(px * 0.35)) * (1.0 - 0.06 * smoothstep(0.0, 1.0, c));
    chart = mix(chart, ink, engraved * 0.55);
    COLOR = vec4(mix(col, chart, paper), 1.0);
}
