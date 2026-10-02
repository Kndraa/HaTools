// Shader Fallback Preview tests. Full docs: CLAUDE.md > Tools > Shader Fallback Preview.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Kind = HaTools.ShaderFallbackPreview.Kind;
using Mode = HaTools.ShaderFallbackPreview.Mode;

namespace HaTools.Tests
{
    public class ShaderFallbackPreviewTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        T Track<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        Material Mat(string shader, string tag = null)
        {
            var m = Track(new Material(Shader.Find(shader)) { name = "Test " + shader });
            if (tag != null) m.SetOverrideTag("VRCFallback", tag);
            return m;
        }

        // Avatar root with two cubes sharing one material, and a third with another
        GameObject Avatar(Material shared, Material other)
        {
            var root = Track(new GameObject("Test Avatar"));
            for (int i = 0; i < 3; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                cube.transform.localPosition = new Vector3(0f, i, 0f);
                cube.GetComponent<Renderer>().sharedMaterial = i < 2 ? shared : other;
            }
            return root;
        }

        static ShaderFallbackPreview.Fallback Tag(string tag) => ShaderFallbackPreview.Resolve(tag, "Custom/Anything", false, false, false);
        static ShaderFallbackPreview.Fallback Name(string shader, bool ramp = false, bool blend = false, bool test = false) =>
            ShaderFallbackPreview.Resolve("", shader, ramp, blend, test);

        // ---------------------------------------------------------------- Rules: VRCFallback tag
        // (expected values are passed as names: test methods are public, the rule enums are internal)

        [TestCase("Toon", "Toon", "Opaque")]
        [TestCase("ToonCutout", "Toon", "Cutout")]
        [TestCase("Unlit", "Unlit", "Opaque")]
        [TestCase("UnlitCutout", "Unlit", "Cutout")]
        [TestCase("UnlitFade", "Unlit", "Fade")]
        [TestCase("VertexLit", "VertexLit", "Opaque")]
        [TestCase("MobileToon", "MobileToon", "Opaque")]
        [TestCase("Particle", "Particle", "Opaque")]
        [TestCase("Sprite", "Sprite", "Opaque")]
        [TestCase("Matcap", "Matcap", "Opaque")]
        [TestCase("Transparent", "Standard", "Transparent")]
        [TestCase("Cutout", "Standard", "Cutout")]
        [TestCase("SomethingElse", "Standard", "Opaque")]
        public void TagPicksTypeAndMode(string tag, string kind, string mode)
        {
            var f = Tag(tag);
            Assert.AreEqual(kind, f.Kind.ToString());
            Assert.AreEqual(mode, f.Mode.ToString());
            Assert.IsTrue(f.FromTag);
        }

        [TestCase("ToonTransparent", "Transparent")]
        [TestCase("ToonFade", "Fade")]
        public void TransparentToonFallsBackToUnlit(string tag, string mode)
        {
            var f = Tag(tag);
            Assert.AreEqual(Kind.Unlit, f.Kind);
            Assert.AreEqual(mode, f.Mode.ToString());
        }

        [Test]
        public void ToonDoubleSidedCutoutCombines()
        {
            var f = Tag("ToonDoubleSidedCutout");
            Assert.AreEqual(Kind.Toon, f.Kind);
            Assert.AreEqual(Mode.Cutout, f.Mode);
            Assert.IsTrue(f.DoubleSided);
        }

        [Test]
        public void SpecialTags()
        {
            Assert.AreEqual(Kind.Hidden, Tag("Hidden").Kind);
            Assert.AreEqual(Kind.ToonStandard, Tag("toonstandard").Kind);
            Assert.AreEqual(Kind.ToonStandardOutline, Tag("toonstandardoutline").Kind);
        }

        [Test]
        public void TagWinsOverShaderName()
        {
            var f = ShaderFallbackPreview.Resolve("Unlit", "Standard", true, false, false);
            Assert.AreEqual(Kind.Unlit, f.Kind);
        }

        // ---------------------------------------------------------------- Rules: no tag (old system)

        [TestCase("Standard")]
        [TestCase("Sprites/Default")]
        [TestCase("Legacy Shaders/Transparent/Cutout/Diffuse")]
        [TestCase("Toon/Lit Cutout")]
        public void BuiltInShadersAreKept(string shader) => Assert.AreEqual(Kind.Keep, Name(shader).Kind);

