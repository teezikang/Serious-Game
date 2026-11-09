Shader "Custom/ToonWaterOrb"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Light Blue", Color) = (0.25, 0.65, 0.95, 1)
        _DeepColor    ("Deep Blue", Color)  = (0.05, 0.22, 0.60, 1)
        _TealColor    ("Teal Accent", Color) = (0.10, 0.65, 0.80, 1)
        _FoamColor    ("Foam / Highlight Color", Color) = (0.95, 0.98, 1, 1)
        _OutlineColor ("Outline Color", Color) = (0.03, 0.15, 0.40, 1)

        [Header(Shape)]
        _Radius       ("Orb Radius (of quad)", Range(0.2, 1)) = 0.7
        _WobbleAmp    ("Wobble Amount", Range(0, 0.2)) = 0.05
        _WobbleSpeed  ("Wobble Speed", Float) = 1.5

        [Header(Surface Cells)]
        _CellScale    ("Cell Scale", Float) = 3
        _CellSpeed    ("Cell Animation Speed", Float) = 0.5
        _SpinY        ("Spin Around Y", Float) = 0.35
        _SpinX        ("Spin Around X", Float) = 0.12
        _LineWidth    ("Foam Line Width", Range(0, 0.5)) = 0.07
        _LineSoftness ("Foam Line Softness", Range(0.001, 0.5)) = 0.06
        _JunctionSize ("Junction Size", Range(0, 0.6)) = 0.14
        _LineStrength ("Foam Line Strength", Range(0, 1)) = 0.9

        [Header(Toon Lighting)]
        _LightDir     ("Light Direction (x, y)", Vector) = (-0.5, 0.6, 0, 0)
        _LightZ       ("Light Height", Range(0.1, 2)) = 0.8
        _LightSteps   ("Light Steps", Range(1, 6)) = 3
        _ShadowTint   ("Shadow Tint", Color) = (0.55, 0.68, 0.95, 1)

        [Header(Rim and Outline)]
        _RimStart     ("Rim Band Start", Range(0.5, 0.98)) = 0.86
        _RimStrength  ("Rim Strength", Range(0, 1)) = 0.55
        _OutlineWidth ("Outline Width", Range(0, 0.15)) = 0.04

        [Header(Highlights)]
        _HighlightPos  ("Highlight Distance From Center", Range(0, 0.9)) = 0.5
        _HighlightSize ("Highlight Size", Range(0.02, 0.5)) = 0.16
        _CausticStrength ("Bottom Caustic Strength", Range(0, 1)) = 0.6

        [Header(Transparency and Glow)]
        _CenterAlpha  ("Center Opacity", Range(0, 1)) = 0.6
        _EdgeAlpha    ("Edge Opacity", Range(0, 1)) = 0.95
        _GlowSize     ("Glow Size", Range(0, 0.5)) = 0.22
        _GlowStrength ("Glow Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ToonWaterOrb"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _TealColor, _FoamColor, _OutlineColor;
                float _Radius, _WobbleAmp, _WobbleSpeed;
                float _CellScale, _CellSpeed, _SpinY, _SpinX;
                float _LineWidth, _LineSoftness, _JunctionSize, _LineStrength;
                float4 _LightDir;
                float _LightZ, _LightSteps;
                half4 _ShadowTint;
                float _RimStart, _RimStrength, _OutlineWidth;
                float _HighlightPos, _HighlightSize, _CausticStrength;
                float _CenterAlpha, _EdgeAlpha, _GlowSize, _GlowStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 size : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.size = float2(length(unity_ObjectToWorld._m00_m10_m20),
                                  length(unity_ObjectToWorld._m01_m11_m21));
                return OUT;
            }

            float3 hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            float3 rotY(float3 v, float a)
            {
                float c = cos(a), sn = sin(a);
                return float3(v.x * c + v.z * sn, v.y, -v.x * sn + v.z * c);
            }

            float3 rotX(float3 v, float a)
            {
                float c = cos(a), sn = sin(a);
                return float3(v.x, v.y * c - v.z * sn, v.y * sn + v.z * c);
            }

            struct Vor3
            {
                float f1, f2, f3;
                float3 id;
            };

            Vor3 voronoi3(float3 x, float tt)
            {
                float3 cell = floor(x);
                float3 f = frac(x);
                Vor3 v;
                v.f1 = 8; v.f2 = 8; v.f3 = 8; v.id = 0;

                for (int k = -1; k <= 1; k++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        for (int i = -1; i <= 1; i++)
                        {
                            float3 g = float3(i, j, k);
                            float3 o = 0.5 + 0.42 * sin(tt + 6.2831 * hash33(cell + g));
                            float d = length(g + o - f);

                            if (d < v.f1)      { v.f3 = v.f2; v.f2 = v.f1; v.f1 = d; v.id = cell + g; }
                            else if (d < v.f2) { v.f3 = v.f2; v.f2 = d; }
                            else if (d < v.f3) { v.f3 = d; }
                        }
                    }
                }
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;

                // ---------- Orb shape on the quad (circle fits the shortest side) ----------
                float2 p = (IN.uv - 0.5) * 2.0;
                p *= IN.size / min(IN.size.x, IN.size.y);

                float ang = atan2(p.y, p.x);
                float wob = 1.0 + _WobbleAmp * (sin(ang * 3.0 + t * _WobbleSpeed) * 0.6
                                              + sin(ang * 5.0 - t * _WobbleSpeed * 1.3) * 0.4);
                float2 q = p / (_Radius * wob);   // q has length 1 on the orb edge
                float s = length(q);

                float aa = max(fwidth(s), 1e-4);
                float mask = 1.0 - smoothstep(1.0 - aa * 2.0, 1.0, s);

                // outer glow (also the early exit for pixels far outside the orb)
                float glowA = (1.0 - smoothstep(1.0, 1.0 + _GlowSize, s)) * _GlowStrength * (1.0 - mask);
                if (s > 1.0 + _GlowSize)
                    return half4(0, 0, 0, 0);

                // ---------- Fake sphere normal ----------
                float z = sqrt(saturate(1.0 - s * s));
                float3 N = normalize(float3(q, z));

                // ---------- Cells live on the sphere surface (3D voronoi, no seams) ----------
                float3 P = rotY(N, t * _SpinY);
                P = rotX(P, t * _SpinX + 0.4);
                Vor3 v = voronoi3(P * _CellScale + 13.7, t * _CellSpeed);

                float edge = v.f2 - v.f1;
                float junction = v.f3 - v.f1;
                float lineMask = 1.0 - smoothstep(_LineWidth, _LineWidth + _LineSoftness, edge);
                float junc = 1.0 - smoothstep(0.0, max(_JunctionSize, 0.0001), junction);
                float gap = saturate(max(lineMask, junc * junc)) * _LineStrength;

                // ---------- Per-cell color ----------
                float h  = hash33(v.id).x;
                float h2 = hash33(v.id + 17.3).y;
                half3 col = lerp(_DeepColor.rgb, _ShallowColor.rgb, 0.35 + 0.65 * h);
                col = lerp(col, _TealColor.rgb, smoothstep(0.7, 1.0, h2) * 0.7);
                col = lerp(col, _ShallowColor.rgb, (1.0 - smoothstep(0.0, 0.6, v.f1)) * 0.2);
                float cellRim = 1.0 - smoothstep(_LineWidth, _LineWidth + 0.2, edge);
                col *= 1.0 - cellRim * 0.25;

                // deeper toward the silhouette
                col = lerp(col, _DeepColor.rgb, pow(1.0 - z, 2.0) * 0.6);

                // ---------- Toon lighting ----------
                float3 L = normalize(float3(_LightDir.xy, _LightZ));
                float ndl = dot(N, L);
                float steps = max(_LightSteps, 1.0);
                float lit = floor(saturate(ndl) * steps + 0.5) / steps;
                col *= lerp(_ShadowTint.rgb, half3(1, 1, 1), lit);

                // ---------- Bottom caustic (light focused through the water) ----------
                float2 ld = normalize(_LightDir.xy + 0.0001);
                float cd = dot(N.xy, -ld);
                float caustic = smoothstep(0.55, 0.95, cd) * smoothstep(0.5, 0.9, s);
                col = lerp(col, _ShallowColor.rgb * 1.3, caustic * _CausticStrength);

                // ---------- Foam lines ----------
                col = lerp(col, _FoamColor.rgb, gap);

                // ---------- Rim band and outline ----------
                float outline = smoothstep(1.0 - _OutlineWidth, 1.0 - _OutlineWidth + 0.02, s);
                float rimBand = smoothstep(_RimStart, _RimStart + 0.03, s) * (1.0 - outline);
                col = lerp(col, _FoamColor.rgb, rimBand * _RimStrength);
                col = lerp(col, _OutlineColor.rgb, outline);

                // ---------- Toon highlights ----------
                float2 dir = normalize(_LightDir.xy + 0.0001);
                float2 hlPos = dir * _HighlightPos;
                float2 dd = q - hlPos;
                float along = dot(dd, dir);
                float perp = dot(dd, float2(-dir.y, dir.x));
                float hl1 = 1.0 - smoothstep(_HighlightSize, _HighlightSize + 0.03,
                                             length(float2(along * 1.0, perp * 0.55)));
                float hl2 = 1.0 - smoothstep(_HighlightSize * 0.35, _HighlightSize * 0.35 + 0.03,
                                             length(q + dir * _HighlightPos * 0.75));
                float hl = saturate(hl1 + hl2) * (1.0 - outline);
                col = lerp(col, _FoamColor.rgb, hl);

                // ---------- Alpha ----------
                float alpha = lerp(_CenterAlpha, _EdgeAlpha, 1.0 - z);
                alpha = max(alpha, gap * 0.95);
                alpha = max(alpha, rimBand * _RimStrength);
                alpha = max(alpha, max(outline, hl));
                alpha *= mask;

                // ---------- Add glow behind the orb ----------
                float totalA = alpha + glowA * (1.0 - alpha);
                half3 rgb = (col * alpha + _ShallowColor.rgb * glowA * (1.0 - alpha)) / max(totalA, 1e-4);

                return half4(rgb, totalA);
            }
            ENDHLSL
        }
    }
}
