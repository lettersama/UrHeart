Shader "UI/OS/TextRedaction"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Paint Color", Color) = (0.22,0.205,0.185,1)
        _Coverage ("Paint Coverage", Range(0,1)) = 0.9
        _EdgeNoise ("Edge Noise", Range(0,1)) = 0.55
        _Grain ("Paint Grain", Range(0,1)) = 0.32
        _Seed ("Pattern Seed", Float) = 17.3
        _RectSize ("Runtime Rect Size", Vector) = (40,10,0,0)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip (
            "Use Alpha Clip",
            Float
        ) = 0
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
                float2 localUv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float4 _ClipRect;
            float _Coverage;
            float _EdgeNoise;
            float _Grain;
            float _Seed;
            float4 _RectSize;

            float Hash11(float value)
            {
                return frac(
                    sin(value * 12.9898 + 78.233) * 43758.5453);
            }

            float Hash21(float2 value)
            {
                return frac(sin(dot(
                    value,
                    float2(12.9898, 78.233))) * 43758.5453);
            }

            float ValueNoise1(float value, float noiseSeed)
            {
                float cell = floor(value);
                float blend = frac(value);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float left = Hash11(cell + noiseSeed);
                float right = Hash11(cell + 1.0 + noiseSeed);
                return lerp(left, right, blend);
            }

            float BrushStroke(
                float2 uv,
                float center,
                float slope,
                float strokeSeed)
            {
                float rectWidth = max(_RectSize.x, 1.0);
                float edgeFrequency = max(4.0, rectWidth * 0.22);
                float centerNoise = ValueNoise1(
                    uv.x * edgeFrequency,
                    strokeSeed + _Seed) - 0.5;
                float widthNoise = ValueNoise1(
                    uv.x * edgeFrequency * 0.67 + 19.7,
                    strokeSeed * 2.13 + _Seed) - 0.5;
                float wave = sin(
                    (uv.x * 2.0 + strokeSeed * 0.17)
                    * 6.2831853) * 0.012;
                float strokeCenter = center
                    + slope * (uv.x - 0.5)
                    + wave
                    + centerNoise
                    * lerp(0.025, 0.085, saturate(_EdgeNoise));
                float halfWidth = lerp(
                    0.075,
                    0.105,
                    saturate(_Coverage));
                halfWidth += widthNoise
                    * 0.032
                    * saturate(_EdgeNoise);
                float edge = lerp(
                    0.012,
                    0.028,
                    saturate(_EdgeNoise));
                float stroke = 1.0 - smoothstep(
                    halfWidth,
                    halfWidth + edge,
                    abs(uv.y - strokeCenter));

                float leftNoise = ValueNoise1(
                    uv.y * 4.0 + strokeSeed,
                    _Seed * 1.37 + 3.1);
                float rightNoise = ValueNoise1(
                    uv.y * 4.0 + strokeSeed * 1.71,
                    _Seed * 1.83 + 7.9);
                float sideNoise = 0.055 * saturate(_EdgeNoise);
                float leftEdge = 0.012 + leftNoise * sideNoise;
                float rightEdge = 0.988 - rightNoise * sideNoise;
                float sides = smoothstep(
                    leftEdge - 0.014,
                    leftEdge + 0.014,
                    uv.x);
                sides *= 1.0 - smoothstep(
                    rightEdge - 0.014,
                    rightEdge + 0.014,
                    uv.x);
                return stroke * sides;
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(
                    output.worldPosition);
                output.localUv = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = saturate(input.localUv);
                float paint = 0.0;
                paint = max(
                    paint,
                    BrushStroke(uv, 0.24, 0.025, 1.1));
                paint = max(
                    paint,
                    BrushStroke(uv, 0.50, -0.018, 7.3));
                paint = max(
                    paint,
                    BrushStroke(uv, 0.76, 0.022, 17.1));

                float2 rectSize = max(
                    _RectSize.xy,
                    float2(1.0, 1.0));
                float2 grainScale = max(
                    rectSize * float2(0.85, 1.2),
                    float2(8.0, 8.0));
                float2 grainCell = floor(uv * grainScale);
                float grainNoise = Hash21(
                    grainCell + _Seed);
                float shade = lerp(
                    1.0,
                    lerp(0.78, 1.08, grainNoise),
                    saturate(_Grain));
                float dryThreshold = lerp(
                    0.995,
                    0.86,
                    saturate(_Grain));
                float dryBrush = smoothstep(
                    dryThreshold,
                    1.0,
                    grainNoise);
                float opacity = paint * lerp(
                    1.0,
                    0.25,
                    dryBrush);

                fixed4 textureColor =
                    tex2D(_MainTex, input.localUv)
                    + _TextureSampleAdd;
                fixed4 color = input.color;
                color.rgb *= shade;
                color.a *= opacity * textureColor.a;

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
