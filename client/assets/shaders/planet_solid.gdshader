shader_type spatial;
uniform int surface_kind = 3;
uniform float surface_seed = 0.0;
varying vec3 sphere_position;
float hash3(vec3 p){return fract(sin(dot(p,vec3(127.1,311.7,74.7)))*43758.5453);}
float noise3(vec3 p){vec3 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
    return mix(mix(mix(hash3(i),hash3(i+vec3(1,0,0)),f.x),mix(hash3(i+vec3(0,1,0)),hash3(i+vec3(1,1,0)),f.x),f.y),
        mix(mix(hash3(i+vec3(0,0,1)),hash3(i+vec3(1,0,1)),f.x),mix(hash3(i+vec3(0,1,1)),hash3(i+vec3(1,1,1)),f.x),f.y),f.z);}
float fbm(vec3 p){return noise3(p)*.55+noise3(p*2.03)*.28+noise3(p*4.11)*.14;}
void vertex(){sphere_position=VERTEX;}
void fragment(){
    vec3 p=normalize(sphere_position);
    vec3 seed=vec3(mod(surface_seed,73.0),mod(surface_seed,97.0),mod(surface_seed,41.0));
    float terrain=fbm(p*4.0+seed);
    float detail=fbm(p*48.0+seed);
    float ridges=1.0-abs(fbm(p*18.0+seed)*2.0-1.0);
    vec3 color;
    float ocean=0.0;
    if(surface_kind==3){
        float land=smoothstep(.49,.52,terrain);
        ocean=1.0-land;
        vec3 sea=mix(vec3(.012,.05,.15),vec3(.025,.23,.32),smoothstep(.40,.52,terrain));
        vec3 soil=mix(vec3(.08,.19,.065),vec3(.37,.31,.16),smoothstep(.57,.72,terrain));
        soil*=.72+detail*.55;
        color=mix(sea,soil,land);
        float ice=smoothstep(.78,.93,abs(p.y)+(terrain-.5)*.2);
        color=mix(color,vec3(.8,.89,.91),ice);
        float angle=TIME*.007;
        vec3 cloud_p=vec3(cos(angle)*p.x+sin(angle)*p.z,p.y,-sin(angle)*p.x+cos(angle)*p.z);
        float cloud=smoothstep(.51,.65,fbm(cloud_p*9.0+seed+vec3(p.y*2.0,0,0)));
        color=mix(color,vec3(.82,.86,.88),cloud*.85);
        ROUGHNESS=mix(.85,.23,ocean*(1.0-cloud));
        METALLIC=.0;SPECULAR=mix(.3,.65,ocean);
    }else if(surface_kind==4){
        color=mix(vec3(.12,.24,.32),vec3(.67,.82,.85),terrain);
        color*=.65+.5*ridges;ROUGHNESS=.65;
    }else if(surface_kind==1){
        color=mix(vec3(.09,.025,.012),vec3(.57,.22,.06),terrain)*(.6+.6*detail);
        float cracks=(1.0-smoothstep(.018,.045,abs(fbm(p*30.0+seed)-.5)))*smoothstep(.55,.7,terrain);
        EMISSION=vec3(1.0,.12,.015)*cracks*.6;ROUGHNESS=.9;
    }else{
        float craters=0.0;vec3 q=p*18.0+seed;vec3 cell=floor(q);
        for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++){
            vec3 c=cell+vec3(float(x),float(y),float(z));
            vec3 site=c+vec3(hash3(c),hash3(c+13.0),hash3(c+37.0));
            float d=length(q-site);float radius=.18+.2*hash3(c+9.0);
            craters+=exp(-pow((d-radius)*24.0,2.0))*.2-(1.0-smoothstep(radius*.65,radius,d))*.22;
        }
        color=mix(vec3(.18,.16,.145),vec3(.48,.43,.37),terrain)+craters;ROUGHNESS=.95;
    }
    ALBEDO=max(color,vec3(.005));
}
