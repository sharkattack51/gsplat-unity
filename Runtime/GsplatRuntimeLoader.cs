using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

using Gsplat;
using Gsplat.Common;
using Cysharp.Threading.Tasks;

[RequireComponent(typeof(GsplatRenderer))]
[RequireComponent(typeof(BoxCollider))]
public class GsplatRuntimeLoader : MonoBehaviour
{
    public string assetPath;
    public bool loadOnStart = false;
    public bool enableRotZ180 = true;

    private GsplatRenderer gsplatRenderer;
    private BoxCollider collid;

    private GsplatAsset gsplatAsset;
    public GsplatAsset LoadedAsset { get{ return gsplatAsset; } }


    void Awake()
    {

    }

    void Start()
    {
        gsplatRenderer = this.gameObject.GetComponent<GsplatRenderer>();
        collid = this.gameObject.GetComponent<BoxCollider>();

        if(loadOnStart)
            LoadAsync().Forget();
    }

    void Update()
    {

    }


    public async UniTask LoadAsync(Action<bool, object> OnLoaded = null, CancellationToken tkn = default)
    {
        await LoadAsync(assetPath, OnLoaded, tkn);
    }

    public async UniTask LoadAsync(string assetPath, Action<bool, object> OnLoaded = null, CancellationToken tkn = default)
    {
        this.assetPath = assetPath;

        bool success = false;
        PlyHeaderInfo plyInfo = null;
        
        await UniTask.Create(async (tkn) => {
            try
            {
                plyInfo = Load();
            }
            catch(Exception e)
            {
                Debug.LogErrorFormat("gsplat load error: {0}", e.Message);
            }
        },  cancellationToken: tkn);

        await UniTask.DelayFrame(1);

        if(plyInfo != null && gsplatAsset != null && gsplatAsset.Bounds != null)
        {
            success = true;

            collid.center = gsplatAsset.Bounds.center;
            collid.size = gsplatAsset.Bounds.size;

            if(enableRotZ180)
                this.gameObject.transform.rotation = Quaternion.Euler(0.0f, 0.0f, 180.0f);

            gsplatRenderer.GsplatAsset = gsplatAsset;

            Debug.LogFormat(string.Format("gsplat loaded: {0} / {1} splat.", assetPath, plyInfo.VertexCount));
        }
        else
            Debug.LogErrorFormat("gsplat data error");

        OnLoaded?.Invoke(success, plyInfo);
    }

    public void Unload()
    {
        gsplatRenderer.GsplatAsset = null;

        GsplatAsset.Destroy(gsplatAsset);
        gsplatAsset = null;

        Debug.LogFormat(string.Format("gsplat unloaded: {0}", assetPath));
    }

#pragma warning disable 1998
    // code from: Gsplat.Editor.GsplatImporter.cs
    private PlyHeaderInfo Load()
    {
        gsplatAsset = ScriptableObject.CreateInstance<GsplatAsset>();
        Bounds bounds = new Bounds();

        PlyHeaderInfo plyInfo;

        using(FileStream fs = new FileStream(assetPath, FileMode.Open, FileAccess.Read))
        {
            // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
            if(fs.Length >= 2 * 1024 * 1024 * 1024L)
            {
                Debug.LogError($"{assetPath} import error: currently files larger than 2GB are not supported");
                return null;
            }

            plyInfo = PlyHeaderInfo.ReadPlyHeader(fs);
            int shCoeffs = plyInfo.SHPropertyCount / 3;
            gsplatAsset.SplatCount = plyInfo.VertexCount;
            gsplatAsset.SHBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);

            if(gsplatAsset.SHBands > 3 ||
                GsplatUtils.SHBandsToCoefficientCount(gsplatAsset.SHBands) * 3 != plyInfo.SHPropertyCount)
            {
                Debug.LogError($"{assetPath} import error: unexpected SH property count {plyInfo.SHPropertyCount}");
                return null;
            }

            if(plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
            {
                Debug.LogError($"{assetPath} import error: missing required properties in PLY header");
                return null;
            }

            gsplatAsset.Positions = new Vector3[plyInfo.VertexCount];
            gsplatAsset.Colors = new Vector4[plyInfo.VertexCount];
            if(shCoeffs > 0)
                gsplatAsset.SHs = new Vector3[plyInfo.VertexCount * shCoeffs];
            gsplatAsset.Scales = new Vector3[plyInfo.VertexCount];
            gsplatAsset.Rotations = new Vector4[plyInfo.VertexCount];

            byte[] buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
            for(uint i = 0; i < plyInfo.VertexCount; i++)
            {
                int readBytes = fs.Read(buffer);
                if(readBytes != buffer.Length)
                {
                    Debug.LogError($"{assetPath} import error: unexpected end of file, got {readBytes} bytes at vertex {i}");
                    break;
                }

                Span<float> properties = MemoryMarshal.Cast<byte, float>(buffer);
                gsplatAsset.Positions[i] = new Vector3(
                    properties[plyInfo.PositionOffset],
                    properties[plyInfo.PositionOffset + 1],
                    properties[plyInfo.PositionOffset + 2]);
                gsplatAsset.Colors[i] = new Vector4(
                    properties[plyInfo.ColorOffset],
                    properties[plyInfo.ColorOffset + 1],
                    properties[plyInfo.ColorOffset + 2],
                    GsplatUtils.Sigmoid(properties[plyInfo.OpacityOffset]));
                for(int j = 0; j < shCoeffs; j++)
                {
                    gsplatAsset.SHs[i * shCoeffs + j] = new Vector3(
                        properties[j + plyInfo.SHOffset],
                        properties[j + plyInfo.SHOffset + shCoeffs],
                        properties[j + plyInfo.SHOffset + shCoeffs * 2]);
                }
                gsplatAsset.Scales[i] = new Vector3(
                    Mathf.Exp(properties[plyInfo.ScaleOffset]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 1]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 2]));
                gsplatAsset.Rotations[i] = new Vector4(
                    properties[plyInfo.RotationOffset],
                    properties[plyInfo.RotationOffset + 1],
                    properties[plyInfo.RotationOffset + 2],
                    properties[plyInfo.RotationOffset + 3]).normalized;

                if(i == 0)
                    bounds = new Bounds(gsplatAsset.Positions[i], Vector3.zero);
                else
                    bounds.Encapsulate(gsplatAsset.Positions[i]);
            }
        }

        gsplatAsset.Bounds = bounds;

        return plyInfo;
    }
# pragma warning restore 1998
}
