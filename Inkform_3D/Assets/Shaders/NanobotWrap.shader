// 包裹 shader：在物体表面建"到入射点 P 的距离场"，阈值 _Grow 0→1 往外扫。
// d < grow → 已包裹(possessed albedo)；d ≈ grow → 发光生长前沿；d > grow → 原表面。
// 前沿叠三平面噪声打碎，呈不规则细胞状而非光滑球面。
// 入射点用从底部爬上来的接触点 P，包裹从底往上裹，和"立起"那一下连续。
Shader "Inkform/NanobotWrap"
{
    Properties
    {
        _BaseColor      ("Base Color (原表面)", Color) = (0.6, 0.6, 0.65, 1)
        _PossessedColor ("Possessed Color (已包裹)", Color) = (0.15, 0.35, 0.55, 1)
        _EdgeColor      ("Edge Color (生长前沿)", Color) = (0.4, 0.9, 1, 1)
        [HDR]_EmissionColor ("Emission (高亮用)", Color) = (0, 0, 0, 1)

        _Grow        ("Grow", Range(0,1)) = 0
        _EntryPoint  ("Entry Point (world)", Vector) = (0,0,0,0)
        _MaxDist     ("Max Dist", Float) = 1
        _HorizPenalty("Horizontal Penalty (优先竖爬)", Range(0,3)) = 0.6
        _EdgeWidth   ("Edge Width", Range(0.001,0.3)) = 0.06
        _NoiseScale  ("Noise Scale", Float) = 6
        _NoiseAmp    ("Noise Amp", Range(0,0.3)) = 0.08

        _Smoothness  ("Smoothness", Range(0,1)) = 0.4
        _Metallic    ("Metallic", Range(0,1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _EMISSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PossessedColor;
                float4 _EdgeColor;
                float4 _EmissionColor;
                float  _Grow;
                float4 _EntryPoint;
                float  _MaxDist;
                float  _HorizPenalty;
                float  _EdgeWidth;
                float  _NoiseScale;
                float  _NoiseAmp;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            // 简易 3D 值噪声（三平面用），无纹理依赖。
            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float vnoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i + float3(0,0,0));
                float n100 = hash13(i + float3(1,0,0));
                float n010 = hash13(i + float3(0,1,0));
                float n110 = hash13(i + float3(1,1,0));
                float n001 = hash13(i + float3(0,0,1));
                float n101 = hash13(i + float3(1,0,1));
                float n011 = hash13(i + float3(0,1,1));
                float n111 = hash13(i + float3(1,1,1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 到入射点的距离场：加水平惩罚让阈值面优先竖着爬。
                float3 delta = IN.positionWS - _EntryPoint.xyz;
                float horiz = length(delta.xz);
                float vert = abs(delta.y);
                float d = (length(delta) + horiz * _HorizPenalty) / max(0.0001, _MaxDist);

                // 前沿叠噪声打碎，细胞感。
                float n = (vnoise(IN.positionWS * _NoiseScale) - 0.5) * _NoiseAmp;
                d += n;

                float grow = _Grow;
                // 三态：已包裹 / 前沿 / 原表面。
                float wrapped = step(d, grow);
                float edge = smoothstep(grow + _EdgeWidth, grow, d) * (1.0 - wrapped);

                half3 albedo = lerp(_BaseColor.rgb, _PossessedColor.rgb, wrapped);

                // URP 标准光照。
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalize(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic * wrapped;       // 包裹后更金属
                surfaceData.smoothness = lerp(_Smoothness, 0.7, wrapped);
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;
                // 发光 = 生长前沿 + 高亮 emission
                surfaceData.emission = _EdgeColor.rgb * edge * 3.0 + _EmissionColor.rgb;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                return color;
            }
            ENDHLSL
        }

        // 阴影 + 深度：复用 URP/Lit 的 pass，否则不投影、SSAO/景深缺。
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Universal Render Pipeline/Lit"
}
