Shader "UI/StateCheckCharge"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OffColor ("Unpowered Color", Color) = (0.2941177,0.282353,0.2745098,1)
        _OnColor ("Powered Color", Color) = (0.7803922,0.6980392,0.1215686,1)
        _Charge ("Charge", Range(0,1)) = 0
        _State ("Node State", Range(0,1)) = 0
        _Reverse ("Reverse", Range(0,1)) = 0
        _NodeMode ("Node Mode", Range(0,1)) = 0
        _Feather ("Charge Feather", Range(0.001,0.2)) = 0.045
        _ElectricStrength ("Line Electric Strength", Range(0,1)) = 1
        _LocalRect ("Local Rect", Vector) = (0,0,1,1)
        _SpriteUvRect ("Current Sprite UV Rect", Vector) = (0,0,1,1)
        _AtlasTexelSize ("Atlas Texel Size", Vector) = (1,1,1,1)
        _ClosedUvRect ("Closed Sprite UV Rect", Vector) = (0,0,1,1)
        _OpenUvRect ("Open Sprite UV Rect", Vector) = (0,0,1,1)
        _FlowStart ("Flow Start", Vector) = (0,0.5,0,0)
        _FlowEnd ("Flow End", Vector) = (1,0.5,0,0)
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
                float2 localUv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            fixed4 _OffColor;
            fixed4 _OnColor;
            float4 _ClipRect;
            float4 _LocalRect;
            float4 _SpriteUvRect;
            float4 _AtlasTexelSize;
            float4 _ClosedUvRect;
            float4 _OpenUvRect;
            float4 _FlowStart;
            float4 _FlowEnd;
            float _Charge;
            float _State;
            float _NodeMode;
            float _Feather;
            float _ElectricStrength;

            float Hash21(float2 value)
            {
                return frac(sin(dot(
                    value,
                    float2(12.9898, 78.233))) * 43758.5453);
            }

            float2 GetSpritePixelCount(float4 uvRect)
            {
                return max(
                    float2(1.0, 1.0),
                    floor(uvRect.zw * _AtlasTexelSize.zw + 0.5));
            }

            fixed SampleSpriteAlpha(float2 localUv, float4 uvRect)
            {
                float2 pixelCount = GetSpritePixelCount(uvRect);
                float2 pixel = min(
                    floor(saturate(localUv) * pixelCount),
                    pixelCount - 1.0);
                float2 uv = uvRect.xy
                    + (pixel + 0.5) * _AtlasTexelSize.xy;
                return saturate(
                    tex2D(_MainTex, uv).a + _TextureSampleAdd.a);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.localUv =
                    (input.vertex.xy - _LocalRect.xy) * _LocalRect.zw;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float nodeMode = saturate(_NodeMode);
                float2 spriteLocalUv =
                    (input.texcoord - _SpriteUvRect.xy)
                    / max(_SpriteUvRect.zw, float2(0.000001, 0.000001));
                float2 localUv = saturate(spriteLocalUv);
                float2 flowDelta = _FlowEnd.xy - _FlowStart.xy;
                float flowLengthSquared = max(
                    0.0001,
                    dot(flowDelta, flowDelta));
                float position = saturate(dot(
                    localUv - _FlowStart.xy,
                    flowDelta) / flowLengthSquared);

                float lineMode = 1.0 - nodeMode;
                float electricActive = lineMode
                    * step(0.0001, _Charge)
                    * (1.0 - step(0.9999, _Charge));
                float electricTick = floor(_Time.y * 42.0);
                float frontFlicker = Hash21(float2(
                    electricTick,
                    floor(_Charge * 37.0) + 3.1));
                float electricPhase = _Time.y * 42.0
                    + _Charge * 31.0
                    + frontFlicker * 6.2831853;
                float electricWave = sin(
                    position * 47.0 + electricPhase);
                float secondaryWave = sin(
                    position * 89.0 - electricPhase * 1.37);
                float chargeJitter = (
                    (frontFlicker - 0.5) * 0.026
                    + electricWave * 0.012) * electricActive;
                float visualCharge = lerp(
                    _Charge,
                    saturate(_Charge + chargeJitter),
                    lineMode);

                float feather = max(0.001, _Feather);
                float charged = 1.0 - smoothstep(
                    visualCharge - feather,
                    visualCharge + feather,
                    position);
                charged *= step(0.0001, _Charge);
                charged = lerp(
                    charged,
                    1.0,
                    smoothstep(0.98, 1.0, _Charge));

                float front = 1.0 - saturate(
                    abs(position - visualCharge) / feather);
                front *= step(0.0001, _Charge)
                    * (1.0 - step(0.9999, _Charge));

                float2 flowDirection = flowDelta
                    * rsqrt(flowLengthSquared);
                float2 flowNormal = float2(
                    -flowDirection.y,
                    flowDirection.x);
                float2 flowPoint = _FlowStart.xy
                    + flowDelta * position;
                float lateral = dot(
                    localUv - flowPoint,
                    flowNormal);
                float arcBend = (
                    electricWave + secondaryWave) * 0.06;
                float electricCore = 1.0 - smoothstep(
                    0.07,
                    0.18,
                    abs(lateral - arcBend));
                float electricHead = 1.0 - smoothstep(
                    0.035,
                    0.14,
                    abs(position - visualCharge));
                float electricRegion = electricActive
                    * saturate(charged + electricHead);
                float electricStrength = saturate(_ElectricStrength);
                float electricShape = saturate(
                    0.2
                    + electricCore * 0.92
                    + electricHead * 0.95);
                float lineAlphaMultiplier = lerp(
                    1.0,
                    electricShape,
                    electricRegion * electricStrength * 0.9);

                fixed4 sample =
                    tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                float state = saturate(_State);
                float2 closedPixels = GetSpritePixelCount(
                    _ClosedUvRect);
                float2 cell = min(
                    floor(localUv * closedPixels),
                    closedPixels - 1.0);
                float2 cellUv = (cell + 0.5) / closedPixels;
                float cellNoise = Hash21(cell);
                float centerDistance = saturate(
                    length((cellUv - 0.5) * 2.0) * 0.70710678);
                float stateThreshold = 0.1
                    + centerDistance * 0.58
                    + cellNoise * 0.18;
                float stateReveal = smoothstep(
                    stateThreshold - 0.055,
                    stateThreshold + 0.055,
                    state);

                float glitchPulse = smoothstep(0.04, 0.11, state)
                    * (1.0 - smoothstep(0.25, 0.35, state));
                float glitchDirection = lerp(
                    -1.0,
                    1.0,
                    step(0.5, Hash21(float2(cell.y, floor(state * 32.0)))));
                float2 glitchUv = localUv;
                glitchUv.x = saturate(
                    glitchUv.x
                    + glitchDirection * glitchPulse / closedPixels.x);

                fixed closedAlpha = SampleSpriteAlpha(
                    localUv,
                    _ClosedUvRect);
                fixed shiftedClosedAlpha = SampleSpriteAlpha(
                    glitchUv,
                    _ClosedUvRect);
                fixed openAlpha = SampleSpriteAlpha(
                    localUv,
                    _OpenUvRect);
                fixed commonAlpha = min(closedAlpha, openAlpha);
                fixed removedAlpha = saturate(
                    shiftedClosedAlpha - openAlpha);
                fixed addedAlpha = saturate(openAlpha - closedAlpha);
                fixed nodeAlpha = commonAlpha
                    + removedAlpha * (1.0 - stateReveal)
                    + addedAlpha * stateReveal;
                fixed shapeDifference = saturate(
                    abs(openAlpha - closedAlpha));
                float shapeEdge = shapeDifference
                    * (1.0 - saturate(
                        abs(state - stateThreshold) / 0.075));
                fixed finalAlpha = lerp(
                    sample.a * lineAlphaMultiplier,
                    nodeAlpha,
                    nodeMode);

                fixed3 stateColor = lerp(
                    _OffColor.rgb,
                    _OnColor.rgb,
                    charged);
                float sparkNoise = Hash21(float2(
                    floor(position * 19.0),
                    electricTick));
                float sparkPattern = (
                    electricWave * 0.5 + 0.5) * 0.58
                    + sparkNoise * 0.42;
                float spark = smoothstep(0.68, 0.96, sparkPattern);
                float hotMask = electricRegion
                    * electricStrength
                    * saturate(
                        electricHead
                        + electricCore * charged
                            * (0.72 + spark * 0.28));
                fixed3 electricColor = fixed3(1.0, 0.95, 0.32);
                fixed4 color;
                color.rgb = input.color.rgb * stateColor;
                color.rgb *= 1.0 - electricRegion
                    * electricStrength
                    * (1.0 - electricCore)
                    * (1.0 - electricHead)
                    * 0.35;
                color.rgb = lerp(
                    color.rgb,
                    electricColor,
                    saturate(hotMask));
                color.rgb += _OnColor.rgb
                    * front
                    * finalAlpha
                    * 0.2;
                color.rgb += electricColor
                    * electricHead
                    * electricActive
                    * finalAlpha
                    * electricStrength
                    * 0.28;

                fixed3 neutralLatch = saturate(
                    _OffColor.rgb * 1.65 + 0.06);
                fixed3 poweredLatch = saturate(
                    _OnColor.rgb * 1.2 + 0.08);
                fixed3 latchColor = lerp(
                    neutralLatch,
                    poweredLatch,
                    saturate(_Charge));
                color.rgb = lerp(
                    color.rgb,
                    latchColor,
                    saturate(shapeEdge * nodeMode * 0.88));
                color.a = finalAlpha * input.color.a;

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
