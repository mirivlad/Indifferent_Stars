shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque, fog_disabled;

#include "res://assets/shaders/star_geometry.gdshaderinc"

uniform vec3 core_color : source_color = vec3(1.0, 0.23, 0.025);
uniform vec3 hot_color : source_color = vec3(1.0, 0.85, 0.48);
uniform float pulse_speed = 0.08;
uniform float animation_time = 0.0;

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
            mix(hash13(i + vec3(0,1,1)), hash13(i + vec3(1,1,1)), f.x), f.y), f.z);
}
void vertex() {
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}
void fragment() {
    vec3 ray = stellar_ray(SCREEN_UV, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX);
    float cosine = dot(ray, star_direction);
    float discriminant = radius_ratio * radius_ratio - (1.0 - cosine) * (1.0 + cosine);
    if (cosine <= 0.0 || discriminant < 0.0) discard;
    float travel = stellar_intersection(cosine);
    vec3 normal = normalize((ray * travel - star_direction) / radius_ratio);
    // Sphere-local 3D noise has no longitude seam or stretched polar texels.
    float t = animation_time * pulse_speed;
    vec3 p = normal * 15.0 + vec3(t * 0.3, t, -t * 0.2);
    float broad = noise3(p * 0.35);
    float grains = noise3(p + broad * 2.0);
    float detail_filter = 1.0 - smoothstep(0.2, 0.8, length(fwidth(p * 7.0)));
    float fine = noise3(p * 7.0 + t) * detail_filter;
    float n = broad * 0.3 + grains * 0.55 + fine * 0.15;
    float limb = clamp(dot(normal, -ray), 0.0, 1.0);
    vec3 col = mix(core_color, hot_color, smoothstep(0.22, 0.77, n));
    float spots = smoothstep(0.63, 0.78, noise3(normal * 6.0 + t * 0.12));
    col *= (0.65 + grains * 0.6) * (1.0 - spots * 0.6) * (0.6 + limb * 0.4);
    float pulse = 1.0 + sin(animation_time * 0.17) * 0.025;
    ALBEDO = col * 2.4 * pulse;
    EMISSION = col * 2.2 * pulse;
    DEPTH = stellar_depth(ray, travel * distance_metres, VIEW_MATRIX, PROJECTION_MATRIX);
}
