shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

uniform float energy = 1.0;
varying float axial;

void vertex() {
    axial = clamp(VERTEX.y + 0.5, 0.0, 1.0);
}

void fragment() {
    float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
    float core = pow(facing, 2.5);
    float envelope = smoothstep(0.0, 0.045, axial) * pow(max(0.0, 1.0 - axial), 1.4);
    float waves = 0.86 + 0.09 * sin(axial * 32.0 - TIME * 9.0)
        + 0.05 * sin(axial * 71.0 + TIME * 13.0);
    vec3 fringe = mix(vec3(0.08, 0.34, 1.0), vec3(0.43, 0.13, 0.88), axial);
    vec3 color = mix(fringe, vec3(0.66, 0.92, 1.0), core * (1.0 - axial * 0.65));
    ALBEDO = color * envelope;
    EMISSION = color * energy * (0.6 + core * 1.6) * envelope * waves;
    ALPHA = envelope * smoothstep(0.0, 0.65, facing) * waves * 0.65;
}
