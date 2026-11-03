vec3 plasma_hash(vec3 p) {
    p = fract(p * vec3(.1031,.1030,.0973));
    p += dot(p,p.yxz+33.33);
    return fract((p.xxy+p.yxx)*p.zyx);
}
float plasma_noise(vec3 p) {
    vec3 i=floor(p), f=fract(p);f=f*f*(3.0-2.0*f);
    return mix(mix(mix(plasma_hash(i).x,plasma_hash(i+vec3(1,0,0)).x,f.x),
                   mix(plasma_hash(i+vec3(0,1,0)).x,plasma_hash(i+vec3(1,1,0)).x,f.x),f.y),
               mix(mix(plasma_hash(i+vec3(0,0,1)).x,plasma_hash(i+vec3(1,0,1)).x,f.x),
                   mix(plasma_hash(i+vec3(0,1,1)).x,plasma_hash(i+vec3(1,1,1)).x,f.x),f.y),f.z);
}
float plasma_fbm(vec3 p) {
    return plasma_noise(p)*.57+plasma_noise(p*2.03+vec3(7.2,3.1,9.4))*.28+plasma_noise(p*4.07-5.8)*.15;
}
vec2 plasma_cells(vec3 p,float time) {
    vec3 cell=floor(p);vec2 nearest=vec2(100.0);
    for(int z=0;z<2;z++)for(int y=0;y<2;y++)for(int x=0;x<2;x++) {
        vec3 corner=cell+vec3(float(x),float(y),float(z));
        vec3 seed=plasma_hash(corner);
        vec3 center=corner+.28*sin(seed*6.283185+time*(.7+seed));
        float d=length(p-center);
        if(d<nearest.x){nearest.y=nearest.x;nearest.x=d;}else nearest.y=min(nearest.y,d);
    }
    return nearest;
}
