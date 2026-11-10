Shader "UI/OS/CursorExperimentSignalTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("Frozen monitor frame", 2D) = "black" {}
        _Distortion ("Signal corruption", Range(0,1)) = 0
        _SignalClock ("Story-controlled clock", Float) = 0
        _Opacity ("Screen cover opacity", Range(0,1)) = 0
        _Blackout ("Cut to black", Range(0,1)) = 0
        _HasCapture ("Captured frame available", Float) = 0
        _PatchOnly ("Ambient patches only", Float) = 0
        _Seed ("Per-pulse pattern", Float) = 0
        _CaptureFlipY ("GPU capture vertical orientation", Float) = 0
        _NativeSize ("Native pixel grid", Vector) = (640,360,0,0)
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float _Distortion, _SignalClock, _Opacity, _HasCapture, _Blackout, _PatchOnly, _Seed, _CaptureFlipY;
            float4 _NativeSize, _ClipRect;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex); o.local = v.vertex;
                o.color = v.color; o.uv = v.uv;
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            fixed4 frag(v2f i) : SV_Target
            {
                float d = saturate(_Distortion);
                float2 size = max(_NativeSize.xy, 1.0);
                float2 pixel = floor(i.uv * size);
                // Three to five short horizontal displacements. Outside those
                // rectangles the captured frame is sampled without alteration.
                // Only the supplied clock moves them; no _Time or live noise.
                float tick = floor(_SignalClock * 18.0) + _Seed;
                float count = _PatchOnly > .5 ? 12.0 + floor(Hash(float2(tick, 19.0)) * 5.0) : 3.0 + floor(Hash(float2(tick, 19.0)) * 3.0);
                float shift = 0.0;
                float shade = 1.0;
                float patchMask = 0.0;
                [loop] for (int block = 0; block < 16; block++)
                {
                    float k = (float)block;
                    if (k >= count) break;
                    float h = Hash(float2(k + 7.0, tick));
                    float2 extent = floor(float2(44.0 + h * 118.0, 10.0 + Hash(float2(k + 31.0, tick)) * 32.0));
                    float2 origin = floor(float2(Hash(float2(k + 53.0, tick)) * max(1.0, size.x - extent.x),
                        (k + .12 + Hash(float2(k + 97.0, tick)) * .40) * size.y / 5.0));
                    // Keep one patch near the suspended cursor's central area.
                    if (block == 0) origin = floor(size * float2(.40, .46));
                    if (_PatchOnly > .5)
                    {
                        // Thin, SHORT rectangles, never a full-width scan band.
                        // A few cross each foreground window, the rest scatter
                        // through the screen; no large square cutouts.
                        extent = floor(float2(28.0 + h * 68.0, 2.0 + Hash(float2(k + 31.0, tick)) * 4.0));
                        origin = floor(float2(Hash(float2(k + 53.0, tick)) * (size.x - extent.x),
                            Hash(float2(k + 97.0, tick)) * (size.y - extent.y)));
                        if (block < 6)
                        {
                            float2 region = block % 2 == 0 ? float2(.25, .24) : float2(.76, .35);
                            origin = floor(size * (region + float2(Hash(float2(k + 67.0, tick)) * .075,
                                Hash(float2(k + 97.0, tick)) * .34)));
                        }
                    }
                    float inside = step(origin.x, pixel.x) * (1.0 - step(origin.x + extent.x, pixel.x))
                        * step(origin.y, pixel.y) * (1.0 - step(origin.y + extent.y, pixel.y))
                        * (1.0 - step(count, k)) * step(.001, d);
                    float direction = Hash(float2(k + 113.0, tick)) > .5 ? 1.0 : -1.0;
                    float distance = floor(_PatchOnly > .5 ? lerp(10.0, 23.0, d) : lerp(12.0, 24.0, d));
                    shift = lerp(shift, direction * distance, inside);
                    shade = lerp(shade, _PatchOnly > .5 ? 1.0 : 1.0 - .32 * d * step(.76, h), inside);
                    patchMask = max(patchMask, inside);
                }
                float2 uv = i.uv + float2(shift / size.x, 0);
                // ScreenCapture's GPU surface is top-origin on D3D. The CPU
                // Texture2D used by the story transition is already normalized.
                uv.y = lerp(uv.y, 1.0 - uv.y, saturate(_CaptureFlipY));
                float3 source = tex2D(_MainTex, saturate(uv)).rgb * saturate(_HasCapture) * shade;
                // The blackout phase discards the old screenshot entirely.
                // Fading opacity while this remains 1 reveals only the new UI.
                source = lerp(source, float3(0,0,0), saturate(_Blackout));
                float alpha = saturate(_Opacity) * i.color.a;
                if (_PatchOnly > .5) alpha *= patchMask * saturate(_HasCapture);
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - .001);
                #endif
                return fixed4(source * i.color.rgb, alpha);
            }
            ENDCG
        }
    }
}
