#version 330 core
in vec2 TexCoord;

uniform sampler2D TextureUnit;

out vec4 outColour;

void main()
{
	outColour = texture(TextureUnit, TexCoord);
}