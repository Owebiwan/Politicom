// Over the whole map: a light colour grade (warmer, a touch more contrast), a soft vignette,
// and, as the view widens, drifting clouds with their shadows, anchored to the world so they
// stay put as the map pans. They clear again when the map turns into the paper chart.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec2 origin;      // lon/lat at the top-left
uniform vec2 deg_per_px;  // degrees per pixel
uniform vec2 size;
uniform float clouds = 0.0; // 0..1: how cloudy the view is (from the zoom)
uniform float grade = 1.0;  // 0 = off (city maps, generated maps keep their own look)

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
        vec3 g = mix(vec3(lum), col, 1.06);                 // a touch more colour
        g = g + (g - 0.5) * 0.06;                           // and contrast
        g *= vec3(1.03, 1.0, 0.95);                         // warmer
        col = mix(col, clamp(g, 0.0, 1.0), grade);
        vec2 v = UV - 0.5;
        col *= 1.0 - grade * 0.28 * smoothstep(0.35, 0.85, length(v * vec2(1.0, 1.25)));
    }
    if (clouds > 0.0) {
        vec2 ll = origin + px * deg_per_px;
        float c = cloud(ll) * clouds;
        float shadow = cloud(ll + vec2(-0.9, 0.7)) * clouds;  // shadows fall south-east
        col *= 1.0 - 0.28 * shadow;
        col = mix(col, vec3(0.93, 0.93, 0.9), c * 0.72);
    }
    COLOR = vec4(col, 1.0);
}
