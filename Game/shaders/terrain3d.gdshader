// The 3D map's ground: a grid raised by the real elevation and draped with the flat map (drawn
// off-screen by MapView). Mountains are lifted (exaggerated, as on Victoria 3's map) and lit a
// little from the north-west so their slopes catch the light; the distance fades into haze.
shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D map_tex : source_color, filter_linear;
uniform sampler2D height : filter_linear, repeat_enable;
uniform vec2 extent;            // the map's drawing area, in pixels (= world units)
uniform vec2 origin;            // lon/lat at its top-left
uniform vec2 deg_per_px;        // degrees per pixel
uniform float lift = 0.0;       // world units of height per metre (0 = flat)
uniform vec3 haze : source_color = vec3(0.11, 0.15, 0.2);
uniform float haze_near = 1000.0;
uniform float haze_far = 3000.0;
uniform float haze_amount = 0.0;

varying float shade;

float elevation(vec2 uv) {
    vec2 ll = origin + uv * extent * deg_per_px;
    if (abs(ll.y) > 89.0) return 0.0;
    return texture(height, vec2((ll.x + 180.0) / 360.0, (90.0 - ll.y) / 180.0)).r * 8848.0;
}

void vertex() {
    shade = 1.0;
    if (lift > 0.0) {
        float h = elevation(UV);
        VERTEX.y += h * lift;
        // Slope lighting from the neighbouring heights, to give the raised relief its form.
        vec2 e = vec2(1.0) / extent * 6.0;
        float hx = (elevation(UV + vec2(e.x, 0.0)) - elevation(UV - vec2(e.x, 0.0))) * lift;
        float hz = (elevation(UV + vec2(0.0, e.y)) - elevation(UV - vec2(0.0, e.y))) * lift;
        vec3 n = normalize(vec3(-hx, 12.0, -hz));
        shade = 0.82 + 0.3 * dot(n, normalize(vec3(-0.5, 1.0, -0.6)));
    }
}

void fragment() {
    vec3 c = texture(map_tex, UV).rgb * shade;
    float d = length(VERTEX);
    float f = smoothstep(haze_near, haze_far, d) * haze_amount;
    ALBEDO = mix(c, haze, f);
}
