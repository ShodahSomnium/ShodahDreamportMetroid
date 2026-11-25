// Made with Amplify Shader Editor v1.9.7.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "ARS_CustomRenderTexture"
{
    Properties
    {
		[Header(High Pass)]_FFT_HighPass_Gain("FFT_HighPass_Gain", Range( 1 , 100)) = 3
		_FFT_HighPass_Curve("FFT_HighPass_Curve", Range( 0 , 100)) = 3
		[Header(Amplitude Correction)]_FFT_Gain("FFT_Gain", Range( 0 , 200)) = 45
		_FFT_Power("FFT_Power", Range( 0 , 10)) = 1
		[Header(FFT B Smoothing)]_FFTB_Smoth("FFTB_Smoth", Range( 0 , 1)) = 0.5
		[Header(Render Texture)]_ARS_Texture1("ARS_Texture1", 2D) = "white" {}

    }

	SubShader
	{
		LOD 0

		
		CGINCLUDE
		#pragma target 3.0
		ENDCG
		Blend Off
		AlphaToMask Off
		Cull Back
		ColorMask RGBA
		ZWrite On
		ZTest LEqual
		
		
        Pass
        {
			Name "Custom RT Update"
            CGPROGRAM
            #define ASE_VERSION 19701

            #include "UnityCustomRenderTexture.cginc"
            #pragma vertex ASECustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 3.0
			

			struct ase_appdata_customrendertexture
			{
				uint vertexID : SV_VertexID;
				
			};

			struct ase_v2f_customrendertexture
			{
				float4 vertex           : SV_POSITION;
				float3 localTexcoord    : TEXCOORD0;    // Texcoord local to the update zone (== globalTexcoord if no partial update zone is specified)
				float3 globalTexcoord   : TEXCOORD1;    // Texcoord relative to the complete custom texture
				uint primitiveID        : TEXCOORD2;    // Index of the update zone (correspond to the index in the updateZones of the Custom Texture)
				float3 direction        : TEXCOORD3;    // For cube textures, direction of the pixel being rendered in the cubemap
				
			};

			uniform float ARS_FFTArray1[1024];
			uniform sampler2D _ARS_Texture1;
			float4 _ARS_Texture1_TexelSize;
			uniform float ARS_FFTArray1Size;
			uniform float _FFT_HighPass_Curve;
			uniform float _FFT_HighPass_Gain;
			uniform float ARS_Texture1Gain;
			uniform float _FFT_Gain;
			uniform float _FFT_Power;
			uniform float _FFTB_Smoth;
			float FFTSmoothSample151( float U, int ArrayLength )
			{
				float val = 0.0;
				int nn = 0;
				int ii = 0;
				int len = ArrayLength - 1;
				for(int n = nn; n < 12; n++)
				{ 
				    ii = clamp(U+n, 0, len);
				    val += ARS_FFTArray1[ii];
				}
				return (val / (float)n);
			}
			


			ase_v2f_customrendertexture ASECustomRenderTextureVertexShader(ase_appdata_customrendertexture IN  )
			{
				ase_v2f_customrendertexture OUT;
				
			#if UNITY_UV_STARTS_AT_TOP
				const float2 vertexPositions[6] =
				{
					{ -1.0f,  1.0f },
					{ -1.0f, -1.0f },
					{  1.0f, -1.0f },
					{  1.0f,  1.0f },
					{ -1.0f,  1.0f },
					{  1.0f, -1.0f }
				};

				const float2 texCoords[6] =
				{
					{ 0.0f, 0.0f },
					{ 0.0f, 1.0f },
					{ 1.0f, 1.0f },
					{ 1.0f, 0.0f },
					{ 0.0f, 0.0f },
					{ 1.0f, 1.0f }
				};
			#else
				const float2 vertexPositions[6] =
				{
					{  1.0f,  1.0f },
					{ -1.0f, -1.0f },
					{ -1.0f,  1.0f },
					{ -1.0f, -1.0f },
					{  1.0f,  1.0f },
					{  1.0f, -1.0f }
				};

				const float2 texCoords[6] =
				{
					{ 1.0f, 1.0f },
					{ 0.0f, 0.0f },
					{ 0.0f, 1.0f },
					{ 0.0f, 0.0f },
					{ 1.0f, 1.0f },
					{ 1.0f, 0.0f }
				};
			#endif

				uint primitiveID = IN.vertexID / 6;
				uint vertexID = IN.vertexID % 6;
				float3 updateZoneCenter = CustomRenderTextureCenters[primitiveID].xyz;
				float3 updateZoneSize = CustomRenderTextureSizesAndRotations[primitiveID].xyz;
				float rotation = CustomRenderTextureSizesAndRotations[primitiveID].w * UNITY_PI / 180.0f;

			#if !UNITY_UV_STARTS_AT_TOP
				rotation = -rotation;
			#endif

				// Normalize rect if needed
				if (CustomRenderTextureUpdateSpace > 0.0) // Pixel space
				{
					// Normalize xy because we need it in clip space.
					updateZoneCenter.xy /= _CustomRenderTextureInfo.xy;
					updateZoneSize.xy /= _CustomRenderTextureInfo.xy;
				}
				else // normalized space
				{
					// Un-normalize depth because we need actual slice index for culling
					updateZoneCenter.z *= _CustomRenderTextureInfo.z;
					updateZoneSize.z *= _CustomRenderTextureInfo.z;
				}

				// Compute rotation

				// Compute quad vertex position
				float2 clipSpaceCenter = updateZoneCenter.xy * 2.0 - 1.0;
				float2 pos = vertexPositions[vertexID] * updateZoneSize.xy;
				pos = CustomRenderTextureRotate2D(pos, rotation);
				pos.x += clipSpaceCenter.x;
			#if UNITY_UV_STARTS_AT_TOP
				pos.y += clipSpaceCenter.y;
			#else
				pos.y -= clipSpaceCenter.y;
			#endif

				// For 3D texture, cull quads outside of the update zone
				// This is neeeded in additional to the preliminary minSlice/maxSlice done on the CPU because update zones can be disjointed.
				// ie: slices [1..5] and [10..15] for two differents zones so we need to cull out slices 0 and [6..9]
				if (CustomRenderTextureIs3D > 0.0)
				{
					int minSlice = (int)(updateZoneCenter.z - updateZoneSize.z * 0.5);
					int maxSlice = minSlice + (int)updateZoneSize.z;
					if (_CustomRenderTexture3DSlice < minSlice || _CustomRenderTexture3DSlice >= maxSlice)
					{
						pos.xy = float2(1000.0, 1000.0); // Vertex outside of ncs
					}
				}

				OUT.vertex = float4(pos, 0.0, 1.0);
				OUT.primitiveID = asuint(CustomRenderTexturePrimitiveIDs[primitiveID]);
				OUT.localTexcoord = float3(texCoords[vertexID], CustomRenderTexture3DTexcoordW);
				OUT.globalTexcoord = float3(pos.xy * 0.5 + 0.5, CustomRenderTexture3DTexcoordW);
			#if UNITY_UV_STARTS_AT_TOP
				OUT.globalTexcoord.y = 1.0 - OUT.globalTexcoord.y;
			#endif
				OUT.direction = CustomRenderTextureComputeCubeDirection(OUT.globalTexcoord.xy);

				return OUT;
			}

            float4 frag(ase_v2f_customrendertexture IN ) : COLOR
            {
				float4 finalColor;
				float TexH185 = _ARS_Texture1_TexelSize.y;
				float2 appendResult165 = (float2(IN.localTexcoord.xy.x , ( IN.localTexcoord.xy.y - TexH185 )));
				float4 tex2DNode129 = tex2D( _ARS_Texture1, appendResult165 );
				float ArraySize245 = ARS_FFTArray1Size;
				float temp_output_7_0 = ( IN.localTexcoord.xy.x * ArraySize245 );
				float U151 = temp_output_7_0;
				int ArrayLength151 = (int)ArraySize245;
				float localFFTSmoothSample151 = FFTSmoothSample151( U151 , ArrayLength151 );
				float Texture1Gain259 = ARS_Texture1Gain;
				float temp_output_199_0 = pow( ( ( ( localFFTSmoothSample151 * ( ( ( 1.0 - exp2( -( IN.localTexcoord.xy.x * _FFT_HighPass_Curve ) ) ) * _FFT_HighPass_Gain ) + 1.0 ) ) * Texture1Gain259 ) * _FFT_Gain ) , _FFT_Power );
				float ifLocalVar137 = 0;
				if( IN.localTexcoord.xy.y >= TexH185 )
				ifLocalVar137 = tex2DNode129.r;
				else
				ifLocalVar137 = temp_output_199_0;
				float lerpResult214 = lerp( temp_output_199_0 , tex2D( _ARS_Texture1, IN.localTexcoord.xy ).g , pow( IN.localTexcoord.xy.y , _FFTB_Smoth ));
				float2 appendResult225 = (float2(ifLocalVar137 , lerpResult214));
				
                finalColor = float4( appendResult225, 0.0 , 0.0 );
				return finalColor;
            }
            ENDCG
		}
    }
	
	CustomEditor "ASEMaterialInspector"
	Fallback Off
}
/*ASEBEGIN
Version=19701
Node;AmplifyShaderEditor.CommentaryNode;190;-1392,-304;Inherit;False;526.9257;492.9041;Attenuate Low Frequency;9;179;173;177;178;184;187;181;176;251;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;194;-1648,-1504;Inherit;False;611.4211;575.8799;Register;10;239;245;185;217;167;238;210;131;258;259;;1,1,1,1;0;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;181;-1344,-160;Inherit;False;0;2;0;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;179;-1104,96;Inherit;False;Property;_FFT_HighPass_Curve;FFT_HighPass_Curve;1;0;Create;True;0;0;0;False;0;False;3;1.8;0;100;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;178;-1104,0;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexturePropertyNode;131;-1600,-1376;Inherit;True;Property;_ARS_Texture1;ARS_Texture1;6;1;[Header];Create;True;1;Render Texture;0;0;False;0;False;None;30543b9cd190a104da1ee755064164f0;False;white;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.RegisterLocalVarNode;210;-1600,-1456;Inherit;False;FFT_Texture;-1;True;1;0;SAMPLER2D;0;False;1;SAMPLER2D;0
Node;AmplifyShaderEditor.NegateNode;177;-1104,-80;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;239;-1600,-1008;Inherit;False;Global;ARS_FFTArray1Size;ARS_FFTArray1Size;4;0;Create;True;0;0;0;False;0;False;1024;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.Exp2OpNode;173;-1104,-160;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;192;-1136,-672;Inherit;False;260;338.8;Sample Array;3;7;151;246;;1,1,1,1;0;0
Node;AmplifyShaderEditor.GetLocalVarNode;217;-1296,-1200;Inherit;False;210;FFT_Texture;1;0;OBJECT;;False;1;SAMPLER2D;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;245;-1600,-1088;Inherit;False;ArraySize;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexelSizeNode;167;-1296,-1376;Inherit;False;-1;1;0;SAMPLER2D;;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleSubtractOpNode;176;-1104,-256;Inherit;False;2;0;FLOAT;1;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;6;-1472,-704;Inherit;False;0;2;0;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;250;-944,-48;Inherit;False;Property;_FFT_HighPass_Gain;FFT_HighPass_Gain;0;1;[Header];Create;True;1;High Pass;0;0;False;0;False;3;12;1;100;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;246;-1104,-416;Inherit;False;245;ArraySize;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;195;-688,-1056;Inherit;False;1112.517;345.6782;Get -1 sample;8;226;129;212;165;135;186;227;228;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;185;-1296,-1456;Inherit;False;TexH;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;7;-1104,-512;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;330;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;251;-928,-160;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;258;-1296,-1008;Inherit;False;Global;ARS_Texture1Gain;ARS_Texture1Gain;7;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;186;-368,-832;Inherit;False;185;TexH;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;135;-624,-1008;Inherit;False;0;2;0;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CommentaryNode;200;-666.5336,-448;Inherit;False;558.5924;331.1303;Gain and Power;6;5;260;261;2;199;196;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleAddOpNode;248;-928,-256;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;259;-1296,-1088;Inherit;False;Texture1Gain;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CustomExpressionNode;151;-1104,-608;Inherit;False;float val = 0.0@$int nn = 0@$int ii = 0@$int len = ArrayLength - 1@$for(int n = nn@ n < 12@ n++)${ $    ii = clamp(U+n, 0, len)@$    val += ARS_FFTArray1[ii]@$}$return (val / (float)n)@;1;Create;2;True;U;FLOAT;0;In;;Inherit;False;True;ArrayLength;INT;0;In;;Inherit;False;FFT SmoothSample;True;False;0;;False;2;0;FLOAT;0;False;1;INT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;227;-368,-928;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;180;-816,-368;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;260;-640,-208;Inherit;False;259;Texture1Gain;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;165;-224,-1008;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;212;-16,-816;Inherit;False;210;FFT_Texture;1;0;OBJECT;;False;1;SAMPLER2D;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;261;-640,-400;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;5;-528,-288;Inherit;False;Property;_FFT_Gain;FFT_Gain;2;1;[Header];Create;True;1;Amplitude Correction;0;0;False;0;False;45;10;0;200;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;233;-64,0;Inherit;False;210;FFT_Texture;1;0;OBJECT;;False;1;SAMPLER2D;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;252;-64,80;Inherit;False;0;2;0;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.TexCoordVertexDataNode;236;256,-128;Inherit;False;0;2;0;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;213;496,16;Inherit;False;Property;_FFTB_Smoth;FFTB_Smoth;4;1;[Header];Create;True;1;FFT B Smoothing;0;0;False;0;False;0.5;0.25;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;129;-16,-1008;Inherit;True;Property;_RT_FFT;RT_FFT;5;0;Create;True;0;0;0;False;0;False;131;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode;196;-384,-208;Inherit;False;Property;_FFT_Power;FFT_Power;3;0;Create;True;0;0;0;False;0;False;1;2;0;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;2;-464,-400;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;237;512,-128;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.RelayNode;226;288,-1008;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;170;816,-1008;Inherit;False;438.0999;447.3869;Write to first pixel line;3;223;193;137;;1,1,1,1;0;0
Node;AmplifyShaderEditor.PowerNode;199;-304,-400;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;235;-64,-192;Inherit;True;Property;_RT_FFT2;RT_FFT;5;0;Create;True;0;0;0;False;0;False;131;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.LerpOp;214;656,-256;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;193;1024,-656;Inherit;False;185;TexH;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;223;1024,-784;Inherit;False;0;2;0;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RelayNode;257;576,-688;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RelayNode;247;576,-784;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;189;512,224;Inherit;False;457.6;261.68;Smoother;3;171;9;172;;1,1,1,1;0;0
Node;AmplifyShaderEditor.ConditionalIfNode;137;1024,-960;Inherit;False;False;5;0;FLOAT;0;False;1;FLOAT;0.015625;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RelayNode;183;656,-400;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;256;928,-176;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;10;False;1;FLOAT;0
Node;AmplifyShaderEditor.RelayNode;228;288,-800;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;184;-1344,-256;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TexCoordVertexDataNode;9;560,272;Inherit;False;0;2;0;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;172;560,400;Inherit;False;Property;_FFTB_SmoothCurve;FFTB_SmoothCurve;5;0;Create;True;0;0;0;False;0;False;1;50;0.25;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;171;800,272;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;17;1008,224;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;225;1712,-608;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;187;-1344,-32;Inherit;False;185;TexH;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GlobalArrayNode;220;-848,-624;Inherit;False;FFT_Audio_1;0;256;0;False;False;0;1;False;Instance;238;4;0;INT;0;False;2;INT;0;False;1;INT;0;False;3;INT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GlobalArrayNode;238;-1600,-1184;Inherit;False;ARS_FFTArray1;0;1024;0;False;False;0;1;True;Object;-1;4;0;INT;0;False;2;INT;0;False;1;INT;0;False;3;INT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;0;1712,-720;Float;False;True;-1;2;ASEMaterialInspector;0;2;ARS_CustomRenderTexture;32120270d1b3a8746af2aca8bc749736;True;Custom RT Update;0;0;Custom RT Update;1;False;True;0;1;False;;0;False;;0;1;False;;0;False;;True;0;False;;0;False;;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;True;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;True;True;0;False;;True;0;False;;True;False;0;False;;0;False;;True;0;True;2;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;0;;0;0;Standard;0;0;1;True;False;;False;0
WireConnection;178;0;181;1
WireConnection;178;1;179;0
WireConnection;210;0;131;0
WireConnection;177;0;178;0
WireConnection;173;0;177;0
WireConnection;245;0;239;0
WireConnection;167;0;217;0
WireConnection;176;1;173;0
WireConnection;185;0;167;2
WireConnection;7;0;6;1
WireConnection;7;1;246;0
WireConnection;251;0;176;0
WireConnection;251;1;250;0
WireConnection;248;0;251;0
WireConnection;259;0;258;0
WireConnection;151;0;7;0
WireConnection;151;1;246;0
WireConnection;227;0;135;2
WireConnection;227;1;186;0
WireConnection;180;0;151;0
WireConnection;180;1;248;0
WireConnection;165;0;135;1
WireConnection;165;1;227;0
WireConnection;261;0;180;0
WireConnection;261;1;260;0
WireConnection;129;0;212;0
WireConnection;129;1;165;0
WireConnection;2;0;261;0
WireConnection;2;1;5;0
WireConnection;237;0;236;2
WireConnection;237;1;213;0
WireConnection;226;0;129;1
WireConnection;199;0;2;0
WireConnection;199;1;196;0
WireConnection;235;0;233;0
WireConnection;235;1;252;0
WireConnection;214;0;199;0
WireConnection;214;1;235;2
WireConnection;214;2;237;0
WireConnection;257;0;199;0
WireConnection;247;0;226;0
WireConnection;137;0;223;2
WireConnection;137;1;193;0
WireConnection;137;2;247;0
WireConnection;137;3;247;0
WireConnection;137;4;257;0
WireConnection;183;0;214;0
WireConnection;228;0;129;2
WireConnection;184;0;181;1
WireConnection;184;1;187;0
WireConnection;171;0;9;2
WireConnection;171;1;172;0
WireConnection;17;2;171;0
WireConnection;225;0;137;0
WireConnection;225;1;183;0
WireConnection;220;0;7;0
WireConnection;238;1;239;0
WireConnection;0;0;225;0
ASEEND*/
//CHKSM=A3798AC5F342392311956B334338C7B3A25BA5B5