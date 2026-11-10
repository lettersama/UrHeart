Shader "UI/OS/GiftLikeBurst"
{
    // Pixel-grid like burst. Every layer is hard-edged on the UI's logical
    // pixels; fades are Bayer-dither dissolves, never soft alpha.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _CoreColor ("Core", Color) = (0.925,0.918,0.902,1)
        _AccentColor ("Accent", Color) = (0.451,0.949,1,1)
        _DimColor ("Ripple Dots", Color) = (0.275,0.471,0.502,1)
        _RestColor ("Rest Dots", Color) = (0.204,0.196,0.188,1)
        _BurstTime ("Burst Time (s)", Float) = 0
        _RectSize ("Rect Size", Vector) = (176,104,0,0)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _CoreColor;
            fixed4 _AccentColor;
            fixed4 _DimColor;
            fixed4 _RestColor;
            float4 _ClipRect;
            float4 _RectSize;
            float _BurstTime;

            #define SPARKS 14
            #define TAU 6.2831853

            float Hash11(float value)
            {
                return frac(sin(value * 91.3458) * 47453.5453);
            }

            // 4x4 ordered dither threshold in [0, 1).
            float Bayer2(float2 a)
            {
                a = floor(a);
                return frac(dot(a, float2(0.5, a.y * 0.75)));
            }

            float Bayer4(float2 a)
            {
                return Bayer2(0.5 * a) * 0.25 + Bayer2(a);
            }

            float EaseOutCubic(float x)
            {
                x = 1.0 - saturate(x);
                return 1.0 - x * x * x;
            }

            float EaseOutQuad(float x)
            {
                x = 1.0 - saturate(x);
                return 1.0 - x * x;
            }

            float EaseInQuad(float x)
            {
                x = saturate(x);
                return x * x;
            }

            // Coverage in [0,1] -> on/off for this pixel.
            float Dither(float coverage, float threshold)
            {
                return step(threshold + 0.001, coverage);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // Pixel centres sit on half-integers around the rect centre.
                float2 cell = floor((input.texcoord - 0.5) * _RectSize.xy);
                float2 p = cell + 0.5;
                float d = length(p);
                float t = max(0.0, _BurstTime);
                float bayer = Bayer4(cell + 64.0);
                float halfWidth = _RectSize.x * 0.5;

                float3 color = 0;
                float alpha = 0;

                // Rest dots: a faint 4px screen grid dissolves in and stays.
                float onGrid = step(abs(fmod(cell.x + 1024.0, 4.0) - 1.0), 0.1)
                    * step(abs(fmod(cell.y + 1024.0, 4.0) - 1.0), 0.1);
                float restCover = saturate((t - 0.25) / 0.6);
                float rest = onGrid * Dither(restCover, bayer);
                color = lerp(color, _RestColor.rgb, rest);
                alpha = max(alpha, rest);

                // Ripple: the grid lights up in a band trailing the cyan ring.
                float ringA = 14.0 + 52.0 * EaseOutCubic(t / 0.7);
                float band = 1.0 - saturate(abs(d - (ringA - 5.0)) / 8.0);
                float rippleCover = band * (1.0 - saturate((t - 0.3) / 0.5));
                float ripple = onGrid * Dither(rippleCover, bayer);
                color = lerp(color, _DimColor.rgb, ripple);
                alpha = max(alpha, ripple);

                // Flash line: opens fast across the rect, then thins and dissolves
                // from its ends inward.
                float open = EaseOutQuad(t / 0.08);
                float reach = floor(lerp(6.0, halfWidth, open));
                float inReach = step(abs(p.x), reach);
                float ends = abs(p.x) / halfWidth;
                float lineLife = 1.0 - saturate((t - 0.1) / 0.35);
                float lineCover = saturate(lineLife * 1.5 - ends * 0.5);
                float rowCore = step(abs(p.y), 0.6);
                float rowGlow = step(abs(abs(p.y) - 1.5), 0.1) * step(t, 0.16);
                float rowHalo = step(abs(abs(p.y) - 2.5), 0.1) * step(t, 0.1)
                    * Dither(0.5, bayer);
                float lineOn = inReach * step(0.0001, t)
                    * Dither(lineCover, bayer);
                float coreWhite = step(t, 0.18);
                float lineCore = lineOn * rowCore;
                float lineAccent = lineOn * max(rowGlow, rowHalo);
                color = lerp(color, _AccentColor.rgb, lineAccent);
                alpha = max(alpha, lineAccent);
                color = lerp(color,
                    lerp(_AccentColor.rgb, _CoreColor.rgb, coreWhite),
                    lineCore);
                alpha = max(alpha, lineCore);

                // Ring A: cyan, 2px at the hit, then 1px, dissolving out.
                float ringWidth = t < 0.12 ? 1.2 : 0.7;
                float ringCover = 1.0 - saturate((t / 0.7 - 0.45) / 0.55);
                float ringOn = step(abs(d - ringA), ringWidth)
                    * step(0.0001, t) * Dither(ringCover, bayer);
                color = lerp(color, _AccentColor.rgb, ringOn);
                alpha = max(alpha, ringOn);

                // Ring B: white dashes, a beat behind and tighter.
                float tB = (t - 0.05) / 0.45;
                float ringB = 10.0 + 30.0 * EaseOutQuad(tB);
                float angle = atan2(p.y, p.x);
                float dash = step(fmod(floor((angle + UNITY_PI) / TAU * 28.0), 2.0), 0.5);
                float ringBCover = 1.0 - saturate((tB - 0.45) / 0.55);
                float ringBOn = step(abs(d - ringB), 0.7) * dash
                    * step(0.0, tB) * step(tB, 1.0) * Dither(ringBCover, bayer);
                color = lerp(color, _CoreColor.rgb, ringBOn);
                alpha = max(alpha, ringBOn);

                // Rays: vertical pair and four diagonals shoot out and shrink.
                float tR = saturate(t / 0.32);
                float rayOuter = lerp(16.0, 48.0, EaseOutCubic(tR));
                float rayInner = lerp(15.0, 48.0, EaseInQuad(tR));
                float vertical = step(abs(p.x), 1.0)
                    * step(rayInner, abs(p.y)) * step(abs(p.y), rayOuter);
                float diagonal = step(abs(abs(p.x) - abs(p.y)), 0.1)
                    * step(rayInner, d) * step(d, rayOuter);
                float rayOn = max(vertical, diagonal)
                    * step(0.0001, t) * step(t, 0.32);
                color = lerp(color, _CoreColor.rgb, rayOn);
                alpha = max(alpha, rayOn);

                // Sparks: axis-aligned crosses, blocks and single pixels fly out,
                // drift down a little and blink before they go.
                [unroll]
                for (int i = 0; i < SPARKS; i++)
                {
                    float index = (float)i;
                    float randomA = Hash11(index + 3.1);
                    float randomB = Hash11(index + 17.7);
                    float sparkAngle = (index + 0.5) / SPARKS * TAU
                        + (randomA - 0.5) * 0.45;
                    float life = 0.75 + randomB * 0.3;
                    float sparkT = (t - 0.03) / life;
                    float radius = lerp(12.0, 36.0 + randomA * 30.0,
                        EaseOutCubic(sparkT));
                    float2 centre = float2(cos(sparkAngle), sin(sparkAngle))
                        * radius;
                    centre.y -= 14.0 * sparkT * sparkT;
                    float2 q = p - (floor(centre) + 0.5);
                    float shape = fmod(index, 3.0);
                    float plusShape = max(
                        step(abs(q.x), 0.1) * step(abs(q.y), 1.1),
                        step(abs(q.y), 0.1) * step(abs(q.x), 1.1));
                    float block = step(-0.1, q.x) * step(q.x, 1.1)
                        * step(-0.1, q.y) * step(q.y, 1.1);
                    float dot1 = step(abs(q.x), 0.1) * step(abs(q.y), 0.1);
                    float hit = shape < 0.5 ? plusShape : (shape < 1.5 ? block : dot1);
                    float blink = sparkT < 0.55
                        ? 1.0
                        : step(fmod(floor(t * 22.0 + index), 2.0), 0.5);
                    float alive = step(0.0, sparkT) * step(sparkT, 1.0);
                    float sparkOn = hit * blink * alive;
                    float3 sparkColor = fmod(index, 2.0) < 0.5
                        ? _AccentColor.rgb : _CoreColor.rgb;
                    color = lerp(color, sparkColor, sparkOn);
                    alpha = max(alpha, sparkOn);
                }

                fixed4 result = fixed4(color, alpha * input.color.a);

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif

                return result;
            }
            ENDCG
        }
    }
}
