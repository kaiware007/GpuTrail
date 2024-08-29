Shader "GpuTrail/WireFrame Bounds"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            #include "GpuTrailAABBCSInclude.hlsl"
            
            StructuredBuffer<AABB> _ResultBuffer;
            
            struct appdata
            {
                uint vertexId : SV_VertexID;
                uint instanceId : SV_InstanceID;
            };

            struct v2g
            {
                uint vertexId : TEXCOORD0;
                uint instanceId : TEXCOORD1;
            };
            
            struct g2f
            {
                float4 vertex : SV_POSITION;
            };

            float4 _Color;
            int _VertexNum;
            
            v2g vert (appdata v)
            {
                v2g o;
                o.instanceId = v.instanceId;
                o.vertexId = v.vertexId;
                return o;
            }
            
            [maxvertexcount(18)] 
            void geom(point v2g input[1], inout LineStream<g2f> stream)
            {
                uint index = input[0].instanceId;
                AABB trail = _ResultBuffer[index];
                float3 minPos = trail.minPos;
                float3 maxPos = trail.maxPos;
                
                g2f o;

                // 底面
                o.vertex = UnityObjectToClipPos(minPos);
                stream.Append(o);
                
                o.vertex = UnityObjectToClipPos(float3(minPos.x, minPos.y, maxPos.z));
                stream.Append(o);
                
                o.vertex = UnityObjectToClipPos(float3(maxPos.x, minPos.y, maxPos.z));
                stream.Append(o);
                
                o.vertex = UnityObjectToClipPos(float3(maxPos.x, minPos.y, minPos.z));
                stream.Append(o);
                
                o.vertex = UnityObjectToClipPos(minPos);
                stream.Append(o);

                // 上面
                o.vertex = UnityObjectToClipPos(float3(minPos.x, maxPos.y, minPos.z));
                stream.Append(o);

                o.vertex = UnityObjectToClipPos(float3(minPos.x, maxPos.y, maxPos.z));
                stream.Append(o);

                o.vertex = UnityObjectToClipPos(maxPos);
                stream.Append(o);

                 o.vertex = UnityObjectToClipPos(float3(maxPos.x, maxPos.y, minPos.z));
                stream.Append(o);

                o.vertex = UnityObjectToClipPos(float3(minPos.x, maxPos.y, minPos.z));
                stream.Append(o);

                stream.RestartStrip();
                
                // 間の辺
                o.vertex = UnityObjectToClipPos(float3(maxPos.x, minPos.y, minPos.z));
                stream.Append(o);
                o.vertex = UnityObjectToClipPos(float3(maxPos.x, maxPos.y, minPos.z));
                stream.Append(o);
                stream.RestartStrip();
                
                o.vertex = UnityObjectToClipPos(float3(maxPos.x, minPos.y, maxPos.z));
                stream.Append(o);
                o.vertex = UnityObjectToClipPos(float3(maxPos.x, maxPos.y, maxPos.z));
                stream.Append(o);
                stream.RestartStrip();
                
                o.vertex = UnityObjectToClipPos(float3(minPos.x, minPos.y, maxPos.z));
                stream.Append(o);
                o.vertex = UnityObjectToClipPos(float3(minPos.x, maxPos.y, maxPos.z));
                stream.Append(o);
                stream.RestartStrip();
                
            }
            
            fixed4 frag (g2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
