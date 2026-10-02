#version 450
// Issue #584 fixture: a vertex stage that loops long enough for macOS to kill
// the command buffer (MTLCommandBufferErrorDomain Code=2, "Caused GPU Hang
// Error"). It reproduces the shape of bezel/scanline-classic's mask.slang,
// whose vertex stage integrates the slot mask over 216 x 72 samples twice per
// vertex. The loop bounds are parameters so the compiler cannot fold them.

layout(push_constant) uniform Push
{
	vec4 SourceSize;
	vec4 OutputSize;
	float hang_x;
	float hang_y;
} params;

#pragma parameter hang_x "Vertex loop width" 1024.0 1.0 4096.0 1.0
#pragma parameter hang_y "Vertex loop height" 1024.0 1.0 4096.0 1.0

layout(std140, set = 0, binding = 0) uniform UBO
{
	mat4 MVP;
} global;

#pragma stage vertex
layout(location = 0) in vec4 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 0) out vec2 vTexCoord;
layout(location = 1) out float vWeight;
void main()
{
	gl_Position = global.MVP * Position;
	vTexCoord = TexCoord;
	int nx = int(params.hang_x);
	int ny = int(params.hang_y);
	float sum = 0.0;
	for(int j = 0; j < ny; ++j) {
		for(int i = 0; i < nx; ++i) {
			float d = mod(float(i) * 0.37 + float(j) * 0.11 + TexCoord.x, 6.0);
			sum += exp(-0.5 * d * d);
		}
	}
	vWeight = sum / float(max(nx * ny, 1));
}

#pragma stage fragment
layout(location = 0) in vec2 vTexCoord;
layout(location = 1) in float vWeight;
layout(location = 0) out vec4 FragColor;
layout(set = 0, binding = 2) uniform sampler2D Source;
void main()
{
	vec4 c = texture(Source, vTexCoord);
	FragColor = vec4(c.rgb * clamp(vWeight * 4.0, 0.0, 1.0), c.a);
}
