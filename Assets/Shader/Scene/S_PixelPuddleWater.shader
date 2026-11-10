Shader "MrTGame/Scene/PixelPuddleWater"
{
    Properties
    {
        [PerRendererData] _MainTex ("Puddle Mask", 2D) = "white" {}
        [HDR] _DeepColor ("Deep Color", Color) = (0.025, 0.105, 0.14, 0.72)
        [HDR] _ShallowColor ("Shallow Color", Color) = (0.07, 0.24, 0.28, 0.48)
        [HDR] _HighlightColor ("Moving Highlight Color", Color) = (0.30, 0.72, 0.76, 1)
        [HDR] _ShoreColor ("Shore Highlight Color", Color) = (0.18, 0.48, 0.50, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.82
        _WaveSize ("Wave Size (World Units)", Range(0.1, 5)) = 0.85
        _FlowSpeed ("Flow Speed XZ", Vector) = (0.12, 0.035, 0, 0)
        _WorldOffset ("World Pattern Offset", Vector) = (0, 0, 0, 0)
        _WaveDistortion ("Wave Distortion", Range(0, 2)) = 0.55
        _WaveContrast ("Wave Contrast", Range(0, 1)) = 0.32
        _HighlightAmount ("Moving Highlight Amount", Range(0, 2)) = 0.7
        _HighlightWidth ("Moving Highlight Width", Range(0.01, 0.45)) = 0.12
        _PixelSize ("Effect Pixel Size (World Units)", Range(0.01, 0.5)) = 0.075
        _ShoreWidthPixels ("Shore Width (Screen Pixels)", Range(0.5, 6)) = 1.5
        _ShoreStrength ("Shore Highlight Strength", Range(0, 2)) = 0.9
        _MaskCutoff ("Mask Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
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
            Name "PixelPuddleWater"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/SurfaceData2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/InputData2D.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 worldXZ : TEXCOORD1;
                half2 lightingUV : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _HighlightColor;
                half4 _ShoreColor;
                float4 _FlowSpeed;
                float4 _WorldOffset;
                half _Opacity;
                float _WaveSize;
                float _WaveDistortion;
                half _WaveContrast;
                half _HighlightAmount;
                half _HighlightWidth;
                float _PixelSize;
                float _ShoreWidthPixels;
                half _ShoreStrength;
                half _MaskCutoff;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.uv = input.uv;
                output.worldXZ = positionWS.xz;
                output.lightingUV = half2(
                    ComputeScreenPos(output.positionCS / output.positionCS.w).xy
                );
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

            half SampleMask(float2 uv)
            {
                return step(
                    _MaskCutoff,
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a
                );
            }

            half InnerShoreMask(float2 uv, half centerMask)
            {
                float2 sampleStep = max(
                    fwidth(uv) * max(_ShoreWidthPixels, 0.5),
                    float2(0.00001, 0.00001)
                );

                half neighborMin = 1.0h;
                neighborMin = min(neighborMin, SampleMask(uv + float2(sampleStep.x, 0.0)));
                neighborMin = min(neighborMin, SampleMask(uv - float2(sampleStep.x, 0.0)));
                neighborMin = min(neighborMin, SampleMask(uv + float2(0.0, sampleStep.y)));
                neighborMin = min(neighborMin, SampleMask(uv - float2(0.0, sampleStep.y)));
                neighborMin = min(neighborMin, SampleMask(uv + sampleStep));
                neighborMin = min(neighborMin, SampleMask(uv - sampleStep));
                neighborMin = min(neighborMin, SampleMask(uv + float2(sampleStep.x, -sampleStep.y)));
                neighborMin = min(neighborMin, SampleMask(uv + float2(-sampleStep.x, sampleStep.y)));
                return centerMask * (1.0h - neighborMin);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half mask = SampleMask(input.uv);
                clip(mask - 0.5h);

                float pixelSize = max(_PixelSize, 0.001);
                float2 snappedWorld =
                    (floor((input.worldXZ + _WorldOffset.xy) / pixelSize) + 0.5)
                    * pixelSize;

                float waveSize = max(_WaveSize, 0.001);
                float2 flow = _FlowSpeed.xy * _Time.y;
                float2 wavePosition = (snappedWorld + flow) / waveSize;

                float broadNoise = ValueNoise(wavePosition * 0.72);
                float detailNoise = ValueNoise(
                    wavePosition * 1.83 + float2(-_Time.y * 0.035, _Time.y * 0.02)
                );
                float distortion =
                    (broadNoise - 0.5) * _WaveDistortion
                    + (detailNoise - 0.5) * _WaveDistortion * 0.35;

                float phaseA =
                    wavePosition.y * 5.15
                    + wavePosition.x * 0.58
                    + distortion * 4.0;
                float phaseB =
                    wavePosition.y * 2.7
                    - wavePosition.x * 1.15
                    - distortion * 2.2
                    - _Time.y * 0.18;

                float waveA = sin(phaseA) * 0.5 + 0.5;
                float waveB = sin(phaseB) * 0.5 + 0.5;
                float variation = saturate(
                    0.5
                    + (broadNoise - 0.5) * 0.8
                    + (waveA - 0.5) * _WaveContrast
                    + (waveB - 0.5) * _WaveContrast * 0.45
                );

                half3 waterColor = lerp(
                    _DeepColor.rgb,
                    _ShallowColor.rgb,
                    variation
                );
                half waterAlpha =
                    lerp(_DeepColor.a, _ShallowColor.a, variation)
                    * _Opacity
                    * input.color.a;

                float crestThreshold = 1.0 - max(_HighlightWidth, 0.01);
                half crestA = smoothstep(crestThreshold, 1.0, waveA);
                half crestB = smoothstep(crestThreshold + 0.035, 1.0, waveB);
                half brokenPattern = step(
                    0.48,
                    ValueNoise(wavePosition * 2.45 + float2(3.7, -8.2))
                );
                half movingHighlight =
                    saturate(max(crestA, crestB * 0.55) * brokenPattern)
                    * _HighlightAmount;

                half shore = InnerShoreMask(input.uv, mask);
                half shorePulse = lerp(
                    0.72h,
                    1.0h,
                    sin((snappedWorld.x + snappedWorld.y) * 4.0 - _Time.y * 0.65)
                        * 0.5h + 0.5h
                );

                waterColor += _HighlightColor.rgb * movingHighlight;
                waterColor = lerp(
                    waterColor,
                    _ShoreColor.rgb,
                    saturate(shore * _ShoreStrength * shorePulse)
                );
                waterColor *= input.color.rgb;

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(waterColor, waterAlpha, half4(1, 1, 1, 1), surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
