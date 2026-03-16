using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    [RequireComponent(typeof(GsplatRenderer))]
    public class GsplatGpuValidator : MonoBehaviour
    {
        private GsplatRenderer gsplatRend;
        private bool gpuSortValidated = false;
        private bool validationRequested = false;
        private int  frameCount = 0;

        public Action OnFallbackToCpu;

        private const int k_ValidationFrameDelay = 3; // GPU Sortが安定するまで待つフレーム数


        void Awake()
        {
            gsplatRend = GetComponent<GsplatRenderer>();
        }

        void Start()
        {

        }

        void LateUpdate()
        {
            TryValidateGpuSort();
        }


        private void TryValidateGpuSort()
        {
            if(gpuSortValidated)
            {
                Debug.Log("GPU sort validated.");
                GsplatGpuValidator.Destroy(this);
                return;
            }

            frameCount++;

            // GPU Sort初回実行されるまで数フレーム待ってからReadbackをリクエスト
            if((frameCount >= k_ValidationFrameDelay) && !validationRequested)
            {
                if(gsplatRend.Renderer == null || gsplatRend.Renderer.OrderBuffer == null)
                    return;

                GraphicsBuffer orderBuffer = gsplatRend.Renderer.OrderBuffer;
                if(orderBuffer == null)
                    return;

                validationRequested = true;

                if(GsplatSorter.Instance.Valid)
                {
                    AsyncGPUReadback.Request(orderBuffer, req => {
                        if(req.hasError)
                        {
                            Debug.LogWarning("order buffer readback error.");
                            FallbackToCpu();
                            return;
                        }

                        NativeArray<uint> data = req.GetData<uint>();
                        if(IsOrderBufferUnsorted(data))
                        {
                            Debug.LogWarning("GPU sort id not runnnig.");
                            FallbackToCpu();
                        }

                        gpuSortValidated = true;
                    });
                }
                else
                {
                    Debug.LogWarning("GPU sorter invalid.");
                    FallbackToCpu();
                }
            }
        }

        private bool IsOrderBufferUnsorted(NativeArray<uint> data)
        {
            if(data.Length < 2)
                return false;

            // 先頭要素が 0, 1, 2, ... の連番なら未ソートと判断
            const int sampleCount = 32;
            int checkCount = Mathf.Min(sampleCount, data.Length);
            int sequentialCount = 0;
            for(int i = 0; i < checkCount; i++)
            {
                if(data[i] == (uint)i)
                    sequentialCount++;
            }

            // サンプルの80%以上が連番で未ソートと判定
            return sequentialCount >= checkCount * 0.8f;
        }

        private void FallbackToCpu()
        {
            Debug.LogWarning("fallback to CPU sort.");

            gsplatRend.gameObject.AddComponent<GsplatCpuRenderer>();
            OnFallbackToCpu?.Invoke();
        }
    }
}
