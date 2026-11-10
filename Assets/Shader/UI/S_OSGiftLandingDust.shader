Shader "UI/OS/GiftLandingDust"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0.55,0.57,0.58,0.68)
        _Progress ("Progress", Range(0,1)) = 0
        _RectSize ("Rect Size", Vector) = (72,24,0,0)
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
            #pragma target 2.0
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

            fixed4 _Color;
            float4 _ClipRect;
            float4 _RectSize;
            float _Progress;

            float Hash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float progress = saturate(_Progress);
                float2 pixel = floor(
                    (input.texcoord - 0.5) * _RectSize.xy) + 0.5;
                float dust = 0.0;

                [unroll]
                for (int i = 0; i < 10; i++)
                {
                    float index = (float)i;
                    float direction = i < 5 ? -1.0 : 1.0;
                    float lane = fmod(index, 5.0);
                    float randomA = Hash11(index + 2.3);
                    float randomB = Hash11(index + 19.7);
                    float distance = lerp(
                        4.0 + lane,
                        22.0 + randomA * 10.0,
                        progress);
                    float arc = sin(progress * UNITY_PI)
                        * (3.0 + randomB * 6.0);
                    float2 center = float2(
                        direction * distance,
                        -6.0 + arc - lane * 0.35);
                    center = floor(center) + 0.5;
                    float size = 1.0 + step(0.56, randomA);
                    float2 delta = abs(pixel - center);
                    float particle = step(
                        max(delta.x, delta.y),
                        size);
                    dust = max(dust, particle);
                }

                float lineSpread = floor(lerp(5.0, 31.0, progress));
                float lineWidth = lerp(8.0, 3.0, progress);
                float lineY = step(abs(pixel.y + 6.0), 0.75);
                float leftLine = step(
                    abs(pixel.x + lineSpread),
                    lineWidth);
                float rightLine = step(
                    abs(pixel.x - lineSpread),
                    lineWidth);
                dust = max(dust, lineY * max(leftLine, rightLine));

                float fade = 1.0 - smoothstep(0.48, 1.0, progress);
                fixed4 color = input.color;
                color.a *= dust * fade;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(
                    input.worldPosition.xy,
                    _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
