// metacube metaball：在覆盖蜂群 AABB 的包围盒上做光线步进，对每采样点所在网格格内的 box-SDF
// 取平滑并集(smin) → 相邻方块融成连续的块状金属表面（保留棱角）。命中写 SV_Depth 与不透明几何正确遮挡。
Shader "Inkform/MetacubeRaymarch"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.7, 0.78, 0.9, 1)
        _EmissionColor("Emission", Color) = (0.10, 0.18, 0.30, 1)
        _FresnelPow("Fresnel Power", Float) = 4
        _ReflStrength("Reflection", Range(0,1)) = 0.9
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+450" "RenderType" = "Opaque" }

        Pass
        {
            Cull Front      // 渲染背面 → 相机在盒内/外都有片元
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct GpuCube { float3 pos; float halfExtent; };

            StructuredBuffer<GpuCube> _Cubes;
            StructuredBuffer<int> _CellCount;
            StructuredBuffer<int> _CellItems;

            int _Count;
            int _GridRes;
            int _MaxPerCell;
            int _MaxSteps;
            float3 _GridMin;
            float _CellSize;
            float _SmoothK;

            float4 _BaseColor;
            float4 _EmissionColor;
            float _FresnelPow;
            float _ReflStrength;

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 worldPos : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(IN.positionOS);
                o.worldPos = w;
                o.positionHCS = TransformWorldToHClip(w);
                return o;
            }

            int CellIndex(int3 c) { return (c.z * _GridRes + c.y) * _GridRes + c.x; }

            float sdBox(float3 p, float3 b)
            {
                float3 q = abs(p) - b;
                return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
            }

            float smin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            float mapSDF(float3 P)
            {
                int3 c = (int3)floor((P - _GridMin) / _CellSize);
                if (any(c < 0) || any(c >= _GridRes)) return _CellSize; // 网格外：步进一格
                int cell = CellIndex(c);
                int n = min(_CellCount[cell], _MaxPerCell);
                if (n == 0) return _CellSize;

                float d = 1e9;
                float k = max(1e-4, _SmoothK);
                [loop] for (int i = 0; i < n; i++)
                {
                    GpuCube cu = _Cubes[_CellItems[cell * _MaxPerCell + i]];
                    float3 he = cu.halfExtent; // 标量→float3 splat
                    float db = sdBox(P - cu.pos, he);
                    d = smin(d, db, k);
                }
                return d;
            }

            float3 calcNormal(float3 P)
            {
                float e = 0.01;
                float2 h = float2(e, 0);
                return normalize(float3(
                    mapSDF(P + h.xyy) - mapSDF(P - h.xyy),
                    mapSDF(P + h.yxy) - mapSDF(P - h.yxy),
                    mapSDF(P + h.yyx) - mapSDF(P - h.yyx)));
            }

            bool intersectAABB(float3 ro, float3 rd, float3 bmin, float3 bmax, out float tn, out float tf)
            {
                float3 inv = 1.0 / rd;
                float3 t0 = (bmin - ro) * inv;
                float3 t1 = (bmax - ro) * inv;
                float3 tmin = min(t0, t1), tmax = max(t0, t1);
                tn = max(max(tmin.x, tmin.y), tmin.z);
                tf = min(min(tmax.x, tmax.y), tmax.z);
                return tf >= max(tn, 0.0);
            }

            void frag(Varyings IN, out half4 col : SV_Target, out float depth : SV_Depth)
            {
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(IN.worldPos - ro);
                float3 bmin = _GridMin;
                float3 bmax = _GridMin + _CellSize * _GridRes;

                float tn, tf;
                if (!intersectAABB(ro, rd, bmin, bmax, tn, tf)) discard;
                tn = max(tn, 0.0);

                float t = tn;
                bool hit = false;
                float3 P = ro;
                [loop] for (int i = 0; i < _MaxSteps; i++)
                {
                    P = ro + rd * t;
                    float d = mapSDF(P);
                    if (d < 0.002) { hit = true; break; }
                    t += clamp(d, 0.01, _CellSize);
                    if (t > tf) break;
                }
                if (!hit) discard;

                float3 N = calcNormal(P);
                float3 V = -rd;
                float3 Rr = reflect(rd, N);

                // 反射探针环境（金属感）。
                half4 enc = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, Rr, 0);
                half3 env = DecodeHDREnvironment(enc, unity_SpecCube0_HDR);

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(N, mainLight.direction));
                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPow);

                float3 baseLit = _BaseColor.rgb * (0.2 + 0.8 * ndl) * mainLight.color;
                float3 c = baseLit + env * _ReflStrength + fres * 0.5 + _EmissionColor.rgb;
                col = half4(c, 1);

                float4 clip = TransformWorldToHClip(P);
                depth = clip.z / clip.w;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
