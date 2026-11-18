// Behind a window: the map blurred and darkened, rising with `amount`
// (0 = clear, 1 = fully blurred) so a window can ease in.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float amount = 1.0;
uniform float darken = 0.55;

void fragment() {
    vec3 col = textureLod(screen_tex, SCREEN_UV, 3.2 * amount).rgb;
    // A wider soft sample on top of the mip blur, so it doesn't look blocky.
    vec2 px = SCREEN_PIXEL_SIZE * 6.0 * amount;
    col = col * 0.4 + 0.15 * (textureLod(screen_tex, SCREEN_UV + vec2(px.x, px.y), 3.2 * amount).rgb
        + textureLod(screen_tex, SCREEN_UV + vec2(-px.x, px.y), 3.2 * amount).rgb
        + textureLod(screen_tex, SCREEN_UV + vec2(px.x, -px.y), 3.2 * amount).rgb
        + textureLod(screen_tex, SCREEN_UV + vec2(-px.x, -px.y), 3.2 * amount).rgb);
    // Darker toward the edges, so the window in the middle holds the eye.
    float v = length((SCREEN_UV - 0.5) * vec2(1.0, 1.2));
    col *= 1.0 - amount * (darken + 0.25 * smoothstep(0.2, 0.8, v));
    COLOR = vec4(col, 1.0);
}
