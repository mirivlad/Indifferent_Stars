shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float energy : hint_range(0.0, 1.0) = 0.0;
uniform float travel : hint_range(0.0, 1.0) = 0.0;
uniform float aspect = 1.7778;
uniform float tactical = 0.0;
uniform float detail = 1.0;

float hash(float x) {
    return fract(sin(x * 127.1 + 17.13) * 43758.5453);
}

void fragment() {
    vec2 p = (UV - vec2(0.5)) * vec2(aspect, 1.0);
    float radius = length(p);
    float angle = atan(p.y, p.x);
    float safe_radius = max(radius, 0.025);
    float speed = mix(0.5, 4.0, travel);
    float lane = floor((angle + 3.141593) * 73.0);
    float noise = hash(lane);
    float radial = log(safe_radius) * 8.0 - TIME * speed * (1.0 + noise * 0.2);
    float filament = pow(max(0.0, sin(angle * 231.0 + sin(angle * 37.0) * 2.0)), 46.0);
    float streak = filament * smoothstep(0.25, 0.95, fract(radial * 0.17 + noise));
    float ring = pow(0.5 + 0.5 * cos(radial * 4.0 + sin(angle * 5.0 + TIME) * 0.35), 24.0);
    float spiral = pow(max(0.0, sin(angle * 7.0 - log(safe_radius) * 9.0 + TIME * speed)), 8.0);
    float center_clear = smoothstep(0.12, 0.32, radius);
    float edge = smoothstep(0.85, 0.35, radius);
    vec3 cold = mix(vec3(0.08, 0.38, 0.8), vec3(0.25, 0.85, 1.0), noise);
    vec3 violet = vec3(0.3, 0.1, 0.62);
    vec3 light = cold * streak * (0.25 + travel * 0.6) + mix(violet, cold, 0.6) * ring * 0.35;
    light += violet * spiral * 0.14 * detail;
    float sheath = (0.5 + 0.5 * sin(log(safe_radius) * 12.0 - TIME * speed * 0.7
        + sin(angle * 3.0 + TIME * 0.4) * 1.5));
    light += mix(violet, cold, sheath) * pow(sheath, 3.0) * 0.24 * travel;
    float border = smoothstep(0.35, 0.8, radius);
    float mask = mix(center_clear * edge, border * 0.3, tactical);
    COLOR = vec4(light * energy * mask, energy * mask);
}
