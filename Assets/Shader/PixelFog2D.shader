Shader "MrTGame/Visual/PixelFog2D"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FogColor ("Fog Color", Color) = (0.32, 0.36, 0.42, 1)
        _Density ("Density", Range(0, 1)) = 0.2
        _NoiseScale ("Noise Scale", Range(0.01, 2)) = 0.3
        _HorizontalStretch ("Horizontal Stretch", Range(0.25, 12)) = 4
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.25
        _Coverage ("Coverage", Range(0, 1)) = 0.48
        _EdgeWidth ("Edge Width", Range(0.01, 1)) = 0.2
        _BaseFill ("Base Fill", Range(0, 1)) = 0
        _BandCenter ("Band Center", Range(-0.5, 1.5)) = 0.5
        _BandWidth ("Band Width", Range(0.01, 2)) = 0.8
        _Speed ("Speed", Vector) = (0.1, 0, 0, 0)
        _Seed ("Seed", Float) = 0
        _Parallax ("Parallax", Range(0, 1)) = 0.2
        _PixelWorldSize ("Pixel World Size", Float) = 0.05
        _FrameRate ("Animation FPS", Range(1, 30)) = 8
        _AlphaSteps ("Alpha Steps", Range(1, 64)) = 16
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "PixelFog2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float4 _Speed;
                float _Density;
                float _NoiseScale;
                float _HorizontalStretch;
                float _DetailStrength;
                float _Coverage;
                float _EdgeWidth;
                float _BaseFill;
                float _BandCenter;
                float _BandWidth;
                float _Seed;
                float _Parallax;
                float _PixelWorldSize;
                float _FrameRate;
                float _AlphaSteps;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float ValueNoise(float2 value)
            {
                float2 cell = floor(value);
                float2 local = frac(value);
                local = local * local * (3.0 - 2.0 * local);

                float bottom = lerp(Hash21(cell), Hash21(cell + float2(1, 0)), local.x);
                float top = lerp(Hash21(cell + float2(0, 1)), Hash21(cell + 1.0), local.x);
                return lerp(bottom, top, local.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                float pixelSize = max(_PixelWorldSize, 0.0001);
                float2 pixelWorld = (floor(input.positionWS.xy / pixelSize) + 0.5) * pixelSize;
                pixelWorld.x *= _Parallax;

                float frameRate = max(_FrameRate, 1.0);
                float steppedTime = floor(_Time.y * frameRate) / frameRate;
                float2 motion = _Speed.xy * steppedTime;

                float2 fogPosition = float2(
                    pixelWorld.x / max(_HorizontalStretch, 0.01),
                    pixelWorld.y
                ) * _NoiseScale + motion + _Seed;

                float coarse = ValueNoise(fogPosition);
                float detail = ValueNoise(fogPosition * 2.07 + float2(19.31, -7.17));
                float noise = lerp(coarse, detail, _DetailStrength);

                float edgeWidth = max(_EdgeWidth, 0.001);
                float breakup = smoothstep(
                    _Coverage - edgeWidth,
                    _Coverage + edgeWidth,
                    noise
                );

                float flowA = ValueNoise(float2(fogPosition.x * 0.73 + _Seed, _Seed + 3.17));
                float flowB = ValueNoise(float2(fogPosition.x * 1.21 - _Seed, _Seed + 11.83));
                float centerA = _BandCenter + (flowA - 0.5) * _BandWidth * 0.32;
                float centerB = _BandCenter + _BandWidth * 0.18
                    + (flowB - 0.5) * _BandWidth * 0.24;

                float ridgeWidth = max(_BandWidth * 0.07, 0.003);
                float ridgeA = 1.0 - smoothstep(
                    ridgeWidth,
                    ridgeWidth * 3.0,
                    abs(input.uv.y - centerA)
                );
                float ridgeB = 1.0 - smoothstep(
                    ridgeWidth * 0.8,
                    ridgeWidth * 2.4,
                    abs(input.uv.y - centerB)
                );

                float halfBand = max(_BandWidth * 0.5, 0.001);
                float broadBand = 1.0 - saturate(abs(input.uv.y - _BandCenter) / halfBand);
                broadBand = broadBand * broadBand * (3.0 - 2.0 * broadBand);

                float wisps = saturate(ridgeA * 0.8 + ridgeB * 0.55) * breakup;
                float fogShape = saturate(wisps + broadBand * _BaseFill);
                float alpha = fogShape * _Density;

                float alphaSteps = max(_AlphaSteps, 1.0);
                alpha = floor(alpha * alphaSteps + 0.5) / alphaSteps;
                alpha *= sprite.a * input.color.a * _FogColor.a;

                return half4(_FogColor.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
