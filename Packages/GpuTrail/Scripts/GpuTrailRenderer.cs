﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace GpuTrailSystem
{
    /// <summary>
    /// Rendering GpuTrail
    /// 
    /// Processing flow:
    ///  GpuTrailAppendNode.UpdateInputBuffer() -(InputBuffer)-> GpuTrailAppendNode.AppendNode() -(TrailBuffer,NodeBuffer)-> 
    ///  [GpuTrailCulling -(TrailIndexBuffer)->] [GpuTrailRendering_CalcLod -(TrailIndexBufers)->] 
    ///  GpuTrailRendering_Lod.UpdateVertexBuffer()/UpdateArgsBuffer() -(VertexBuffer, ArgsBuffer) -> GpuTrailRendering_Lod.OnRenderObject()
    /// </summary>
    [RequireComponent(typeof(IGpuTrailAppendNode))]
    public class GpuTrailRenderer : MonoBehaviour
    {
        #region Type Define

        [Serializable]
        public class LodSetting
        {
            public bool enable = true;

            [Tooltip("The distance where this Lod starts.\nThe smallest Lod will be treated as 0.")]
            public float startDistance = 0f;

            [Tooltip("The node steps to generate a vertex.\n1: all nodes, 2: 1/2 nodes, 3: 1/3 nodes...")]
            public int lodNodeStep = 1; // Node steps to generate a vertex.　1:all nodes, 2:1/2 nodes, 3:1/3 nodes...

            [Tooltip("The lod specific material.\nIf null then GpuTrailRenderer.defaultMaterial would be used.")]
            public Material material;
        }

        #endregion


        public ComputeShader trailIndexDispatcherCS;
        public ComputeShader calcLodCS;
        public ComputeShader cullingCS;
        public ComputeShader updateVertexCS;

        public Material defaultMaterial;
        public float startWidth = 0.1f;
        public float endWidth = 0.1f;

        public Camera targetCamera;
        
        public Bounds bounds = new(Vector3.zero, Vector3.one * 100000f);

        protected IGpuTrailAppendNode gpuTrailAppendNode;

        // Culling/CalcLod function can be customized.
        public Func<Camera, GpuTrail, float, GraphicsBuffer> calcTrailIndexBufferCulling;
        public Func<IEnumerable<float>, Camera, GpuTrail, GraphicsBuffer, IReadOnlyList<GraphicsBuffer>> calcTrailIndexBufferCalcLod;

        protected GpuTrailRendererCulling defaultCulling;
        protected GpuTrailRendererCalcLod defaultCalcLod;
        
        [SerializeField]
        protected List<LodSetting> lodSettings = new();
        protected List<GpuTrailRendererLod> lodList = new();

        [Header("Debug")]
        public bool appendNodeEnable = true;
        public bool cullingEnable = true;
        public bool updateVertexEnable = true;
        public bool renderingEnable = true;

        protected GpuTrail GpuTrail => gpuTrailAppendNode.GpuTrail;
        protected virtual Camera TargetCamera => targetCamera != null ? targetCamera : Camera.main;

        protected bool IsNoCulling => (targetCamera == null  && !cullingEnable);
        
        #region Unity

        protected virtual void Start()
        {
            if (trailIndexDispatcherCS != null)
            {
                GpuTrailIndexDispatcher.Init(trailIndexDispatcherCS);
            }

            gpuTrailAppendNode ??= GetComponent<IGpuTrailAppendNode>();

            if (!lodSettings.Any()) lodSettings.Add(new LodSetting());
            
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            }
        }


        protected virtual void LateUpdate()
        {
            if (lodSettings.Count != lodList.Count) ResetLodList();

            // AppendNode
            if (appendNodeEnable)
            {
                gpuTrailAppendNode.AppendNode();
            }

            if (IsNoCulling)
            {
                UpdateVertex(TargetCamera);
                Render(TargetCamera);
            }
        }

        public virtual void OnDestroy()
        {
            DisposeLodList();
            defaultCulling?.Dispose();
            defaultCalcLod?.Dispose();
            
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            }
        }

        #endregion

        protected virtual void UpdateVertex(Camera camera)
        {
            // Culling
            GraphicsBuffer trailIndexBufferCulling = null;
            if (cullingEnable)
            {
                if (calcTrailIndexBufferCulling == null)
                {
                    defaultCulling = new GpuTrailRendererCulling(cullingCS);
                    calcTrailIndexBufferCulling = defaultCulling.CalcTrailIndexBuffer;
                }

                Profiler.BeginSample("GpuTrailRenderer.CalcTrailIndexBufferCulling");
                float width = Mathf.Max(startWidth, endWidth);
                trailIndexBufferCulling = calcTrailIndexBufferCulling(camera, GpuTrail, width);
                Profiler.EndSample();
            }

            // CalcLod
            IReadOnlyList<GraphicsBuffer> trailIndexBuffersLod = null;
            bool needCalcLod = lodSettings.Count > 1;
            if (needCalcLod)
            {
                if (calcTrailIndexBufferCalcLod == null)
                {
                    defaultCalcLod = new GpuTrailRendererCalcLod(calcLodCS);
                    calcTrailIndexBufferCalcLod = defaultCalcLod.CalcTrailIndexBuffers;
                }

                Profiler.BeginSample("GpuTrailRenderer.CalcTrailIndexBufferCalcLod");
                trailIndexBuffersLod = calcTrailIndexBufferCalcLod(lodSettings.Select(setting => setting.startDistance), camera, GpuTrail, trailIndexBufferCulling);
                Profiler.EndSample();
            }
            
            // UpdateVertex
            if (updateVertexEnable)
            {
                Profiler.BeginSample("GpuTrailRenderer.UpdateVertexBuffer");
                ForeachLod((lod, idx) =>
                {
                    var trailIndexBuffer = trailIndexBuffersLod?[idx] ?? trailIndexBufferCulling;
                    lod.UpdateVertexBuffer(camera, startWidth, endWidth, trailIndexBuffer);
                });
                Profiler.EndSample();
            }
            

            Profiler.BeginSample("GpuTrailRenderer.UpdateArgsBuffer");
            // UpdateArgsBuffer
            ForeachLod((lod, idx) =>
            {
                var trailIndexBuffer = trailIndexBuffersLod?[idx] ?? trailIndexBufferCulling;
                if (trailIndexBuffer != null)
                {
                    lod.UpdateArgsBuffer(trailIndexBuffer);
                }
                else
                {
                    lod.ResetArgsBuffer();
                }
            });
            Profiler.EndSample();
        }

        protected virtual void Render(Camera camera)
        {
            // Rendering
            if (renderingEnable)
            {
                ForeachLod((lod, idx) =>
                {
                    var settings = lodSettings[idx];

                    var material = settings.material;
                    if (material == null) material = defaultMaterial;

                    lod.Render(material, startWidth, endWidth, bounds, camera);
                });
            }
        }
        
        void ForeachLod(Action<GpuTrailRendererLod, int> action)
        {
            for (var i = 0; i < lodList.Count; ++i)
            {
                if (lodSettings[i].enable)
                {
                    action(lodList[i], i);
                }
            }
        }

        protected void ResetLodList()
        {
            DisposeLodList();

            lodList = lodSettings.Select(settings => new GpuTrailRendererLod(GpuTrail, updateVertexCS, settings)).ToList();
        }

        private void DisposeLodList()
        {
            lodList.ForEach(lod => lod.Dispose());
            lodList.Clear();
        }

        /// <summary>
        /// カメラごとのレンダリング前のカリング処理(カリング有効時のみ)
        /// Per-camera pre-render culling 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="camera"></param>
        protected virtual void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (lodSettings.Count != lodList.Count) ResetLodList();

            if(targetCamera != null && targetCamera != camera)
                return;

            var layer = 1 << gameObject.layer;
            if((camera.cullingMask & layer) == 0)
                return;
            
            if (!cullingEnable)
                return;
            
            UpdateVertex(camera);
            Render(camera);
        }

        
        #region Debug

        public void OnDrawGizmosSelected()
        {
            lodList.ForEach(lod => lod.OnDrawGizmosSelected());
        }

        #endregion
    }
}