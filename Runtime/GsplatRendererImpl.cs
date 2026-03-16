// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public class GsplatRendererImpl
    {
        public uint SplatCount { get; private set; }
        public byte SHBands { get; private set; }

        MaterialPropertyBlock m_propertyBlock;
        public MaterialPropertyBlock PropertyBlock { get{ return m_propertyBlock; } }
        public GraphicsBuffer PositionBuffer { get; private set; }
        public GraphicsBuffer ScaleBuffer { get; private set; }
        public GraphicsBuffer RotationBuffer { get; private set; }
        public GraphicsBuffer ColorBuffer { get; private set; }
        public GraphicsBuffer SHBuffer { get; private set; }
        public GraphicsBuffer OrderBuffer { get; private set; }
        public GraphicsBuffer DX11ArgsBuffer { get; private set; }
        public ISorterResource SorterResource { get; private set; }

        public bool Valid =>
            PositionBuffer != null &&
            ScaleBuffer != null &&
            RotationBuffer != null &&
            ColorBuffer != null &&
            (SHBands == 0 || SHBuffer != null);

        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");
        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_scaleBuffer = Shader.PropertyToID("_ScaleBuffer");
        static readonly int k_rotationBuffer = Shader.PropertyToID("_RotationBuffer");
        static readonly int k_colorBuffer = Shader.PropertyToID("_ColorBuffer");
        static readonly int k_shBuffer = Shader.PropertyToID("_SHBuffer");
        static readonly int k_matrixM = Shader.PropertyToID("_MATRIX_M");
        static readonly int k_splatInstanceSize = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_gammaToLinear = Shader.PropertyToID("_GammaToLinear");
        static readonly int k_shDegree = Shader.PropertyToID("_SHDegree");

        public GsplatRendererImpl(uint splatCount, byte shBands)
        {
            SplatCount = splatCount;
            SHBands = shBands;
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        public void RecreateResources(uint splatCount, byte shBands)
        {
            if (SplatCount == splatCount && SHBands == shBands)
                return;
            Dispose();
            SplatCount = splatCount;
            SHBands = shBands;
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        void CreateResources(uint splatCount)
        {
            PositionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            ScaleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            RotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector4)));
            ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector4)));
            if (SHBands > 0)
                SHBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    GsplatUtils.SHBandsToCoefficientCount(SHBands) * (int)splatCount,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));

            OrderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount, sizeof(uint));
            // 初回フォールバック用に連番で初期化（CPU ソートが引き継ぐまでの保険）
            var initIndices = new uint[splatCount];
            for (int i = 0; i < splatCount; i++) initIndices[i] = (uint)i;
            OrderBuffer.SetData(initIndices);

            DX11ArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, sizeof(uint));

            SorterResource = GsplatSorter.Instance.CreateSorterResource(splatCount, PositionBuffer, OrderBuffer);
        }

        void CreatePropertyBlock()
        {
            m_propertyBlock ??= new MaterialPropertyBlock();
            m_propertyBlock.SetBuffer(k_orderBuffer, OrderBuffer);
            m_propertyBlock.SetBuffer(k_positionBuffer, PositionBuffer);
            m_propertyBlock.SetBuffer(k_scaleBuffer, ScaleBuffer);
            m_propertyBlock.SetBuffer(k_rotationBuffer, RotationBuffer);
            m_propertyBlock.SetBuffer(k_colorBuffer, ColorBuffer);
            if (SHBands > 0)
                m_propertyBlock.SetBuffer(k_shBuffer, SHBuffer);
        }

        public void Dispose()
        {
            PositionBuffer?.Dispose();
            ScaleBuffer?.Dispose();
            RotationBuffer?.Dispose();
            ColorBuffer?.Dispose();
            SHBuffer?.Dispose();
            OrderBuffer?.Dispose();
            DX11ArgsBuffer?.Dispose();
            SorterResource?.Dispose();

            PositionBuffer = null;
            ScaleBuffer = null;
            RotationBuffer = null;
            ColorBuffer = null;
            SHBuffer = null;
            OrderBuffer = null;
            DX11ArgsBuffer = null;
        }

        /// <summary>
        /// Render the splats.
        /// </summary>
        /// <param name="splatCount">It can be less than or equal to the SplatCount property.</param> 
        /// <param name="transform">Object transform.</param>
        /// <param name="localBounds">Bounding box in object space.</param>
        /// <param name="layer">Layer used for rendering.</param>
        /// <param name="gammaToLinear">Covert color space from Gamma to Linear.</param>
        /// <param name="shDegree">Order of SH coefficients used for rendering. The final value is capped by the SHBands property.</param>
        public void Render(uint splatCount, Transform transform, Bounds localBounds, int layer,
            bool gammaToLinear = false, int shDegree = 3)
        {
            if (!Valid || !GsplatSettings.Instance.Valid || (GsplatUtils.IsRuntimeDX11() ? false : !GsplatSorter.Instance.Valid))
                return;

            m_propertyBlock.SetInteger(k_splatCount, (int)splatCount);
            m_propertyBlock.SetInteger(k_gammaToLinear, gammaToLinear ? 1 : 0);
            m_propertyBlock.SetInteger(k_splatInstanceSize, (int)GsplatSettings.Instance.SplatInstanceSize);
            m_propertyBlock.SetInteger(k_shDegree, shDegree);
            m_propertyBlock.SetMatrix(k_matrixM, transform.localToWorldMatrix);

            if(GsplatUtils.IsRuntimeDX11())
            {
                var args = new uint[5];
                args[0] = (uint)GsplatSettings.Instance.Mesh.GetIndexCount(0);
                args[1] = (splatCount + (uint)GsplatSettings.Instance.SplatInstanceSize - 1) / (uint)GsplatSettings.Instance.SplatInstanceSize;
                args[2] = (uint)GsplatSettings.Instance.Mesh.GetIndexStart(0);
                args[3] = (uint)GsplatSettings.Instance.Mesh.GetBaseVertex(0);
                args[4] = 0;
                DX11ArgsBuffer.SetData(args);

                Graphics.DrawMeshInstancedIndirect(
                    GsplatSettings.Instance.Mesh, 0, GsplatSettings.Instance.Materials[SHBands],
                    new Bounds(transform.position, localBounds.size * 2f),
                    DX11ArgsBuffer, 0,
                    m_propertyBlock, ShadowCastingMode.Off, false, layer);
            }
            else
            {
                var rp = new RenderParams(GsplatSettings.Instance.Materials[SHBands])
                {
                    worldBounds = GsplatUtils.CalcWorldBounds(localBounds, transform),
                    matProps = m_propertyBlock,
                    layer = layer
                };
                

                Graphics.RenderMeshPrimitives(rp, GsplatSettings.Instance.Mesh, 0,
                    Mathf.CeilToInt(splatCount / (float)GsplatSettings.Instance.SplatInstanceSize));
            }
        }
    }
}