Shader "UI/OS/ChapterRecord"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _GlitchAmount ("Horizontal Glitch Amount", Range(0,1)) = 0
        _GlitchRate ("Glitch Rate", Range(1,20)) = 7
        _GlitchRows ("Glitch Rows", Range(4,64)) = 18
        _GlitchPixels ("Glitch Offset Pixels", Range(0,4)) = 1.25
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _DissolveEdgeWidth ("Dissolve Edge Width", Range(0,0.25)) = 0.08
        _DissolveEdgeColor ("Dissolve Edge Color", Color) = (0.741,0.122,0.106,1)
        _ColorReveal ("Color Reveal", Range(0,1)) = 1
        _RevealFromColor ("Reveal From Color", Color) = (0.557,0.522,0.518,1)
        _RevealBlockPixels ("Reveal Block Pixels", Range(1,8)) = 2
        _SpriteUvRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _AtlasTexelSize ("Atlas Texel Size", Vector) = (0.00390625,0.00390625,256,256)
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
            "CanUseSpriteAtlas"="True"
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

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            fixed4 _DissolveEdgeColor;
            fixed4 _RevealFromColor;
            float4 _ClipRect;
            float4 _SpriteUvRect;
            float4 _AtlasTexelSize;
            float _GlitchAmount;
            float _GlitchRate;
            float _GlitchRows;
            float _GlitchPixels;
            float _Dissolve;
            float _DissolveEdgeWidth;
            float _ColorReveal;
            float _RevealBlockPixels;

            float Hash21(float2 value)
            {
                value = frac(value * float2(0.1031, 0.1030));
                value += dot(value, value.yx + 33.33);
                return frac((value.x + value.y) * value.x);
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
                float2 rectSize = max(
                    _SpriteUvRect.zw,
                    float2(0.000001, 0.000001));
                float2 localUv = saturate(
                    (input.texcoord - _SpriteUvRect.xy) / rectSize);

                float rows = max(1.0, floor(_GlitchRows + 0.5));
                float row = min(floor(localUv.y * rows), rows - 1.0);
                float tick = floor(_Time.y * max(1.0, _GlitchRate));
                float rowNoise = Hash21(float2(row + 3.7, tick + 11.3));
                float activeRow = step(
                    1.0 - saturate(_GlitchAmount),
                    rowNoise);
                float shiftNoise = Hash21(float2(row * 2.17, tick * 1.31));
                float direction = lerp(-1.0, 1.0, step(0.5, shiftNoise));
                float shiftPixels = direction
                    * _GlitchPixels
                    * activeRow
                    * lerp(0.55, 1.0, shiftNoise);

                float2 sampleUv = input.texcoord;
                sampleUv.x += shiftPixels * _AtlasTexelSize.x;
                float horizontalInset = min(
                    _AtlasTexelSize.x * 0.5,
                    rectSize.x * 0.25);
                sampleUv.x = clamp(
                    sampleUv.x,
                    _SpriteUvRect.x + horizontalInset,
                    _SpriteUvRect.x + rectSize.x - horizontalInset);

                fixed4 color =
                    (tex2D(_MainTex, sampleUv) + _TextureSampleAdd)
                    * input.color;

                float2 spritePixels = max(
                    rectSize * _AtlasTexelSize.zw,
                    float2(1.0, 1.0));
                float revealBlockPixels = max(
                    1.0,
                    floor(_RevealBlockPixels + 0.5));
                float2 dissolveGrid = max(
                    float2(1.0, 1.0),
                    floor(spritePixels / revealBlockPixels));
                float2 dissolveCell = floor(
                    min(localUv, float2(0.999999, 0.999999))
                    * dissolveGrid);
                float dissolveNoise = Hash21(
                    dissolveCell + float2(19.7, 43.1));

                float colorReveal = saturate(_ColorReveal);
                float revealThreshold = lerp(-0.01, 1.01, colorReveal);
                float revealed = step(dissolveNoise, revealThreshold);
                color.rgb = lerp(
                    color.rgb * _RevealFromColor.rgb,
                    color.rgb,
                    revealed);

                float dissolve = saturate(_Dissolve);
                float threshold = lerp(-0.01, 1.01, dissolve);
                float visible = step(threshold, dissolveNoise);
                float edge = visible
                    * (1.0 - step(
                        threshold + max(0.001, _DissolveEdgeWidth),
                        dissolveNoise))
                    * step(0.001, dissolve);
                color.rgb = lerp(
                    color.rgb,
                    _DissolveEdgeColor.rgb,
                    edge * _DissolveEdgeColor.a);
                color.a *= visible;

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
