// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

using Gsplat.Common;

namespace Gsplat.Editor
{
    [ScriptedImporter(1, "ply")]
    public class GsplatImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var gsplatAsset = ScriptableObject.CreateInstance<GsplatAsset>();
            var bounds = new Bounds();

            using (var fs = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read))
            {
                // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
                if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: currently files larger than 2GB are not supported");
                    return;
                }

                var plyInfo = PlyHeaderInfo.ReadPlyHeader(fs);
                var shCoeffs = plyInfo.SHPropertyCount / 3;
                gsplatAsset.SplatCount = plyInfo.VertexCount;
                gsplatAsset.SHBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);

                if (gsplatAsset.SHBands > 3 ||
                    GsplatUtils.SHBandsToCoefficientCount(gsplatAsset.SHBands) * 3 != plyInfo.SHPropertyCount)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: unexpected SH property count {plyInfo.SHPropertyCount}");
                    return;
                }

                if (plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                    plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: missing required properties in PLY header");
                    return;
                }

                gsplatAsset.Positions = new Vector3[plyInfo.VertexCount];
                gsplatAsset.Colors = new Vector4[plyInfo.VertexCount];
                if (shCoeffs > 0)
                    gsplatAsset.SHs = new Vector3[plyInfo.VertexCount * shCoeffs];
                gsplatAsset.Scales = new Vector3[plyInfo.VertexCount];
                gsplatAsset.Rotations = new Vector4[plyInfo.VertexCount];

                var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
                for (uint i = 0; i < plyInfo.VertexCount; i++)
                {
                    var readBytes = fs.Read(buffer);
                    if (readBytes != buffer.Length)
                    {
                        if (GsplatSettings.Instance.ShowImportErrors)
                            Debug.LogError(
                                $"{ctx.assetPath} import error: unexpected end of file, got {readBytes} bytes at vertex {i}");
                        return;
                    }

                    var properties = MemoryMarshal.Cast<byte, float>(buffer);
                    gsplatAsset.Positions[i] = new Vector3(
                        properties[plyInfo.PositionOffset],
                        properties[plyInfo.PositionOffset + 1],
                        properties[plyInfo.PositionOffset + 2]);
                    gsplatAsset.Colors[i] = new Vector4(
                        properties[plyInfo.ColorOffset],
                        properties[plyInfo.ColorOffset + 1],
                        properties[plyInfo.ColorOffset + 2],
                        GsplatUtils.Sigmoid(properties[plyInfo.OpacityOffset]));
                    for (int j = 0; j < shCoeffs; j++)
                        gsplatAsset.SHs[i * shCoeffs + j] = new Vector3(
                            properties[j + plyInfo.SHOffset],
                            properties[j + plyInfo.SHOffset + shCoeffs],
                            properties[j + plyInfo.SHOffset + shCoeffs * 2]);
                    gsplatAsset.Scales[i] = new Vector3(
                        Mathf.Exp(properties[plyInfo.ScaleOffset]),
                        Mathf.Exp(properties[plyInfo.ScaleOffset + 1]),
                        Mathf.Exp(properties[plyInfo.ScaleOffset + 2]));
                    gsplatAsset.Rotations[i] = new Vector4(
                        properties[plyInfo.RotationOffset],
                        properties[plyInfo.RotationOffset + 1],
                        properties[plyInfo.RotationOffset + 2],
                        properties[plyInfo.RotationOffset + 3]).normalized;

                    if (i == 0) bounds = new Bounds(gsplatAsset.Positions[i], Vector3.zero);
                    else bounds.Encapsulate(gsplatAsset.Positions[i]);
                    EditorUtility.DisplayProgressBar("Importing Gsplat Asset", "Reading vertices",
                        i / (float)plyInfo.VertexCount);
                }
            }

            gsplatAsset.Bounds = bounds;
            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);
        }
    }
}