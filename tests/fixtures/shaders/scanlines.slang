#version 450
// P.8 fixture: darkens every second output row. Deliberately trivial - the
// test only needs a shader whose effect is unmistakable and deterministic.

layout(push_constant) uniform Push
{
	vec4 SourceSize;
	vec4 OutputSize;
	float darkness;
} params;

#pragma parameter darkness "Scanline darkness" 0.5 0.0 1.0 0.05

layout(std140, set = 0, binding = 0) uniform UBO
{
	mat4 MVP;
} global;

#pragma stage vertex
layout(location = 0) in vec4 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 0) out vec2 vTexCoord;
void main()
{
	gl_Position = global.MVP * Position;
	vTexCoord = TexCoord;
}

#pragma stage fragment
layout(location = 0) in vec2 vTexCoord;
layout(location = 0) out vec4 FragColor;
layout(set = 0, binding = 2) uniform sampler2D Source;
void main()
{
	vec4 c = texture(Source, vTexCoord);
	float row = floor(vTexCoord.y * params.OutputSize.y);
	float dark = mod(row, 2.0) < 1.0 ? 1.0 - params.darkness : 1.0;
	FragColor = vec4(c.rgb * dark, c.a);
}
