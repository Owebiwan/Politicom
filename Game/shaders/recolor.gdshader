// A kit model's texture recoloured to one hue: its light and dark (window openings, frames,
// shutters, doors, tiles) kept as shading, its own colours replaced by `tone`, so a town's
// buildings share their age's plaster or stone and still show every window.
shader_type spatial;

uniform sampler2D tex : source_color, filter_linear_mipmap;
uniform vec3 tone : source_color = vec3(0.86, 0.79, 0.64);
uniform float contrast = 1.0;

void fragment() {
    vec3 c = texture(tex, UV).rgb;
    float l = dot(c, vec3(0.299, 0.587, 0.114));
    // Dark details (openings) sink well below the tone; light ones rise a little above it.
    float k = smoothstep(0.08, 0.7, l);
    ALBEDO = mix(tone * 0.28, tone * 1.08, pow(k, contrast));
    ROUGHNESS = 0.92;
}
