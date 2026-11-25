// Effects: the game's particle quads, beam bolts, swooshes and HUD lines - texture x vertex colour x colour, the game's blend
// (additive or alpha), drawn after the world. Colours are the game's (gamma space): converted for a linear frame buffer like MpTev.
Shader "Somnium Metroid/Fx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth write", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _AlphaClip ("Alpha clip", Float) = 0
        _Billboard ("Billboard (faces the camera)", Float) = 0
        _Text ("Text (R fill, G outline)", Float) = 0
        _OutlineColor ("Outline colour", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _ZTest;
                float _Cull;
                float _AlphaClip;
                float _Billboard;
                float _Text;
                float4 _OutlineColor;
            CBUFFER_END

            float _MpLinearOut;

            float3 MpSrgbToLinear(float3 c)
            {
                c = saturate(c);
                return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                if (_Billboard > 0.5)
                {
                    // a quad of corners +-0.5 turned to the camera (each eye) at the object's origin, sized by the object's scale
                    float3 centre = TransformWorldToView(TransformObjectToWorld(float3(0, 0, 0)));
                    float size = length(unity_ObjectToWorld._m00_m10_m20);
                    o.positionCS = TransformWViewToHClip(centre + float3(v.positionOS.xy * size, 0));
                }
                else o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                o.color = v.color * _Color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 c;
                if (_Text > 0.5)
                {
                    // the game's font layers: R = fill (the text colour), G = outline (CTextRenderBuffer SetFontPalette)
                    float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                    float fill = t.r, outline = t.g * (1 - t.r);
                    c.rgb = i.color.rgb * fill + _OutlineColor.rgb * outline;
                    c.a = saturate(i.color.a * fill + _OutlineColor.a * outline);
                }
                else c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                if (_AlphaClip > 0) clip(c.a - _AlphaClip);
                if (_MpLinearOut > 0.5) c.rgb = MpSrgbToLinear(c.rgb);
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
