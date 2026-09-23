shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D texture_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D texture_orm : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform bool has_orm_texture = false;
uniform bool has_player_mask = false;
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

uniform float unit_rim_power : hint_range(1.0, 8.0) = 2.5;

uniform bool use_alpha_blend = false;
uniform bool use_alpha_scissor = false;
uniform float alpha_scissor_threshold : hint_range(0.0, 1.0) = 0.5;

uniform vec3 uv1_scale = vec3(1.0, 1.0, 1.0);
uniform vec3 uv1_offset = vec3(0.0, 0.0, 0.0);

uniform sampler2D shroud_texture : hint_default_black, filter_linear_mipmap, repeat_disable;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);
uniform bool shroud_enabled = false;

instance uniform vec4 player_color : source_color = vec4(0.620, 0.541, 0.431, 1.0);
instance uniform float model_brightness : hint_range(0.10, 2.0) = 0.5;
instance uniform vec4 model_color_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
instance uniform float ignore_player_color : hint_range(0.0, 1.0) = 0.0;
instance uniform float unit_ambient_boost : hint_range(0.0, 0.5) = 0.0;
instance uniform float unit_rim_intensity : hint_range(0.0, 1.0) = 0.0;
instance uniform float hide_in_shroud : hint_range(0.0, 1.0) = 0.0;

varying vec2 base_uv;
varying vec3 v_world_pos;

void vertex() {
	base_uv = UV * uv1_scale.xy + uv1_offset.xy;
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
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

float calculate_cube_root(float value) {
	return value < 0.0 ? -pow(-value, 1.0 / 3.0) : pow(value, 1.0 / 3.0);
}

vec3 convert_rgb_to_oklab(vec3 color) {
	float l = 0.4122214708 * color.r + 0.5363325363 * color.g + 0.0514459929 * color.b;
	float m = 0.2119034982 * color.r + 0.6806995451 * color.g + 0.1073969566 * color.b;
	float s = 0.0883024619 * color.r + 0.2817188376 * color.g + 0.6299787005 * color.b;

	float l_root = calculate_cube_root(l);
	float m_root = calculate_cube_root(m);
	float s_root = calculate_cube_root(s);

	return vec3(
		0.2104542553 * l_root + 0.7936177850 * m_root - 0.0040720468 * s_root,
		1.9779984951 * l_root - 2.4285922050 * m_root + 0.4505937099 * s_root,
		0.0259040371 * l_root + 0.7827717662 * m_root - 0.8086757660 * s_root
	);
}

vec3 convert_oklab_to_rgb(vec3 oklab) {
	float l_root = oklab.x + 0.3963377774 * oklab.y + 0.2158037573 * oklab.z;
	float m_root = oklab.x - 0.1055613458 * oklab.y - 0.0638541728 * oklab.z;
	float s_root = oklab.x - 0.0894841775 * oklab.y - 1.2914855480 * oklab.z;

	float l = l_root * l_root * l_root;
	float m = m_root * m_root * m_root;
	float s = s_root * s_root * s_root;

	float r_linear = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
	float g_linear = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
	float b_linear = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

	return clamp(vec3(r_linear, g_linear, b_linear), 0.0, 1.0);
}

void fragment() {
	if (shroud_enabled && hide_in_shroud > 0.5) {
		vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
		float shroud_factor = texture(shroud_texture, clamp(shroud_uv, 0.0, 1.0)).r;
		if (shroud_factor > 0.8) {
			discard;
		}
	}

	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}

	vec4 albedo_tex = texture(texture_albedo, base_uv);
	vec3 base_albedo = albedo_tex.rgb * albedo_color.rgb;
	
	float player_mask = 0.0;
	float roughness = roughness_value;
	float metallic = metallic_value;
	float specular = specular_value;

	if (has_orm_texture) {
		vec4 orm_tex = texture(texture_orm, base_uv);
		if (has_player_mask && ignore_player_color < 0.5) {
			player_mask = clamp(orm_tex.r, 0.0, 1.0);
		}
		roughness *= orm_tex.g;
		metallic *= orm_tex.b;
	}

	vec3 adjusted_base_albedo = base_albedo * model_brightness * model_color_tint.rgb;
	vec3 final_color = adjusted_base_albedo;

	if (player_mask > 0.005) {
		float luminance = dot(base_albedo, vec3(0.2126, 0.7152, 0.0722));
		vec3 grayscale_oklab = convert_rgb_to_oklab(vec3(luminance));
		vec3 target_oklab = convert_rgb_to_oklab(player_color.rgb);

		float shadow_falloff = smoothstep(0.0, 0.15, grayscale_oklab.x);

		float lightness = grayscale_oklab.x <= target_oklab.x
			? grayscale_oklab.x
			: mix(target_oklab.x, grayscale_oklab.x, 0.25);

		float chroma_boost = mix(1.0, 1.4, smoothstep(target_oklab.x, 0.8, grayscale_oklab.x));

		vec3 reconstructed_color = convert_oklab_to_rgb(vec3(
			lightness,
			target_oklab.y * shadow_falloff * chroma_boost,
			target_oklab.z * shadow_falloff * chroma_boost
		)) * model_brightness;

		final_color = mix(adjusted_base_albedo, reconstructed_color, player_mask);
		specular = mix(specular, 0.02, player_mask);
		metallic = mix(metallic, 0.0, player_mask);
		roughness = mix(roughness, 1.0, player_mask);
	}

	ALBEDO = final_color;

	if (use_alpha_scissor) {
		ALPHA_SCISSOR_THRESHOLD = alpha_scissor_threshold;
		ALPHA = albedo_tex.a * albedo_color.a;
	} else if (use_alpha_blend) {
		ALPHA = albedo_tex.a * albedo_color.a;
	}

	ROUGHNESS = roughness;
	METALLIC = metallic;
	SPECULAR = specular;

	if (has_normal_texture) {
		vec3 normal_sample = texture(texture_normal, base_uv).rgb;
		NORMAL = perturb_normal_cotangent(NORMAL, VERTEX, base_uv, normal_sample, normal_scale);
	}

	vec3 world_normal = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
	float underside_factor = clamp(0.5 - world_normal.y * 0.5, 0.0, 1.0);
	vec3 terrain_separation_ambient = final_color * underside_factor * unit_ambient_boost;

	float n_dot_v = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float fresnel = pow(1.0 - n_dot_v, unit_rim_power);
	vec3 rim_glow = final_color * fresnel * unit_rim_intensity;

	vec3 base_emission = vec3(0.0);
	if (has_emission_texture) {
		base_emission = texture(texture_emission, base_uv).rgb * emission_color.rgb * emission_energy;
	} else if (emission_color.r > 0.0 || emission_color.g > 0.0 || emission_color.b > 0.0) {
		base_emission = emission_color.rgb * emission_energy;
	}

	vec3 player_color_ambient = final_color * 0.15 * player_mask;
	EMISSION = base_emission + terrain_separation_ambient + rim_glow + player_color_ambient;
}
