using UnityEngine;

namespace Gsplat
{
    [DefaultExecutionOrder(32000)]
    [RequireComponent(typeof(GsplatRenderer))]
    public class GsplatCpuRenderer : MonoBehaviour
    {
        public Camera renderCam;

        private GsplatRenderer gsplatRend;
        private GsplatCpuSortPass cpuSortPass;


        void Awake()
        {
            gsplatRend = GetComponent<GsplatRenderer>();
        }

        void OnEnable()
        {
            Setup();
        }

        void OnDisable()
        {
            Dispose();
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

            // CPU ソートが引き継ぐため GPU ソートを解除
            GsplatSorter.Instance.UnregisterGsplat(gsplatRend);
        }

        private void DoCpuSort()
        {
            if(cpuSortPass == null)
            {
                Setup();
                if(cpuSortPass == null)
                    return;
            }

            if(gsplatRend == null
                || gsplatRend.SorterResource == null
                || gsplatRend.SorterResource.PositionBuffer == null
                || gsplatRend.Renderer == null
                || gsplatRend.Renderer.OrderBuffer == null
                || renderCam == null)
                return;

            // CPUソートを実行 PositionBuffer->_orderData の更新
            cpuSortPass.RecordSort(gsplatRend.SorterResource.PositionBuffer, renderCam);

            // ソート結果を renderer.OrderBuffer に upload
            // LateUpdate(33000) の DrawMeshInstancedIndirect より前に GPU へ送ることで正しい描画順を保証
            cpuSortPass.UploadSort(gsplatRend.Renderer.OrderBuffer);
        }
    }
}
