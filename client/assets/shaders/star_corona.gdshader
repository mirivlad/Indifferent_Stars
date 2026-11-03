shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;
#include "res://assets/shaders/star_geometry.gdshaderinc"
#include "res://assets/shaders/star_plasma.gdshaderinc"
uniform float animation_time=0.0;
uniform int volume_steps=12;
// Diagnostic capture can isolate the independently evaluated emissive layers.
uniform int layer_mask=3;
uniform sampler2D scene_depth : hint_depth_texture, filter_nearest;

vec2 stellar_layers(vec3 p,float time){
    float r=length(p);if(r<1.0||r>1.42)return vec2(0.0);
    vec3 n=p/r;float h=r-1.0;
    float convection=plasma_noise(n*28.0+vec3(time*.025,-time*.018,time*.012));
    float chromo=exp(-pow((h-(.006+convection*convection*.035))/.012,2.0))*(1.0+pow(convection,3.0)*45.0);
    vec3 warp=vec3(plasma_noise(p*5.0),plasma_noise(p*5.0+7.0),plasma_noise(p*5.0-7.0))-.5;
    float structure=plasma_noise(p*13.0+warp*2.5+vec3(0,time*.02,0));
    float region=.1+2.5*pow(plasma_noise(n*4.0+vec3(2,5,1)),3.0);
    float corona=exp(-h*10.0)*smoothstep(.28,.77,structure)*region*.9;
    corona*=1.0-smoothstep(.28,.42,h);
    if((layer_mask&1)==0)chromo=0.0;
    if((layer_mask&2)==0)corona=0.0;
    return vec2(chromo,corona);
}
void vertex(){POSITION=vec4(VERTEX.xy,1.0,1.0);}
void fragment(){
    vec3 ray=stellar_ray(SCREEN_UV,INV_PROJECTION_MATRIX,INV_VIEW_MATRIX);
    vec3 center=star_direction/radius_ratio;
    float along=dot(ray,center);
    float impact=length(cross(ray,center));
    if(impact>1.42)discard;
    float half_path=sqrt(max(0.0,1.42*1.42-impact*impact));
    float entry=max(0.0,along-half_path), exit=along+half_path;
    float cosine=dot(ray,star_direction);
    bool hits_surface=cosine>0.0&&impact<1.0;
    if(hits_surface)exit=min(exit,stellar_intersection(cosine)/radius_ratio);
    float depth=textureLod(scene_depth,SCREEN_UV,0.0).r;
    if(depth>0.0){
        vec4 opaque=INV_PROJECTION_MATRIX*vec4(SCREEN_UV*2.0-1.0,depth,1.0);
        float opaque_distance=length(opaque.xyz/opaque.w);
        // The photosphere's distant depth is a bounded projection, not a nearby occluder.
        if(opaque_distance<148500.0)exit=min(exit,opaque_distance/(distance_metres*radius_ratio));
    }
    // A few kilometres of optically thin foreground plasma contribute less than one display level.
    float span=exit-entry;if(span<.00001)discard;
    vec3 accumulated=vec3(0.0);float transmittance=1.0;
    for(int i=0;i<12;i++){
        if(i>=volume_steps)break;
        float u=(float(i)+.5)/float(volume_steps);
        float sample_t;
        float weight;
        if(hits_surface){sample_t=mix(entry,exit,1.0-(1.0-u)*(1.0-u));weight=span*2.0*(1.0-u)/float(volume_steps);}
        else if(along<0.0){sample_t=mix(entry,exit,u*u);weight=span*2.0*u/float(volume_steps);}
        else {float v=u*2.0-1.0;float focus=clamp(along,entry,exit);float half_span=v<0.0?focus-entry:exit-focus;
            sample_t=focus+sign(v)*v*v*half_span;weight=half_span*4.0*abs(v)/float(volume_steps);}
        vec3 p=ray*sample_t-center;
        vec2 layers=stellar_layers(p,animation_time);
        vec3 radiance=layers.x*vec3(1.0,.36,.055)*2.8+layers.y*vec3(1.0,.25,.04)*1.4;
        accumulated+=transmittance*radiance*weight;
        transmittance*=exp(-dot(layers,vec2(.28,.12))*weight);
    }
    ALBEDO=accumulated;EMISSION=vec3(0.0);ALPHA=1.0;
    DEPTH=stellar_depth(ray,min(entry*distance_metres*radius_ratio,149000.0),VIEW_MATRIX,PROJECTION_MATRIX);
}
