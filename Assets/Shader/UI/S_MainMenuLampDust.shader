Shader "UI/MainMenu/LampDust"
{
    Properties
    {
        [PerRendererData] _MainTex ("Light Area Mask", 2D) = "white" {}
        [HDR] _DustColor ("Dust Color", Color) = (1.0,0.85,0.29,0.34)
        _Density ("Density", Range(0,0.3)) = 0.16
        _CellSize ("Cell Size", Range(16,64)) = 32
        _AmbientDensity ("Ambient Density", Range(0,0.2)) = 0.11
        _AmbientCellSize ("Ambient Cell Size", Range(48,128)) = 72
        _AmbientStrength ("Ambient Strength", Range(0,1)) = 0.42
        _ParticleSize ("Particle Size", Range(0.25,2.5)) = 0.48
        _Drift ("Drift", Vector) = (0.55,1.35,0,0)
        _Wander ("Wander", Range(0,4)) = 0.8
        _Intensity ("Intensity", Range(0,4)) = 1.05
        _ReferenceSize ("Reference Size", Vector) = (640,360,0,0)
        _Seed ("Seed", Float) = 17

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 14
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
        Blend SrcAlpha One
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
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
            fixed4 _DustColor;
            float4 _ClipRect;
            float4 _ReferenceSize;
            float4 _Drift;
            half _Density;
            half _CellSize;
            half _AmbientDensity;
            half _AmbientCellSize;
            half _AmbientStrength;
            half _ParticleSize;
            half _Wander;
            half _Intensity;
            half _Seed;

            float Hash21(float2 value)
            {
                return frac(sin(dot(value, float2(127.1, 311.7)))
                    * 43758.5453);
            }

            half DustLayer(
                float2 pixel,
                float time,
                float cellSize,
                float2 velocity,
                float density,
                float sizeScale,
                float seed)
            {
                float2 movingPixel = pixel - velocity * time;
                float2 cell = floor(movingPixel / cellSize);
                float2 localPixel = frac(movingPixel / cellSize)
                    * cellSize;

                float presence = Hash21(cell + seed);
                float enabled = step(1.0 - saturate(density), presence);
                float2 center = cellSize * (0.2 + 0.6 * float2(
                    Hash21(cell + seed + 13.17),
                    Hash21(cell + seed + 29.41)));

                float phase = Hash21(cell + seed + 47.83) * 6.2831853;
                float wanderSpeed = lerp(
                    0.35,
                    0.75,
                    Hash21(cell + seed + 71.09));
                center += float2(
                    sin(time * wanderSpeed + phase),
                    cos(time * wanderSpeed * 0.73 + phase))
                    * _Wander;

                float radius = _ParticleSize * sizeScale * lerp(
                    0.65,
                    1.35,
                    Hash21(cell + seed + 89.23));
                float distanceToDust = length(localPixel - center);
                float core = 1.0 - smoothstep(
                    radius,
                    radius + 0.4,
                    distanceToDust);
                float halo = (1.0 - smoothstep(
                    radius + 0.4,
                    radius + 1.35,
                    distanceToDust)) * 0.12;
                float shimmer = lerp(
                    0.72,
                    1.0,
                    0.5 + 0.5 * sin(
                        time * lerp(
                            0.45,
                            0.9,
                            Hash21(cell + seed + 103.61))
                        + phase));

                return enabled * (core + halo) * shimmer;
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 referenceSize = max(
                    _ReferenceSize.xy,
                    float2(1.0, 1.0));
                float2 pixel = floor(input.texcoord * referenceSize) + 0.5;
                float time = _Time.y;

                half litDust = DustLayer(
                    pixel,
                    time,
                    max(16.0, _CellSize),
                    _Drift.xy,
                    _Density,
                    1.0,
                    _Seed);
                litDust += DustLayer(
                    pixel,
                    time,
                    max(12.0, _CellSize * 0.72),
                    float2(-_Drift.x * 0.55, _Drift.y * 0.68),
                    _Density * 0.52,
                    0.72,
                    _Seed + 113.0);

                half ambientDust = DustLayer(
                    pixel,
                    time,
                    max(48.0, _AmbientCellSize),
                    float2(-_Drift.x * 0.3, _Drift.y * 0.42),
                    _AmbientDensity,
                    0.65,
                    _Seed + 271.0);

                fixed mask = tex2D(_MainTex, input.texcoord).a;
                half dust = litDust * mask
                    + ambientDust * (1.0 - mask) * _AmbientStrength;
                fixed alpha = saturate(
                    dust * input.color.a * _DustColor.a);

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(
                    input.worldPosition.xy,
                    _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(
                    _DustColor.rgb * input.color.rgb * _Intensity,
                    alpha);
            }
            ENDCG
        }
    }
}
