#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec3 aColour;

out vec4 vertexColour;

void main()
{
    gl_Position = vec4(aPosition, 0.0, 1.0);
	vertexColour = vec4(aColour, 1.0);
}