using UnityEngine;

namespace Gsplat
{
    [DefaultExecutionOrder(32000)]
    [RequireComponent(typeof(GsplatRenderer))]
    public class GsplatCpuRenderer : MonoBehaviour
    {
        private static readonly int k_OrderBufferID = Shader.PropertyToID("_OrderBuffer");

        public Camera renderCam;

        private GsplatRenderer gsplatRend;
        private GsplatCpuSortPass cpuSortPass;
        private bool unregisteredGpuSort = false;


        void Awake()
        {
            gsplatRend = GetComponent<GsplatRenderer>();
        }

        void OnEnable()
        {
            Camera.onPreCull += OnPreCullCamera;
            Setup();
        }

        void OnDisable()
        {
            Camera.onPreCull -= OnPreCullCamera;
            Dispose();

            unregisteredGpuSort = false;
        }

        void OnDestroy()
        {
            Dispose();
        }

        void LateUpdate()
        {
            DoCpuSort();
        }


        private void Dispose()
        {
            cpuSortPass?.Dispose();
            cpuSortPass = null;
        }


        private void Setup()
        {
            if(gsplatRend == null || gsplatRend.SplatCount <= 0)
                return;

            if(renderCam == null)
                renderCam = Camera.main;

            Dispose();
            cpuSortPass = new GsplatCpuSortPass((int)gsplatRend.SplatCount);
        }

        private void DoCpuSort()
        {
            if(cpuSortPass == null)
            {
                Setup();
                if(cpuSortPass == null)
                    return;
            }

            if(gsplatRend.SorterResource.PositionBuffer == null
                || renderCam == null)
                return;

            // CPUソートを実行 PositionBuffer->OrderBufferの更新
            cpuSortPass.RecordSort(gsplatRend.SorterResource.PositionBuffer, renderCam);
        }

        private void OnPreCullCamera(Camera cam)
        {
            if(gsplatRend.Renderer.PropertyBlock == null
                || cpuSortPass.OrderBuffer == null)
                return;

            // OrderBufferをシェーダーのMaterialPropertyBlockに上書き
            gsplatRend.Renderer.PropertyBlock.SetBuffer(k_OrderBufferID, cpuSortPass.OrderBuffer);

            if(!unregisteredGpuSort)
            {
                GsplatSorter.Instance.UnregisterGsplat(gsplatRend);
                unregisteredGpuSort = true;
            }
        }
    }
}
