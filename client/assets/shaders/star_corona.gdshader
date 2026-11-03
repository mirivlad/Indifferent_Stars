shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

#include "res://assets/shaders/star_geometry.gdshaderinc"

uniform float animation_time = 0.0;

void vertex() {
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}
void fragment() {
    vec3 ray = stellar_ray(SCREEN_UV, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX);
    float cosine = dot(ray, star_direction);
    float sine = sqrt(max(0.0, (1.0 - cosine) * (1.0 + cosine)));
    float radial = sine / max(radius_ratio, 0.000001);
    if (cosine <= 0.0 || radial < 1.0 || radial > 3.2) discard;
    vec3 axis = abs(star_direction.y) < 0.95 ? vec3(0,1,0) : vec3(1,0,0);
    vec3 right = normalize(cross(star_direction, axis));
    vec3 up = cross(right, star_direction);
    float angle = atan(dot(ray, up), dot(ray, right));
    float t = animation_time;
    float rays = pow(max(0.0, 0.5 + 0.5 * sin(angle * 37.0 + sin(angle * 11.0 + t * 0.18) * 2.0)), 5.0);
    float halo = exp(-(radial - 1.0) * 8.0) * 0.24;
    halo += rays * exp(-(radial - 1.0) * 4.0) * 0.12;
    float loops = 0.0;
    for (int i=0; i<6; i++) {
        float fi = float(i);
        float center = fi * 1.0472 + sin(fi * 12.35) * 0.35 + sin(t * 0.035 + fi) * 0.13;
        float offset = atan(sin(angle - center), cos(angle - center));
        float height = 0.1 + 0.15 * (0.5 + 0.5 * sin(t * 0.15 + fi * 2.7));
        vec2 q = vec2(offset / (0.08 + height * 0.2), (radial - 1.0) / height);
        q.x += sin(q.y * 4.0 + fi + t * 0.08) * 0.15 * q.y;
        float arc = exp(-pow((length(q) - 1.0) * 16.0, 2.0));
        float activity = pow(max(0.0, sin(t * 0.12 + fi * 1.9)), 2.0);
        loops += arc * smoothstep(1.0, 1.035, radial) * 0.45 * activity;
    }
    float fade = 1.0 - smoothstep(2.0, 3.2, radial);
    vec3 color = mix(vec3(1.0,0.35,0.05), vec3(1.0,0.8,0.4), exp(-(radial-1.0)*12.0));
    ALBEDO = color * (halo + loops) * 2.0 * fade;
    EMISSION = ALBEDO;
    ALPHA = fade;
    DEPTH = stellar_depth(ray, distance_metres * surface_gap_ratio, VIEW_MATRIX, PROJECTION_MATRIX);
}
