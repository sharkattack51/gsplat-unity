using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    [DefaultExecutionOrder(32000)]
    [RequireComponent(typeof(GsplatRenderer))]
    public class GsplatCpuRenderer : MonoBehaviour
    {
        public static bool IsGpuSupport()
        {
            if(Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
                return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 && SystemInfo.graphicsShaderLevel >= 60;
            else if(Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor)
                return true;
            else if(Application.platform == RuntimePlatform.Android)
                return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan;
            else if(Application.platform == RuntimePlatform.IPhonePlayer)
                return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;
            else if(Application.platform == RuntimePlatform.WebGLPlayer)
                return false;
            else
                return false;
        }

        private static readonly int k_OrderBufferID = Shader.PropertyToID("_OrderBuffer");

        private GsplatRenderer gsplatRend;
        private GsplatCpuSortPass cpuSortPass;
        private Camera cam;
        private bool unregisterdGpu = false;


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

            unregisterdGpu = false;
        }

        void OnDestroy()
        {
            Dispose();
        }

        void LateUpdate()
        {
            DoCpuSort();
        }


        private void Setup()
        {
            if(gsplatRend == null || gsplatRend.SplatCount <= 0)
                return;

            cam = Camera.main;

            Dispose();
            cpuSortPass = new GsplatCpuSortPass((int)gsplatRend.SplatCount);
        }

        private void Dispose()
        {
            cpuSortPass?.Dispose();
            cpuSortPass = null;
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
                || cam == null)
                return;

            // CPUソートを実行 PositionBuffer->OrderBufferの更新
            cpuSortPass.RecordSort(gsplatRend.SorterResource.PositionBuffer, cam);
        }

        private void OnPreCullCamera(Camera cam)
        {
            if(gsplatRend.Renderer.PropertyBlock == null
                || cpuSortPass.OrderBuffer == null)
                return;

            // OrderBufferをシェーダーのMaterialPropertyBlockに上書き
            gsplatRend.Renderer.PropertyBlock.SetBuffer(k_OrderBufferID, cpuSortPass.OrderBuffer);

            if(!unregisterdGpu)
            {
                GsplatSorter.Instance.UnregisterGsplat(gsplatRend);
                unregisterdGpu = true;
            }
        }
    }
}
