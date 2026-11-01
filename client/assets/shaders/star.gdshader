shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque;

uniform vec3 core_color : source_color = vec3(1.0, 0.74, 0.42);
uniform vec3 hot_color : source_color = vec3(1.0, 0.95, 0.82);
uniform float pulse_speed = 0.08;

float hash13(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float noise3(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(hash13(i), hash13(i + vec3(1,0,0)), f.x),
            mix(hash13(i + vec3(0,1,0)), hash13(i + vec3(1,1,0)), f.x), f.y),
        mix(mix(hash13(i + vec3(0,0,1)), hash13(i + vec3(1,0,1)), f.x),
            mix(hash13(i + vec3(0,1,1)), hash13(i + vec3(1,1,1)), f.x), f.y),
        f.z);
}

void fragment() {
    vec3 p = vec3(UV * 14.0, TIME * pulse_speed);
    float n = noise3(p) * 0.55 + noise3(p * 2.4 + 3.0) * 0.3 + noise3(p * 5.1 - 7.0) * 0.15;
    float limb = smoothstep(0.0, 0.55, dot(NORMAL, normalize(VIEW)));
    vec3 col = mix(core_color, hot_color, smoothstep(0.35, 0.8, n));
    col *= 0.75 + n * 0.9;
    col *= 0.85 + limb * 0.45;
    ALBEDO = col * 1.25;
    EMISSION = col * 1.45;
}