shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never;

uniform vec3 atmosphere_color : source_color = vec3(0.28, 0.55, 1.0);
uniform float strength = 1.5;

void fragment() {
    float rim = pow(1.0 - abs(dot(NORMAL, normalize(VIEW))), 2.6);
    ALBEDO = atmosphere_color * rim * strength;
    ALPHA = clamp(rim * 0.85, 0.0, 1.0);
}