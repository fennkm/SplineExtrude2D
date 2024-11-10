using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace UnityEngine.Splines
{
    public static class SplineMesh2D
    {
        public interface ISplineVertexData
        {
            Vector3 position { get; set; }

            Vector3 normal { get; set; }

            Vector2 texture { get; set; }
        }

        private struct VertexData : ISplineVertexData
        {
            public Vector3 position { get; set; }

            public Vector3 normal { get; set; }

            public Vector2 texture { get; set; }
        }

        private struct Settings
        {
            public int segments { get; private set; }

            public bool closed { get; private set; }

            public float2 range { get; private set; }

            public float width { get; private set; }

            public Settings(int segments, bool closed, float2 range, float width)
            {
                this.segments = math.clamp(segments, 2, 4096);
                this.range = new float2(math.min(range.x, range.y), math.max(range.x, range.y));
                this.closed = math.abs(1f - (this.range.y - this.range.x)) < float.Epsilon && closed;
                this.width = math.clamp(width, 1E-05f, 10000f);
            }
        }

        private const float k_WidthMin = 1E-05f;

        private const float k_WidthMax = 10000f;

        private const int k_SegmentsMin = 2;

        private const int k_SegmentsMax = 4096;

        private static readonly VertexAttributeDescriptor[] k_PipeVertexAttribs = new VertexAttributeDescriptor[3]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
        };

        private static void ExtrudeRect<T, K>(T spline, float t, NativeArray<K> data, int start, float width) where T : ISpline where K : struct, ISplineVertexData
        {
            float num = spline.Closed ? math.frac(t) : math.clamp(t, 0f, 1f);
            spline.Evaluate(num, out var position, out var tangent, out var upVector);
            float num2 = math.lengthsq(tangent);
            if (num2 == 0f || float.IsNaN(num2))
            {
                float t2 = math.clamp(num + 0.0001f * ((t < 1f) ? 1f : (-1f)), 0f, 1f);
                spline.Evaluate(t2, out var _, out tangent, out upVector);
            }

            tangent = math.normalize(tangent);
            quaternion q = quaternion.LookRotationSafe(tangent, upVector);
            // For each face
            for (int i = 0; i < 2; i++)
            {
                // TVertexType
                K value = new K();
                // Get point on circle corresponding to side, stretch to correct width
                float3 v = new float3(1 - i * 2, 0f, 0f) * width;
                // Rotate point from spline space to world space
                value.position = position + math.rotate(q, v);
                // Calculate normal
                value.normal = (value.position - (Vector3)position).normalized;
                value.texture = new Vector2(i, t * spline.GetLength());
                data[start + i] = value;
            }
        }

        public static void GetVertexAndIndexCount(int segments, bool closed, Vector2 range, out int vertexCount, out int indexCount)
        {
            GetVertexAndIndexCount(new Settings(segments, closed, range, 1f), out vertexCount, out indexCount);
        }

        /*
        Gets number of faces (vertexCount) and number of vertices (indexCount) in final mesh.
        */
        private static void GetVertexAndIndexCount(Settings settings, out int vertexCount, out int indexCount)
        {
            vertexCount = 2 * settings.segments;
            indexCount = 6 * (settings.segments - (!settings.closed ? 1 : 0));
        }

        public static void Extrude<T>(T spline, Mesh mesh, float width, int segments) where T : ISpline
        {
            Extrude(spline, mesh, width, segments, new float2(0f, 1f));
        }

        /*
        Extrude a spline into a thick mesh line.
        */
        public static void Extrude<T>(T spline, Mesh mesh, float width, int segments, float2 range) where T : ISpline
        {
            GetVertexAndIndexCount(new Settings(segments, spline.Closed, range, width), out var vertexCount, out var indexCount);
            Mesh.MeshDataArray data = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData meshData = data[0];
            IndexFormat indexFormat = ((vertexCount >= 65535) ? IndexFormat.UInt32 : IndexFormat.UInt16);
            meshData.SetIndexBufferParams(indexCount, indexFormat);
            meshData.SetVertexBufferParams(vertexCount, k_PipeVertexAttribs);
            NativeArray<VertexData> vertexData = meshData.GetVertexData<VertexData>();
            if (indexFormat == IndexFormat.UInt16)
            {
                NativeArray<ushort> indexData = meshData.GetIndexData<ushort>();
                Extrude(spline, vertexData, indexData, width, segments, range);
            }
            else
            {
                NativeArray<uint> indexData2 = meshData.GetIndexData<uint>();
                Extrude(spline, vertexData, indexData2, width, segments, range);
            }

            mesh.Clear();
            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount));
            Mesh.ApplyAndDisposeWritableMeshData(data, mesh);
            mesh.RecalculateBounds();
        }

        /*
        Extrude a spline into a thick mesh line.
        */
        public static void Extrude<T>(IReadOnlyList<T> splines, Mesh mesh, float width, float segmentsPerUnit, float2 range) where T : ISpline
        {
            mesh.Clear();
            if (splines == null)
            {
                if (Application.isPlaying)
                {
                    Debug.LogError("Trying to extrude a spline mesh with no valid splines.");
                }

                return;
            }

            Mesh.MeshDataArray data = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData meshData = data[0];
            meshData.subMeshCount = 1;
            int num = 0;
            int num2 = 0;
            Settings[] array = new Settings[splines.Count];
            float num3 = Mathf.Abs(range.y - range.x);
            (int, int)[] array2 = new (int, int)[splines.Count];
            for (int i = 0; i < splines.Count; i++)
            {
                T val = splines[i];
                int segments = Mathf.Max((int)Mathf.Ceil(val.GetLength() * num3 * segmentsPerUnit), 1);
                array[i] = new Settings(segments, val.Closed, range, width);
                GetVertexAndIndexCount(array[i], out var vertexCount, out var indexCount);
                array2[i] = (num2, num);
                num += vertexCount;
                num2 += indexCount;
            }

            IndexFormat indexFormat = ((num >= 65535) ? IndexFormat.UInt32 : IndexFormat.UInt16);
            meshData.SetIndexBufferParams(num2, indexFormat);
            meshData.SetVertexBufferParams(num, k_PipeVertexAttribs);
            NativeArray<VertexData> vertexData = meshData.GetVertexData<VertexData>();
            if (indexFormat == IndexFormat.UInt16)
            {
                NativeArray<ushort> indexData = meshData.GetIndexData<ushort>();
                for (int j = 0; j < splines.Count; j++)
                {
                    Extrude(splines[j], vertexData, indexData, array[j], array2[j].Item2, array2[j].Item1);
                }
            }
            else
            {
                NativeArray<uint> indexData2 = meshData.GetIndexData<uint>();
                for (int k = 0; k < splines.Count; k++)
                {
                    Extrude(splines[k], vertexData, indexData2, array[k], array2[k].Item2, array2[k].Item1);
                }
            }

            // for (int i = 0; i < vertexData.Length; i++)
            // {
            //     Debug.Log(i + ": " + vertexData[i].position);
            // }

            meshData.SetSubMesh(0, new SubMeshDescriptor(0, num2));
            Mesh.ApplyAndDisposeWritableMeshData(data, mesh);
            mesh.RecalculateBounds();
        }

        public static void Extrude<TSplineType, TVertexType, TIndexType>(TSplineType spline, NativeArray<TVertexType> vertices, NativeArray<TIndexType> indices, float radius, int segments, float2 range) where TSplineType : ISpline where TVertexType : struct, ISplineVertexData where TIndexType : struct
        {
            Extrude(spline, vertices, indices, new Settings(segments, spline.Closed, range, radius));
        }

        private static void Extrude<TSplineType, TVertexType, TIndexType>(TSplineType spline, NativeArray<TVertexType> vertices, NativeArray<TIndexType> indices, Settings settings, int vertexArrayOffset = 0, int indicesArrayOffset = 0) where TSplineType : ISpline where TVertexType : struct, ISplineVertexData where TIndexType : struct
        {
            float width = settings.width;
            int segments = settings.segments;
            float2 range = settings.range;
            GetVertexAndIndexCount(settings, out var vertexCount, out var indexCount);

            if (segments < 2)
            {
                throw new ArgumentOutOfRangeException("segments", "Segments must be greater than 2");
            }

            if (vertices.Length < vertexCount)
            {
                throw new ArgumentOutOfRangeException($"Vertex array is incorrect size. Expected {vertexCount} or more, but received {vertices.Length}.");
            }

            if (indices.Length < indexCount)
            {
                throw new ArgumentOutOfRangeException($"Index array is incorrect size. Expected {indexCount} or more, but received {indices.Length}.");
            }

            if (typeof(TIndexType) == typeof(ushort))
            {
                WindTris(indices.Reinterpret<ushort>(), settings, vertexArrayOffset, indicesArrayOffset);
            }
            else
            {
                if (!(typeof(TIndexType) == typeof(uint)))
                {
                    throw new ArgumentException("Indices must be UInt16 or UInt32", "indices");
                }

                WindTris(indices.Reinterpret<uint>(), settings, vertexArrayOffset, indicesArrayOffset);
            }

            for (int i = 0; i < segments; i++)
            {
                ExtrudeRect(spline, math.lerp(range.x, range.y, (float)i / ((float)segments - 1f)), vertices, vertexArrayOffset + i * 2, width);
            }
        }


        private static void WindTris(NativeArray<ushort> indices, Settings settings, int vertexArrayOffset = 0, int indexArrayOffset = 0)
        {
            bool closed = settings.closed;
            int segments = settings.segments;

            for (int i = 0; i < (closed ? segments : (segments - 1)); i++)
            {
                int num  = vertexArrayOffset +  i * 2;
                int num2 = vertexArrayOffset +  i * 2 + 1;
                int num3 = vertexArrayOffset + (i + 1) % segments * 2;
                int num4 = vertexArrayOffset + (i + 1) % segments * 2 + 1;
                indices[indexArrayOffset + i * 6    ] = (ushort)num;
                indices[indexArrayOffset + i * 6 + 1] = (ushort)num2;
                indices[indexArrayOffset + i * 6 + 2] = (ushort)num3;
                indices[indexArrayOffset + i * 6 + 3] = (ushort)num2;
                indices[indexArrayOffset + i * 6 + 4] = (ushort)num4;
                indices[indexArrayOffset + i * 6 + 5] = (ushort)num3;
            }
        }

        private static void WindTris(NativeArray<uint> indices, Settings settings, int vertexArrayOffset = 0, int indexArrayOffset = 0)
        {
            bool closed = settings.closed;
            int segments = settings.segments;

            for (int i = 0; i < (closed ? segments : (segments - 1)); i++)
            {
                int num  = vertexArrayOffset +  i * 2;
                int num2 = vertexArrayOffset +  i * 2 + 1;
                int num3 = vertexArrayOffset + (i + 1) % segments * 2;
                int num4 = vertexArrayOffset + (i + 1) % segments * 2 + 1;
                indices[indexArrayOffset + i * 6    ] = (ushort)num;
                indices[indexArrayOffset + i * 6 + 1] = (ushort)num2;
                indices[indexArrayOffset + i * 6 + 2] = (ushort)num3;
                indices[indexArrayOffset + i * 6 + 3] = (ushort)num2;
                indices[indexArrayOffset + i * 6 + 4] = (ushort)num4;
                indices[indexArrayOffset + i * 6 + 5] = (ushort)num3;
            }
        }
    }
}