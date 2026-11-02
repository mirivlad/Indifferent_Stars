shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

varying vec4 trail_color;

void vertex() {
    trail_color = COLOR;
}

void fragment() {
    float cross_section = pow(max(0.0, 1.0 - abs(UV.x * 2.0 - 1.0)), 2.0);
    ALBEDO = trail_color.rgb;
    ALPHA = trail_color.a * cross_section;
}
