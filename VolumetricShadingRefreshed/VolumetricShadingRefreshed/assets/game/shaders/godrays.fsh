#version 330 core

uniform sampler2D inputTexture;
uniform sampler2D glowParts;
uniform vec3 sunPos3dIn;
uniform mat4 invProjectionMatrix;
uniform mat4 invModelViewMatrix;

in vec2 texCoord;
in vec3 sunPosScreen;
in float iGlobalTime;
in float direction;
in vec3 frontColor;
in vec3 backColor;

out vec4 outColor;

#include printvalues.fsh

vec4 applyVolumetricLighting(in vec3 color, in vec2 uv, float intensity) {
    vec2 texelSize = 1.0 / textureSize(glowParts, 0);
    float vgr =
        texture(glowParts, uv).g * 0.50 +
        texture(glowParts, uv + vec2(texelSize.x, 0.0)).g * 0.125 +
        texture(glowParts, uv - vec2(texelSize.x, 0.0)).g * 0.125 +
        texture(glowParts, uv + vec2(0.0, texelSize.y)).g * 0.125 +
        texture(glowParts, uv - vec2(0.0, texelSize.y)).g * 0.125;

    vgr = max(vgr - 0.012, 0.0);
    vgr = min(vgr * 1.25, 0.85);
    if (vgr <= 0.0) {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec3 vgrC = color * intensity * vgr;
    return vec4(vgrC, 1.0);
}

void main(void) {
    vec4 proCoord = invProjectionMatrix * vec4(texCoord * 2.0 - 1.0, -1.0, 1);
    proCoord.xyz /= proCoord.w;
    proCoord.w = 0;
    proCoord = invModelViewMatrix * proCoord;

    float dp = dot(normalize(sunPos3dIn), normalize(proCoord.xyz));
    vec3 useColor = mix(backColor, frontColor, dp * 0.5 + 0.5);
    outColor = applyVolumetricLighting(useColor, texCoord, VOLUMETRIC_INTENSITY);
}
