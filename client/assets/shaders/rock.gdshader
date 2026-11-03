shader_type spatial;
varying vec3 rock_position;
uniform float ice_fraction = 0.0;

float noise3(vec3 p) {
    vec3 i=floor(p), f=fract(p);f=f*f*(3.0-2.0*f);
    vec3 h=vec3(127.1,311.7,74.7);
    return mix(mix(mix(fract(sin(dot(i,h))*43758.5453),fract(sin(dot(i+vec3(1,0,0),h))*43758.5453),f.x),
        mix(fract(sin(dot(i+vec3(0,1,0),h))*43758.5453),fract(sin(dot(i+vec3(1,1,0),h))*43758.5453),f.x),f.y),
        mix(mix(fract(sin(dot(i+vec3(0,0,1),h))*43758.5453),fract(sin(dot(i+vec3(1,0,1),h))*43758.5453),f.x),
        mix(fract(sin(dot(i+vec3(0,1,1),h))*43758.5453),fract(sin(dot(i+vec3(1,1,1),h))*43758.5453),f.x),f.y),f.z);
}
void vertex() { rock_position=VERTEX; }
void fragment() {
    float layers=sin(rock_position.y*18.0+noise3(rock_position*4.0)*5.0);
    float grains=noise3(rock_position*38.0);
    float filtered=1.0-smoothstep(.4,1.4,length(fwidth(rock_position*38.0)));
    float mineral=noise3(rock_position*7.0);
    ALBEDO=mix(vec3(.085,.075,.063),vec3(.28,.25,.21),mineral)*(.85+layers*.12+(grains-.5)*.65*filtered);
    ALBEDO=mix(ALBEDO,mix(vec3(.18,.28,.34),vec3(.65,.78,.8),mineral),ice_fraction*.85);
    vec3 dx=dFdx(VERTEX), dy=dFdy(VERTEX);
    vec3 rx=cross(dy,NORMAL), ry=cross(NORMAL,dx);
    float determinant=dot(dx,rx);
    float height=grains*filtered*.12;
    vec3 gradient=(rx*dFdx(height)+ry*dFdy(height))/max(abs(determinant),0.000001);
    NORMAL=normalize(NORMAL-gradient*sign(determinant));
    ROUGHNESS=mix(.88+mineral*.1,.43+mineral*.2,ice_fraction);
    SPECULAR=.13;
}
