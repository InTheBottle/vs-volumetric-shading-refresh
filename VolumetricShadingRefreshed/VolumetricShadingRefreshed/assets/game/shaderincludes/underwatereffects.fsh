uniform sampler2D liquidDepth;
uniform float cameraUnderwater;
uniform vec2 frameSize;
uniform vec4 waterMurkColor;

float getSkyMurkiness() {
    if (cameraUnderwater > 0.7) {
        return 0.0;
    }

    float ldepth1 = linearDepth(texture(liquidDepth, gl_FragCoord.xy / frameSize.xy).r);
    float ldepth2 = linearDepth(texture(liquidDepth, (gl_FragCoord.xy + vec2(0, 3)) / frameSize.xy).r);
    float ldepth3 = linearDepth(texture(liquidDepth, (gl_FragCoord.xy + vec2(0, 6)) / frameSize.xy).r);

    return 1.0 - (ldepth1 + ldepth2 + ldepth3) / 3.0;
}

float getUnderwaterMurkiness() {
    if (cameraUnderwater > 0.7) {
        return 0.0;
    }

    float ldepth = linearDepth(
        max(
            texture(liquidDepth, gl_FragCoord.xy / frameSize.xy).r,
            texture(liquidDepth, (gl_FragCoord.xy + vec2(0, 3)) / frameSize.xy).r
        )
    );

    float fdepth = linearDepth(gl_FragCoord.z);
    return clamp(max(0.0, fdepth - ldepth) * 350.0, 0.0, 1.0);
}

vec3 applyUnderwaterEffects(vec3 color, float murkiness) {
    float opticalDepth = max(0.0, murkiness);
    vec3 absorption = vec3(1.55, 0.68, 0.28);
    vec3 transmittance = exp(-absorption * opticalDepth);

    vec3 scatterColor = waterMurkColor.rgb * vec3(0.42, 0.58, 0.85);
    float scatter = 1.0 - exp(-2.1 * opticalDepth);

    return color * transmittance + scatterColor * scatter * (1.0 - transmittance * 0.35);
}
