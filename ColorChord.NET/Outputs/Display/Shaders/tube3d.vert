#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTextureLoc;
layout (location = 2) in float aSegmentSide;

out vec4 vertexColour;

uniform mat4 projection;
uniform sampler2D tex;
uniform float depthOffset;
uniform mat4 transform;

void main()
{
	ivec2 TexSize = textureSize(tex, 0);

    gl_Position = vec4(aPosition, 1.0) * transform * projection;

	float TexY = mod(1.0 - (aTextureLoc.y - depthOffset), 1.0);
	vec4 FromTex = texture(tex, vec2(aTextureLoc.x, TexY));
	vec4 FromTexLeft = texture(tex, vec2(mod(aTextureLoc.x - (1.0 / TexSize.x), 1.0), TexY));
	vec4 FromTexRight = texture(tex, vec2(mod(aTextureLoc.x + (1.0 / TexSize.x), 1.0), TexY));

	vec4 LeftMix = mix(FromTexLeft, FromTex, aSegmentSide);
	vec4 RightMix = mix(FromTex, FromTexRight, 1.0 - aSegmentSide);

	vec4 TexOut = RightMix * (1.0 - step(0.5, aSegmentSide)) + LeftMix * step(0.5, aSegmentSide);

	vertexColour = vec4(TexOut.xyz * (2.5 + aPosition.z), 1.0);


	//vec4(1.0, 1.0, aPosition.z + 2.0, 1.0);
}