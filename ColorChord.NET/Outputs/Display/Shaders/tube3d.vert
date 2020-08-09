#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTextureLoc;

out vec4 vertexColour;

uniform mat4 projection;
uniform sampler2D tex;
uniform float depthOffset;
uniform mat4 transform;

void main()
{
    gl_Position = vec4(aPosition, 1.0) * transform * projection;
	vertexColour = texture(tex, vec2(aTextureLoc.x, mod(aTextureLoc.y - depthOffset, 1.0)));
	//vec4(1.0, 1.0, aPosition.z + 2.0, 1.0);
}