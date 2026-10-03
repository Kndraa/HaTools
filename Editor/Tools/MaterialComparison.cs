// Material Comparison: shows two materials side by side with editable fields and highlights every setting that differs. Full docs: CLAUDE.md > Tools > Material Comparison.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaTools
{
    public class MaterialComparison : EditorWindow
    {
        const string Title = "Material Comparison";
        const float RowHeight = 20f, PreviewHeight = 140f, ScrollbarWidth = 14f;
        static readonly Color Highlight = new Color(1f, 0.55f, 0f, 0.25f);
        static readonly GUIContent[] Axes = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z"), new GUIContent("W") };

        [MenuItem(HaToolsMenu.Root + Title)]
        static void Open() => GetWindow<MaterialComparison>(Title).minSize = new Vector2(560f, 320f);

        // ---------------------------------------------------------------- Window

        [SerializeField] internal Material A, B;
        [SerializeField] internal bool OnlyDifferences;
        [SerializeField] internal string Search = "";
        internal List<Row> Visible;
        List<Row> rows;
        Editor previewA, previewB;
        string shownMaterials, shownFilter;
        int shownDirty;
        Vector2 scroll;

        void OnEnable()
        {
            var selected = Selection.GetFiltered<Material>(SelectionMode.Unfiltered);
            if (A == null && selected.Length > 0) A = selected[0];
            if (B == null && selected.Length > 1) B = selected[1];
        }

        void OnDisable()
        {
            DestroyImmediate(previewA);
            DestroyImmediate(previewB);
        }

        // Shows changes made elsewhere (the material's inspector, Undo) without waiting for a click in this window
        void OnInspectorUpdate()
        {
            if (A != null && B != null && DirtyCount() != shownDirty) Repaint();
        }

        int DirtyCount() => EditorUtility.GetDirtyCount(A) + EditorUtility.GetDirtyCount(B);

        void OnGUI()
        {
            var line = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            A = (Material)EditorGUI.ObjectField(Half(line, 0), A, typeof(Material), false);
            B = (Material)EditorGUI.ObjectField(Half(line, 1), B, typeof(Material), false);
            // The rest of the window was laid out for the old materials: draw it again from the start
            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
                return;
            }
            if (A == null || B == null)
            {
                EditorGUILayout.HelpBox("Pick two materials to compare.", MessageType.Info);
                return;
            }

            line = GUILayoutUtility.GetRect(0f, PreviewHeight, GUILayout.ExpandWidth(true));
            Preview(Half(line, 0), A, ref previewA);
            Preview(Half(line, 1), B, ref previewB);

            Refresh();
            using (new EditorGUILayout.HorizontalScope())
            {
                OnlyDifferences = EditorGUILayout.ToggleLeft("Only differences", OnlyDifferences, GUILayout.Width(120f));
                Search = EditorGUILayout.TextField(Search, EditorStyles.toolbarSearchField);
                GUILayout.Label($"{rows.Count(r => r.Differs)} of {rows.Count} differ", GUILayout.ExpandWidth(false));
            }

            var area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Layout) return; // the area's size isn't known yet

            var content = new Rect(0f, 0f, area.width - ScrollbarWidth, Visible.Count * RowHeight);
            var scrolled = GUI.BeginScrollView(area, scroll, content, false, true);
            // Unity tells fields apart by draw order, so after scrolling the focused field would be another row's
            if (scrolled != scroll) GUIUtility.keyboardControl = 0;
            scroll = scrolled;

            // Only the rows in view are drawn: large shaders (Poiyomi) have a very long property list
            int first = Mathf.Max(0, (int)(scroll.y / RowHeight));
            int last = Mathf.Min(Visible.Count, first + (int)(area.height / RowHeight) + 2);
            for (int i = first; i < last; i++)
            {
                var row = new Rect(0f, i * RowHeight, area.width, RowHeight - 2f);
                if (Visible[i].Differs) EditorGUI.DrawRect(new Rect(0f, row.y, content.width, RowHeight - 1f), Highlight);
                DrawCell(Half(row, 0), A, Visible[i]);
                DrawCell(Half(row, 1), B, Visible[i]);
            }
            GUI.EndScrollView();
        }

        // Left or right column. Both stop short of the table's scrollbar, so everything above the table lines up with it.
        static Rect Half(Rect r, int column)
        {
            float width = (r.width - ScrollbarWidth) / 2f;
            return new Rect(r.x + column * width + 2f, r.y, width - 6f, r.height);
        }

        // The sphere from the material's inspector; drag to rotate
        static void Preview(Rect r, Material m, ref Editor editor)
        {
            Editor.CreateCachedEditor(m, null, ref editor);
            editor.OnInteractivePreviewGUI(r, "PreBackground");
        }

        // Rebuilds, compares and filters the rows, each only when something it depends on has changed
        internal void Refresh()
        {
            string materials = $"{A.GetInstanceID()} {A.shader.GetInstanceID()} {B.GetInstanceID()} {B.shader.GetInstanceID()}";
            string filter = OnlyDifferences + Search;
            int dirty = DirtyCount();
            bool rebuilt = rows == null || materials != shownMaterials;
            bool edited = dirty != shownDirty;

            if (rebuilt) rows = Rows(A, B);
            else if (edited) Compare(rows, A, B);

            // A row edited here until it matches stays listed (without its highlight) until the filter is used again:
            // removing it mid-edit would hand the keyboard focus to the row that takes its place
            bool editedElsewhere = edited && focusedWindow != this;
            if (rebuilt || filter != shownFilter || editedElsewhere)
            {
                Visible = rows.Where(r => Matches(r, OnlyDifferences, Search)).ToList();
                if (editedElsewhere) GUIUtility.keyboardControl = 0;
            }

            shownMaterials = materials;
            shownFilter = filter;
            shownDirty = dirty;
        }

        static void DrawCell(Rect r, Material m, Row row)
        {
            var label = new Rect(r.x, r.y + 1f, r.width * 0.4f, EditorGUIUtility.singleLineHeight);
            var field = new Rect(label.xMax + 2f, label.y, r.xMax - label.xMax - 2f, label.height);
            object value = Value(m, row);

            using (new EditorGUI.DisabledScope(value == Missing))
            {
                GUI.Label(label, new GUIContent(row.Label, row.Tooltip), EditorStyles.label);
                if (value == Missing) GUI.Label(field, "not in this shader", EditorStyles.miniLabel);
            }
            if (value == Missing) return;

            // Built-in materials and materials inside a model file can't be changed
            using (new EditorGUI.DisabledScope((m.hideFlags & HideFlags.NotEditable) != 0))
            {
                EditorGUI.BeginChangeCheck();
                object edited = Field(field, m, row, value);
                if (EditorGUI.EndChangeCheck()) Set(m, row, edited);
            }
        }

        // Draws the field for one value and returns what the user changed it to
        static object Field(Rect r, Material m, Row row, object value)
        {
            int index = row.Type == RowType.Property ? m.shader.FindPropertyIndex(row.Name) : -1;
            switch (value)
            {
                case Shader shader:
                    GUI.Label(r, new GUIContent(shader.name, shader.name), EditorStyles.label);
                    return value;
                case bool on:
                    return EditorGUI.Toggle(r, on);
                case string keywords:
                    return EditorGUI.DelayedTextField(r, keywords);
                case int number:
                    return EditorGUI.IntField(r, number);
                case float number:
                    if (m.shader.GetPropertyType(index) != ShaderPropertyType.Range) return EditorGUI.FloatField(r, number);
                    var limits = m.shader.GetPropertyRangeLimits(index);
                    return EditorGUI.Slider(r, number, limits.x, limits.y);
                case Color color:
                    bool hdr = (m.shader.GetPropertyFlags(index) & ShaderPropertyFlags.HDR) != 0;
                    return EditorGUI.ColorField(r, GUIContent.none, color, true, true, hdr);
                case Vector4 vector:
                    var xyzw = new[] { vector.x, vector.y, vector.z, vector.w };
                    EditorGUI.MultiFloatField(r, Axes, xyzw);
                    return new Vector4(xyzw[0], xyzw[1], xyzw[2], xyzw[3]);
                default: // a texture, or null for an empty texture slot
                    return EditorGUI.ObjectField(r, (Texture)value, typeof(Texture), false);
            }
        }

        // ---------------------------------------------------------------- Rows

        internal enum RowType { Shader, RenderQueue, Instancing, DoubleSidedGI, Keywords, Property, Tiling }

        internal class Row
        {
            public RowType Type;
            public string Name, Tooltip; // Name is the shader property's name for Property and Tiling rows
            public bool Differs;
            public string Label => Type == RowType.Tiling ? Name + "_ST" : Name;
        }

        // What Value returns when the material's shader doesn't have the row's property
        internal static readonly object Missing = new object();

        // One row per material setting and per property of either shader (matched by name), already compared
        internal static List<Row> Rows(Material a, Material b)
        {
            var rows = new List<Row>
            {
                new Row { Type = RowType.Shader, Name = "Shader", Tooltip = "Change the shader in the material's own inspector." },
                new Row { Type = RowType.RenderQueue, Name = "Render Queue", Tooltip = "-1 goes back to the shader's own queue." },
                new Row { Type = RowType.Instancing, Name = "GPU Instancing" },
                new Row { Type = RowType.DoubleSidedGI, Name = "Double Sided GI" },
                new Row { Type = RowType.Keywords, Name = "Keywords", Tooltip = "Enabled shader keywords, separated by spaces." },
            };

            // Properties in the order of A's shader, then the ones only B's shader has
            var seen = new HashSet<string>();
            foreach (var shader in new[] { a.shader, b.shader })
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string name = shader.GetPropertyName(i);
                    if (!seen.Add(name)) continue;
                    rows.Add(new Row { Type = RowType.Property, Name = name, Tooltip = shader.GetPropertyDescription(i) });
                    if (shader.GetPropertyType(i) == ShaderPropertyType.Texture &&
                        (shader.GetPropertyFlags(i) & ShaderPropertyFlags.NoScaleOffset) == 0)
                        rows.Add(new Row { Type = RowType.Tiling, Name = name, Tooltip = $"Tiling (X, Y) and offset (Z, W) of {name}." });
                }

            Compare(rows, a, b);
            return rows;
        }

        internal static void Compare(List<Row> rows, Material a, Material b)
        {
            foreach (var row in rows) row.Differs = !Equals(Value(a, row), Value(b, row));
        }

        internal static bool Matches(Row row, bool onlyDifferences, string search) =>
            (!onlyDifferences || row.Differs) &&
            (string.IsNullOrEmpty(search) || Contains(row.Label, search) || Contains(row.Tooltip, search));

        static bool Contains(string text, string part) => text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        // The material's value for a row, boxed so any two can be compared with Equals (exact, no rounding)
        internal static object Value(Material m, Row row)
        {
            switch (row.Type)
            {
                case RowType.Shader: return m.shader;
                case RowType.RenderQueue: return m.renderQueue;
                case RowType.Instancing: return m.enableInstancing;
                case RowType.DoubleSidedGI: return m.doubleSidedGI;
                case RowType.Keywords: return string.Join(" ", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal));
            }

            int index = m.shader.FindPropertyIndex(row.Name);
            if (index < 0) return Missing;
            var type = m.shader.GetPropertyType(index);
            if (row.Type == RowType.Tiling)
            {
                if (type != ShaderPropertyType.Texture) return Missing;
                Vector2 scale = m.GetTextureScale(row.Name), offset = m.GetTextureOffset(row.Name);
                return new Vector4(scale.x, scale.y, offset.x, offset.y);
            }
            switch (type)
            {
                case ShaderPropertyType.Color: return m.GetColor(row.Name);
                case ShaderPropertyType.Vector: return m.GetVector(row.Name);
                case ShaderPropertyType.Int: return m.GetInteger(row.Name);
                case ShaderPropertyType.Texture:
                    var texture = m.GetTexture(row.Name);
                    return texture != null ? texture : null; // a deleted texture counts as an empty slot
                default: return m.GetFloat(row.Name);
            }
        }

        // Writes a value of the kind Value returns. Sets the raw value, like an animation would. Registers Undo.
        internal static void Set(Material m, Row row, object value)
        {
            Undo.RecordObject(m, "Material Comparison: " + row.Label);
            switch (row.Type)
            {
                case RowType.RenderQueue: m.renderQueue = (int)value; break;
                case RowType.Instancing: m.enableInstancing = (bool)value; break;
                case RowType.DoubleSidedGI: m.doubleSidedGI = (bool)value; break;
                case RowType.Keywords:
                    m.shaderKeywords = ((string)value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    break;
                case RowType.Tiling:
                    var st = (Vector4)value;
                    m.SetTextureScale(row.Name, new Vector2(st.x, st.y));
                    m.SetTextureOffset(row.Name, new Vector2(st.z, st.w));
                    break;
                case RowType.Property:
                    switch (value)
                    {
                        case Color color: m.SetColor(row.Name, color); break;
                        case Vector4 vector: m.SetVector(row.Name, vector); break;
                        case int number: m.SetInteger(row.Name, number); break;
                        case float number: m.SetFloat(row.Name, number); break;
                        default: m.SetTexture(row.Name, (Texture)value); break;
                    }
                    break;
            }
            EditorUtility.SetDirty(m);
        }
    }
}
