// Material Comparison tests. Full docs: CLAUDE.md > Tools > Material Comparison.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Row = HaTools.MaterialComparison.Row;

namespace HaTools.Tests
{
    public class MaterialComparisonTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        Material Mat(string shader = "Standard")
        {
            var m = new Material(Shader.Find(shader)) { name = "Test " + shader };
            created.Add(m);
            return m;
        }

        static Row Find(Material a, Material b, string label) => MaterialComparison.Rows(a, b).Single(r => r.Label == label);

        static string[] Differing(Material a, Material b) =>
            MaterialComparison.Rows(a, b).Where(r => r.Differs).Select(r => r.Label).ToArray();

        // ---------------------------------------------------------------- Rows and differences

        [Test]
        public void IdenticalMaterialsHaveNoDifferences()
        {
            Material a = Mat(), b = Mat();
            var labels = MaterialComparison.Rows(a, b).Select(r => r.Label).ToList();
            CollectionAssert.IsSubsetOf(new[] { "Shader", "Render Queue", "GPU Instancing", "Double Sided GI", "Keywords",
                "_Color", "_MainTex", "_MainTex_ST", "_Glossiness" }, labels);
            CollectionAssert.AllItemsAreUnique(labels);
            Assert.IsEmpty(Differing(a, b));
        }

        [Test]
        public void EachChangeIsTheOnlyDifference()
        {
            void Check(string label, Action<Material> change)
            {
                Material a = Mat(), b = Mat();
                change(b);
                CollectionAssert.AreEqual(new[] { label }, Differing(a, b), label);
            }

            Check("_Color", m => m.SetColor("_Color", Color.red));
            Check("_Glossiness", m => m.SetFloat("_Glossiness", 0.123f));
            Check("_MainTex", m => m.SetTexture("_MainTex", Texture2D.whiteTexture));
            Check("_MainTex_ST", m => m.SetTextureScale("_MainTex", new Vector2(2f, 2f)));
            Check("_MainTex_ST", m => m.SetTextureOffset("_MainTex", new Vector2(0.5f, 0f)));
            Check("Render Queue", m => m.renderQueue = 3000);
            Check("GPU Instancing", m => m.enableInstancing = true);
            Check("Double Sided GI", m => m.doubleSidedGI = true);
            Check("Keywords", m => m.EnableKeyword("_EMISSION"));
        }

        [Test]
        public void KeywordOrderDoesNotMatter()
        {
            Material a = Mat(), b = Mat();
            a.shaderKeywords = new[] { "_EMISSION", "_NORMALMAP" };
            b.shaderKeywords = new[] { "_NORMALMAP", "_EMISSION" };
            Assert.IsEmpty(Differing(a, b));
        }

        [Test]
        public void DifferentShadersAreMatchedByPropertyName()
        {
            Material standard = Mat(), unlit = Mat("Unlit/Color");
            var differing = Differing(standard, unlit);
            CollectionAssert.Contains(differing, "Shader");
            CollectionAssert.Contains(differing, "_Glossiness", "A property only one shader has is a difference");
            CollectionAssert.DoesNotContain(differing, "_Color", "Both shaders have a white _Color");
            Assert.AreSame(MaterialComparison.Missing, MaterialComparison.Value(unlit, Find(standard, unlit, "_Glossiness")));
            Assert.AreSame(MaterialComparison.Missing, MaterialComparison.Value(unlit, Find(standard, unlit, "_MainTex_ST")));

            // Properties only the second material's shader has are listed too, after the first one's
            var labels = MaterialComparison.Rows(unlit, standard).Select(r => r.Label).ToList();
            CollectionAssert.Contains(labels, "_Glossiness");
            Assert.Less(labels.IndexOf("_Color"), labels.IndexOf("_Glossiness"));
        }

        [Test]
        public void TexturesWithoutTilingHaveNoTilingRow()
        {
            // The skybox textures are marked [NoScaleOffset]
            var skybox = Mat("Skybox/6 Sided");
            var labels = MaterialComparison.Rows(skybox, skybox).Select(r => r.Label).ToList();
            CollectionAssert.Contains(labels, "_FrontTex");
            CollectionAssert.DoesNotContain(labels, "_FrontTex_ST");
        }

