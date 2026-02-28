namespace GiantsEdit.App.Rendering;

/// <summary>
/// GLSL ES 3.0 shader source strings used by the OpenGL renderer.
/// </summary>
internal static class ShaderSources
{
    public const string TerrainVert = """
        #version 300 es
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec4 aColor;
        layout(location = 2) in vec4 aBumpDiffuse;
        uniform mat4 uMVP;
        out vec4 vColor;
        out vec4 vBumpDiffuse;
        out vec3 vWorldPos;
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
            vColor = aColor;
            vBumpDiffuse = aBumpDiffuse;
            vWorldPos = aPos;
        }
        """;

    public const string TerrainFrag = """
        #version 300 es
        precision highp float;
        in vec4 vColor;
        in vec4 vBumpDiffuse;
        in vec3 vWorldPos;
        uniform int uHasTex;
        uniform sampler2D uGroundTex;
        uniform sampler2D uSlopeTex;
        uniform sampler2D uWallTex;
        uniform float uGroundWrap;
        uniform float uSlopeWrap;
        uniform float uWallWrap;
        uniform int uHasBump;
        uniform sampler2D uBumpTex;
        uniform float uBumpWrap;
        out vec4 FragColor;

        // Anti-tiling: hash function for random offsets per tile
        vec4 hash4(vec2 p) {
            return fract(
                sin(vec4(
                    1.0 + dot(p, vec2(37.0, 17.0)),
                    2.0 + dot(p, vec2(11.0, 47.0)),
                    3.0 + dot(p, vec2(41.0, 29.0)),
                    4.0 + dot(p, vec2(23.0, 31.0))
                )) * 103.0);
        }

        // Anti-tiling texture sample using 4 offset/rotated lookups blended together
        vec4 textureNoTile(sampler2D tex, vec2 uv) {
            vec2 iuv = floor(uv);
            vec2 fuv = fract(uv);

            vec4 ofa = hash4(iuv + vec2(0.0, 0.0));
            vec4 ofb = hash4(iuv + vec2(1.0, 0.0));
            vec4 ofc = hash4(iuv + vec2(0.0, 1.0));
            vec4 ofd = hash4(iuv + vec2(1.0, 1.0));

            vec2 ddxuv = dFdx(uv);
            vec2 ddyuv = dFdy(uv);

            ofa.zw = sign(ofa.zw - 0.5);
            ofb.zw = sign(ofb.zw - 0.5);
            ofc.zw = sign(ofc.zw - 0.5);
            ofd.zw = sign(ofd.zw - 0.5);

            vec2 uva = uv * ofa.zw + ofa.xy; vec2 ddxa = ddxuv * ofa.zw; vec2 ddya = ddyuv * ofa.zw;
            vec2 uvb = uv * ofb.zw + ofb.xy; vec2 ddxb = ddxuv * ofb.zw; vec2 ddyb = ddyuv * ofb.zw;
            vec2 uvc = uv * ofc.zw + ofc.xy; vec2 ddxc = ddxuv * ofc.zw; vec2 ddyc = ddyuv * ofc.zw;
            vec2 uvd = uv * ofd.zw + ofd.xy; vec2 ddxd = ddxuv * ofd.zw; vec2 ddyd = ddyuv * ofd.zw;

            vec2 b = smoothstep(0.25, 0.75, fuv);

            return mix(
                mix(textureGrad(tex, uva, ddxa, ddya),
                    textureGrad(tex, uvb, ddxb, ddyb), b.x),
                mix(textureGrad(tex, uvc, ddxc, ddyc),
                    textureGrad(tex, uvd, ddxd, ddyd), b.x),
                b.y);
        }

        // Expand [0,1] to [-1,1]
        vec4 bx2(vec4 x) { return 2.0 * x - 1.0; }

        void main() {
            if (uHasTex == 1) {
                vec3 dpdx = dFdx(vWorldPos);
                vec3 dpdy = dFdy(vWorldPos);
                vec3 faceN = normalize(cross(dpdx, dpdy));
                float steepness = abs(faceN.z);

                vec2 groundUV = vWorldPos.xy / uGroundWrap;
                vec2 slopeUV = vWorldPos.xy / uSlopeWrap;
                vec2 wallUV = abs(faceN.x) > abs(faceN.y)
                    ? vWorldPos.yz / uWallWrap
                    : vWorldPos.xz / uWallWrap;

                vec3 groundCol = textureNoTile(uGroundTex, groundUV).rgb;
                vec3 slopeCol = textureNoTile(uSlopeTex, slopeUV).rgb;
                vec3 wallCol = textureNoTile(uWallTex, wallUV).rgb;

                float groundFactor = smoothstep(0.6, 0.8, steepness);
                float wallFactor = smoothstep(0.4, 0.2, steepness);
                float slopeFactor = 1.0 - groundFactor - wallFactor;

                vec3 texCol = groundCol * groundFactor + slopeCol * slopeFactor + wallCol * wallFactor;

                if (uHasBump == 1) {
                    // Dot3 bump mapping: bump texture dotted with per-vertex light direction
                    vec2 bumpUV = vWorldPos.xy / uBumpWrap;
                    vec4 bumpTexColor = bx2(textureNoTile(uBumpTex, bumpUV));
                    vec4 bumpDiffuse = bx2(vBumpDiffuse);
                    float dot3Light = clamp(dot(bumpTexColor.rgb, bumpDiffuse.rgb), 0.0, 1.0);

                    // Game formula: 2.0 * (dot3Light * diffuseTexture + bakedLightmap * 0.5)
                    FragColor = vec4(2.0 * (dot3Light * texCol + vColor.rgb * 0.5), 1.0);
                } else {
                    // Non-bump: game uses saturate(texture + lightmap)
                    FragColor = vec4(clamp(texCol + vColor.rgb, 0.0, 1.0), 1.0);
                }
            } else {
                FragColor = vColor;
            }
        }
        """;

