#version 430 core

layout (location = 0) in vec3 position;
layout (location = 2) in vec2 texcoords;
layout (location = 4) in vec4 color;

layout (location = 1) uniform vec2 scaler = vec2(1.0);
layout (location = 2) uniform vec2 adder = vec2(0.0);
layout (location = 3) uniform vec4 v_color = vec4(1.0);
layout (location = 4) uniform vec3 rotation = vec3(0.0);

layout (location = 0) out vec4 f_color;
layout (location = 1) out vec2 f_texcoord;

void main()
{
    f_color = color * v_color;
	f_texcoord = texcoords;
	gl_Position = vec4(position, 1.0) * vec4(scaler, 1.0, 1.0) + vec4(adder, 0.0, 0.0);
}
