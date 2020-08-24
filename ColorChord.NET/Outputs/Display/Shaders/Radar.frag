#version 330 core

in vec4 vertexColour;
out vec4 FragColor;

void main()
{
    FragColor = vertexColour;
}