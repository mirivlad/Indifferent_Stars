shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never;

uniform vec3 dust_color : source_color = vec3(0.82, 0.70, 0.52);
uniform vec3 ice_color : source_color = vec3(0.55, 0.68, 0.82);
uniform float opacity = 0.75;

void fragment() {
    float r = UV.x;
    float bands = sin(r * 96.0) * 0.5 + 0.5;
    float fine = sin(r * 310.0 + 1.7) * 0.5 + 0.5;
    float gap = smoothstep(0.42, 0.46, r) * (1.0 - smoothstep(0.50, 0.54, r));
    float fade = smoothstep(0.0, 0.06, r) * (1.0 - smoothstep(0.92, 1.0, r));
    float brightness = 0.42 + bands * 0.5 + fine * 0.18;
    vec3 col = mix(dust_color, ice_color, smoothstep(0.2, 0.85, r * 0.7 + bands * 0.3));
    col *= brightness;
    ALBEDO = col;
    ALPHA = clamp((0.20 + brightness * 0.62) * fade * (1.0 - gap * 0.85) * opacity, 0.0, 1.0);
}