Shader "Custom/ToonCloudWeather"
{
    Properties
    {
        [Header(Weather)]
        _Weather      ("Weather  (0 Sunny, 1 Cloudy, 2 Rainy)", Range(0, 2)) = 0
        [Toggle] _AutoCycle ("Auto Cycle Through Weather (demo)", Float) = 0
        _CycleSpeed   ("Auto Cycle Speed", Float) = 0.3

        [Header(Sunny Colors)]
        _LitSunny     ("Sunny Lit", Color)   = (1, 1, 1, 1)
        _ShadeSunny   ("Sunny Shade", Color) = (0.68, 0.80, 0.96, 1)

        [Header(Cloudy Colors)]
        _LitCloudy    ("Cloudy Lit", Color)   = (0.86, 0.89, 0.93, 1)
        _ShadeCloudy  ("Cloudy Shade", Color) = (0.52, 0.58, 0.70, 1)

        [Header(Rainy Colors)]
        _LitRainy     ("Rainy Lit", Color)   = (0.47, 0.52, 0.60, 1)
        _ShadeRainy   ("Rainy Shade", Color) = (0.17, 0.20, 0.29, 1)

        [Header(Cloud Shape and Toon)]
        _CloudBase    ("Cloud Base Height", Range(-0.3, 0.4)) = 0.05
        _Fluff        ("Surface Fluff", Range(0, 2)) = 1
        _Steps        ("Toon Light Steps", Range(1, 5)) = 3
        _OutlineWidth ("Outline Width", Range(0, 0.6)) = 0.22
        _OutlineStrength ("Outline Strength", Range(0, 1)) = 0.6
        _EdgeSoft     ("Edge Anti-aliasing", Range(0.002, 0.05)) = 0.012

        [Header(Rain)]
        _RainColor    ("Rain Color", Color) = (0.70, 0.85, 1, 1)
        _RainOpacity  ("Rain Opacity", Range(0, 1)) = 0.85
        _RainDensity  ("Rain Columns Per Unit", Float) = 4
        _RainCoverage ("Rain Coverage", Range(0, 1)) = 0.65
        _RainWidth    ("Rain Streak Width", Range(0.01, 0.3)) = 0.07
        _RainStreakFreq ("Rain Streak Frequency", Float) = 1.2
        _RainLen      ("Rain Streak Length", Range(0.05, 0.9)) = 0.35
        _RainSpeed    ("Rain Fall Speed", Float) = 2.2

        [Header(Lightning (rainy only))]
        _Lightning    ("Lightning Strength", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ToonCloud"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front   // draw the back faces so the cloud also works when the camera is inside the cube

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Weather, _AutoCycle, _CycleSpeed;
                half4 _LitSunny, _ShadeSunny, _LitCloudy, _ShadeCloudy, _LitRainy, _ShadeRainy;
                float _CloudBase, _Fluff, _Steps, _OutlineWidth, _OutlineStrength, _EdgeSoft;
                half4 _RainColor;
                float _RainOpacity, _RainDensity, _RainCoverage, _RainWidth, _RainStreakFreq, _RainLen, _RainSpeed;
                float _Lightning;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 scale      : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.scale = float3(length(unity_ObjectToWorld._m00_m10_m20),
                                   length(unity_ObjectToWorld._m01_m11_m21),
                                   length(unity_ObjectToWorld._m02_m12_m22));
                return OUT;
            }

            float hash21(float2 p) { return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453); }

            float smin(float a, float b, float k)
            {
                float h = max(k - abs(a - b), 0.0) / k;
                return min(a, b) - h * h * k * 0.25;
            }

            float smax(float a, float b, float k)
            {
                return -smin(-a, -b, k);
            }

            // x, y, z (x/z as fraction of box size, y in units of R), radius (units of R)
            static const float4 BLOBS[9] =
            {
                float4( 0.00, 0.10,  0.00, 0.24),
                float4(-0.20, 0.03,  0.04, 0.19),
                float4( 0.21, 0.04, -0.03, 0.20),
                float4(-0.08, 0.20, -0.08, 0.17),
                float4( 0.09, 0.19,  0.09, 0.16),
                float4( 0.02, 0.05,  0.22, 0.16),
                float4(-0.02, 0.06, -0.22, 0.16),
                float4(-0.33, 0.01,  0.00, 0.12),
                float4( 0.33, 0.01,  0.02, 0.12)
            };

            struct CloudParams
            {
                float3 scl;
                float baseY;
                float R;
                float sizeMul;
                float stretch;
                float t;
            };

            float cloudSDF(float3 p, CloudParams c)
            {
                float3 q = p;
                q.y = c.baseY + (p.y - c.baseY) * c.stretch;   // stretch > 1 makes the cloud flatter

                float d = 1e5;
                float k = 0.10 * c.R;

                [unroll] for (int i = 0; i < 9; i++)
                {
                    float4 b = BLOBS[i];
                    float3 ctr = float3(b.x * c.scl.x * c.sizeMul,
                                        c.baseY + b.y * c.R * c.sizeMul,
                                        b.z * c.scl.z * c.sizeMul);
                    ctr.y += sin(c.t * 0.6 + i * 1.7) * 0.012 * c.R;   // gentle bobbing
                    d = smin(d, length(q - ctr) - b.w * c.R * c.sizeMul, k);
                }

                // soft surface bumps
                float f = 9.0 / c.R;
                float bump = sin(q.x * f + c.t * 0.5) * sin(q.y * f * 1.3 - c.t * 0.4) * sin(q.z * f + c.t * 0.3);
                d += bump * _Fluff * c.R * 0.02;

                // flat bottom
                d = smax(d, c.baseY - p.y, 0.04 * c.R);

                return d / max(c.stretch, 1.0);
            }

            float3 cloudNormal(float3 p, CloudParams c)
            {
                float e = 0.01 * c.R;
                float3 k0 = float3( 1, -1, -1);
                float3 k1 = float3(-1, -1,  1);
                float3 k2 = float3(-1,  1, -1);
                float3 k3 = float3( 1,  1,  1);
                return normalize(k0 * cloudSDF(p + k0 * e, c)
                               + k1 * cloudSDF(p + k1 * e, c)
                               + k2 * cloudSDF(p + k2 * e, c)
                               + k3 * cloudSDF(p + k3 * e, c));
            }

            // one rain "sheet": a vertical plane with falling streak columns
            float rainSample(float3 p, float u, float seed, float t, float valid, float3 scl, float baseY)
            {
                float cu = u * _RainDensity + seed * 7.13;
                float cell = floor(cu);
                float fu = cu - cell;
                float h1 = hash21(float2(cell, seed));
                float h2 = hash21(float2(cell + 31.7, seed + 5.3));
                float h3 = hash21(float2(cell * 1.7 + 9.1, seed * 3.3));

                float colMask = 1.0 - smoothstep(_RainWidth, _RainWidth * 1.4, abs(fu - (0.2 + 0.6 * h1)));
                float exists = step(h2, _RainCoverage);

                float yy = p.y * _RainStreakFreq + t * _RainSpeed * (0.8 + 0.4 * h2) + h3 * 13.0;
                float streak = 1.0 - smoothstep(_RainLen, _RainLen + 0.05, frac(yy));

                float baseFade = smoothstep(0.0, 0.10 * scl.y, baseY - p.y);
                float bottomFade = smoothstep(-0.5 * scl.y, -0.35 * scl.y, p.y);
                float foot = 1.0 - smoothstep(0.30, 0.42, length(float2(p.x / scl.x, p.z / scl.z)));

                return colMask * exists * streak * baseFade * bottomFade * foot * valid;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;

                // ---------- Weather value ----------
                float w = _AutoCycle > 0.5 ? 1.0 + sin(t * _CycleSpeed) : _Weather;
                w = clamp(w, 0.0, 2.0);
                float wc = saturate(w);          // 0 sunny -> 1 cloudy
                float wr = saturate(w - 1.0);    // 0 cloudy -> 1 rainy

                // ---------- Work in "scaled object space" so cloud blobs stay round on any cube scale ----------
                float3 scl = IN.scale;
                float3 H = 0.5 * scl;
                float R = min(scl.x, scl.z);

                float3 ax0 = normalize(unity_ObjectToWorld._m00_m10_m20);
                float3 ax1 = normalize(unity_ObjectToWorld._m01_m11_m21);
                float3 ax2 = normalize(unity_ObjectToWorld._m02_m12_m22);

                float3 camOS = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 pEnd = IN.positionOS * scl;
                float3 roPersp = camOS * scl;
                float3 rdPersp = normalize(pEnd - roPersp);

                // orthographic cameras (isometric games) use parallel rays
                float3 rdW = -UNITY_MATRIX_V[2].xyz;
                float3 rdOrtho = float3(dot(rdW, ax0), dot(rdW, ax1), dot(rdW, ax2));
                float isOrtho = step(0.5, unity_OrthoParams.w);
                float3 rd = normalize(lerp(rdPersp, rdOrtho, isOrtho));
                float3 ro = lerp(roPersp, pEnd - rd * (2.0 * length(scl)), isOrtho);

                // sun direction in the same space
                float3 Lw = normalize(_MainLightPosition.xyz);
                float3 Lo = float3(dot(Lw, ax0), dot(Lw, ax1), dot(Lw, ax2));

                // ---------- Ray vs cube ----------
                float3 safeRd = float3(abs(rd.x) < 1e-6 ? 1e-6 : rd.x,
                                       abs(rd.y) < 1e-6 ? 1e-6 : rd.y,
                                       abs(rd.z) < 1e-6 ? 1e-6 : rd.z);
                float3 inv = 1.0 / safeRd;
                float3 t0 = (-H - ro) * inv;
                float3 t1 = ( H - ro) * inv;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                float tN = max(max(tmin.x, tmin.y), tmin.z);
                float tF = min(min(tmax.x, tmax.y), tmax.z);
                float tStart = max(tN, 0.0);
                if (tF <= tStart)
                    return half4(0, 0, 0, 0);

                // ---------- Weather shapes the cloud ----------
                CloudParams cp;
                cp.scl = scl;
                cp.R = R;
                cp.t = t;
                cp.sizeMul = lerp(lerp(0.85, 1.0, wc), 1.1, wr);
                cp.stretch = lerp(lerp(1.0, 1.15, wc), 1.5, wr);
                cp.baseY = (_CloudBase + 0.10 * wr) * scl.y;   // cloud lifts a little to leave room for rain

                // ---------- Sphere-trace the cloud ----------
                float eps = 0.0025 * R;
                float tt = tStart;
                float minD = 1e5;
                bool hit = false;
                [loop] for (int i = 0; i < 64; i++)
                {
                    float3 p = ro + rd * tt;
                    float d = cloudSDF(p, cp);
                    minD = min(minD, d);
                    if (d < eps) { hit = true; break; }
                    tt += d * 0.9;
                    if (tt > tF) break;
                }

                // ---------- Colors by weather ----------
                half3 litCol = lerp(lerp(_LitSunny.rgb, _LitCloudy.rgb, wc), _LitRainy.rgb, wr);
                half3 shCol  = lerp(lerp(_ShadeSunny.rgb, _ShadeCloudy.rgb, wc), _ShadeRainy.rgb, wr);

                // lightning flicker (rainy only)
                float ft = frac(t * 0.21 + 0.37);
                float flash = smoothstep(0.0, 0.01, ft) * (1.0 - smoothstep(0.03, 0.09, ft))
                            + 0.7 * smoothstep(0.12, 0.125, ft) * (1.0 - smoothstep(0.125, 0.17, ft));
                flash *= wr * _Lightning;

                half3 cloudCol = shCol * 0.7;   // used for the anti-aliased silhouette when we only graze the cloud
                if (hit)
                {
                    float3 p = ro + rd * tt;
                    float3 n = cloudNormal(p, cp);

                    // toon bands from wrapped lighting, darker underside in bad weather
                    float wrap = dot(n, Lo) * 0.5 + 0.5;
                    wrap -= saturate(-n.y) * 0.35 * (0.4 + wr);
                    float stepsN = max(_Steps, 1.0);
                    float band = floor(saturate(wrap) * stepsN + 0.5) / stepsN;
                    cloudCol = lerp(shCol, litCol, band);

                    // darker pockets in the creases
                    float aoDist = 0.12 * R;
                    float ao = smoothstep(0.35, 0.5, saturate(cloudSDF(p + n * aoDist, cp) / aoDist));
                    cloudCol = lerp(cloudCol * 0.82, cloudCol, ao);

                    // outline toward the silhouette
                    float fres = saturate(dot(n, -rd));
                    float outline = 1.0 - smoothstep(_OutlineWidth, _OutlineWidth + 0.05, fres);
                    cloudCol = lerp(cloudCol, shCol * 0.7, outline * _OutlineStrength);

                    // lightning lights the cloud from inside
                    cloudCol += half3(0.55, 0.65, 1.0) * flash * (0.4 + 0.6 * (1.0 - band));
                }

                float softW = _EdgeSoft * R;
                float edgeA = hit ? 1.0 : saturate(1.0 - minD / softW);

                // ---------- Rain: crisp streaks drawn on vertical sheets, only in front of the cloud ----------
                float tEnd = hit ? tt : tF;
                float rainA = 0.0;
                if (wr > 0.01)
                {
                    float rainAmt = smoothstep(0.0, 1.0, wr);
                    [unroll] for (int k = 0; k < 4; k++)
                    {
                        float fk = (k + 0.5) / 4.0 - 0.5;

                        // sheet perpendicular to Z
                        float tz = (fk * scl.z - ro.z) / safeRd.z;
                        float3 pz = ro + rd * tz;
                        float vz = step(tStart, tz) * step(tz, tEnd) * step(abs(pz.x), H.x) * step(abs(pz.y), H.y);
                        float az = rainSample(pz, pz.x, k * 1.7 + 0.3, t, vz, scl, cp.baseY);
                        rainA = 1.0 - (1.0 - rainA) * (1.0 - az);

                        // sheet perpendicular to X
                        float tx = (fk * scl.x - ro.x) / safeRd.x;
                        float3 px = ro + rd * tx;
                        float vx = step(tStart, tx) * step(tx, tEnd) * step(abs(px.z), H.z) * step(abs(px.y), H.y);
                        float ax_ = rainSample(px, px.z, k * 2.3 + 5.1, t, vx, scl, cp.baseY);
                        rainA = 1.0 - (1.0 - rainA) * (1.0 - ax_);
                    }
                    rainA *= _RainOpacity * rainAmt;
                }
                half3 rainCol = _RainColor.rgb * (1.0 + flash * 0.8);

                // ---------- Composite: rain in front, cloud behind ----------
                float totalA = rainA + edgeA * (1.0 - rainA);
                half3 rgb = (rainCol * rainA + cloudCol * edgeA * (1.0 - rainA)) / max(totalA, 1e-4);

                return half4(rgb, totalA);
            }
            ENDHLSL
        }
    }
}
