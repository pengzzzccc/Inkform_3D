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

        [HDR]_TipColor ("Tip Color (生长前沿)", Color) = (0.6, 0.95, 1.0, 1)
        _TipGlow     ("Tip Glow (前沿亮度)", Float) = 4.0

        [HDR]_PanelColor ("Panel Color (面板缝发光)", Color) = (0.3, 0.7, 1.0, 1)
        _PanelGlow   ("Panel Glow", Float) = 2.0
        _PanelLineWidth ("Panel Line Width", Range(0.005,0.2)) = 0.04
        _PanelLengthwise ("Panel Lengthwise (沿管缝密度)", Float) = 2.0
        _PanelAround ("Panel Around (绕截面缝密度)", Float) = 1.0

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
                float4 _TipColor;
                float  _TipGlow;
                float4 _PanelColor;
                float  _PanelGlow;
                float  _PanelLineWidth;
                float  _PanelLengthwise;
                float  _PanelAround;
                float  _NoiseAmp;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0; // uv.x=绕截面[0,1), uv.y=沿管弧长(尾→头)
                float2 uv2        : TEXCOORD1; // uv2.x=节内相位 segPhase, uv2.y=节奇偶
                float4 color      : COLOR;     // color.a = 管头(生长前沿)度
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  axis01      : TEXCOORD2; // 沿管弧长归一 [0,1]（尾→头）
                float  tip         : TEXCOORD3; // 管头度
                float  around      : TEXCOORD4; // 绕截面 [0,1)
                float2 seg         : TEXCOORD5; // x=节内相位, y=节奇偶
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
                OUT.axis01 = saturate(IN.uv.y);     // 沿管弧长(尾→头)
                OUT.tip = saturate(IN.color.a);     // 生长前沿度
                OUT.around = IN.uv.x;               // 绕截面
                OUT.seg = IN.uv2;                   // 节相位/奇偶
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

                // ── 硬表面面板线 ──
                // 沿管节缝：节相位 segPhase 接近 0/1 处亮(节与节交界)。
                float segEdge = min(IN.seg.x, 1.0 - IN.seg.x);                 // 到节缝距离
                float panelLen = 1.0 - smoothstep(0.0, _PanelLineWidth, segEdge);
                // 额外沿管细缝(密度 _PanelLengthwise)。
                float lw = frac(IN.axis01 * _PanelLengthwise);
                panelLen = max(panelLen, 1.0 - smoothstep(0.0, _PanelLineWidth, min(lw, 1.0 - lw)));
                // 绕截面棱：4 条边界(q/4)处亮 + 额外密度。
                float aw = frac(IN.around * 4.0 * max(1.0, _PanelAround));
                float panelAround = 1.0 - smoothstep(0.0, _PanelLineWidth * 2.0, min(aw, 1.0 - aw));
                float panel = saturate(max(panelLen, panelAround));
                // 缝处更暗哑(金属分块感)。
                float matVar = 1.0 - panel * 0.5;

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalize(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                // 反射探针 / 环境 IBL 需要 normalizedScreenSpaceUV（粗略给个）
                inputData.normalizedScreenSpaceUV = IN.positionCS.xy / _ScaledScreenParams.xy;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo * matVar;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness * matVar; // 缝处更哑
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;
                // 发光 = 高亮 + 流动扫线 + 生长前沿(管头) + 面板缝。
                surfaceData.emission = _EmissionColor.rgb
                    + _ScanColor.rgb * (band * _ScanGlow)
                    + _TipColor.rgb * (IN.tip * _TipGlow)
                    + _PanelColor.rgb * (panel * _PanelGlow);

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
