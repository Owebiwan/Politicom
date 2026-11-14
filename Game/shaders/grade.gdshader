// Over the whole map: a light colour grade (a touch cooler and clearer, a little more colour and
// contrast, as Victoria 3's map is), a faint vignette,
// and, as the view widens, drifting clouds with their shadows, anchored to the world so they
// stay put as the map pans. They clear again when the map turns into the paper chart.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec2 origin;      // lon/lat at the top-left
uniform vec2 deg_per_px;  // degrees per pixel
uniform vec2 size;
uniform float clouds = 0.0; // 0..1: how cloudy the view is (from the zoom)
uniform float grade = 1.0;  // 0 = off (city maps, generated maps keep their own look)
uniform float snowfall = 0.0; // 0..1: falling snow over the view

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}
float fbm(vec2 p) {
    float h = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { h += a * noise(p); p = p * 2.07 + vec2(13.1, 7.7); a *= 0.5; }
    return h;
}
float cloud(vec2 ll) {
    vec2 p = ll / 9.0 + vec2(TIME * 0.012, TIME * 0.004);
    float c = fbm(p) * 0.65 + fbm(p * 2.3 + 4.0) * 0.35;
    return smoothstep(0.52, 0.78, c);
}

void fragment() {
    vec3 col = texture(screen_tex, SCREEN_UV).rgb;
    vec2 px = UV * size;
    if (grade > 0.0) {
        float lum = dot(col, vec3(0.299, 0.587, 0.114));
        vec3 g = mix(vec3(lum), col, 1.1);                  // a touch more colour
        g = g + (g - 0.5) * 0.07;                           // and contrast
        g *= vec3(0.985, 1.0, 1.03);                        // cooler and cleaner
        col = mix(col, clamp(g, 0.0, 1.0), grade);
        vec2 v = UV - 0.5;
        col *= 1.0 - grade * 0.16 * smoothstep(0.4, 0.9, length(v * vec2(1.0, 1.25)));
    }
    if (clouds > 0.0) {
        vec2 ll = origin + px * deg_per_px;
        float c = cloud(ll) * clouds;
        float shadow = cloud(ll + vec2(-0.9, 0.7)) * clouds;  // shadows fall south-east
        col *= 1.0 - 0.28 * shadow;
        col = mix(col, vec3(0.93, 0.93, 0.9), c * 0.72);
    }
    if (snowfall > 0.0) {
        // Three layers of flakes, near ones larger and faster, drifting as they fall.
        for (int i = 0; i < 3; i++) {
            float layer = float(i);
            float cell = 26.0 + layer * 14.0;
            vec2 p = px / cell + vec2(sin(TIME * 0.4 + layer) * 0.6, -TIME * (0.5 + layer * 0.35));
            vec2 id = floor(p);
            vec2 f = fract(p) - 0.5;
            vec2 jitter = vec2(hash(id + layer * 7.1), hash(id + 3.3 + layer)) - 0.5;
            float r = 0.05 + 0.04 * layer;
            float flake = 1.0 - smoothstep(r * 0.5, r, length(f - jitter * 0.7));
            flake *= step(0.55, hash(id * 1.7 + layer));
            col = mix(col, vec3(0.96, 0.97, 1.0), flake * snowfall * (0.45 + 0.2 * layer));
        }
    }
    COLOR = vec4(col, 1.0);
}
