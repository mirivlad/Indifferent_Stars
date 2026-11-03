shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;
#include "res://assets/shaders/star_geometry.gdshaderinc"
uniform vec3 anchor=vec3(0,1,0);
uniform vec3 tangent=vec3(1,0,0);
uniform float phase=0.0;
uniform float strand=0.0;
uniform float animation_time=0.0;
varying vec3 stellar_point;
varying vec3 tube_normal;
void vertex(){
    float t=animation_time;
    float theta=UV.x*3.141593;
    float phi=UV.y*6.283185;
    float width=(.13+.025*sin(phase*3.7))*(1.0+strand*.05);
    float height=.14+.055*sin(t*.045+phase)+strand*.01;
    vec3 side=normalize(tangent-anchor*dot(tangent,anchor));
    vec3 binormal=cross(anchor,side);
    vec3 arch=anchor*(sqrt(1.0-width*width)+sin(theta)*height)+side*cos(theta)*width;
    arch+=binormal*sin(theta)*(sin(theta*3.0+t*.035+phase)*.018+strand*.008+sin(theta*13.0-t*.25+phase)*.003);
    vec3 direction=normalize(anchor*cos(theta)*height-side*sin(theta)*width);
    vec3 section=normalize(cross(direction,binormal));
    tube_normal=section*cos(phi)+binormal*sin(phi);
    float radius=(.004+.0015*sin(theta*7.0-t*.18+phase+strand))*(1.0-abs(strand)*.35)*smoothstep(0.0,.12,sin(theta));
    stellar_point=arch+tube_normal*radius;
    vec3 relative=star_direction/radius_ratio+stellar_point;
    vec3 ray=normalize(relative);
    vec3 local=(VIEW_MATRIX*vec4(ray,0.0)).xyz*clamp(length(relative)*distance_metres*radius_ratio,1.0,149000.0);
    POSITION=PROJECTION_MATRIX*vec4(local,1.0);
}
void fragment(){
    vec3 relative=star_direction/radius_ratio+stellar_point;
    vec3 ray=normalize(relative);
    float cosine=dot(ray,star_direction);
    float impact=length(cross(ray,star_direction/radius_ratio));
    if(cosine>0.0&&impact<1.0&&length(relative)>stellar_intersection(cosine)/radius_ratio+.0005)discard;
    float facing=abs(dot(normalize(tube_normal),-ray));
    float streams=.65+.35*sin(UV.x*45.0-animation_time*.8+sin(UV.y*13.0+phase));
    float activity=.65+.35*sin(animation_time*.055+phase);
    ALBEDO=mix(vec3(1.0,.12,.008),vec3(1.0,.65,.12),pow(facing,3.0))*streams*activity*3.5;
    ALPHA=pow(facing,.7)*.55;
    DEPTH=stellar_depth(ray,min(length(relative)*distance_metres*radius_ratio,149000.0),VIEW_MATRIX,PROJECTION_MATRIX);
}
