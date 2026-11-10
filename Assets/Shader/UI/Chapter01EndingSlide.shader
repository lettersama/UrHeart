Shader "UI/OS/Chapter01EndingSlide"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _UvRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _Phase ("Externally Driven Phase", Float) = 0
        _Exposure ("Exposure", Range(0,32)) = 1
        _LightGain ("Local Light Gain", Range(0,0.5)) = 0.04
        _LightCenter ("Local Light Center", Vector) = (0.5,0.5,0,0)
        _LightRadius ("Local Light Radius", Range(0.001,1)) = 0.22
        _EdgeShade ("Edge Shade", Range(0,1)) = 0.08
        _FocusCenter ("Focus Center", Vector) = (0.5,0.5,0,0)
        _FocusRadius ("Focus Radius", Range(0.05,1.5)) = 0.6
        _FocusStrength ("Focus Darkness", Range(0,1)) = 0.5
        _Shutter ("Closing Shutter", Range(0,1)) = 0
        _ShutterTilt ("Shutter Tilt", Range(-0.5,0.5)) = 0.12
        _OsdTex ("Camera Overlay (source pixels)", 2D) = "black" {}
        _OsdOn ("Camera Overlay On", Float) = 0
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
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

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

            sampler2D _MainTex, _OsdTex;
            fixed4 _Color, _TextureSampleAdd;
            float4 _MainTex_TexelSize, _UvRect, _LightCenter, _FocusCenter, _ClipRect;
            float _Phase, _Exposure, _LightGain, _LightRadius, _EdgeShade;
            float _FocusRadius, _FocusStrength, _Shutter, _ShutterTilt, _OsdOn;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // Effects use sprite-local coordinates; texture sampling never moves.
                float2 size = max(_UvRect.zw, float2(0.000001, 0.000001));
                float2 localUV = saturate((input.texcoord - _UvRect.xy) / size);
                float2 pixelCount = max(size * _MainTex_TexelSize.zw, 1.0);
                // All treatment edges land on source pixels; the drawing never moves.
                float2 pixelUV = (floor(localUV * pixelCount) + 0.5) / pixelCount;
                float light = 1.0 - smoothstep(0.0, max(0.001, _LightRadius), distance(pixelUV, _LightCenter.xy));
                float2 edge = (pixelUV - 0.5) * 2.0;
                float shade = smoothstep(0.3, 1.4, dot(edge, edge));
                float2 focalDistance = (pixelUV - _FocusCenter.xy) * float2(1.0, 0.75);
                float focusShade = smoothstep(_FocusRadius * 0.2, max(0.001, _FocusRadius), length(focalDistance));
                float opening = lerp(0.8, -0.01, saturate(_Shutter));
                float shutterDistance = abs(pixelUV.y - 0.5 + (pixelUV.x - 0.5) * _ShutterTilt);
                float shutter = 1.0 - smoothstep(opening, opening + 1.0 / pixelCount.y, shutterDistance);

                fixed4 color = (tex2D(_MainTex, input.texcoord) + _TextureSampleAdd) * input.color;
                // Multiplication preserves the original black linework.
                color.rgb *= max(0.0, _Exposure) * (1.0 + saturate(_LightGain) * light)
                    * (1.0 - saturate(_EdgeShade) * shade)
                    * (1.0 - saturate(_FocusStrength) * focusShade) * shutter;
                // The camera's on-screen display sits above the picture's light and shutter.
                fixed4 osd = tex2D(_OsdTex, pixelUV);
                color.rgb = lerp(color.rgb, osd.rgb, osd.a * saturate(_OsdOn));
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
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
