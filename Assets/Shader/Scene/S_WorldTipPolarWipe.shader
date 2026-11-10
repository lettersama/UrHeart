Shader "MrTGame/Scene/WorldTipPolarWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Diffuse", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        [MaterialToggle] _ZWrite ("ZWrite", Float) = 0
        _Reveal ("Reveal", Range(0, 1)) = 1
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.1)) = 0.015

        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Reveal;
            half _EdgeSoftness;
        CBUFFER_END

        half PolarReveal(float2 uv)
        {
            float2 centeredUv = uv - float2(0.5, 0.5);

            // atan2(x, y) starts at twelve o'clock and increases clockwise.
            float clockwiseAngle = frac(
                atan2(centeredUv.x, centeredUv.y) * 0.15915494309189535 + 1.0
            );
            half wipe = saturate(
                (_Reveal - clockwiseAngle) / max(_EdgeSoftness, 0.0001)
            );
            half fullyVisible = step(0.9999, _Reveal);
            half notEmpty = step(0.0001, _Reveal);
            return lerp(wipe, 1.0, fullyVisible) * notEmpty;
        }
        ENDHLSL

        Pass
        {
            Name "SpriteLitPolarWipe"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonLitVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                input.color.a *= PolarReveal(input.uv);
                return CommonLitFragment(input, input.color);
            }
            ENDHLSL
        }

        Pass
        {
            Name "NormalsPolarWipe"
            Tags { "LightMode" = "NormalsRendering" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            Varyings NormalsVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonNormalsVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }

            half4 NormalsFragment(Varyings input) : SV_Target
            {
                input.color.a *= PolarReveal(input.uv);
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SpriteForwardPolarWipe"
            Tags
            {
                "LightMode" = "UniversalForward"
                "Queue" = "Transparent"
                "RenderType" = "Transparent"
            }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonUnlitVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                input.color.a *= PolarReveal(input.uv);
                return CommonUnlitFragment(input, input.color);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
