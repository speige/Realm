shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 base_color : source_color = vec4(0.15, 0.12, 0.1, 1.0);
uniform vec4 emission_color : source_color = vec4(1.0, 0.4, 0.05, 1.0);
uniform float emission_energy : hint_range(0.0, 20.0) = 4.0;

uniform float fresnel_power : hint_range(0.1, 10.0) = 3.0;
uniform vec4 fresnel_color : source_color = vec4(1.0, 0.6, 0.1, 1.0);
uniform float fresnel_factor : hint_range(0.0, 5.0) = 1.5;

uniform sampler2D noise_texture : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_procedural_noise = true;
uniform float noise_scale : hint_range(0.1, 20.0) = 3.0;

uniform vec2 uv_scroll_speed_1 = vec2(0.3, 0.2);
uniform vec2 uv_scroll_speed_2 = vec2(-0.2, 0.4);

uniform float threshold_cutoff : hint_range(0.0, 1.0) = 0.5;
uniform float threshold_smoothness : hint_range(0.001, 0.5) = 0.1;

uniform int emission_mask_source : hint_range(0, 3) = 0; // 0: Noise Only, 1: Vertex Color / Spikes, 2: Fresnel Only, 3: Texture Alpha

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float procedural_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(dot(hash2(i + vec2(0.0, 0.0)), f - vec2(0.0, 0.0)),
	               dot(hash2(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0)), u.x),
	           mix(dot(hash2(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0)),
	               dot(hash2(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0)), u.x), u.y) * 0.5 + 0.5;
}

varying vec2 base_uv;
varying vec4 vertex_color;

void vertex() {
	base_uv = UV;
	vertex_color = COLOR;
}

void fragment() {
	vec2 uv1 = base_uv * noise_scale + uv_scroll_speed_1 * TIME;
	vec2 uv2 = base_uv * noise_scale * 1.37 + uv_scroll_speed_2 * TIME;

	float n1 = use_procedural_noise ? procedural_noise(uv1) : texture(noise_texture, uv1).r;
	float n2 = use_procedural_noise ? procedural_noise(uv2) : texture(noise_texture, uv2).r;
	float combined_noise = (n1 * 0.6 + n2 * 0.4);

	float crack_mask = smoothstep(threshold_cutoff - threshold_smoothness, threshold_cutoff + threshold_smoothness, combined_noise);
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), fresnel_power) * fresnel_factor;

	float mask = crack_mask;
	if (emission_mask_source == 1) {
		float vert_mask = max(vertex_color.r, max(vertex_color.g, vertex_color.b));
		if (vert_mask < 0.001 && vertex_color.a > 0.001) {
			vert_mask = vertex_color.a;
		}
		mask = (vert_mask > 0.001) ? (vert_mask * crack_mask) : crack_mask;
	} else if (emission_mask_source == 2) {
		mask = fresnel;
	} else if (emission_mask_source == 3) {
		float tex_a = use_procedural_noise ? 1.0 : texture(noise_texture, base_uv).a;
		mask = tex_a * crack_mask;
	}

	vec3 albedo_out = mix(base_color.rgb, emission_color.rgb, mask * 0.7);
	vec3 emission_out = (emission_color.rgb * mask * emission_energy);
	if (emission_mask_source != 2) {
		emission_out += (fresnel_color.rgb * fresnel);
	}

	ALBEDO = albedo_out;
	EMISSION = emission_out;
	ROUGHNESS = clamp(0.8 - mask * 0.5, 0.1, 1.0);
	METALLIC = 0.1;
}
