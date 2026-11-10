Shader "UI/HeartHealStateTransfer"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EffectColor ("Effect Color", Color) = (0.1569,0.5059,0.1569,1)
        _Progress ("Progress", Range(0,1)) = 0
        _ScatterProgress ("Scatter Progress", Range(0,1)) = 0
        _Seed ("Seed", Float) = 0
        _SquareScale ("Square Scale", Range(0.1,2)) = 1
        _BurstMode ("Burst Mode", Range(0,1)) = 0
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
            fixed4 _EffectColor;
            float _Progress;
            float _ScatterProgress;
            float _Seed;
            float _SquareScale;
            float _BurstMode;
            float4 _ClipRect;

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

            float Hash(float value)
            {
                return frac(sin(value * 91.3458 + _Seed * 17.17) * 47453.5453);
            }

            float HollowSquare(
                float2 uv,
                float2 center,
                float halfSize,
                float thickness)
            {
                float distanceToEdge = max(
                    abs(uv.x - center.x),
                    abs(uv.y - center.y));
                float antialias = max(fwidth(distanceToEdge), 0.0015);
                float outer = 1.0 - smoothstep(
                    halfSize,
                    halfSize + antialias,
                    distanceToEdge);
                float innerSize = max(0.0, halfSize - thickness);
                float inner = 1.0 - smoothstep(
                    innerSize,
                    innerSize + antialias,
                    distanceToEdge);
                return saturate(outer - inner);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float progress = saturate(_Progress);
                float scatterProgress = saturate(_ScatterProgress);
                float scatter = 1.0 - pow(1.0 - scatterProgress, 3.0);
                float converge = progress;
                float burstProgress = 1.0 - pow(1.0 - progress, 3.0);
                float burstMode = step(0.5, _BurstMode);
                float squareScale = max(0.01, _SquareScale);
                float animationProgress = scatterProgress * 0.32
                    + progress * 0.68;
                float pulse = 0.96
                    + sin(animationProgress * 15.0 + _Seed) * 0.04;
                float squares = 0.0;

                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    float index = (float)i;
                    float angle = Hash(index + 1.0) * 6.2831853;
                    float radius = lerp(0.12, 0.29, Hash(index + 8.0));
                    float2 scattered = float2(0.5, 0.5)
                        + float2(cos(angle), sin(angle)) * radius;
                    float2 destination = float2(0.5, 0.5)
                        + float2(
                            Hash(index + 18.0) - 0.5,
                            Hash(index + 27.0) - 0.5) * 0.045;
                    float2 burstOrigin = float2(0.5, 0.5)
                        + float2(
                            Hash(index + 48.0) - 0.5,
                            Hash(index + 57.0) - 0.5) * 0.025;
                    float2 transferOrigin = float2(0.5, 0.5)
                        + float2(
                            Hash(index + 68.0) - 0.5,
                            Hash(index + 77.0) - 0.5) * 0.02;
                    float2 expandedCenter = lerp(
                        transferOrigin,
                        scattered,
                        scatter);
                    float2 transferCenter = lerp(
                        expandedCenter,
                        destination,
                        converge);
                    float2 burstCenter = lerp(
                        burstOrigin,
                        scattered,
                        burstProgress);
                    float2 center = lerp(
                        transferCenter,
                        burstCenter,
                        burstMode);
                    float size = lerp(0.0175, 0.0325, Hash(index + 38.0))
                        * pulse
                        * squareScale;
                    squares = max(
                        squares,
                        HollowSquare(
                            input.texcoord,
                            center,
                            size,
                            0.006 * squareScale));
                }

                fixed4 color = _EffectColor * input.color;
                color.a *= squares;

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
