shader_type spatial;

uniform vec3 band_deep : source_color = vec3(0.10, 0.16, 0.24);
uniform vec3 band_light : source_color = vec3(0.72, 0.62, 0.48);
uniform vec3 storm_color : source_color = vec3(0.62, 0.30, 0.20);
uniform float band_frequency = 26.0;
uniform float drift = 0.004;

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

float fbm(vec3 p) {
    return noise3(p) * 0.55 + noise3(p * 2.1) * 0.28 + noise3(p * 4.3) * 0.13;
}

void fragment() {
    float lat = UV.y * 2.0 - 1.0;
    float turbulence = fbm(vec3(UV.x * 11.0, lat * 4.0, TIME * drift));
    float bands = sin(lat * band_frequency + turbulence * 4.0);
    float grain = fbm(vec3(UV.x * 38.0, lat * 19.0, TIME * drift * 2.0));

    vec3 col = mix(band_deep, band_light, smoothstep(-0.55, 0.75, bands + grain * 0.35));
    col = mix(col, storm_color, smoothstep(0.70, 0.93, grain) * (1.0 - abs(lat)) * 0.55);
    col *= 0.82 + grain * 0.35;

    // Subtle polar darkening and a thin atmospheric rim.
    col *= 1.0 - pow(abs(lat), 6.0) * 0.22;
    float rim = pow(1.0 - abs(dot(NORMAL, normalize(VIEW))), 3.0);
    col += vec3(0.12, 0.26, 0.42) * rim * 0.55;

    ALBEDO = col;
    ROUGHNESS = 0.88;
    SPECULAR = 0.18;
    EMISSION = col * 0.035;
}