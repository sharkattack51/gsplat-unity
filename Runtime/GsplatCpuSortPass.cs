using System;
using UnityEngine;

namespace Gsplat
{
    public class GsplatCpuSortPass : IDisposable
    {
        private GsplatCpuSorterResource _sorterResource;

        public GraphicsBuffer OrderBuffer => _sorterResource?.OrderBuffer;


        public GsplatCpuSortPass(int splatCount)
        {
            _sorterResource = new GsplatCpuSorterResource(splatCount);
        }

        ~GsplatCpuSortPass()
        {
            Dispose();
        }


        public void Dispose()
        {
            _sorterResource?.Dispose();
            _sorterResource = null;
        }


        // CommandBufferのコンテキストでCPUソートを実行しOrderBufferを更新
        public void RecordSort(GraphicsBuffer positionBuffer, Camera camera)
        {
            if(positionBuffer == null || camera == null)
                return;

            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            _sorterResource.Sort(positionBuffer, viewMatrix);
        }

        public void UploadSort(GraphicsBuffer targetBuffer)
        {
            _sorterResource.Upload(targetBuffer);
        }
    }
}
