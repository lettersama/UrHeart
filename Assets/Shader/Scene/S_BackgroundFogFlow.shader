Shader "MrTGame/Scene/BackgroundFogFlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BackgroundColor ("Background Color", Color) = (0.025, 0.035, 0.055, 1)
        _FogColor ("Fog Color", Color) = (0.20, 0.27, 0.34, 1)
        _FogSize ("Fog Size", Range(0.1, 20)) = 4
        _Speed ("Fog Movement (Positive X = Left)", Vector) = (0.025, 0.008, 0, 0)
        _UVOffset ("UV Offset", Vector) = (0, 0, 0, 0)
        _ParallaxStrength ("Depth-Matched Parallax", Range(0, 2)) = 1
        _Distortion ("Distortion", Range(0, 2)) = 0.45
        _FogAmount ("Fog Amount", Range(0, 1)) = 0.8
        _Softness ("Softness", Range(0.01, 0.5)) = 0.18
        _TopCutoff ("Top Cutoff", Range(-0.25, 1.25)) = 0.54
        _TopFeather ("Top Feather", Range(0.001, 0.3)) = 0.04
        _WaveLength ("Edge Wave Length", Range(0.05, 2)) = 0.45
        _WaveAmplitude ("Edge Wave Height", Range(0, 0.3)) = 0.055
        _WaveSpeed ("Edge Move Left Speed", Range(-0.5, 0.5)) = 0.04
        _EdgeNoise ("Edge Noise", Range(0, 0.25)) = 0.035
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            Name "BackgroundFogFlow"
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
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 screenPosition : TEXCOORD1;
                float2 parallaxOffset : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BackgroundColor;
                float4 _FogColor;
                float4 _Speed;
                float4 _UVOffset;
                float _FogSize;
                float _ParallaxStrength;
                float _Distortion;
                float _FogAmount;
                float _Softness;
                float _TopCutoff;
                float _TopFeather;
                float _WaveLength;
                float _WaveAmplitude;
                float _WaveSpeed;
                float _EdgeNoise;
                float _Opacity;
                float _Seed;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.screenPosition = ComputeScreenPos(output.positionCS);

                float3 objectOriginWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float4 objectOriginCS = TransformWorldToHClip(objectOriginWS);
                float2 objectOriginUV =
                    objectOriginCS.xy / max(abs(objectOriginCS.w), 0.0001) * 0.5 + 0.5;
                output.parallaxOffset = float2(
                    (0.5 - objectOriginUV.x) * _ParallaxStrength,
                    0.0
                );
                return output;
            }

            float Hash21(float2 value)
            {
                value = float2(
                    dot(value, float2(127.1, 311.7)),
                    dot(value, float2(269.5, 183.3))
                );
                return frac(sin(value.x + value.y) * 43758.5453);
            }

            float ValueNoise(float2 value)
            {
                float2 cell = floor(value);
                float2 local = frac(value);
                local = local * local * (3.0 - 2.0 * local);

                float bottom = lerp(
                    Hash21(cell),
                    Hash21(cell + float2(1.0, 0.0)),
                    local.x
                );
                float top = lerp(
                    Hash21(cell + float2(0.0, 1.0)),
                    Hash21(cell + float2(1.0, 1.0)),
                    local.x
                );
                return lerp(bottom, top, local.y);
            }

            float FractalNoise(float2 value)
            {
                float result = ValueNoise(value) * 0.533333;
                value = value * 2.03 + float2(17.13, 9.21);
                result += ValueNoise(value) * 0.266667;
                value = value * 2.01 + float2(-6.73, 13.87);
                result += ValueNoise(value) * 0.133333;
                value = value * 2.04 + float2(11.29, -4.61);
                result += ValueNoise(value) * 0.066667;
                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                float2 screenUV =
                    input.screenPosition.xy / max(input.screenPosition.w, 0.0001);
                float2 anchoredUV =
                    screenUV + input.parallaxOffset + _UVOffset.xy;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float bottomUpY = input.positionCS.y / max(_ScreenParams.y, 1.0);
                bottomUpY += _UVOffset.y;

                // Screen-space density keeps the fog scale stable when the Sprite is scaled.
                float2 movement = _Speed.xy * _Time.y;
                float seed = _Seed * 17.317;
                float2 basePosition =
                    float2(anchoredUV.x * aspect, anchoredUV.y)
                    * (24.0 / max(_FogSize, 0.001));

                float warpX = FractalNoise(
                    basePosition * 0.58 + movement * 0.65 + seed
                );
                float warpY = FractalNoise(
                    basePosition * 0.58
                    - movement * 0.45
                    + float2(7.13, -3.71)
                    - seed
                );

                float2 displacement = (float2(warpX, warpY) - 0.5) * 2.0;
                float2 fogPosition = basePosition
                    + displacement * _Distortion
                    + movement;

                float fog = FractalNoise(fogPosition);
                float softness = max(_Softness, 0.001);
                float fogShape = smoothstep(
                    0.5 - softness,
                    0.5 + softness,
                    fog
                );
                float fogBlend = saturate(fogShape * _FogAmount);

                // Positive wave speed advances phase toward +X, so the visible crest moves left.
                float waveLength = max(_WaveLength, 0.001);
                float waveTravel = _Time.y * _WaveSpeed;
                float wavePhase =
                    (anchoredUV.x + waveTravel + _Seed * 0.071) / waveLength;
                float edgeWave =
                    sin(wavePhase * TWO_PI) * 0.72
                    + sin((wavePhase * 2.17 + 0.37) * TWO_PI) * 0.28;
                float edgeNoise = FractalNoise(float2(
                    (anchoredUV.x + waveTravel * 0.65) * aspect * 2.3,
                    _Seed * 3.71 + 5.19
                ));
                edgeNoise = (edgeNoise - 0.5) * 2.0;

                float topEdge =
                    _TopCutoff
                    + edgeWave * _WaveAmplitude
                    + edgeNoise * _EdgeNoise;
                float edgeMask = 1.0 - smoothstep(
                    topEdge - max(_TopFeather, 0.001),
                    topEdge + max(_TopFeather, 0.001),
                    bottomUpY
                );

                half4 color = lerp(_BackgroundColor, _FogColor, fogBlend);
                color.rgb *= input.color.rgb;
                color.a *= sprite.a * input.color.a * _Opacity * edgeMask;
                clip(color.a - 0.001);
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
