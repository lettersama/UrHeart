Shader "UI/OS/Chapter01EndingFilmGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _UvRect ("Sprite UV Rect", Vector) = (0,0,1,1)
        _Amount ("Glitch Amount", Range(0,1)) = 0
        _Tick ("Externally Driven Tick", Float) = 0
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

            sampler2D _MainTex;
            fixed4 _Color, _TextureSampleAdd;
            float4 _MainTex_TexelSize, _UvRect, _ClipRect;
            float _Amount, _Tick;

            float Hash(float value)
            {
                return frac(sin(value) * 43758.5453);
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
                // Work in source pixels of this atlas frame, so every displaced
                // block lands on the 640x360 grid and only authored colors appear.
                float2 size = max(_UvRect.zw, float2(0.000001, 0.000001));
                float2 art = max(floor(size * _MainTex_TexelSize.zw + 0.5), 1.0);
                float2 pixel = min(floor(saturate((input.texcoord - _UvRect.xy) / size) * art), art - 1.0);
                float2 source = pixel;
                float amount = saturate(_Amount);
                float tick = _Tick;
                float dropout = 0.0;

                // Small tears flicker from the first frame; by the end every tick tears.
                if (amount > 0.0 && Hash(tick * 0.731 + 3.1) < lerp(0.35, 1.0, smoothstep(0.1, 0.65, amount)))
                {
                    // 16-row cells split into 16/8/4/2-row bands, chosen per tick.
                    float cell = floor(pixel.y / 16.0);
                    float bandHeight = 16.0 / exp2(floor(Hash(cell * 1.37 + tick * 0.173) * 4.0));
                    float bandTop = cell * 16.0 + floor((pixel.y - cell * 16.0) / bandHeight) * bandHeight;
                    if (Hash(bandTop * 1.9898 + tick * 7.233) < lerp(0.035, 0.5, amount))
                    {
                        float shiftLimit = lerp(2.0, 84.0, amount * amount);
                        float shift = round((Hash(bandTop * 4.71 + tick * 1.31) - 0.5) * 2.0 * shiftLimit);
                        float blockWidth = 8.0 * exp2(floor(Hash(bandTop * 9.13 + tick * 2.71) * 4.0));
                        float block = floor(pixel.x / blockWidth);
                        source.x = pixel.x - shift;
                        if (Hash(bandTop * 3.17 + block * 7.31 + tick * 5.11) < smoothstep(0.25, 1.0, amount) * 0.6)
                        {
                            float mode = Hash(bandTop * 1.13 + block * 2.37 + tick * 0.97);
                            if (mode < 0.45)
                                source.x = block * blockWidth - shift;  // smear one column across the block
                            else if (mode < 0.8)
                                source.x += round((Hash(bandTop * 5.3 + block * 1.7 + tick * 3.3) - 0.5) * 2.0 * shiftLimit);
                            else if (amount > 0.5)
                                source.y = bandTop;                     // one row repeated: a stuck scanline
                            dropout = step(0.96, mode) * step(0.7, amount);
                        }
                    }
                }

                source.x -= art.x * floor(source.x / art.x);
                float2 uv = _UvRect.xy + (source + 0.5) / art * size;
                fixed4 color = (tex2D(_MainTex, uv) + _TextureSampleAdd) * input.color;
                color.rgb *= 1.0 - dropout;
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
