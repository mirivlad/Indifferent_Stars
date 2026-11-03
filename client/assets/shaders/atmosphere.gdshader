shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never;

uniform vec3 atmosphere_color : source_color = vec3(0.28, 0.55, 1.0);
uniform float strength = 1.5;
uniform vec3 sun_direction = vec3(-1.0, 0.0, 0.0);
varying vec3 shell_normal;

void vertex() {
    shell_normal = NORMAL;
}

void fragment() {
    float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
    // Fade the outer silhouette instead of cutting maximum additive brightness to black.
    float edge_fade = smoothstep(0.0, max(0.06, fwidth(facing) * 1.5), facing);
    float rim = pow(1.0 - facing, 5.0) * edge_fade;
    float sunlight = smoothstep(-0.18, 0.25, dot(normalize(shell_normal), sun_direction));
    ALBEDO = atmosphere_color * rim * strength * mix(0.06, 1.0, sunlight);
    ALPHA = clamp(rim * 0.85, 0.0, 1.0);
}
