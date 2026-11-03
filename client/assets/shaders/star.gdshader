shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque, fog_disabled;
#include "res://assets/shaders/star_geometry.gdshaderinc"
#include "res://assets/shaders/star_plasma.gdshaderinc"
uniform float animation_time=0.0;
void vertex(){POSITION=vec4(VERTEX.xy,1.0,1.0);}
void fragment(){
    vec3 ray=stellar_ray(SCREEN_UV,INV_PROJECTION_MATRIX,INV_VIEW_MATRIX);
    float cosine=dot(ray,star_direction);
    float discriminant=radius_ratio*radius_ratio-(1.0-cosine)*(1.0+cosine);
    if(cosine<=0.0||discriminant<0.0)discard;
    float travel=stellar_intersection(cosine);
    vec3 normal=normalize((ray*travel-star_direction)/radius_ratio);
    float t=animation_time*.045;
    vec3 flow=vec3(plasma_noise(normal*9.0+vec3(t,0,0)),plasma_noise(normal*9.0+vec3(7,t,3)),plasma_noise(normal*9.0+vec3(3,7,t)))-.5;
    vec3 p=normal*48.0+flow*2.2;
    p+=vec3(plasma_noise(p*1.7+t),plasma_noise(p*1.7+7.0-t),plasma_noise(p*1.7-7.0+t))*.75;
    vec2 large=plasma_cells(p,t);
    vec2 small=plasma_cells(normal*210.0+flow*5.0,t*1.7);
    float fine_filter=1.0-smoothstep(.18,.8,length(fwidth(normal*210.0)));
    float downflows=mix(exp(-large.x*large.x*2.5),smoothstep(.01,.2,large.y-large.x),.35);
    float small_flows=exp(-small.x*small.x*3.0);
    float thermal=plasma_fbm(normal*6.0+flow*.6);
    float heat=.3+.4*downflows+.2*mix(.5,small_flows,fine_filter)+.15*thermal;
    float close_filter=1.0-smoothstep(.2,1.0,length(fwidth(normal*16000.0)));
    heat+=(plasma_noise(normal*16000.0+flow*7.0+t)-.5)*.2*close_filter;
    float penumbra=1.0-smoothstep(.025,.095,length(normal-normalize(vec3(.9,.26,-.34))));
    float filament=pow(max(0.0,1.0-abs(plasma_noise(normal*27.0+flow*3.0)-.5)*12.0),2.0);
    heat-=penumbra*.36+filament*penumbra*.12;
    vec3 cool=vec3(.75,.045,.002), warm=vec3(1.0,.32,.025), hot=vec3(1.0,.76,.27);
    cool=mix(cool,stellar_tint*.25,hot_star);warm=mix(warm,stellar_tint*.7,hot_star);hot=mix(hot,stellar_tint,hot_star);
    vec3 color=mix(cool,warm,smoothstep(.25,.65,heat));
    color=mix(color,hot,smoothstep(.67,.95,heat));
    float limb=clamp(dot(normal,-ray),0.0,1.0);
    float radiance=(.18+pow(heat,3.0)*3.5)*(.6+.4*pow(limb,.35));
    ALBEDO=color*radiance*stellar_tint;
    EMISSION=vec3(0.0);
    DEPTH=stellar_depth(ray,travel*distance_metres,VIEW_MATRIX,PROJECTION_MATRIX);
}
