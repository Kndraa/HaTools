// Lighting Test Scene tests. Full docs: CLAUDE.md > Tools > Lighting Test Scene.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kndra.Tools.Tests
{
    public class LightingTestSceneTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        [OneTimeSetUp]
        public void BuildOnce() => LightingTestScene.BuildScene();

        [OneTimeTearDown]
        public void RemoveGeneratedAssets() => RemoveGenerated();

        internal static void RemoveGenerated()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(LightingTestScene.Folder);
            if (AssetDatabase.FindAssets("", new[] { LightingTestScene.ParentFolder }).Length == 0)
                AssetDatabase.DeleteAsset(LightingTestScene.ParentFolder);
        }

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (var go in created) if (go != null) Object.DestroyImmediate(go);
            created.Clear();
        }

        GameObject Create(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        static Transform Station(int i) => GameObject.Find(LightingTestScene.StationsRootName).transform.GetChild(i);

        // ---------------------------------------------------------------- Build

        [Test]
        public void SavesSceneAndLightingSettings()
        {
            Assert.AreEqual(LightingTestScene.ScenePath, SceneManager.GetActiveScene().path);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(LightingTestScene.ScenePath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingTestScene.SettingsPath));
        }

        [Test]
        public void CreatesAllStationsWithProbesAroundTheAvatar()
        {
            var root = GameObject.Find(LightingTestScene.StationsRootName);
            Assert.IsNotNull(root);
            Assert.AreEqual(LightingTestScene.StationNames.Length, root.transform.childCount);

            for (int i = 0; i < LightingTestScene.StationNames.Length; i++)
            {
                var st = Station(i);
                Assert.AreEqual(LightingTestScene.StationNames[i], st.name);
                Assert.AreEqual(LightingTestScene.StationPos(i), st.position);

                // Probes must enclose an avatar standing at the station, or it gets lit by extrapolated probes
                var probes = st.GetComponentInChildren<LightProbeGroup>().probePositions;
                var bounds = new Bounds(probes[0], Vector3.zero);
                foreach (var p in probes) bounds.Encapsulate(p);
                Assert.IsTrue(bounds.Contains(new Vector3(-1f, 0.3f, -1f)) && bounds.Contains(new Vector3(1f, 2.1f, 1f)),
                    $"{st.name}: probes span {bounds.min} to {bounds.max}");
            }
        }

        [Test]
        public void StationLightsMatchTheirDescriptions()
        {
            Light LampAt(int i) => Station(i).GetComponentInChildren<Light>();

            Assert.AreEqual(LightmapBakeType.Baked, LampAt(0).lightmapBakeType, "A: baked lamp");
            Assert.AreEqual(LightmapBakeType.Realtime, LampAt(1).lightmapBakeType, "B: realtime lamp");
            Assert.AreEqual(LightRenderMode.ForceVertex, LampAt(1).renderMode, "B: vertex light");
            Assert.AreEqual(LightmapBakeType.Realtime, LampAt(2).lightmapBakeType, "C: realtime lamp");
            Assert.AreEqual(LightRenderMode.ForcePixel, LampAt(2).renderMode, "C: pixel light");
            Assert.IsNull(LampAt(3), "D: ambient only");
            Assert.AreEqual(LightmapBakeType.Baked, LampAt(4).lightmapBakeType, "E: baked lamp");

            var f = Station(5).GetComponentsInChildren<Light>();
            Assert.AreEqual(2, f.Length, "F: two lamps");
            Assert.IsTrue(f.All(l => l.lightmapBakeType == LightmapBakeType.Baked), "F: both baked");
        }

        [Test]
        public void OptionalSunIsDisabled()
        {
            var sun = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.GetComponent<Light>() != null);
            Assert.IsFalse(sun.activeSelf);
        }

        // ---------------------------------------------------------------- Move

        [Test]
        public void MoveToStationMovesObjectsInTestSceneWithUndo()
        {
            var avatar = Create("Avatar");
            var start = new Vector3(1f, 2f, 3f);
            var startRotation = Quaternion.Euler(0f, 90f, 0f);
            avatar.transform.SetPositionAndRotation(start, startRotation);

            Undo.IncrementCurrentGroup();
            Assert.AreEqual(1, LightingTestScene.MoveToStation(new[] { avatar.transform }, 2));
            Assert.AreEqual(LightingTestScene.StationPos(2), avatar.transform.position);
            Assert.Less(Quaternion.Angle(Quaternion.identity, avatar.transform.rotation), 0.01f);

            Undo.PerformUndo();
            Assert.AreEqual(start, avatar.transform.position);
            Assert.Less(Quaternion.Angle(startRotation, avatar.transform.rotation), 0.01f);
        }

        [Test]
        public void MoveToStationIgnoresObjectsInOtherScenes()
        {
            var otherScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var avatar = Create("Avatar in user's scene");
                SceneManager.MoveGameObjectToScene(avatar, otherScene);
                var start = new Vector3(5f, 0f, 5f);
                avatar.transform.position = start;

                Assert.AreEqual(0, LightingTestScene.MoveToStation(new[] { avatar.transform }, 1));
                Assert.AreEqual(start, avatar.transform.position);
            }
            finally
            {
                EditorSceneManager.CloseScene(otherScene, true);
            }
        }

        // ---------------------------------------------------------------- Renderer check

        GameObject AvatarWithCubes(int count)
        {
            var root = Create("Test Avatar");
            for (int i = 0; i < count; i++)
                GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(root.transform, false);
            return root;
        }

        [Test]
        public void RendererCheckCountsRenderersWithoutAnchorSeparately()
        {
            var report = LightingTestScene.AnalyseRenderers(AvatarWithCubes(2));
            Assert.AreEqual(2, report.Renderers);
            Assert.AreEqual(2, report.Unanchored);
            Assert.AreEqual(2, report.SamplePoints);
        }

        [Test]
        public void RendererCheckTreatsSharedAnchorAsOnePoint()
        {
            var root = AvatarWithCubes(3);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.probeAnchor = root.transform;

            var report = LightingTestScene.AnalyseRenderers(root);
            Assert.AreEqual(0, report.Unanchored);
            Assert.AreEqual(1, report.SamplePoints);
        }

        [Test]
        public void RendererCheckCountsSharedAnchorAndUnanchoredRenderers()
        {
            var root = AvatarWithCubes(3);
            var renderers = root.GetComponentsInChildren<Renderer>();
            renderers[0].probeAnchor = root.transform;
            renderers[1].probeAnchor = root.transform;

            var report = LightingTestScene.AnalyseRenderers(root);
            Assert.AreEqual(1, report.Unanchored);
            Assert.AreEqual(2, report.SamplePoints);
        }

        [Test]
        public void RendererCheckFlagsRenderersIgnoringProbes()
        {
            var root = AvatarWithCubes(2);
            root.GetComponentsInChildren<Renderer>()[0].lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            Assert.AreEqual(1, LightingTestScene.AnalyseRenderers(root).ProbeWarnings);
        }

        [Test]
        public void RendererCheckIgnoresParticleRenderers()
        {
            var root = Create("Particles only");
            new GameObject("Particles", typeof(ParticleSystem)).transform.SetParent(root.transform, false);

            Assert.AreEqual(0, LightingTestScene.AnalyseRenderers(root).Renderers);
        }
    }

    // Bakes the scene once and checks each station's light probes give the lighting its name promises
    public class LightingTestSceneBakeTests
    {
        [OneTimeSetUp]
        public void BuildAndBake()
        {
            LightingTestScene.BuildScene();
            // CI machines have no GPU; the CPU lightmapper also gives the same result on every machine
            Lightmapping.lightingSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            Assert.IsTrue(Lightmapping.Bake(), "Bake failed");
        }

        [OneTimeTearDown]
        public void RemoveGeneratedAssets() => LightingTestSceneTests.RemoveGenerated();

        // Baked light reaching a surface at avatar chest height, facing the given direction
        static Color Irradiance(int station, Vector3 normal)
        {
            LightProbes.GetInterpolatedProbe(LightingTestScene.StationPos(station) + Vector3.up * 1.2f, null, out var sh);
            var result = new Color[1];
            sh.Evaluate(new[] { normal }, result);
            return result[0];
        }

        static Color Front(int station) => Irradiance(station, Vector3.forward); // the lamps are in front

        [Test]
        public void BakedLampsLightTheirStations()
        {
            float dark = Front(3).grayscale;
            Assert.Greater(Front(0).grayscale, dark * 3f, "A: baked lamp should be much brighter than D");
            Assert.Greater(Front(4).grayscale, dark * 3f, "E: baked lamp should be much brighter than D");
            Assert.Greater(Front(5).grayscale, dark * 3f, "F: baked lamps should be much brighter than D");
        }

        [Test]
        public void RealtimeLampsAreNotBakedIntoProbes()
        {
            // B and C get their lamp at runtime only, so their probes hold the same ambient as D.
            // This also catches light leaking in from the neighbouring stations.
            float dark = Front(3).grayscale;
            Assert.Less(Front(1).grayscale, dark * 1.5f + 1e-4f, "B");
            Assert.Less(Front(2).grayscale, dark * 1.5f + 1e-4f, "C");
        }

        [Test]
        public void WarmLampIsWarmAndWhiteLampIsNeutral()
        {
            var a = Front(0);
            Assert.Greater(a.r, a.b * 1.5f, $"A should be warm: {a}");

            var e = Front(4);
            float max = Mathf.Max(e.r, e.g, e.b), min = Mathf.Min(e.r, e.g, e.b);
            Assert.Less(max, min * 1.25f, $"E should be neutral: {e}");
        }

        [Test]
        public void SplitStationIsRedOnTheLeftAndBlueOnTheRight()
        {
            var left = Irradiance(5, Vector3.left);
            var right = Irradiance(5, Vector3.right);
            Assert.Greater(left.r, left.b, $"F: side facing -X should be red: {left}");
            Assert.Greater(right.b, right.r, $"F: side facing +X should be blue: {right}");
        }
    }
}
