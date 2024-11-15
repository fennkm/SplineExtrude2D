using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace UnityEngine.Splines
{
    /// <summary>
    /// A component for creating a tube mesh from a Spline at runtime.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Splines/Spline Extrude 2D")]
    public class SplineExtrude2D : MonoBehaviour
    {
        [SerializeField, Tooltip("The Spline to extrude.")]
        SplineContainer m_Container;

        [SerializeField, Tooltip("Enable to regenerate the extruded mesh when the target Spline is modified. Disable " +
             "this option if the Spline will not be modified at runtime.")]
        bool m_RebuildOnSplineChange;

        [SerializeField, Tooltip("The maximum number of times per-second that the mesh will be rebuilt.")]
        int m_RebuildFrequency = 30;

        [SerializeField, Tooltip("Automatically update any Mesh, Box, or Circle collider components when the mesh is extruded.")]
#pragma warning disable 414
        bool m_UpdateColliders = true;
#pragma warning restore 414

        [SerializeField, Tooltip("The number of rects that comprise the length of one unit of the mesh. The " +
             "total number of sections is equal to \"Spline.GetLength() * segmentsPerUnit\".")]
        float m_SegmentsPerUnit = 4;

        [SerializeField, Tooltip("The width of the extruded mesh.")]
        float m_Width = .25f;

        [SerializeField, Tooltip("The section of the Spline to extrude.")]
        Vector2 m_Range = new Vector2(0f, 1f);
        
        Mesh m_Mesh;
        bool m_RebuildRequested;
        float m_NextScheduledRebuild;

        /// <summary>The SplineContainer of the <see cref="Spline"/> to extrude.</summary>
        public SplineContainer Container
        {
            get => m_Container;
            set => m_Container = value;
        }

        /// <summary>
        /// Enable to regenerate the extruded mesh when the target Spline is modified. Disable this option if the Spline
        /// will not be modified at runtime.
        /// </summary>
        public bool RebuildOnSplineChange
        {
            get => m_RebuildOnSplineChange;
            set => m_RebuildOnSplineChange = value;
        }

        /// <summary>The maximum number of times per-second that the mesh will be rebuilt.</summary>
        public int RebuildFrequency
        {
            get => m_RebuildFrequency;
            set => m_RebuildFrequency = Mathf.Max(value, 1);
        }

        /// <summary>How many rects comprise the one unit length of the mesh.</summary>
        public float SegmentsPerUnit
        {
            get => m_SegmentsPerUnit;
            set => m_SegmentsPerUnit = Mathf.Max(value, .0001f);
        }

        /// <summary>The width of the extruded mesh.</summary>
        public float Width
        {
            get => m_Width;
            set => m_Width = Mathf.Max(value, .00001f);
        }

        /// <summary>
        /// The section of the Spline to extrude.
        /// </summary>
        public Vector2 Range
        {
            get => m_Range;
            set => m_Range = new Vector2(Mathf.Min(value.x, value.y), Mathf.Max(value.x, value.y));
        }

        /// <summary>The main Spline to extrude.</summary>
        public Spline Spline
        {
            get => m_Container?.Spline;
        }

        /// <summary>The Splines to extrude.</summary>
        public IReadOnlyList<Spline> Splines
        {
            get => m_Container?.Splines;
        }

        internal void Reset()
        {
            TryGetComponent(out m_Container);

            if (TryGetComponent<MeshFilter>(out var filter))
                filter.sharedMesh = m_Mesh = CreateMeshAsset();

            if (TryGetComponent<MeshRenderer>(out var renderer) && renderer.sharedMaterial == null)
            {
                // todo Make Material.GetDefaultMaterial() public
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var mat = cube.GetComponent<MeshRenderer>().sharedMaterial;
                DestroyImmediate(cube);
                renderer.sharedMaterial = mat;
            }

            Rebuild();
        }

        void Start()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
#endif
            {
                if(m_Container == null || m_Container.Spline == null || m_Container.Splines.Count == 0)
                    return;
                if((m_Mesh = GetComponent<MeshFilter>().sharedMesh) == null)
                    return;
            }
            
            Rebuild();
        }
        
        internal static readonly string k_EmptyContainerError = "Spline Extrude does not have a valid SplineContainer set.";
        bool IsNullOrEmptyContainer()
        {
            var isNull = m_Container == null || m_Container.Spline == null || m_Container.Splines.Count == 0; 
            if (isNull)
            {
                if(Application.isPlaying)
                    Debug.LogError(k_EmptyContainerError, this);
            }
            return isNull;
        }

        internal static readonly string k_EmptyMeshFilterError = "SplineExtrude2D.createMeshInstance is disabled," +
                                                                         " but there is no valid mesh assigned. " +
                                                                         "Please create or assign a writable mesh asset.";
        bool IsNullOrEmptyMeshFilter()
        {
            var isNull = (m_Mesh = GetComponent<MeshFilter>().sharedMesh) == null; 
            if(isNull)
                Debug.LogError(k_EmptyMeshFilterError, this);
            return isNull;
        }
        
        void OnEnable()
        {
            Spline.Changed += OnSplineChanged;
        }

        void OnDisable()
        {
            Spline.Changed -= OnSplineChanged;
        }

        void OnSplineChanged(Spline spline, int knotIndex, SplineModification modificationType)
        {
            if (m_Container != null && Splines.Contains(spline) && m_RebuildOnSplineChange)
                m_RebuildRequested = true;
        }

        void Update()
        {
            if(m_RebuildRequested && Time.time >= m_NextScheduledRebuild)
                Rebuild();
        }

        /// <summary>
        /// Triggers the rebuild of a Spline's extrusion mesh and collider.
        /// </summary>
        public void Rebuild()
        {
            if (IsNullOrEmptyContainer() || IsNullOrEmptyMeshFilter())
                return;
            
            SplineMesh2D.Extrude(Splines, m_Mesh, m_Width, m_SegmentsPerUnit, m_Range);
            m_NextScheduledRebuild = Time.time + 1f / m_RebuildFrequency;
    
#if UNITY_PHYSICS_MODULE
            if (m_UpdateColliders)
            {
                if (TryGetComponent<MeshCollider>(out var meshCollider))
                    meshCollider.sharedMesh = m_Mesh;

                if (TryGetComponent<BoxCollider2D>(out var boxCollider2D))
                {
                    boxCollider2D.center = m_Mesh.bounds.center;
                    boxCollider2D.size = m_Mesh.bounds.size;
                }

                if (TryGetComponent<CircleCollider2D>(out var circleCollider2D))
                {
                    circleCollider2D.center = m_Mesh.bounds.center;
                    var ext = m_Mesh.bounds.extents;
                    circleCollider2D.radius = Mathf.Max(ext.x, ext.y, ext.z);
                }
            }
#endif
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if(EditorApplication.isPlaying)
                return;
            Rebuild();
        }
#endif

        internal Mesh CreateMeshAsset()
        {
            var mesh = new Mesh();
            mesh.name = name;

#if UNITY_EDITOR
            var scene = SceneManagement.SceneManager.GetActiveScene();
            var sceneDataDir = "Assets";

            if (!string.IsNullOrEmpty(scene.path))
            {
                var dir = Path.GetDirectoryName(scene.path);
                sceneDataDir = $"{dir}/{Path.GetFileNameWithoutExtension(scene.path)}";
                if (!Directory.Exists(sceneDataDir))
                    Directory.CreateDirectory(sceneDataDir);
            }

            var path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{sceneDataDir}/SplineExtrude_{mesh.name}.asset");
            UnityEditor.AssetDatabase.CreateAsset(mesh, path);
            UnityEditor.EditorGUIUtility.PingObject(mesh);
#endif
            return mesh;
        }
    }
}
