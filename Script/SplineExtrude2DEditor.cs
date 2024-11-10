using System.Linq;
using UnityEngine;
using UnityEngine.Splines;
using UnityEditor;
using System;

internal class Spline2DComponentEditor : Editor
{
    internal struct LabelWidthScope : IDisposable
    {
        private float previousWidth;

        public LabelWidthScope(float width)
        {
            previousWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = width;
        }

        public void Dispose()
        {
            EditorGUIUtility.labelWidth = previousWidth;
        }
    }

    private static GUIStyle s_FoldoutStyle;

    internal static readonly string k_Helpbox = L10n.Tr("Instantiated Objects need a SplineContainer target to be created.");

    protected bool Foldout(bool foldout, GUIContent content)
    {
        return Foldout(foldout, content, toggleOnLabelClick: false);
    }

    public static bool Foldout(bool foldout, GUIContent content, bool toggleOnLabelClick)
    {
        if (s_FoldoutStyle == null)
        {
            s_FoldoutStyle = new GUIStyle(EditorStyles.foldout);
            s_FoldoutStyle.fontStyle = FontStyle.Bold;
        }

        return EditorGUILayout.Foldout(foldout, content, toggleOnLabelClick, s_FoldoutStyle);
    }
}

namespace UnityEditor.Splines
{
    [CustomEditor(typeof(SplineExtrude2D))]
    [CanEditMultipleObjects]
    class SplineExtrudeEditor2D : Spline2DComponentEditor
    {
        SerializedProperty m_Container;
        SerializedProperty m_RebuildOnSplineChange;
        SerializedProperty m_RebuildFrequency;
        SerializedProperty m_SegmentsPerUnit;
        SerializedProperty m_Width;
        SerializedProperty m_Range;
        SerializedProperty m_UpdateColliders;

        static readonly GUIContent k_RangeContent = new GUIContent("Range", "The section of the Spline to extrude.");
        static readonly GUIContent k_AdvancedContent = new GUIContent("Advanced", "Advanced Spline Extrude settings.");
        static readonly GUIContent k_PercentageContent = new GUIContent("Percentage", "The section of the Spline to extrude in percentages.");

        static readonly string k_Spline = "Spline";
        static readonly string k_Geometry = L10n.Tr("Geometry");
        static readonly string k_AutoRegenGeo = "Auto-Regen Geometry";
        static readonly string k_To = L10n.Tr("to");
        static readonly string k_From = L10n.Tr("from");

        SplineExtrude2D[] m_Components;
        bool m_AnyMissingMesh;

        protected void OnEnable()
        {
            m_Container = serializedObject.FindProperty("m_Container");
            m_RebuildOnSplineChange = serializedObject.FindProperty("m_RebuildOnSplineChange");
            m_RebuildFrequency = serializedObject.FindProperty("m_RebuildFrequency");
            m_SegmentsPerUnit = serializedObject.FindProperty("m_SegmentsPerUnit");
            m_Width = serializedObject.FindProperty("m_Width");
            m_Range = serializedObject.FindProperty("m_Range");
            m_UpdateColliders = serializedObject.FindProperty("m_UpdateColliders");

            m_Components = targets.Select(x => x as SplineExtrude2D).Where(y => y != null).ToArray();
            m_AnyMissingMesh = false;

            Spline.Changed += OnSplineChanged;
            EditorSplineUtility.AfterSplineWasModified += OnSplineModified;
            SplineContainer.SplineAdded += OnContainerSplineSetModified;
            SplineContainer.SplineRemoved += OnContainerSplineSetModified;
        }

        void OnDisable()
        {
            Spline.Changed -= OnSplineChanged;
            EditorSplineUtility.AfterSplineWasModified -= OnSplineModified;
            SplineContainer.SplineAdded -= OnContainerSplineSetModified;
            SplineContainer.SplineRemoved -= OnContainerSplineSetModified;
        }

        void OnSplineModified(Spline spline)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            foreach (var extrude in m_Components)
            {
                if (extrude.Container != null && extrude.Splines.Contains(spline))
                    extrude.Rebuild();
            }
        }

        void OnSplineChanged(Spline spline, int knotIndex, SplineModification modificationType)
        {
            OnSplineModified(spline);
        }

        void OnContainerSplineSetModified(SplineContainer container, int spline)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            foreach (var extrude in m_Components)
            {
                if (extrude.Container == container)
                    extrude.Rebuild();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            m_AnyMissingMesh = m_Components.Any(x => x.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh == null);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(m_Container, new GUIContent(k_Spline, m_Container.tooltip));
            if(m_Container.objectReferenceValue == null)
                EditorGUILayout.HelpBox(k_Helpbox, MessageType.Warning);
            
            EditorGUILayout.LabelField(k_Geometry, EditorStyles.boldLabel);

            if(m_AnyMissingMesh)
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(" ");
                if(GUILayout.Button("Create Mesh Asset"))
                    CreateMeshAssets(m_Components);
                GUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_Width);
            if(EditorGUI.EndChangeCheck())
                m_Width.floatValue = Mathf.Clamp(m_Width.floatValue, .00001f, 1000f);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_SegmentsPerUnit);
            if(EditorGUI.EndChangeCheck())
                m_SegmentsPerUnit.floatValue = Mathf.Clamp(m_SegmentsPerUnit.floatValue, .00001f, 4096f);

            m_Range.isExpanded = Foldout(m_Range.isExpanded, k_AdvancedContent);
            if (m_Range.isExpanded)
            {
                EditorGUI.indentLevel++;

                EditorGUI.showMixedValue = m_Range.hasMultipleDifferentValues;
                var range = m_Range.vector2Value;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.MinMaxSlider(k_RangeContent, ref range.x, ref range.y, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                    m_Range.vector2Value = range;

                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(k_PercentageContent);
                EditorGUI.indentLevel--;
                EditorGUI.indentLevel--;
                EditorGUI.indentLevel--;

                EditorGUI.BeginChangeCheck();
                var newRange = new Vector2(range.x, range.y);
                using (new LabelWidthScope(30f))
                    newRange.x = EditorGUILayout.FloatField(k_From, range.x * 100f) / 100f;

                using (new LabelWidthScope(15f))
                    newRange.y = EditorGUILayout.FloatField(k_To, range.y * 100f) / 100f;

                if (EditorGUI.EndChangeCheck())
                {
                    newRange.x = Mathf.Min(Mathf.Clamp(newRange.x, 0f, 1f), range.y);
                    newRange.y = Mathf.Max(newRange.x, Mathf.Clamp(newRange.y, 0f, 1f));
                    m_Range.vector2Value = newRange;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUI.showMixedValue = false;

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_RebuildOnSplineChange, new GUIContent(k_AutoRegenGeo, m_RebuildOnSplineChange.tooltip));
                if (m_RebuildOnSplineChange.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginDisabledGroup(!m_RebuildOnSplineChange.boolValue);
                    EditorGUILayout.PropertyField(m_RebuildFrequency);
                    EditorGUI.EndDisabledGroup();
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(m_UpdateColliders);

                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();

            if(EditorGUI.EndChangeCheck())
                foreach(var extrude in m_Components)
                    extrude.Rebuild();
        }

        void CreateMeshAssets(SplineExtrude2D[] components)
        {
            foreach (var extrude in components)
            {
                if (!extrude.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null)
                    filter.sharedMesh = extrude.CreateMeshAsset();
            }

            m_AnyMissingMesh = false;
        }
    }
}
