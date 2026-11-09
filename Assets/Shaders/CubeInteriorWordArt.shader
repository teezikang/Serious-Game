// URP-compatible intro title for the KBandar city.
// Put this on a Unity Cube scaled around the whole city. You look INTO the box:
//   left back wall  = left half of the texture  ("WELCOME TO")
//   right back wall = right half of the texture ("KBANDAR")
// _Reveal (0..1): letters rise up and fade in, staggered from left to right.
// _Fade   (1..0): fades the whole title out. IntroTitle.cs drives both.
Shader "Custom/CubeInteriorWordArt"
{
    Properties
    {
        _MainTex      ("Text Strip (alpha = text, 2:1)", 2D) = "white" {}

        [Header(Animation)]
        _Reveal       ("Reveal (0 = hidden, 1 = fully shown)", Range(0, 1)) = 1
        _Fade         ("Fade (1 = visible, 0 = gone)", Range(0, 1)) = 1
        _Rise         ("Rise Distance", Range(0, 0.5)) = 0.2

        [Header(Look)]
        _FrontColor   ("Front Color (top)",    Color) = (1.00, 0.97, 0.80, 1)
        _FrontColor2  ("Front Color (bottom)", Color) = (0.80, 0.92, 0.55, 1)
        _SideColor    ("Extrusion Color",      Color) = (0.08, 0.38, 0.18, 1)
        _ShineColor   ("Shine Color",          Color) = (1, 1, 0.9, 1)
        _Shine        ("Shine Strength", Range(0, 2)) = 0.25
        _Stripe       ("Stripe Contrast", Range(0, 1)) = 0.45
        _Emission     ("Brightness", Range(0.5, 3)) = 1.1

        [Header(Layout)]
        _Depth        ("Extrusion Depth (fraction of cube)", Range(0.01, 0.4)) = 0.06
        _Shrink       ("Text Shrink (1 = full wall width)", Range(1, 3)) = 1.6
        _VShift       ("Move Text Up/Down", Range(-0.5, 0.5)) = 0.25
        [IntRange] _Corner ("Back Corner (0-3) - change until the text is on the far walls", Range(0, 3)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front          // draw the inside of the cube
            ZWrite Off
            ZTest LEqual        // buildings in front hide the text
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _FrontColor, _FrontColor2, _SideColor, _ShineColor;
            float _Reveal, _Fade, _Rise;
            float _Depth, _Emission, _Corner, _Shine, _Stripe, _VShift, _Shrink;

            #define LAYERS 32

            struct v2f
            {
                float4 pos  : SV_POSITION;
                float3 lp   : TEXCOORD0;
                float3 lcam : TEXCOORD1;
                float  vr   : TEXCOORD2;   // height / width ratio of the cube
            };

            float3 RotCorner(float3 p)
            {
                int k = (int)(_Corner + 0.5);
                [unroll] for (int i = 0; i < 3; i++)
                {
                    if (i < k) p = float3(p.z, p.y, -p.x);
                }
                return p;
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos  = UnityObjectToClipPos(v.vertex);
                o.lp   = v.vertex.xyz;
                o.lcam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;

                float sx = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float sy = length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21));
                float sz = length(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22));
                o.vr = sy / max(0.0001, 0.5 * (sx + sz));
                return o;
            }

            // Back corner at (x=+0.5, z=+0.5). Left -> right seen from inside:
            //   left wall = +Z wall (s 0..1), right wall = +X wall (s 1..2). They meet at s = 1.
            float2 SV(float3 q, bool isZ, float vr)
            {
                float s = isZ ? q.x + 0.5 : 1.5 - q.z;
                float c = isZ ? 0.5 : 1.5;                    // centre of this wall's half
                s = c + (s - c) * _Shrink;                    // shrink the text around its own centre
                float v = 0.5 + (q.y - _VShift) * vr * _Shrink;
                return float2(s, v);
            }

            // text mask with reveal animation (letters rise and fade in, staggered along s)
            float MaskAnim(float2 sv, bool isZ)
            {
                float r = saturate(_Reveal * 1.6 - sv.x * 0.3);
                r = r * r * (3.0 - 2.0 * r);
                sv.y += (1.0 - r) * _Rise * _Shrink;          // letters start lower and rise up
                // each wall may only show its own half of the strip (keeps the corner clean)
                if (isZ ? sv.x > 1.0 : sv.x < 1.0) return 0;
                if (sv.x < 0 || sv.x > 2 || sv.y < 0 || sv.y > 1) return 0;
                return tex2D(_MainTex, float2(sv.x * 0.5, sv.y)).a * r;
            }

            float3 LayerCol(int k, float f, float2 sv, float shade)
            {
                float3 c;
                if (k == LAYERS - 1)
                {
                    c = lerp(_FrontColor2.rgb, _FrontColor.rgb, saturate(sv.y));
                    float band = 0.5 + 0.5 * sin((sv.x * 2.2 - sv.y * 1.3) * 6.2831 - _Time.y * 1.5);
                    c += _ShineColor.rgb * pow(band, 12.0) * _Shine;
                }
                else
                {
                    float odd = frac(k * 0.5) * 2.0;
                    float stripe = 1.0 - _Stripe * odd;
                    c = _SideColor.rgb * lerp(0.35, 1.0, f) * stripe;
                }
                return c * shade;
            }

            void Put(inout float3 C, inout float A, float a, float3 col)
            {
                C = lerp(C, col, a);
                A = max(A, a);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(_Fade - 0.003);                          // fully faded: nothing to draw

                float3 p   = RotCorner(i.lp);
                float3 cam = RotCorner(i.lcam);
                float3 vd  = normalize(p - cam);

                float3 C = 0;
                float  A = 0;

                for (int k = 0; k < LAYERS; k++)
                {
                    float f = (float)k / (LAYERS - 1);
                    float t = _Depth * f;

                    float lz = (vd.z > 1e-4) ? (0.5 - t - cam.z) / vd.z : -1.0;
                    float lx = (vd.x > 1e-4) ? (0.5 - t - cam.x) / vd.x : -1.0;
                    float3 qz = cam + vd * lz;
                    float3 qx = cam + vd * lx;

                    float az = 0, ax = 0;
                    float2 svz = 0, svx = 0;
                    if (lz > 0 && abs(qz.x) <= 0.5) { svz = SV(qz, true,  i.vr); az = MaskAnim(svz, true);  }
                    if (lx > 0 && abs(qx.z) <= 0.5) { svx = SV(qx, false, i.vr); ax = MaskAnim(svx, false); }

                    float3 cz = LayerCol(k, f, svz, 1.00);
                    float3 cx = LayerCol(k, f, svx, 0.80);

                    if (lz > lx) { Put(C, A, az, cz); Put(C, A, ax, cx); }
                    else         { Put(C, A, ax, cx); Put(C, A, az, cz); }
                }

                A *= _Fade;
                clip(A - 0.002);
                return fixed4(C * _Emission, A);
            }
            ENDCG
        }
    }
}
