shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D texture_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D texture_orm : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform bool has_orm_texture = false;
uniform bool has_player_mask = true;
uniform sampler2D texture_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool has_normal_texture = false;
uniform float normal_scale = 1.0;
uniform sampler2D texture_emission : source_color, hint_default_black, filter_linear_mipmap, repeat_enable;
uniform bool has_emission_texture = false;

uniform vec4 albedo_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_energy = 1.0;
uniform float roughness_value : hint_range(0.0, 1.0) = 1.0;
uniform float metallic_value : hint_range(0.0, 1.0) = 0.0;
uniform float specular_value : hint_range(0.0, 1.0) = 0.5;

uniform bool use_alpha_blend = false;
uniform bool use_alpha_scissor = false;
uniform float alpha_scissor_threshold : hint_range(0.0, 1.0) = 0.5;

uniform vec3 uv1_scale = vec3(1.0, 1.0, 1.0);
uniform vec3 uv1_offset = vec3(0.0, 0.0, 0.0);

instance uniform vec4 player_color : source_color = vec4(0.620, 0.541, 0.431, 1.0);
instance uniform float model_brightness : hint_range(0.25, 1.75) = 1.0;
instance uniform vec4 model_color_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);

varying vec2 base_uv;

void vertex() {
	base_uv = UV * uv1_scale.xy + uv1_offset.xy;
}

vec3 perturb_normal_cotangent(vec3 view_normal, vec3 view_pos, vec2 uv, vec3 normal_sample, float scale) {
	vec3 map_n = normal_sample * 2.0 - 1.0;
	map_n.xy *= scale;
	
	vec3 dp1 = dFdx(view_pos);
	vec3 dp2 = dFdy(view_pos);
	vec2 duv1 = dFdx(uv);
	vec2 duv2 = dFdy(uv);
	
	vec3 dp2perp = cross(dp2, view_normal);
	vec3 dp1perp = cross(view_normal, dp1);
	
	vec3 t = dp2perp * duv1.x + dp1perp * duv2.x;
	vec3 b = dp2perp * duv1.y + dp1perp * duv2.y;
	
	float invmax = inversesqrt(max(dot(t, t), dot(b, b)));
	mat3 tbn = mat3(t * invmax, b * invmax, view_normal);
	return normalize(tbn * map_n);
}

void fragment() {
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}

	vec4 albedo_tex = texture(texture_albedo, base_uv);
	vec3 base_albedo = albedo_tex.rgb * albedo_color.rgb;
	
	float player_mask = 0.0;
	float roughness = roughness_value;
	float metallic = metallic_value;

	if (has_orm_texture) {
		vec4 orm_tex = texture(texture_orm, base_uv);
		if (has_player_mask) {
			player_mask = clamp(orm_tex.r, 0.0, 1.0);
		}
		roughness *= orm_tex.g;
		metallic *= orm_tex.b;
	}

	vec3 adjusted_base_albedo = base_albedo * model_brightness * model_color_tint.rgb;
	vec3 final_color = mix(adjusted_base_albedo, player_color.rgb, player_mask);

	ALBEDO = final_color;

	if (use_alpha_scissor) {
		ALPHA_SCISSOR_THRESHOLD = alpha_scissor_threshold;
		ALPHA = albedo_tex.a * albedo_color.a;
	} else if (use_alpha_blend) {
		ALPHA = albedo_tex.a * albedo_color.a;
	}

	ROUGHNESS = roughness;
	METALLIC = metallic;
	SPECULAR = specular_value;

	if (has_normal_texture) {
		vec3 normal_sample = texture(texture_normal, base_uv).rgb;
		NORMAL = perturb_normal_cotangent(NORMAL, VERTEX, base_uv, normal_sample, normal_scale);
	}

	if (has_emission_texture) {
		EMISSION = texture(texture_emission, base_uv).rgb * emission_color.rgb * emission_energy;
	} else if (emission_color.r > 0.0 || emission_color.g > 0.0 || emission_color.b > 0.0) {
		EMISSION = emission_color.rgb * emission_energy;
	}
}
