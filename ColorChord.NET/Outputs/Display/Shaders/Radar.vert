#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTextureLoc;
layout (location = 2) in float aSegmentSide;

out vec4 vertexColour;

uniform sampler2D tex;
uniform mat4 projection;

void main()
{
	float TexY = mod(1.0 - (aTextureLoc.y - 0.0), 1.0);
	vec4 FromTex = texture(tex, vec2(aTextureLoc.x, TexY));

	vertexColour = FromTex;//vec4(aTextureLoc.x, aTextureLoc.y, 0.0, 1.0);

	gl_Position = vec4(aPosition, 1.0) * projection;
}