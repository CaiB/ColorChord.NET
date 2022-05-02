#version 330 core
in vec2 TexCoord;
in vec3 Colour;

out vec4 outColour;

void main()
{
	float Dist = distance(vec2(0.5), TexCoord);
	outColour = vec4(Colour, (0.5 - vec3(Dist)) * 2);
}