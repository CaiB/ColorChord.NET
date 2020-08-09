#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTextureLoc;

out vec4 vertexColour;

uniform mat4 projection;
uniform sampler2D tex;

void main()
{
    gl_Position = vec4(aPosition, 1.0) * projection;
	vertexColour = texture(tex, aTextureLoc);
	//vec4(1.0, 1.0, aPosition.z + 2.0, 1.0);
}