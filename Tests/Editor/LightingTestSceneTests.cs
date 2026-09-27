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
        public void RemoveGeneratedAssets()
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
        public void CreatesAllStationsWithLightProbes()
        {
            var root = GameObject.Find(LightingTestScene.StationsRootName);
            Assert.IsNotNull(root);
            Assert.AreEqual(LightingTestScene.StationNames.Length, root.transform.childCount);

            for (int i = 0; i < LightingTestScene.StationNames.Length; i++)
            {
                var st = Station(i);
                Assert.AreEqual(LightingTestScene.StationNames[i], st.name);
                Assert.AreEqual(LightingTestScene.StationPos(i), st.position);
                Assert.AreEqual(125, st.GetComponentInChildren<LightProbeGroup>().probePositions.Length, st.name);
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
            avatar.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, 90f, 0f));

            Undo.IncrementCurrentGroup();
            Assert.AreEqual(1, LightingTestScene.MoveToStation(new[] { avatar.transform }, 2));
            Assert.AreEqual(LightingTestScene.StationPos(2), avatar.transform.position);
            Assert.Less(Quaternion.Angle(Quaternion.identity, avatar.transform.rotation), 0.01f);

            Undo.PerformUndo();
            Assert.AreEqual(start, avatar.transform.position);
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
}