    public const string SolidVert = """
        #version 300 es
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMVP;
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
        }
        """;

    public const string SolidFrag = """
        #version 300 es
        precision mediump float;
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() {
            FragColor = uColor;
        }
        """;

    public const string DomeVert = """
        #version 300 es
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUV;
        uniform mat4 uMVP;
        out vec2 vUV;
        void main() {
            gl_Position = uMVP * vec4(aPos, 1.0);
            vUV = aUV;
        }
        """;

    public const string DomeFrag = """
        #version 300 es
        precision mediump float;
        uniform sampler2D uTex;
        in vec2 vUV;
        out vec4 FragColor;
        void main() {
            FragColor = texture(uTex, vUV);
        }
        """;

    public const string ModelVert = """
        #version 300 es
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUV;
        layout(location = 3) in vec3 aColor;
        uniform mat4 uMVP;
        uniform mat4 uModel;
        out vec3 vColor;
        out vec2 vUV;
        out vec3 vNormal;
        out vec3 vWorldPos;
        void main() {
            gl_Position = uMVP * uModel * vec4(aPos, 1.0);
            vColor = aColor;
            vUV = aUV;
            // Transform normal to world space (using model matrix upper-3x3)
            vNormal = mat3(uModel) * aNormal;
            vWorldPos = (uModel * vec4(aPos, 1.0)).xyz;
        }
        """;

    public const string ModelFrag = """
        #version 300 es
        precision mediump float;
        in vec3 vColor;
        in vec2 vUV;
        in vec3 vNormal;
        in vec3 vWorldPos;
        uniform int uHasTex;
        uniform sampler2D uTex;
        uniform int uHasNormals;
        uniform int uLightCount;
        uniform vec3 uLightDir[4];
        uniform vec3 uLightColor[4];
        uniform vec3 uSceneAmbient;
        uniform vec3 uMatAmbient;
        uniform vec3 uMatDiffuse;
        uniform vec3 uMatEmissive;
        uniform vec3 uMatSpecular;
        uniform float uMatPower;
        uniform vec3 uCameraPos;
        uniform float uColorScale;
        out vec4 FragColor;
        void main() {
            vec3 texColor = vec3(1.0);
            float alpha = 1.0;
            if (uHasTex == 1) {
                vec4 t = texture(uTex, vUV);
                texColor = t.rgb;
                alpha = t.a;
            }

            if (uHasNormals == 1 && uLightCount > 0) {
                vec3 N = normalize(vNormal);
                vec3 lightDiffuse = vec3(0.0);
                vec3 lightSpecular = vec3(0.0);
                vec3 V = normalize(uCameraPos - vWorldPos);
                for (int i = 0; i < 4; i++) {
                    if (i >= uLightCount) break;
                    vec3 L = normalize(uLightDir[i]);
                    float NdotL = max(0.0, dot(N, L));
                    lightDiffuse += uLightColor[i] * NdotL;
                    if (uMatPower > 0.0) {
                        vec3 H = normalize(V + L);
                        float NdotH = max(0.0, dot(H, N));
                        lightSpecular += uLightColor[i] * pow(NdotH, uMatPower);
                    }
                }
                vec3 specular = clamp(uMatSpecular * lightSpecular, 0.0, 1.0);
                vec3 finalColor;
                if (uColorScale > 0.0) {
                    vec3 ambient = uMatAmbient * uSceneAmbient;
                    vec3 diffuse = uMatDiffuse * lightDiffuse;
                    vec3 lighting = clamp(ambient + diffuse + uMatEmissive, 0.0, 1.0);
                    finalColor = texColor * lighting * uColorScale + specular;
                } else {
                    finalColor = texColor + specular;
                }
                FragColor = vec4(finalColor, alpha);
            } else {
                FragColor = vec4(texColor * vColor, alpha);
            }
        }
        """;
}
