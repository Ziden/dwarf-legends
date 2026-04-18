shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque;

uniform sampler2DArray tile_array : filter_nearest, repeat_disable;
uniform vec4 tint_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
    vec4 surface = texture(tile_array, vec3(UV, UV2.x));
    if (surface.a < 0.01) {
        discard;
    }
    ALBEDO = surface.rgb * tint_color.rgb;
}
