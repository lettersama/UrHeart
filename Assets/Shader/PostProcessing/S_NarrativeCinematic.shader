Shader "Hidden/MrTGame/PostProcessing/NarrativeCinematic"
{
    Properties
    {
        [Header(Master)]
        [Enum(Off,0,On,1)] _EffectEnabled ("Enabled", Float) = 1
        _Intensity ("Master Intensity", Range(0, 1)) = 1

        [Header(Film Grade)]
        _Exposure ("Exposure", Range(-1, 1)) = -0.04
        _Contrast ("Contrast", Range(0.5, 1.5)) = 1.08
        _Saturation ("Saturation", Range(0, 1.5)) = 0.88
        _CurveStrength ("Filmic S-Curve", Range(0, 1)) = 0.2
        _HighlightRolloff ("Highlight Rolloff", Range(0, 1)) = 0.25
        _BlackLift ("Black Lift", Range(-0.08, 0.08)) = 0.008

        [Header(Split Tone)]
        _ShadowTint ("Shadow Tint", Color) = (0.72, 0.84, 1, 1)
        _ShadowTintStrength ("Shadow Tint Strength", Range(0, 0.5)) = 0.12
        _HighlightTint ("Highlight Tint", Color) = (1, 0.82, 0.58, 1)
        _HighlightTintStrength ("Highlight Tint Strength", Range(0, 0.5)) = 0.08
        _ShadowDesaturation ("Shadow Desaturation", Range(0, 1)) = 0.08

        [Header(Focus Vignette)]
        _FocusCenter ("Focus Center", Vector) = (0.5, 0.46, 0, 0)
        _VignetteColor ("Vignette Color", Color) = (0.006, 0.009, 0.014, 1)
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.2
        _VignetteInner ("Vignette Inner", Range(0, 2)) = 0.62
        _VignetteOuter ("Vignette Outer", Range(0.01, 3)) = 1.42
        _VignetteRoundness ("Vignette Roundness", Range(0, 1)) = 0.32
        _PeripheralDesaturation ("Peripheral Desaturation", Range(0, 1)) = 0.08

        [Header(Film Grain)]
        _DitherStrength ("Dark Dither Strength", Range(0, 2)) = 0.4
        _GrainIntensity ("Grain Intensity", Range(0, 0.15)) = 0.004
        _GrainSize ("Grain Pixel Size", Range(1, 8)) = 2
        _GrainFPS ("Grain Update Rate", Range(1, 30)) = 8
        _GrainResponse ("Grain Luminance Response", Range(0, 1)) = 0.75

        [Header(Cutscene Controls)]
        _Letterbox ("Letterbox Bar Height", Range(0, 0.25)) = 0
        _LetterboxSoftness ("Letterbox Softness", Range(0.0001, 0.02)) = 0.001
        _LetterboxColor ("Letterbox Color", Color) = (0, 0, 0, 1)
        _Fade ("Fade", Range(0, 1)) = 0
        _FadeColor ("Fade Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "NarrativeCinematic"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowTint;
                half4 _HighlightTint;
                half4 _VignetteColor;
                half4 _LetterboxColor;
                half4 _FadeColor;
                float4 _FocusCenter;
                half _EffectEnabled;
                half _Intensity;
                half _Exposure;
                half _Contrast;
                half _Saturation;
                half _CurveStrength;
                half _HighlightRolloff;
                half _BlackLift;
                half _ShadowTintStrength;
                half _HighlightTintStrength;
                half _ShadowDesaturation;
                half _VignetteIntensity;
                half _VignetteInner;
                half _VignetteOuter;
                half _VignetteRoundness;
                half _PeripheralDesaturation;
                half _DitherStrength;
                half _GrainIntensity;
                half _GrainSize;
                half _GrainFPS;
                half _GrainResponse;
                half _Letterbox;
                half _LetterboxSoftness;
                half _Fade;
            CBUFFER_END

            static const half3 LUMINANCE = half3(0.2126h, 0.7152h, 0.0722h);

            half LuminanceOf(half3 color)
            {
                return dot(color, LUMINANCE);
            }

            half3 LuminancePreservingTint(half3 tint)
            {
                return tint / max(LuminanceOf(tint), 0.001h);
            }

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float InterleavedGradientNoise(float2 pixel)
            {
                return frac(
                    52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))
                );
            }

            half3 ApplyFilmGrade(half3 source)
            {
                half3 color = max(source * exp2(_Exposure), 0.0h);

                color = (color - 0.18h) * _Contrast + 0.18h;
                half3 curveInput = saturate(color);
                half3 sCurve = curveInput * curveInput * (3.0h - 2.0h * curveInput);
                color = lerp(color, sCurve, _CurveStrength);

                half3 shoulder = max(color - 0.72h, 0.0h);
                color -=
                    shoulder * shoulder
                    / max(shoulder + 0.28h, 0.001h)
                    * _HighlightRolloff;

                half luminance = saturate(LuminanceOf(color));
                half shadowWeight = 1.0h - smoothstep(0.08h, 0.58h, luminance);
                half highlightWeight = smoothstep(0.42h, 0.95h, luminance);

                half3 coolShadows = color * LuminancePreservingTint(_ShadowTint.rgb);
                half3 warmHighlights = color * LuminancePreservingTint(_HighlightTint.rgb);
                color = lerp(
                    color,
                    coolShadows,
                    shadowWeight * _ShadowTintStrength
                );
                color = lerp(
                    color,
                    warmHighlights,
                    highlightWeight * _HighlightTintStrength
                );

                luminance = LuminanceOf(color);
                half shadowSaturation = 1.0h - shadowWeight * _ShadowDesaturation;
                color = lerp(luminance.xxx, color, shadowSaturation);
                color += _BlackLift * shadowWeight;

                luminance = LuminanceOf(color);
                color = lerp(luminance.xxx, color, _Saturation);
                return max(color, 0.0h);
            }

            half4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;
                half4 source = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_PointClamp,
                    uv,
                    _BlitMipLevel
                );

                half3 color = ApplyFilmGrade(source.rgb);

                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 focusVector = (uv - _FocusCenter.xy) * 2.0;
                focusVector.x *= lerp(1.0, aspect, _VignetteRoundness);
                half focusDistance = length(focusVector);
                half vignette = smoothstep(
                    _VignetteInner,
                    max(_VignetteOuter, _VignetteInner + 0.001h),
                    focusDistance
                );

                half luminance = LuminanceOf(color);
                color = lerp(
                    color,
                    luminance.xxx,
                    vignette * _PeripheralDesaturation
                );
                color = lerp(
                    color,
                    _VignetteColor.rgb,
                    vignette * _VignetteIntensity
                );

                float2 outputPixel = floor(uv * _ScreenParams.xy);
                half dither =
                    (half)(InterleavedGradientNoise(outputPixel) - 0.5)
                    * (_DitherStrength / 255.0h);
                luminance = saturate(LuminanceOf(color));
                color += dither * lerp(1.0h, 0.35h, luminance);

                float grainSize = max((float)_GrainSize, 1.0);
                float2 grainCell = floor(uv * _ScreenParams.xy / grainSize);
                float grainFrame = floor(_Time.y * max((float)_GrainFPS, 1.0));
                half grain = (half)Hash31(float3(grainCell, grainFrame)) - 0.5h;
                luminance = saturate(LuminanceOf(color));
                half midtoneResponse = saturate(1.0h - abs(luminance * 2.0h - 1.0h));
                half grainWeight = lerp(1.0h, midtoneResponse, _GrainResponse);
                color += grain * _GrainIntensity * grainWeight;

                half letterboxEnabled = step(0.0001h, _Letterbox);
                half distanceToHorizontalEdge = min(uv.y, 1.0 - uv.y);
                half pictureMask = smoothstep(
                    _Letterbox,
                    _Letterbox + max(_LetterboxSoftness, 0.0001h),
                    distanceToHorizontalEdge
                );
                half bars = (1.0h - pictureMask) * letterboxEnabled;
                color = lerp(color, _LetterboxColor.rgb, bars);
                color = lerp(color, _FadeColor.rgb, _Fade);

                half master = saturate(_EffectEnabled * _Intensity);
                return half4(lerp(source.rgb, color, master), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
