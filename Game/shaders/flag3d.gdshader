// A standard's cloth, rippling in the wind: plain, or divided on the diagonal (black over red
// for the CNT-FAI). UV.x runs from the pole (0) to the fly (1).
shader_type spatial;
render_mode cull_disabled;

uniform vec3 upper : source_color = vec3(0.8, 0.1, 0.1);
uniform vec3 lower : source_color = vec3(0.8, 0.1, 0.1);
uniform bool split = false;

void vertex() {
    float wave = sin(TIME * 4.0 + UV.x * 6.0) * 0.07 * UV.x;
    VERTEX.z += wave;
    VERTEX.y += sin(TIME * 3.0 + UV.x * 4.0) * 0.02 * UV.x;
}

void fragment() {
    vec3 c = upper;
    if (split && UV.y > 1.0 - UV.x) c = lower;
    // The folds catch the light.
    float fold = 0.85 + 0.15 * sin(TIME * 4.0 + UV.x * 6.0 + 1.2);
    ALBEDO = c * fold;
    ROUGHNESS = 0.9;
}