        [TestCase("Custom/My Toon", "Toon", "Opaque")]
        [TestCase("Custom/My Toon Cutout", "Toon", "Cutout")]
        [TestCase("Custom/My Toon Transparent", "Unlit", "Transparent")]
        [TestCase("Custom/Unlit Fade", "Unlit", "Fade")]
        [TestCase("Custom/Sparkle Particle", "Particle", "Opaque")]
        [TestCase("Custom/MatCap", "Matcap", "Opaque")]
        [TestCase("Custom/Glass Transparent", "Standard", "Transparent")]
        [TestCase("Custom/Plain", "Standard", "Opaque")]
        [TestCase("Custom/my toon", "Standard", "Opaque")] // matching is case-sensitive
        public void ShaderNamePicksTypeAndMode(string shader, string kind, string mode)
        {
            var f = Name(shader);
            Assert.AreEqual(kind, f.Kind.ToString());
            Assert.AreEqual(mode, f.Mode.ToString());
            Assert.IsFalse(f.FromTag);
        }

        [Test]
        public void PropertiesAndKeywordsAreSearched()
        {
            Assert.AreEqual(Kind.Toon, Name("Custom/Plain", ramp: true).Kind);
            Assert.AreEqual(Mode.Transparent, Name("Custom/Plain", blend: true).Mode);
            Assert.AreEqual(Mode.Cutout, Name("Custom/Plain", test: true).Mode);
        }

        [Test]
        public void OutlineOnlyCountsForToon()
        {
            Assert.IsTrue(Name("Custom/Toon Outline").Outline);
            Assert.IsFalse(Name("Custom/Outline").Outline);
        }

        // ---------------------------------------------------------------- Fallback materials

        [Test]
        public void KeptMaterialIsReused()
        {
            var src = Mat("Standard");
            var entry = new ShaderFallbackPreview.Entry();
            Assert.AreSame(src, ShaderFallbackPreview.CreateFallbackMaterial(src, entry));
            Assert.AreEqual("Standard", entry.PreviewShader);
        }

        [Test]
        public void TagFallbackUsesMatchingShaderAndCopiesTexture()
        {
            var tex = Track(new Texture2D(4, 4));
            var src = Mat("Unlit/Texture", "UnlitCutout");
            src.mainTexture = tex;
            src.mainTextureScale = new Vector2(2f, 3f);

            var entry = new ShaderFallbackPreview.Entry();
            var fb = Track(ShaderFallbackPreview.CreateFallbackMaterial(src, entry));

            Assert.AreNotSame(src, fb);
            Assert.AreEqual("Unlit/Transparent Cutout", fb.shader.name);
            Assert.AreSame(tex, fb.mainTexture);
            Assert.AreEqual(new Vector2(2f, 3f), fb.mainTextureScale);
            Assert.AreEqual(HideFlags.DontSave, fb.hideFlags);
            StringAssert.EndsWith(ShaderFallbackPreview.MaterialSuffix, fb.name);
            Assert.AreEqual("Unlit/Texture", entry.Shader);
            Assert.AreEqual("Unlit/Transparent Cutout", entry.PreviewShader);
        }

