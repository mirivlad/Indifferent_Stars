shader_type canvas_item;
uniform float strength = 0.0;
void fragment() {
    float edge=smoothstep(0.22,0.70,length((UV-0.5)*vec2(1.3,1.0)));
    COLOR=vec4(0.82,0.35,0.12,edge*strength*0.18);
}
