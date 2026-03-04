using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gsplat
{
    public class GsplatCpuSorterResource : ISorterResource, IDisposable
    {
#region radix sort
        private readonly struct UintDescComparer : IComparer<uint>
        {
            public int Compare(uint a, uint b) => b.CompareTo(a);
        }
        private static readonly UintDescComparer s_Comparer = new UintDescComparer();

        private const int k_RadixBits   = 8;
        private const int k_RadixBuckets = 1 << k_RadixBits; // 256
        private const int k_RadixPasses  = 4; // 32bit / 8bit = 4パス
        private const uint k_RadixMask  = k_RadixBuckets - 1; // 0xFF
#endregion

        private GraphicsBuffer _orderBuffer;
        private int _splatCount;
        private bool _disposed = false;

        // CPU側の作業バッファ
        private uint[] _depthKeys; // 奥行きキー
        private uint[] _depthKeysTmp; // Radix Sortのpingpong用バッファ
        private uint[] _indices; // スプラットインデックス
        private uint[] _indicesTmp; // Radix Sortのpingpong用バッファ
        private uint[] _orderData; // SetData用の最終出力バッファ
        private Vector3[] _positionCache; // GPU->CPU転送の受け取りバッファ

        private int[]  _histogram; // ヒストグラム（256要素 × 再利用）
        private int[]  _prefixSum; // プレフィックスサム（256要素 × 再利用）

        public GraphicsBuffer PositionBuffer { get; }
        public GraphicsBuffer OrderBuffer => _orderBuffer;

        public GsplatCpuSorterResource(int splatCount)
        {
            _splatCount = splatCount;

            _depthKeys  = new uint[splatCount];
            _depthKeysTmp = new uint[splatCount];
            _indices = new uint[splatCount];
            _indicesTmp = new uint[splatCount];
            _orderData = new uint[splatCount];
            _positionCache = new Vector3[splatCount];

            _histogram = new int[k_RadixBuckets];
            _prefixSum = new int[k_RadixBuckets];

            _orderBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                splatCount,
                sizeof(uint)
            );

            for(int i = 0; i < splatCount; i++)
                _orderData[i] = (uint)i;
            _orderBuffer.SetData(_orderData);
        }

        ~GsplatCpuSorterResource()
        {
            Dispose(false);
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // 二重解放防止
        }

        private void Dispose(bool disposing)
        {
            if(_disposed)
                return;
            _disposed = true;

            _orderBuffer?.Dispose();
            _orderBuffer = null;

            if(disposing)
            {
                _depthKeys = null;
                _depthKeysTmp = null;
                _indices = null;
                _indicesTmp = null;
                _orderData = null;
                _positionCache = null;
                _histogram = null;
                _prefixSum = null;
            }
        }


        public void Sort(GraphicsBuffer positionBuffer, Matrix4x4 viewMatrix)
        {
            if(_disposed)
                throw new ObjectDisposedException(nameof(GsplatCpuSorterResource));

            if(positionBuffer == null || _splatCount == 0)
                return;

            // GPU->CPUへ位置データを読み出す 
            positionBuffer.GetData(_positionCache);

            // 各スプラットのビュー空間Zを計算してソートエントリを構築
            for(int i = 0; i < _splatCount; i++)
            {
                // ワールド座標->ビュー座標変換
                Vector3 viewPos = viewMatrix.MultiplyPoint3x4(_positionCache[i]);
                _depthKeys[i] = FloatToSortableUint(-viewPos.z);
                _indices[i] = (uint)i;
            }

            // CPU Radixソート
            LsdRadixSort();

            // GPU側のOrderBufferを更新
            _orderBuffer.SetData(_orderData);
        }

        private static uint FloatToSortableUint(float value)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(value);
            return (bits & 0x80000000u) != 0 ? ~bits : bits ^ 0x80000000u;
        }

        private void LsdRadixSort()
        {
            // LSD Radix Sort 8bit × 4パス
            // 各パスでキーの特定8bitを使い安定ソートを4回繰り返す
            // pingpongバッファを交互に使いArray.Copyを最小化
            uint[] keys = _depthKeys;
            uint[] keysTmp = _depthKeysTmp;
            uint[] vals = _indices;
            uint[] valsTmp = _indicesTmp;

            // LSD は最下位ビットから処理するが、今回は「降順（遠い順）」にしたいので
            // FloatToSortableUint で「遠い = 大きいuint」に変換済みであるため、
            // 昇順のRadix Sortをそのまま適用すると最終的に昇順になる。
            // 降順にするには最終パス後に結果を反転するか、
            // 各パスのプレフィックスサムを後ろから詰める「降順Radix」にする。
            // ここでは各パスを降順バケット割り当てで実装する（後ろから詰める）。
            for(int pass = 0; pass < k_RadixPasses; pass++)
            {
                int shift = pass * k_RadixBits;

                // ヒストグラム集計
                Array.Clear(_histogram, 0, k_RadixBuckets);
                for(int i = 0; i < _splatCount; i++)
                    _histogram[(keys[i] >> shift) & k_RadixMask]++;

                // プレフィックスサム 降順
                // FloatToSortableUint により「遠い = 大きいuint」なので
                // バケット255（最大値）が先頭になるよう255→0の順で先頭から積算
                // 大きいキー = 小さいdest となり遠い順に並ぶ
                _prefixSum[k_RadixBuckets - 1] = 0;
                for (int b = k_RadixBuckets - 2; b >= 0; b--)
                    _prefixSum[b] = _prefixSum[b + 1] + _histogram[b + 1];

                // 散布
                for(int i = 0; i < _splatCount; i++)
                {
                    int bucket = (int)((keys[i] >> shift) & k_RadixMask);
                    int dest = _prefixSum[bucket]++;
                    keysTmp[dest] = keys[i];
                    valsTmp[dest] = vals[i];
                }

                // バッファ参照を入れ替える Array.Copyなし
                uint[] swapK = keys; 
                keys = keysTmp;
                keysTmp = swapK;
                uint[] swapV = vals;
                vals = valsTmp;
                valsTmp = swapV;
            }

            Array.Copy(vals, _orderData, _splatCount);
        }
    }
}
