Shader "UI/OS/Chapter01EndingFreeze"
{
    Properties
    {
        [PerRendererData] _MainTex ("Screenshot", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Amount ("Freeze Distortion", Range(0,1)) = 0
        _Phase ("Externally Driven Phase", Float) = 0
        _ScreenshotGamma ("Screenshot Needs Gamma Decode", Float) = 0
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
            "CanUseSpriteAtlas"="False"
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

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _Amount;
            float _Phase;
            float _ScreenshotGamma;

            float Hash11(float value)
            {
                return frac(sin(value * 91.3458 + 17.13) * 47453.5453);
            }

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
                float amount = saturate(_Amount);
                fixed4 sampleColor = tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                if (amount > 0.0)
                {
                    // Only the caller advances phase. Holding it freezes every band.
                    float tick = floor(max(0.0, _Phase) * 6.0);
                    float band = floor(input.texcoord.y * 24.0);
                    float random = Hash11(band * 17.3 + tick * 11.7);
                    float selected = step(0.62, random);
                    float offset = floor((Hash11(band * 23.9 + tick * 3.1) - 0.5) * 9.0);
                    float2 shiftedUV = input.texcoord;
                    shiftedUV.x = saturate(shiftedUV.x + offset * selected * amount / 640.0);

                    sampleColor = tex2D(_MainTex, shiftedUV) + _TextureSampleAdd;
                    float split = selected * amount * 1.25 / 640.0;
                    sampleColor.r = tex2D(_MainTex, saturate(shiftedUV + float2(split, 0))).r
                        + _TextureSampleAdd.r;
                    sampleColor.b = tex2D(_MainTex, saturate(shiftedUV - float2(split, 0))).b
                        + _TextureSampleAdd.b;
                    float luminance = dot(sampleColor.rgb, float3(0.299, 0.587, 0.114));
                    sampleColor.rgb = lerp(sampleColor.rgb, luminance.xxx,
                        amount * (0.48 + selected * 0.32));

                    // The treatment only darkens: no full-frame flashes or white noise.
                    float thinRow = step(0.83, frac(input.texcoord.y * 90.0));
                    float darken = amount * (0.045 + selected * 0.16 + thinRow * 0.045);
                    sampleColor.rgb *= 1.0 - darken;
                }

                // CaptureScreenshotAsTexture can return display-encoded pixels
                // in a linear texture. Decode once before Unity's output encode.
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_ScreenshotGamma > 0.5)
                    sampleColor.rgb = GammaToLinearSpace(sampleColor.rgb);
                #endif
                fixed4 color = sampleColor * input.color;
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
