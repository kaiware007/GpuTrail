using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GpuTrailSystem
{
    public class GpuTrailCalcBounds : IDisposable
    {
        public static class CsParam
        {
            public const string KernelInitBounds = "InitBounds";
            public const string KernelCalcBounds = "CalcBounds";
            public const string KernelCopyBounds = "CopyBounds";
            
            public static readonly int TrailBuffer = Shader.PropertyToID("_TrailBuffer");
            public static readonly int NodeBuffer = Shader.PropertyToID("_NodeBuffer");
            public static readonly int AABBBuffer = Shader.PropertyToID("_AABBBuffer");
            public static readonly int ResultBuffer = Shader.PropertyToID("_ResultBuffer");
            public static readonly int ResultBufferRW = Shader.PropertyToID("_ResultBufferRW");
            public static readonly int Step = Shader.PropertyToID("_Step");
            public static readonly int BufferLength = Shader.PropertyToID("_BufferLength");
            public static readonly int TrailWidth = Shader.PropertyToID("_TrailWidth");
            public static readonly int TrailNum = Shader.PropertyToID("_TrailNum");
            public static readonly int NodeNumPerTrail = Shader.PropertyToID("_NodeNumPerTrail");
        }
        
        public struct PositionData
        {
            public Vector3 MinPos;
            public Vector3 MaxPos;
        }
        
        private ComputeShader _computeShader;
        private GraphicsBuffer _posBuffer;
        private GraphicsBuffer _resultBuffer;
        
        public GpuTrailCalcBounds(ComputeShader computeShader)
        {
            _computeShader = computeShader;
        }

        public GraphicsBuffer CalcBounds(GraphicsBuffer trailBuffer, GraphicsBuffer nodeBuffer, int nodeNumPerTrail, float trailWidth)
        {
            Initialize(trailBuffer, nodeBuffer, nodeNumPerTrail);
            
            var kernel = _computeShader.FindKernel(CsParam.KernelCalcBounds);
            _computeShader.SetBuffer(kernel, CsParam.AABBBuffer, _posBuffer);
            _computeShader.GetKernelThreadGroupSizes(kernel, out var x, out var y, out var z);
        
            var loopCount = Mathf.CeilToInt(Mathf.Log(nodeNumPerTrail,2));
            // Debug.Log($"loopCount: {loopCount}");
            var len = nodeNumPerTrail;
            var trailNum = trailBuffer.count;
            for (int i = 0; i < loopCount; i++)
            {
                var threadGroupX = Mathf.CeilToInt(len / 2f * trailNum / x);
                var nextStep = Mathf.CeilToInt(len / 2f);
                // Debug.Log($"Loop [{i}] threadGroupX: {threadGroupX} nextStep: {nextStep} len {len}");
                _computeShader.SetInt(CsParam.Step, nextStep);
                _computeShader.SetInt(CsParam.BufferLength, len);
                _computeShader.Dispatch(kernel, threadGroupX, 1, 1);
                len = Mathf.CeilToInt(len / 2f);
            }

            CopyBounds(trailWidth);
            
            return _resultBuffer;
        }

        public void DrawBounds(Material material)
        {
            material.SetBuffer(CsParam.ResultBuffer, _resultBuffer);
            var renderParam = new RenderParams(material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 1000f),
            };
            
            Graphics.RenderPrimitives(renderParam, MeshTopology.Points, 1, _resultBuffer.count);
        }
        
        private void CopyBounds(float trailWidth)
        {
            var kernel = _computeShader.FindKernel(CsParam.KernelCopyBounds);
            _computeShader.SetBuffer(kernel, CsParam.ResultBufferRW, _resultBuffer);
            _computeShader.SetBuffer(kernel, CsParam.AABBBuffer, _posBuffer);
            _computeShader.SetFloat(CsParam.TrailWidth, trailWidth);
            _computeShader.GetKernelThreadGroupSizes(kernel, out var x, out var y, out var z);
            _computeShader.Dispatch(kernel, Mathf.CeilToInt((float)_resultBuffer.count / x), 1, 1);
#if false
            var trailData = new PositionData[_resultBuffer.count];
            _resultBuffer.GetData(trailData);
            for (int i = 0; i < _resultBuffer.count; i++)
            {
                Debug.Log($"[{i}] minPos {trailData[i].MinPos} maxPos {trailData[i].MaxPos}");
            }
#endif
        }
        
        private void Initialize(GraphicsBuffer trailBuffer, GraphicsBuffer nodeBuffer, int nodeNumPerTrail)
        {
            if(_posBuffer == null || _posBuffer.count != nodeBuffer.count)
            {
                _posBuffer?.Dispose();
                _posBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nodeBuffer.count, Marshal.SizeOf<PositionData>());
            }
            if(_resultBuffer == null || _resultBuffer.count != trailBuffer.count)
            {
                _resultBuffer?.Dispose();
                _resultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, trailBuffer.count, Marshal.SizeOf<PositionData>());
            }

            var kernel = _computeShader.FindKernel(CsParam.KernelInitBounds);
            _computeShader.SetInt(CsParam.BufferLength, nodeBuffer.count);
            _computeShader.SetInt(CsParam.TrailNum, trailBuffer.count);
            _computeShader.SetInt(CsParam.NodeNumPerTrail, nodeNumPerTrail);
            _computeShader.SetBuffer(kernel, CsParam.TrailBuffer, trailBuffer);
            _computeShader.SetBuffer(kernel, CsParam.NodeBuffer, nodeBuffer);
            _computeShader.SetBuffer(kernel, CsParam.AABBBuffer, _posBuffer);
            _computeShader.GetKernelThreadGroupSizes(kernel, out var x, out var y, out var z);
            var threadGroupX = Mathf.CeilToInt((float)nodeBuffer.count / x);
            _computeShader.Dispatch(kernel, threadGroupX, 1, 1);
        }
        
        public void Dispose()
        {
            _posBuffer?.Dispose();
            _resultBuffer?.Dispose();
        }
    }
}