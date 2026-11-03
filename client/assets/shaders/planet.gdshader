shader_type spatial;

uniform vec3 band_deep : source_color = vec3(0.22, 0.28, 0.32);
uniform vec3 band_light : source_color = vec3(0.66, 0.62, 0.51);
uniform vec3 storm_color : source_color = vec3(0.49, 0.25, 0.15);
uniform float drift = 0.004;
varying vec3 sphere_position;

float hash13(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}
float noise3(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash13(i), hash13(i+vec3(1,0,0)), f.x),
        mix(hash13(i+vec3(0,1,0)),hash13(i+vec3(1,1,0)),f.x),f.y),
        mix(mix(hash13(i+vec3(0,0,1)),hash13(i+vec3(1,0,1)),f.x),
        mix(hash13(i+vec3(0,1,1)),hash13(i+vec3(1,1,1)),f.x),f.y),f.z);
}
float fbm(vec3 p) {
    return noise3(p)*0.56 + noise3(p*2.03)*0.28 + noise3(p*4.17)*0.14;
}
void vertex() {
    sphere_position = VERTEX;
}
void fragment() {
    vec3 p = normalize(sphere_position);
    float angle = TIME * drift;
    p.xz = mat2(vec2(cos(angle),sin(angle)),vec2(-sin(angle),cos(angle))) * p.xz;
    float large = fbm(p*5.0);
    float clouds = fbm(p*vec3(28.0,12.0,28.0)+large*2.0);
    vec3 center = normalize(vec3(p.x < 0.0 ? -0.72 : 0.72,-0.18,0.66));
    vec3 tangent = normalize(cross(vec3(0,1,0),center));
    vec3 bitangent = cross(center,tangent);
    vec2 q = vec2(dot(p,tangent)*1.5,dot(p,bitangent)*3.5);
    float radius = length(q);
    float storm = (1.0-smoothstep(0.11,0.29,radius))*smoothstep(0.8,0.95,dot(p,center));
    float azimuth = 0.0;
    if (radius > 0.0001) azimuth = atan(q.y,q.x);
    float swirl = sin(radius*70.0-azimuth*3.0) * smoothstep(0.0,.035,radius);
    float latitude = p.y + (large-.5)*.12 + (clouds-.5)*.045 + swirl*storm*.018;
    float bands = sin(latitude*52.0 + sin(latitude*17.0)*1.3 + large*2.2);
    float fine = sin(latitude*137.0+clouds*3.5);
    float filtered = 1.0-smoothstep(.3,1.2,fwidth(latitude*137.0));
    vec3 color = mix(band_deep,band_light,smoothstep(-.65,.8,bands+clouds*.4));
    float wisps = noise3(p*vec3(105.0,43.0,105.0)+clouds*4.0);
    float wisps_filter = 1.0-smoothstep(.4,1.4,length(fwidth(p*105.0)));
    color *= .83 + clouds*.32 + fine*.055*filtered + (wisps-.5)*.18*wisps_filter;
    color = mix(color,storm_color*(.8+swirl*.18+clouds*.25),storm*.72);
    color *= 1.0-pow(abs(p.y),8.0)*.28;
    ALBEDO = color;
    ROUGHNESS = .98;
    SPECULAR = .08;
}
