Shader "GpuTrail/StartEndColor" {
Properties {
	_StartColor("StartColor", Color) = (1,1,1,1)
	_EndColor("EndColor", Color) = (0,0,0,1)
}
   
SubShader {
	Tags { "Queue" = "Transparent" }

	Pass{
		Cull Off Fog { Mode Off }
		ZWrite Off
		Blend SrcAlpha One

		CGPROGRAM
		#pragma target 5.0

		#pragma vertex vert
		#pragma fragment frag
		// #pragma multi_compile_local __ GPUTRAIL_TRAIL_INDEX_ON
		
		#include "UnityCG.cginc"
		#define GPUTRAIL_TRAIL_INDEX_ON
		#include "GpuTrailIndexInclude.hlsl"
		#include "GpuTrailShaderInclude.hlsl"
		
		float4 _StartColor;
		float4 _EndColor;

		struct vs_out {
			float4 pos : SV_POSITION;
			float4 col : COLOR;
			float2 uv  : TEXCOORD;
		};

		vs_out vert (uint vId : SV_VertexID, uint iId : SV_InstanceID)
		{
			vs_out Out;
			// Vertex vtx = GetVertex(vId, iId);
			uint trainIndex = GetTrailIdx(iId);
			vId = vId % _VertexNumPerTrail;
			Vertex vtx = GetVertex(vId, trainIndex);
			
			Out.pos = UnityObjectToClipPos(float4(vtx.pos, 1.0));
			Out.uv = vtx.uv;
			Out.col = lerp(_EndColor, _StartColor, vtx.uv.x);
			//Out.col = vtx.color;

			return Out;
		}

		fixed4 frag (vs_out In) : COLOR0
		{
			return In.col;
		}

		ENDCG
   
	   }
	}
}