        [Test]
        public void FilterKeepsDifferencesAndSearchMatches()
        {
            Material a = Mat(), b = Mat();
            b.SetFloat("_Glossiness", 0.123f);
            var rows = MaterialComparison.Rows(a, b);
            string[] Shown(bool onlyDifferences, string search) =>
                rows.Where(r => MaterialComparison.Matches(r, onlyDifferences, search)).Select(r => r.Label).ToArray();

            Assert.AreEqual(rows.Count, Shown(false, "").Length);
            CollectionAssert.AreEqual(new[] { "_Glossiness" }, Shown(true, ""));
            CollectionAssert.AreEqual(new[] { "_MainTex", "_MainTex_ST" }, Shown(false, "_maintex"), "Search ignores case");
            CollectionAssert.Contains(Shown(false, "smoothness"), "_Glossiness", "Search also looks at the inspector name");
            Assert.IsEmpty(Shown(true, "_MainTex"));
        }

        // ---------------------------------------------------------------- Editing

        [Test]
        public void SetWritesEveryKindOfValueWithUndo()
        {
            void Check(string label, object value)
            {
                Material a = Mat(), b = Mat();
                var row = Find(a, b, label);
                object before = MaterialComparison.Value(a, row);
                Undo.IncrementCurrentGroup();

                MaterialComparison.Set(a, row, value);
                Assert.AreEqual(value, MaterialComparison.Value(a, row), label);
                CollectionAssert.AreEqual(new[] { label }, Differing(a, b), label);

                Undo.PerformUndo();
                Assert.AreEqual(before, MaterialComparison.Value(a, row), label + " after Undo");
            }

            Check("_Color", Color.red);
            Check("_Glossiness", 0.123f);
            Check("_MainTex", Texture2D.whiteTexture);
            Check("_MainTex_ST", new Vector4(2f, 3f, 0.25f, 0.5f));
            Check("Render Queue", 3000);
            Check("GPU Instancing", true);
            Check("Double Sided GI", true);
            Check("Keywords", "_EMISSION _NORMALMAP");
        }

        [Test]
        public void SetHandlesEmptyTextureAndKeywordText()
        {
            Material a = Mat(), b = Mat();
            a.SetTexture("_MainTex", Texture2D.whiteTexture);
            MaterialComparison.Set(a, Find(a, b, "_MainTex"), null);
            Assert.IsNull(a.GetTexture("_MainTex"));

            var keywords = Find(a, b, "Keywords");
            MaterialComparison.Set(a, keywords, "  _NORMALMAP   _EMISSION ");
            Assert.IsTrue(a.IsKeywordEnabled("_EMISSION") && a.IsKeywordEnabled("_NORMALMAP"));
            Assert.AreEqual("_EMISSION _NORMALMAP", MaterialComparison.Value(a, keywords));
            MaterialComparison.Set(a, keywords, "");
            Assert.IsEmpty(a.shaderKeywords);
        }

        // ---------------------------------------------------------------- Window

        [Test]
        public void WindowNoticesEditsUndoAndShaderChanges()
        {
            var window = ScriptableObject.CreateInstance<MaterialComparison>();
            created.Add(window);
            window.A = Mat();
            window.B = Mat();
            window.OnlyDifferences = true;
            string[] Shown()
            {
                window.Refresh();
                return window.Visible.Select(r => r.Label).ToArray();
            }

            Assert.IsEmpty(Shown());

            Undo.IncrementCurrentGroup();
            MaterialComparison.Set(window.B, Find(window.A, window.B, "_Color"), Color.red);
            CollectionAssert.AreEqual(new[] { "_Color" }, Shown());

            Undo.PerformUndo();
            Assert.IsEmpty(Shown(), "Undo should be noticed");

            window.Search = "queue";
            window.OnlyDifferences = false;
            CollectionAssert.AreEqual(new[] { "Render Queue" }, Shown());

            window.Search = "";
            window.OnlyDifferences = true;
            window.B.shader = Shader.Find("Unlit/Color");
            CollectionAssert.Contains(Shown(), "Shader");
            CollectionAssert.Contains(Shown(), "_Glossiness");
        }
    }
}
