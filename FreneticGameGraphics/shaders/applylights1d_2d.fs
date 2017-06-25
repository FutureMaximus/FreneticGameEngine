#version 430 core

layout (binding = 0) uniform sampler1D tex;

layout (location = 3) uniform vec2 l_scaler;
layout (location = 4) uniform vec2 l_adder;
// ...
layout (location = 6) uniform vec4 light_color;
layout (location = 7) uniform float aspect;
layout (location = 8) uniform float extra_light_dist = 50.0;
// ...
layout (location = 21) uniform vec4 sky = vec4(0.0);

layout (location = 1) in vec2 f_texcoord;
layout (location = 2) in vec2 f_pos;

out vec4 color;

void main()
{
	vec2 lmat = (f_texcoord * vec2(2.0) + vec2(-1.0)) * l_scaler + l_adder;
	lmat.y /= aspect;
	vec2 mmat = lmat;
	float modif = 1.0;
	vec2 rel_pos = mmat;
	if (sky.w > 0.5)
	{
		float ownDist = dot(rel_pos.y, rel_pos.y);
		float xDist = texture(tex, rel_pos.x + 0.5).x;
		modif *= ownDist >= xDist ? 1.0 - min((ownDist - xDist) * extra_light_dist, 1.0) : 1.0;
	}
	else
	{
		modif *= max(0.95 - dot(mmat, mmat), 0.0);
		if (modif < 0.01)
		{
			discard;
		}
		float ownDist = dot(rel_pos, rel_pos);
		float xDist = texture(tex, (atan(rel_pos.y, rel_pos.x) * (1.0 / 6.28318) * 0.5) + 0.0).x;
		modif *= ownDist >= xDist ? 1.0 - min((ownDist - xDist) * extra_light_dist, 1.0) : 1.0;
		xDist = texture(tex, (atan(rel_pos.y, rel_pos.x) * (1.0 / 6.28318)) * 0.5 + 0.5).x;
		modif *= ownDist >= xDist ? 1.0 - min((ownDist - xDist) * extra_light_dist, 1.0) : 1.0;
		xDist = texture(tex, (atan(rel_pos.y, rel_pos.x) * (1.0 / 6.28318)) * 0.5 + 1.0).x;
		modif *= ownDist >= xDist ? 1.0 - min((ownDist - xDist) * extra_light_dist, 1.0) : 1.0;
	}
	if (modif < 0.01)
	{
		discard;
	}
	vec4 c = light_color * modif;
	color = vec4(c.xyz * c.xyz, c.w);
}