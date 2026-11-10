Shader "UI/OS/LockHeartPersistentGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _UVRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _SpriteSize ("Sprite Size", Vector) = (21,17,0,0)
        _GlitchRate ("Glitch Rate", Range(0.25,8)) = 2.2
        _BandCount ("Band Count", Range(2,12)) = 6
        _MaxOffsetPixels ("Max Offset Pixels", Range(1,10)) = 6
        _ColorSplitPixels ("Color Split Pixels", Range(0,3)) = 1
        _Seed ("Seed", Range(0,1)) = 0
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
            float4 _ClipRect;
            float4 _UVRect;
            float4 _SpriteSize;
            float _GlitchRate;
            float _BandCount;
            float _MaxOffsetPixels;
            float _ColorSplitPixels;
            float _Seed;

            float Hash11(float value)
            {
                return frac(sin(value * 91.3458) * 47453.5453);
            }

            float2 ToAtlasUV(float2 localUV)
            {
                return _UVRect.xy + saturate(localUV) * _UVRect.zw;
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
                float2 uvSize = max(_UVRect.zw, float2(0.00001, 0.00001));
                float2 localUV = (input.texcoord - _UVRect.xy) / uvSize;
                float tick = floor(_Time.y * max(0.25, _GlitchRate));
                float band = floor(saturate(localUV.y) * max(2.0, _BandCount));
                float bandRandom = Hash11(
                    band * 17.31 + tick * 11.73 + _Seed * 101.0);
                float offsetSteps = floor(lerp(
                    -_MaxOffsetPixels,
                    _MaxOffsetPixels + 0.999,
                    bandRandom));
                float2 shiftedLocalUV = localUV;
                shiftedLocalUV.x = frac(
                    shiftedLocalUV.x
                    + offsetSteps / max(1.0, _SpriteSize.x));

                float splitDirection = Hash11(
                    band * 7.13 + tick * 19.9 + _Seed * 43.0) < 0.5
                    ? -1.0
                    : 1.0;
                float splitUV = _ColorSplitPixels
                    / max(1.0, _SpriteSize.x);
                float2 redLocalUV = shiftedLocalUV;
                float2 blueLocalUV = shiftedLocalUV;
                redLocalUV.x = frac(redLocalUV.x + splitUV * splitDirection);
                blueLocalUV.x = frac(blueLocalUV.x - splitUV * splitDirection);

                fixed4 centerSample = tex2D(
                    _MainTex,
                    ToAtlasUV(shiftedLocalUV)) + _TextureSampleAdd;
                fixed redSample = tex2D(
                    _MainTex,
                    ToAtlasUV(redLocalUV)).r;
                fixed blueSample = tex2D(
                    _MainTex,
                    ToAtlasUV(blueLocalUV)).b;
                centerSample.r = redSample;
                centerSample.b = blueSample;

                float block = floor(
                    saturate(localUV.x) * max(3.0, _SpriteSize.x / 3.0));
                float blockRandom = Hash11(
                    block * 5.71
                    + band * 29.17
                    + tick * 13.37
                    + _Seed * 67.0);
                centerSample.a *= lerp(0.42, 1.0, step(0.2, blockRandom));
                centerSample.rgb *= lerp(0.58, 1.18, blockRandom);

                fixed4 color = centerSample * input.color;
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
