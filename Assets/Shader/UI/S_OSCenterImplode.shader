Shader "UI/OS/BootWaveReveal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Mask Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Progress ("Reveal Progress", Range(0,1)) = 0
        _Origin ("Wave Origin", Vector) = (0.5,0,0,0)
        _ReferenceResolution ("Reference Resolution", Vector) = (640,360,0,0)
        _EdgeDistortionPixels ("Edge Distortion Pixels", Range(0,8)) = 3
        _EdgeSoftnessPixels ("Edge Softness Pixels", Range(0.5,4)) = 1
        _WaveWallPixels ("Wave Wall Width Pixels", Range(1,8)) = 4
        _WaveFrequency ("Wave Frequency", Range(1,16)) = 7
        [HideInInspector] _HasSnapshot ("Has Snapshot", Float) = 0
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
            float4 _ClipRect;
            float _Progress;
            float4 _Origin;
            float4 _ReferenceResolution;
            float _EdgeDistortionPixels;
            float _EdgeSoftnessPixels;
            float _WaveWallPixels;
            float _WaveFrequency;
            float _HasSnapshot;

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
                const float Pi = 3.14159265;
                float progress = saturate(_Progress);
                float2 resolution = max(
                    _ReferenceResolution.xy,
                    float2(1.0, 1.0));
                float2 pixel = saturate(input.texcoord) * resolution;
                float2 origin = float2(
                    saturate(_Origin.x) * resolution.x,
                    _Origin.y * resolution.y);
                float2 offset = pixel - origin;
                float radialDistance = length(offset);

                float maxRadius = max(
                    length(float2(0.0, resolution.y) - origin),
                    length(float2(resolution.x, resolution.y) - origin));
                maxRadius = max(
                    maxRadius,
                    max(length(float2(0.0, 0.0) - origin),
                        length(float2(resolution.x, 0.0) - origin)));

                float angle = atan2(offset.y, offset.x + 0.0001);
                float waveFrequency = max(1.0, _WaveFrequency);
                float waveEnvelope = sin(progress * Pi);
                float edgeNoise = (
                    sin(angle * waveFrequency - progress * 8.0)
                    + sin(
                        angle * waveFrequency * 2.17
                        + progress * 13.0) * 0.5) / 1.5;
                float distortionPixels = max(
                    0.0,
                    _EdgeDistortionPixels);
                float edgeOffset = edgeNoise
                    * distortionPixels
                    * waveEnvelope;
                float edgeSoftness = max(
                    0.5,
                    _EdgeSoftnessPixels);
                float edgePadding = distortionPixels + edgeSoftness;
                float radius = lerp(
                    -edgePadding,
                    maxRadius + edgePadding,
                    progress);
                float signedDistance = radialDistance
                    - radius
                    - edgeOffset;
                float wallWidth = max(1.0, _WaveWallPixels);
                float maskAlpha = smoothstep(
                    -edgeSoftness * 0.5,
                    edgeSoftness * 0.5,
                    signedDistance);

                float wallBand = smoothstep(
                        -wallWidth - edgeSoftness * 0.5,
                        -wallWidth + edgeSoftness * 0.5,
                        signedDistance)
                    * (1.0 - smoothstep(
                        -edgeSoftness * 0.5,
                        edgeSoftness * 0.5,
                        signedDistance));
                float wallCoordinate = saturate(
                    -signedDistance / wallWidth);
                float wallProfile = sin(wallCoordinate * Pi)
                    * wallBand
                    * waveEnvelope;

                float safeDistance = max(radialDistance, 0.0001);
                float2 radialDirection = offset / safeDistance;
                float2 tangentDirection = float2(
                    -radialDirection.y,
                    radialDirection.x);
                float radialTurbulence = (
                    sin(angle * (waveFrequency + 3.0) - progress * 21.0)
                    + sin(angle * waveFrequency * 2.71 + progress * 13.0)
                        * 0.45) / 1.45;
                float tangentTurbulence = (
                    sin(angle * waveFrequency * 1.63 + progress * 17.0)
                    + sin(angle * waveFrequency * 3.17 - progress * 9.0)
                        * 0.35) / 1.35;
                float2 refractionPixels =
                    (radialDirection * radialTurbulence
                        + tangentDirection * tangentTurbulence * 0.7)
                    * distortionPixels
                    * wallProfile;
                float2 refractedUv = clamp(
                    input.texcoord + refractionPixels / resolution,
                    float2(0.0, 0.0),
                    float2(1.0, 1.0));
                fixed4 refractedColor = tex2D(_MainTex, refractedUv)
                    * input.color;
                float lensAlpha = wallProfile
                    * step(0.5, _HasSnapshot)
                    * refractedColor.a;
                float maskLayerAlpha = maskAlpha * input.color.a;
                float combinedAlpha = max(maskLayerAlpha, lensAlpha);
                float lensColorWeight = lensAlpha
                    / max(combinedAlpha, 0.0001);

                // Outside stays black, the revealed interior stays clear, and
                // only the four-pixel wall redraws the cached OS frame warped.
                fixed4 color = fixed4(
                    refractedColor.rgb * lensColorWeight,
                    combinedAlpha);

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