        [Test]
        public void StandardFallbackGetsRenderingMode()
        {
            var src = Mat("Unlit/Color", "Transparent");
            var fb = Track(ShaderFallbackPreview.CreateFallbackMaterial(src, new ShaderFallbackPreview.Entry()));
            Assert.AreEqual("Standard", fb.shader.name);
            Assert.AreEqual(3f, fb.GetFloat("_Mode"));
            Assert.AreEqual(3000, fb.renderQueue);
            Assert.IsTrue(fb.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"));
            Assert.AreEqual(0f, fb.GetFloat("_ZWrite"));
        }

        [Test]
        public void HiddenMaterialDrawsNothing()
        {
            var src = Mat("Unlit/Color", "Hidden");
            var entry = new ShaderFallbackPreview.Entry();
            var fb = Track(ShaderFallbackPreview.CreateFallbackMaterial(src, entry));
            Assert.IsTrue(fb.IsKeywordEnabled("_ALPHATEST_ON"));
            Assert.AreEqual(0f, fb.color.a);
            Assert.Greater(fb.GetFloat("_Cutoff"), 0f);
            Assert.IsNotEmpty(entry.Note);
        }

        [Test]
        public void MissingVrchatShaderShowsStandardWithNote()
        {
            // Only meaningful without the VRChat SDK (as in CI)
            if (Shader.Find("VRChat/Mobile/Toon Standard") != null) Assert.Ignore("VRChat SDK is installed.");
            var entry = new ShaderFallbackPreview.Entry();
            var fb = Track(ShaderFallbackPreview.CreateFallbackMaterial(Mat("Unlit/Color", "toonstandard"), entry));
            Assert.AreEqual("Standard", fb.shader.name);
            StringAssert.Contains("VRChat SDK", entry.Note);
        }

        [Test]
        public void NotesAreCombinedNotReplaced()
        {
            if (Shader.Find("VRChat/Mobile/Toon Lit") != null) Assert.Ignore("VRChat SDK is installed.");
            // "Toon" in the name picks the SDK's Toon Lit (missing here), and there is no _MainTex or _Color
            var shader = Track(ShaderUtil.CreateShaderAsset("Shader \"HaTools Tests/Plain Toon\" { SubShader { Pass { } } }"));
            var src = Track(new Material(shader) { name = "Test plain toon" });

            var entry = new ShaderFallbackPreview.Entry();
            Track(ShaderFallbackPreview.CreateFallbackMaterial(src, entry));
            StringAssert.Contains("VRChat SDK", entry.Note);
            StringAssert.Contains("matcap", entry.Note);
        }

        // ---------------------------------------------------------------- Preview copy

        [Test]
        public void PreviewIsUnsavedCopyBesideTheAvatarWithFallbacks()
        {
            var shared = Mat("Unlit/Texture", "UnlitCutout");
            var kept = Mat("Standard");
            var avatar = Avatar(shared, kept);
            var entries = new List<ShaderFallbackPreview.Entry>();

            var copy = Track(ShaderFallbackPreview.CreatePreview(avatar, entries));

            Assert.AreEqual(avatar.name + ShaderFallbackPreview.CopySuffix, copy.name);
            Assert.AreEqual(avatar.scene, copy.scene);
            Assert.Greater(Vector3.Distance(avatar.transform.position, copy.transform.position), 1f);
            Assert.IsTrue(copy.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.hideFlags == HideFlags.DontSave));

            // One fallback per source material, shared the same way; the avatar itself is untouched
            var copyMats = copy.GetComponentsInChildren<Renderer>().Select(r => r.sharedMaterial).ToArray();
            Assert.AreEqual("Unlit/Transparent Cutout", copyMats[0].shader.name);
            Assert.AreSame(copyMats[0], copyMats[1]);
            Assert.AreSame(kept, copyMats[2]);
            Assert.AreEqual(2, entries.Count);
            Assert.IsTrue(avatar.GetComponentsInChildren<Renderer>().Take(2).All(r => r.sharedMaterial == shared));

            var fallback = copyMats[0];
            ShaderFallbackPreview.DestroyPreview(copy);
            Assert.IsTrue(copy == null);
            Assert.IsTrue(fallback == null, "Fallback material should be destroyed with the copy");
            Assert.IsTrue(kept != null && shared != null, "The avatar's own materials must survive");
        }

        [Test]
        public void PreviewCopyHasNoScripts()
        {
            var avatar = Avatar(Mat("Standard"), Mat("Standard"));
            avatar.AddComponent<Dependent>(); // also adds Required through [RequireComponent]
            var copy = Track(ShaderFallbackPreview.CreatePreview(avatar, new List<ShaderFallbackPreview.Entry>()));

            Assert.IsEmpty(copy.GetComponentsInChildren<MonoBehaviour>(true));
            Assert.IsNotNull(avatar.GetComponent<Dependent>(), "The avatar keeps its scripts");
            Assert.AreEqual(3, copy.GetComponentsInChildren<Renderer>().Length);
        }

        [Test]
        public void InactiveAvatarGivesVisibleCopy()
        {
            var avatar = Avatar(Mat("Standard"), Mat("Standard"));
            avatar.SetActive(false);
            var copy = Track(ShaderFallbackPreview.CreatePreview(avatar, new List<ShaderFallbackPreview.Entry>()));
            Assert.IsTrue(copy.activeSelf);
        }

        [Test]
        public void AvatarUnderDisabledParentGivesVisibleCopy()
        {
            var parent = Track(new GameObject("Disabled parent"));
            parent.transform.localScale = Vector3.one * 2f;
            var avatar = Avatar(Mat("Standard"), Mat("Standard"));
            avatar.transform.SetParent(parent.transform, false);
            parent.SetActive(false);

            var copy = Track(ShaderFallbackPreview.CreatePreview(avatar, new List<ShaderFallbackPreview.Entry>()));
            Assert.IsTrue(copy.activeInHierarchy);
            Assert.AreEqual(avatar.scene, copy.scene);
            Assert.Less(Vector3.Distance(avatar.transform.lossyScale, copy.transform.lossyScale), 1e-4f, "The copy keeps the avatar's size");
        }

        class Required : MonoBehaviour { }

        [RequireComponent(typeof(Required))]
        class Dependent : MonoBehaviour { }
    }
}
