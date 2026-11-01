shader_type sky;

uniform vec3 deep_space : source_color = vec3(0.003, 0.005, 0.012);
uniform vec3 nebula_a : source_color = vec3(0.05, 0.10, 0.20);
uniform vec3 nebula_b : source_color = vec3(0.16, 0.06, 0.20);
uniform float star_gain = 1.0;
uniform int detail = 2;

float hash13(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float value_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = hash13(i);
    float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash13(i + vec3(1.0, 1.0, 1.0));
    return mix(
        mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
        mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y),
        f.z);
}

float fbm(vec3 p) {
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 4; i++) {
        value += value_noise(p) * amplitude;
        p *= 2.07;
        amplitude *= 0.5;
    }
    return value;
}

float star_layer(vec3 dir, float scale, float threshold, float size) {
    vec3 p = dir * scale;
    vec3 cell = floor(p);
    vec3 local = fract(p) - 0.5;
    float seed = hash13(cell);
    if (seed < threshold) {
        return 0.0;
    }
    vec3 offset = (vec3(hash13(cell + 1.7), hash13(cell + 3.1), hash13(cell + 5.3)) - 0.5) * 0.6;
    float d = length(local - offset);
    float twinkle = 0.75 + 0.25 * sin(TIME * 0.8 + seed * 60.0);
    return smoothstep(size, 0.0, d) * twinkle * (0.5 + seed);
}

void sky() {
    vec3 dir = normalize(EYEDIR);
    float band = exp(-pow(dot(dir, normalize(vec3(0.25, 0.72, -0.35))) * 2.2, 2.0) * 6.0);
    float n = fbm(dir * 3.4 + vec3(2.0, 0.5, -1.0));
    float n2 = fbm(dir * 7.0 + vec3(-4.0, 1.0, 3.0));

    vec3 color = deep_space;
    color += nebula_a * smoothstep(0.42, 0.85, n) * (0.35 + band * 1.3);
    color += nebula_b * smoothstep(0.55, 0.92, n2) * (0.20 + band * 0.7);
    color += vec3(0.035, 0.05, 0.09) * band;

    float stars = 0.0;
    stars += star_layer(dir, 92.0, 0.972, 0.075);
    if (detail >= 1) stars += star_layer(dir, 190.0, 0.984, 0.055) * 0.9;
    if (detail >= 2) stars += star_layer(dir, 360.0, 0.991, 0.040) * 0.7;
    color += vec3(0.85, 0.90, 1.0) * stars * star_gain;
    if (detail >= 1) color += vec3(1.0, 0.75, 0.5) * star_layer(dir, 150.0, 0.994, 0.05) * 0.8 * star_gain;
    if (detail >= 2) color += vec3(0.6, 0.8, 1.0) * star_layer(dir, 250.0, 0.995, 0.04) * 0.6 * star_gain;

    COLOR = color;
}
