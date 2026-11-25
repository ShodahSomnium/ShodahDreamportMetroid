// Metroid Prime's own materials (MpTev.hlsl: the TEV stages, texgens and UV animations of each material, read from the disc).
// Passes for the Somnium client: forward rendering with depth priming forced and HBAO wanting normals, so an opaque shader
// needs DepthOnly and DepthNormals passes or it draws nothing in the world (Age of Mythology finding). Blend / depth write /
// cull come from the material (the game's blend factors and depth-write flag).
Shader "Somnium Metroid/Tev"
{
    Properties
    {
        _Tex0 ("Stage 0", 2D) = "white" {}
        _Tex1 ("Stage 1", 2D) = "white" {}
        _Tex2 ("Stage 2", 2D) = "white" {}
        _Tex3 ("Stage 3", 2D) = "white" {}
        _Tex4 ("Stage 4", 2D) = "white" {}
        _Tex5 ("Stage 5", 2D) = "white" {}
        _Ci0 ("C in 0", Vector) = (15, 15, 15, 12)
        _Ci1 ("C in 1", Vector) = (15, 15, 15, 0)
        _Ci2 ("C in 2", Vector) = (15, 15, 15, 0)
        _Ci3 ("C in 3", Vector) = (15, 15, 15, 0)
        _Ci4 ("C in 4", Vector) = (15, 15, 15, 0)
        _Ci5 ("C in 5", Vector) = (15, 15, 15, 0)
        _Ai0 ("A in 0", Vector) = (7, 7, 7, 6)
        _Ai1 ("A in 1", Vector) = (7, 7, 7, 0)
        _Ai2 ("A in 2", Vector) = (7, 7, 7, 0)
        _Ai3 ("A in 3", Vector) = (7, 7, 7, 0)
        _Ai4 ("A in 4", Vector) = (7, 7, 7, 0)
        _Ai5 ("A in 5", Vector) = (7, 7, 7, 0)
        _Ot0 ("Out 0", Vector) = (0, 0, 0, 0)
        _Ot1 ("Out 1", Vector) = (0, 0, 0, 0)
        _Ot2 ("Out 2", Vector) = (0, 0, 0, 0)
        _Ot3 ("Out 3", Vector) = (0, 0, 0, 0)
        _Ot4 ("Out 4", Vector) = (0, 0, 0, 0)
        _Ot5 ("Out 5", Vector) = (0, 0, 0, 0)
        _Tg0 ("Texgen 0", Vector) = (-1, -1, 0, 0)
        _Tg1 ("Texgen 1", Vector) = (-1, -1, 0, 0)
        _Tg2 ("Texgen 2", Vector) = (-1, -1, 0, 0)
        _Tg3 ("Texgen 3", Vector) = (-1, -1, 0, 0)
        _Tg4 ("Texgen 4", Vector) = (-1, -1, 0, 0)
        _Tg5 ("Texgen 5", Vector) = (-1, -1, 0, 0)
        _Tp0 ("Anim 0", Vector) = (0, 0, 0, 0)
        _Tp1 ("Anim 1", Vector) = (0, 0, 0, 0)
        _Tp2 ("Anim 2", Vector) = (0, 0, 0, 0)
        _Tp3 ("Anim 3", Vector) = (0, 0, 0, 0)
        _Tp4 ("Anim 4", Vector) = (0, 0, 0, 0)
        _Tp5 ("Anim 5", Vector) = (0, 0, 0, 0)
        _K0 ("Konst 0", Vector) = (1, 1, 1, 1)
        _K1 ("Konst 1", Vector) = (1, 1, 1, 1)
        _K2 ("Konst 2", Vector) = (1, 1, 1, 1)
        _K3 ("Konst 3", Vector) = (1, 1, 1, 1)
        _K4 ("Konst 4", Vector) = (1, 1, 1, 1)
        _K5 ("Konst 5", Vector) = (1, 1, 1, 1)
        _StageCount ("Stages", Float) = 1
        _AlphaTest ("Alpha test", Float) = 0
        _Lit ("Actor lighting", Float) = 0
        _Ambient ("Ambient", Color) = (0.5, 0.5, 0.5, 1)
        _ModColor ("Colour", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth write", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "MpTev.hlsl"
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex MpTevVert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma target 4.5

            half4 Frag(MpTevVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 c = MpTevShade(i);
                MpAlphaClip(c.a);
                if (_Lit < 1.5) c.rgb = MpVisorLook(c.rgb);
                c.rgb = MpFog(c.rgb, i.viewDepth);
                if (_MpLinearOut > 0.5) c.rgb = MpSrgbToLinear(c.rgb);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex MpTevVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma target 4.5

            half DepthFrag(MpTevVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_AlphaTest > 0.5) MpAlphaClip(MpTevShade(i).a);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex MpTevVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            void DepthNormalsFrag(MpTevVaryings i
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                )
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_AlphaTest > 0.5) MpAlphaClip(MpTevShade(i).a);
                outNormalWS = half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma target 4.5

            float3 _LightDirection;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(MpTevAttributes v)
            {
                ShadowVaryings o = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                return o;
            }

            half ShadowFrag(ShadowVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
