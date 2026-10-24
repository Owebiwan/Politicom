// The land under the map, in the manner of Victoria 3: the ground itself (NASA's Blue Marble,
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

varying vec2 ll;

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
        float lum = dot(ground, vec3(0.299, 0.587, 0.114));
        // The colour carries the ground's light: bright plains, dark forests and mountain shadow show through.
        vec3 tinted = c.rgb * (0.42 + 1.15 * lum) + (ground - lum) * 0.25;
        vec3 col = mix(ground, tinted, c.a);
        // The paper chart: flat colours (bare land in parchment) with a trace of the relief.
        vec3 chart = mix(parchment, c.rgb, c.a > 0.01 ? 0.78 : 0.0) * (0.88 + 0.24 * lum);
        COLOR = vec4(mix(col, chart, paper), 1.0);
    }
}
