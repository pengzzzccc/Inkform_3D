// 纳米金属洪流：bot 实例化液金属 shader。
// 配合 NanobotRenderer：mesh 沿速度方向(局部 Y=长轴)拉丝，本 shader 给每根丝
// 高金属镜面 + 沿长轴的流动扫线 + 头亮尾暗渐变。
// ⚠ 必须支持 GPU 实例化（DrawMeshInstanced），否则所有实例画在同一处。
Shader "Inkform/NanobotFlow"
{
    Properties
    {
        _HeadColor   ("Head Color (丝头)", Color) = (0.7, 0.85, 1.0, 1)
        _TailColor   ("Tail Color (丝尾)", Color) = (0.12, 0.22, 0.4, 1)
        [HDR]_EmissionColor ("Emission (高亮)", Color) = (0, 0, 0, 1)

        _Metallic    ("Metallic", Range(0,1)) = 0.9
        _Smoothness  ("Smoothness", Range(0,1)) = 0.85

        _ScanSpeed   ("Scan Speed (流动速度)", Float) = 2.5
        _ScanFreq    ("Scan Freq (扫线密度)", Float) = 3.0
        _ScanGlow    ("Scan Glow (扫线亮度)", Float) = 1.5
        [HDR]_ScanColor ("Scan Color", Color) = (0.6, 0.9, 1.0, 1)

        _NoiseAmp    ("Noise Amp (表面微噪)", Range(0,0.5)) = 0.06
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
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ _EMISSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _HeadColor;
                float4 _TailColor;
                float4 _EmissionColor;
                float4 _ScanColor;
                float  _Metallic;
                float  _Smoothness;
                float  _ScanSpeed;
                float  _ScanFreq;
                float  _ScanGlow;
                float  _NoiseAmp;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  axis01      : TEXCOORD2; // 局部长轴归一 [0,1]（尾→头）
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

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
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                // 内置 Capsule 局部 Y 约在 [-1,1]，映射到 [0,1]（尾→头）。
                OUT.axis01 = saturate(IN.positionOS.y * 0.5 + 0.5);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // 头亮尾暗渐变（沿丝条长轴）。
                half3 albedo = lerp(_TailColor.rgb, _HeadColor.rgb, IN.axis01);

                // 表面微噪：避免塑料感（极轻）。
                float n = (vnoise(IN.positionWS * 8.0) - 0.5) * _NoiseAmp;
                albedo += n;

                // 流动扫线：沿长轴随时间跑一条亮带，像电流在丝里淌。
                float scan = frac(IN.axis01 * _ScanFreq - _Time.y * _ScanSpeed);
                float band = smoothstep(0.85, 1.0, scan); // 窄亮带

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalize(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                // 反射探针 / 环境 IBL 需要 normalizedScreenSpaceUV（粗略给个）
                inputData.normalizedScreenSpaceUV = IN.positionCS.xy / _ScaledScreenParams.xy;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;
                surfaceData.emission = _EmissionColor.rgb + _ScanColor.rgb * (band * _ScanGlow);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                return color;
            }
            ENDHLSL
        }

        // 阴影 + 深度（自带 instancing 支持的 URP 版本）。
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Universal Render Pipeline/Lit"
}
