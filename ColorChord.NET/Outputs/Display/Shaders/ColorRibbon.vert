#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTexCoord;

out vec2 TexCoord;
out float Brightness;

uniform sampler2D tex;
uniform mat4 Projection;
uniform mat4 View;
uniform float TextureAdvance;

void main()
{
    gl_Position = vec4(aPosition, 1.0) * View * Projection;

    ivec2 TexSize = textureSize(tex, 0);
	float TexY = mod(TextureAdvance - aTexCoord.y + 1.0, 1.0);

	TexCoord = vec2(aTexCoord.x, TexY);
	Brightness = pow(min(1.0, 1.0 - (aPosition.y / 3)), 2.0);
}