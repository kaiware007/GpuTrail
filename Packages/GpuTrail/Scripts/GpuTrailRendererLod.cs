using System;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Pool;
using UnityEngine.XR;

namespace GpuTrailSystem
{
    public class GpuTrailRendererLod : IDisposable
    {
        #region Static

        public static class CsParam
        {
            public const string KernelUpdateVertex = "UpdateVertex";

            public static readonly int Time = Shader.PropertyToID("_Time");
            public static readonly int ToCameraDir = Shader.PropertyToID("_ToCameraDir");
            public static readonly int CameraPos = Shader.PropertyToID("_CameraPos");
            public static readonly int StartWidth = Shader.PropertyToID("_StartWidth");
            public static readonly int EndWidth = Shader.PropertyToID("_EndWidth");
            public static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");
            public static readonly int LodNodeStep = Shader.PropertyToID("_LodNodeStep");
            public static readonly int FrameCount = Shader.PropertyToID("_FrameCount");
            public static readonly int ForceUpdate = Shader.PropertyToID("_ForceUpdate");
            
            public const string KernelArgsBufferMultiply = "ArgsBufferMultiply";
            public static readonly int ArgsBuffer = Shader.PropertyToID("_ArgsBuffer");
        }

        public static class ShaderParam
        {
            public static readonly int StartWidth = Shader.PropertyToID("_StartWidth");
            public static readonly int EndWidth = Shader.PropertyToID("_EndWidth");
            public static readonly int VertexNumPerTrail = Shader.PropertyToID("_VertexNumPerTrail");
            public static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");

            public static readonly int TrailIndexBuffer = Shader.PropertyToID("_TrailIndexBuffer");
        }

        #endregion


        protected readonly GpuTrail gpuTrail;
        protected readonly ComputeShader computeShader;
        protected readonly GpuTrailRenderer.LodSetting lodSetting;
        protected readonly GpuTrailIndexDispatcher gpuTrailIndexDispatcher = new();

        protected GraphicsBuffer vertexBuffer;
        protected GraphicsBuffer indexBuffer;
        protected GraphicsBuffer argsBuffer;
        protected GraphicsBuffer trailIndexBuffer;

        private int LodNodeStep => lodSetting.lodNodeStep;

        public MaterialPropertyBlock PropertyBlock { get; } = new();
        
        public int NodeNumPerTrailWithLod => gpuTrail.NodeNumPerTrail / LodNodeStep;
        public int VertexNumPerTrail => NodeNumPerTrailWithLod * 2;
        public int VertexBufferSize => gpuTrail.trailNum * VertexNumPerTrail;
        public int IndexNumPerTrail => (NodeNumPerTrailWithLod - 1) * 6;

        public GpuTrailRendererLod(GpuTrail gpuTrail, ComputeShader computeShader, GpuTrailRenderer.LodSetting lodSetting)
        {
            this.gpuTrail = gpuTrail;
            this.computeShader = computeShader;
            this.lodSetting = lodSetting;
        }


        public void Dispose()
        {
            ReleaseBuffers();
            gpuTrailIndexDispatcher.Dispose();
        }


        protected void InitBufferIfNeed()
        {
            if ((vertexBuffer != null) && (vertexBuffer.count == VertexBufferSize))
            {
                return;
            }

            Assert.IsTrue(0 < LodNodeStep && LodNodeStep < gpuTrail.NodeNumPerTrail, $"Invalid lodNodeStep[{LodNodeStep}]");


            ReleaseBuffers();

            vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VertexBufferSize, Marshal.SizeOf<Vertex>()); // 1 node to 2 vtx(left,right)
            vertexBuffer.Fill(default(Vertex));

            
            indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Structured, IndexNumPerTrail, Marshal.SizeOf<uint>()); // 1 node to 2 triangles(6vertexs)
            
            
#if UNITY_2022_2_OR_NEWER
            using var indexArray = new NativeArray<int>(indexBuffer.count, Allocator.Temp);
            var indices = indexArray.AsSpan();
#else
            var indices = new NativeArray<int>(IndexNumPerTrail, Allocator.Temp);
#endif
            // 各Nodeの最後と次のNodeの最初はポリゴンを繋がないので-1
            var idx = 0;
            for (var iNode = 0; iNode < NodeNumPerTrailWithLod - 1; ++iNode)
            {
                var offset = iNode * 2;
                indices[idx++] = 0 + offset;
                indices[idx++] = 1 + offset;
                indices[idx++] = 2 + offset;
                indices[idx++] = 2 + offset;
                indices[idx++] = 1 + offset;
                indices[idx++] = 3 + offset;
            }

#if UNITY_2022_2_OR_NEWER
            indexBuffer.SetData(indexArray);
#else
            indexBuffer.SetData(indices);
            indices.Dispose();
#endif


            argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, sizeof(uint));
            ResetArgsBuffer();
        }

        protected void ReleaseBuffers()
        {
            vertexBuffer?.Release();
            indexBuffer?.Release();
            argsBuffer?.Release();

            vertexBuffer = null;
            indexBuffer = null;
            argsBuffer = null;
        }

        public void UpdateVertexBuffer(Camera camera, float startWidth, float endWidth, bool forceUpdate, GraphicsBuffer trailIndexBuffer)
        {
            InitBufferIfNeed();

            var toCameraDir = default(Vector3);
            if (camera.orthographic)
            {
                toCameraDir = -camera.transform.forward;
            }

            computeShader.SetFloat(CsParam.Time, Time.time);

            computeShader.SetVector(CsParam.ToCameraDir, toCameraDir);
            computeShader.SetVector(CsParam.CameraPos, camera.transform.position);

            computeShader.SetFloat(CsParam.StartWidth, startWidth);
            computeShader.SetFloat(CsParam.EndWidth, endWidth);
            computeShader.SetInt(CsParam.LodNodeStep, LodNodeStep);
            computeShader.SetInt(CsParam.FrameCount, Time.frameCount);
            computeShader.SetBool(CsParam.ForceUpdate, forceUpdate);
            
            var kernel = computeShader.FindKernel(CsParam.KernelUpdateVertex);
            gpuTrail.SetCSParams(computeShader, kernel);
            computeShader.SetBuffer(kernel, CsParam.VertexBuffer, vertexBuffer);

            if (trailIndexBuffer != null)
            {
                gpuTrailIndexDispatcher.Dispatch(computeShader, kernel, trailIndexBuffer);
                this.trailIndexBuffer = trailIndexBuffer;
            }
            else
            {
                gpuTrailIndexDispatcher.Dispatch(computeShader, kernel, gpuTrail.trailNum);
                this.trailIndexBuffer = null;
            }



#if false
            var trails = new Trail[gpuTrail.trailNum];
            gpuTrail.TrailBuffer.GetData(trails);
            var str = String.Join(String.Empty, trails.Take(10).Select(t => $"[{t.frameCount}, {t.selectedLod}],").ToArray());
            Debug.Log($"{camera.name} LodNodeStep {LodNodeStep} forceUpdate {forceUpdate} frameCount {Time.frameCount} {str}");
#endif
#if false
            var lastNodeIdx = trails[0].totalInputNum % gpuTrail.NodeNumPerTrail;
            
            var nodes = new Node[gpuTrail.NodeBuffer.count];
            gpuTrail.NodeBuffer.GetData(nodes);
            //nodes = nodes.Take(100).ToArray();
            var idxAndNodes = Enumerable.Range(0, nodes.Length)
                .Zip(nodes, (i, node) => new { i, node })
                .OrderByDescending(iNode => iNode.node.time)
                .ToList();
                
            var vtxs = new Vertex[vertexBuffer.count];
            vertexBuffer.GetData(vtxs);
            //vtxs = vtxs.Take(100).ToArray();
            vtxs = vtxs.ToArray();
            for (var i = 0; i < vtxs.Length; ++i)
            {
                if (vtxs[i].pos == Vector3.zero)
                {
                    Debug.Log(i);
                }
            }
#endif
        }


        // SinglePassInstanced requires you to manually double the number of instances
        // https://docs.unity3d.com/Manual/SinglePassInstancing.html
        protected bool IsSinglePassInstancedRendering => XRSettings.enabled && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced;

        public void UpdateArgsBuffer(GraphicsBuffer trailIndexBuffer)
        {
            InitBufferIfNeed();

            GraphicsBuffer.CopyCount(trailIndexBuffer, argsBuffer, 4);

            if (IsSinglePassInstancedRendering)
            {
                var kernelArgsBufferMultiply = computeShader.FindKernel(CsParam.KernelArgsBufferMultiply);
                computeShader.SetBuffer(kernelArgsBufferMultiply, CsParam.ArgsBuffer, argsBuffer);

                computeShader.Dispatch(kernelArgsBufferMultiply, 1, 1, 1);
            }
            
#if false
            var data = new int[4];
            argsBuffer.GetData(data);
            Debug.Log($"UpdateArgsBuffer LodNodeStep {LodNodeStep} {data[0]} {data[1]} {data[2]} {data[3]}");
#endif
        }

        public void ResetArgsBuffer()
        {
            InitBufferIfNeed();

            using var _ = ListPool<int>.Get(out var argsList);

            argsList.Add(IndexNumPerTrail);
            argsList.Add(gpuTrail.trailNum * (IsSinglePassInstancedRendering ? 2 : 1));
            argsList.Add(0);
            argsList.Add(0);
            argsList.Add(0);

            argsBuffer.SetData(argsList);
        }


        public void Render(Material material, float startWidth, float endWidth, in Bounds bounds, Camera camera)
        {
            PropertyBlock.SetFloat(ShaderParam.StartWidth, startWidth);
            PropertyBlock.SetFloat(ShaderParam.EndWidth, endWidth);
            PropertyBlock.SetInt(ShaderParam.VertexNumPerTrail, VertexNumPerTrail);
            PropertyBlock.SetBuffer(ShaderParam.VertexBuffer, vertexBuffer);
            if (trailIndexBuffer != null)
            {
                PropertyBlock.SetBuffer(ShaderParam.TrailIndexBuffer, trailIndexBuffer);
            }
            
            var renderParams = new RenderParams(material)
            {
                matProps = PropertyBlock,
                worldBounds = bounds
            };
            if (camera != null)
            {
                renderParams.camera = camera;
            }
            Graphics.RenderPrimitivesIndexedIndirect(renderParams, MeshTopology.Triangles, indexBuffer, argsBuffer);
        }


        #region Debug

        public bool debugDrawVertexBuf = false;

        public void OnDrawGizmosSelected()
        {
            if (debugDrawVertexBuf)
            {
                var defaultColor = Color.yellow;
                Gizmos.color = defaultColor;

                var data = new Vertex[vertexBuffer.count];
                vertexBuffer.GetData(data);

                var num = vertexBuffer.count / 2;
                for (var i = 0; i < num; ++i)
                {
                    Color? tmpColor = null;
                    if (i == 0) { tmpColor = Color.red; }
                    if (i == num - 1) { tmpColor = Color.green; }

                    if (tmpColor.HasValue)
                    {
                        Gizmos.color = tmpColor.Value;

                    }

                    var v0 = data[2 * i];
                    var v1 = data[2 * i + 1];

                    Gizmos.DrawLine(v0.pos, v1.pos);

                    if (tmpColor.HasValue)
                    {
                        Gizmos.color = defaultColor;
                    }
                }
            }
        }

        #endregion
    }
}