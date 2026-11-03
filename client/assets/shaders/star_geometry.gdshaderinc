// Ratios come from double-precision world coordinates; geometry stays in the camera frustum.
uniform vec3 star_direction = vec3(-1.0, 0.0, 0.0);
uniform float radius_ratio = 0.00465;
uniform float surface_gap_ratio = 0.99535;
uniform float distance_metres = 149597870700.0;

vec3 stellar_ray(vec2 screen_uv, mat4 inverse_projection, mat4 inverse_view) {
    vec4 view = inverse_projection * vec4(screen_uv * 2.0 - 1.0, 1.0, 1.0);
    return normalize((inverse_view * vec4(view.xyz / view.w, 0.0)).xyz);
}

// Stable near-root form avoids subtracting two almost equal stellar radii at close range.
float stellar_intersection(float cosine) {
    float discriminant = radius_ratio * radius_ratio - (1.0 - cosine) * (1.0 + cosine);
    return surface_gap_ratio * (1.0 + radius_ratio) / max(cosine + sqrt(max(discriminant, 0.0)), 0.000001);
}

float stellar_depth(vec3 ray, float distance, mat4 view, mat4 projection) {
    vec3 position = (view * vec4(ray, 0.0)).xyz * clamp(distance, 1.0, 150000.0);
    vec4 clip = projection * vec4(position, 1.0);
    return clip.z / clip.w;
}
