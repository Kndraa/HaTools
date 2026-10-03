// Lighting Test Scene tests. Full docs: CLAUDE.md > Tools > Lighting Test Scene.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HaTools.Tests
{
    // The saved "main" scenes the tool leaves and returns to, and the cleanup shared by the test classes below
    static class LightingTestMainScene
    {
        internal const string Path = "Assets/HaToolsTestMain.unity";
        internal const string SecondPath = "Assets/HaToolsTestSecond.unity";
        internal const string ThirdPath = "Assets/HaToolsTestThird.unity";

        // The tool only works from scenes that are saved to a file. Returns the avatar in the new main scene.
        internal static GameObject Open()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var avatar = new GameObject("Avatar");
            GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(avatar.transform, false);
            EditorSceneManager.SaveScene(scene, Path);
            return avatar;
        }

        internal static void RemoveGenerated()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            LightingTestScene.DeleteTestScene();
            AssetDatabase.DeleteAsset(Path);
            AssetDatabase.DeleteAsset(SecondPath);
            AssetDatabase.DeleteAsset(ThirdPath);
            EditorUserSettings.SetConfigValue(LightingTestScene.ReturnKey, "");
        }
    }

    public class LightingTestSceneTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        [OneTimeSetUp]
        public void BuildOnce()
        {
            LightingTestMainScene.Open();
            LightingTestScene.BuildScene();
        }

        [OneTimeTearDown]
        public void RemoveGeneratedAssets() => LightingTestMainScene.RemoveGenerated();

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
            // Built beside the main scene and made the active one
            Assert.AreEqual(LightingTestScene.ScenePath, SceneManager.GetActiveScene().path);
            Assert.IsTrue(SceneManager.GetSceneByPath(LightingTestMainScene.Path).isLoaded);
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

            Assert.AreEqual(LightmapBakeType.Realtime, LampAt(6).lightmapBakeType, "G: realtime lamp");
            Assert.AreEqual(LightRenderMode.ForcePixel, LampAt(6).renderMode, "G: pixel light");
            Assert.Greater(LampAt(6).intensity, LampAt(2).intensity, "G: brighter than C");
            Assert.AreEqual(LightmapBakeType.Baked, LampAt(7).lightmapBakeType, "H: baked lamp");

            Assert.AreEqual(LightmapBakeType.Baked, LampAt(8).lightmapBakeType, "I: baked lamp");
            var behindAndBelow = LampAt(8).transform.localPosition;
            Assert.Less(behindAndBelow.z, -0.5f, "I: behind the avatar");
            Assert.Less(behindAndBelow.y, 0.6f, "I: below the avatar's body");
        }

        [Test]
        public void OnlyTheReflectionStationHasAReflectionProbe()
        {
            var probe = Object.FindObjectsOfType<ReflectionProbe>().Single();
            Assert.AreEqual(Station(7), probe.transform.parent);
            Assert.AreEqual(ReflectionProbeMode.Baked, probe.mode);
            // An avatar at the next station must not pick it up
            Assert.IsTrue(probe.bounds.Contains(LightingTestScene.StationPos(7) + Vector3.up));
            Assert.IsFalse(probe.bounds.Contains(LightingTestScene.StationPos(6) + Vector3.up));
        }

        [Test]
        public void OptionalSunIsDisabled()
        {
            var sun = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.GetComponent<Light>() != null);
            Assert.IsFalse(sun.activeSelf);
        }

        // ---------------------------------------------------------------- Stations

        [Test]
        public void MoveToStationMovesObjectsInTestSceneWithUndo()
        {
            var avatar = Create("Avatar");
            var start = new Vector3(1f, 2f, 3f);
            var startRotation = Quaternion.Euler(0f, 90f, 0f);
            avatar.transform.SetPositionAndRotation(start, startRotation);

            Undo.IncrementCurrentGroup();
            Assert.IsTrue(LightingTestScene.MoveToStation(avatar.transform, 2));
            Assert.AreEqual(LightingTestScene.StationPos(2), avatar.transform.position);
            Assert.Less(Quaternion.Angle(Quaternion.identity, avatar.transform.rotation), 0.01f);

            Undo.PerformUndo();
            Assert.AreEqual(start, avatar.transform.position);
            Assert.Less(Quaternion.Angle(startRotation, avatar.transform.rotation), 0.01f);
        }

        [Test]
        public void MoveToStationIgnoresObjectsInOtherScenes()
        {
            var avatar = Create("Avatar in user's scene");
            SceneManager.MoveGameObjectToScene(avatar, SceneManager.GetSceneByPath(LightingTestMainScene.Path));
            var start = new Vector3(5f, 0f, 5f);
            avatar.transform.position = start;

            Assert.IsFalse(LightingTestScene.MoveToStation(avatar.transform, 1));
            Assert.AreEqual(start, avatar.transform.position);
        }
    }

    // Going from the main scene to the test scene and back
    public class LightingTestSceneSwitchTests
    {
        GameObject avatar;

        [SetUp]
        public void OpenMainScene() => avatar = LightingTestMainScene.Open();

        [TearDown]
        public void RemoveGeneratedAssets() => LightingTestMainScene.RemoveGenerated();

        [Test]
        public void EnterOpensTheTestSceneWithACopyAndClosesTheMainScene()
        {
            var copy = LightingTestScene.EnterTestScene(avatar);

            Assert.AreEqual(1, SceneManager.sceneCount);
            Assert.AreEqual(LightingTestScene.ScenePath, SceneManager.GetActiveScene().path);
            Assert.IsNotNull(GameObject.Find(LightingTestScene.StationsRootName));

            Assert.AreEqual(SceneManager.GetActiveScene(), copy.scene);
            Assert.AreEqual("Avatar" + LightingTestScene.CopySuffix, copy.name);
            Assert.IsNotNull(copy.GetComponentInChildren<MeshRenderer>());
        }

        [Test]
        public void CopyIsShownAtItsWorldSizeEvenIfTheAvatarWasHiddenUnderAScaledParent()
        {
            var parent = new GameObject("Scaled parent");
            parent.transform.localScale = Vector3.one * 2f;
            avatar.transform.SetParent(parent.transform, false);
            avatar.SetActive(false);

            var copy = LightingTestScene.EnterTestScene(avatar);

            Assert.IsNull(copy.transform.parent);
            Assert.IsTrue(copy.activeInHierarchy);
            Assert.AreEqual(2f, copy.transform.lossyScale.x, 1e-4f);
        }

        [Test]
        public void ReturnReopensEveryMainSceneAndFindsTheAvatar()
        {
            // The avatar's scene, a second scene that is the active one, and a third that is listed but not loaded
            var second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(second, LightingTestMainScene.SecondPath);
            var third = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(third, LightingTestMainScene.ThirdPath);
            EditorSceneManager.CloseScene(third, false);
            SceneManager.SetActiveScene(second);

            LightingTestScene.EnterTestScene(avatar);
            var original = LightingTestScene.ReturnToMainScene();

            Assert.AreEqual(3, SceneManager.sceneCount);
            Assert.AreEqual(LightingTestMainScene.SecondPath, SceneManager.GetActiveScene().path);
            Assert.IsTrue(SceneManager.GetSceneByPath(LightingTestMainScene.Path).isLoaded);
            Assert.IsFalse(SceneManager.GetSceneByPath(LightingTestMainScene.ThirdPath).isLoaded);

            Assert.IsNotNull(original);
            Assert.AreEqual("Avatar", original.name);
            Assert.AreEqual(LightingTestMainScene.Path, original.scene.path);

            // The test scene is kept for next time
            Assert.IsTrue(File.Exists(LightingTestScene.ScenePath));
        }

        [Test]
        public void ReturnSavesTheTestSceneWithoutTheCopy()
        {
            LightingTestScene.EnterTestScene(avatar);
            LightingTestScene.ReturnToMainScene();

            var test = EditorSceneManager.OpenScene(LightingTestScene.ScenePath, OpenSceneMode.Additive);
            Assert.AreEqual(0, LightingTestScene.Copies(test).Count());
            Assert.IsNotNull(test.GetRootGameObjects().SingleOrDefault(g => g.name == LightingTestScene.StationsRootName));
        }

        [Test]
        public void EnterReplacesACopySavedInTheTestScene()
        {
            LightingTestScene.EnterTestScene(avatar);
            // Saving by hand (Ctrl+S) keeps the copy in the scene file, and opening another scene by hand skips the return
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            EditorSceneManager.OpenScene(LightingTestMainScene.Path);

            LightingTestScene.EnterTestScene(GameObject.Find("Avatar"));
            Assert.AreEqual(1, LightingTestScene.Copies(SceneManager.GetActiveScene()).Count());
        }

        [Test]
        public void BuildReplacesAssetsLeftByAnEarlierBuild()
        {
            LightingTestScene.EnterTestScene(avatar);
            var original = LightingTestScene.ReturnToMainScene();
            // The scene deleted by hand: its materials and lighting settings are still in the folder
            AssetDatabase.DeleteAsset(LightingTestScene.ScenePath);

            LightingTestScene.EnterTestScene(original);
            Assert.IsNotNull(Lightmapping.lightingSettings);
            Assert.AreEqual(LightingTestScene.SettingsPath, AssetDatabase.GetAssetPath(Lightmapping.lightingSettings));
        }

        [Test]
        public void ReturnCancelsABakeThatIsStillRunning()
        {
            LightingTestScene.EnterTestScene(avatar);
            Lightmapping.lightingSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU; // CI has no GPU
            Assert.IsTrue(Lightmapping.BakeAsync(), "Bake didn't start");

            var original = LightingTestScene.ReturnToMainScene();
            Assert.IsFalse(Lightmapping.isRunning);
            Assert.AreEqual(LightingTestMainScene.Path, SceneManager.GetActiveScene().path);

            // The window bakes when it opens a test scene that has no bake
            LightingTestScene.EnterTestScene(original);
            Assert.IsNull(Lightmapping.lightingDataAsset);
        }

        [Test]
        public void ReturnSkipsScenesThatWereDeletedMeanwhile()
        {
            var second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(second, LightingTestMainScene.SecondPath);

            SceneManager.SetActiveScene(avatar.scene);

            LightingTestScene.EnterTestScene(avatar);
            AssetDatabase.DeleteAsset(LightingTestMainScene.Path);

            // The active scene is gone, so the remaining one becomes active
            Assert.IsNull(LightingTestScene.ReturnToMainScene(), "the avatar was in the deleted scene");
            Assert.AreEqual(1, SceneManager.sceneCount);
            Assert.AreEqual(LightingTestMainScene.SecondPath, SceneManager.GetActiveScene().path);
        }

        [Test]
        public void ReturnWithoutARememberedSceneOpensANewOne()
        {
            LightingTestScene.EnterTestScene(avatar);
            EditorUserSettings.SetConfigValue(LightingTestScene.ReturnKey, "");

            Assert.IsNull(LightingTestScene.ReturnToMainScene());
            Assert.AreEqual(1, SceneManager.sceneCount);
            Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        }

        [Test]
        public void DeleteRemovesEverythingTheToolGenerated()
        {
            LightingTestScene.EnterTestScene(avatar);
            LightingTestScene.ReturnToMainScene();
            LightingTestScene.DeleteTestScene();

            Assert.IsFalse(AssetDatabase.IsValidFolder(LightingTestScene.Folder));
            Assert.IsFalse(AssetDatabase.IsValidFolder(LightingTestScene.ParentFolder));
        }

        [Test]
        public void DeleteKeepsTheParentFolderWhenItHoldsSomethingElse()
        {
            LightingTestScene.EnterTestScene(avatar);
            LightingTestScene.ReturnToMainScene();
            AssetDatabase.CreateFolder(LightingTestScene.ParentFolder, "OtherTool");

            LightingTestScene.DeleteTestScene();

            Assert.IsFalse(AssetDatabase.IsValidFolder(LightingTestScene.Folder));
            Assert.IsTrue(AssetDatabase.IsValidFolder(LightingTestScene.ParentFolder + "/OtherTool"));
            AssetDatabase.DeleteAsset(LightingTestScene.ParentFolder);
        }
    }

    // Bakes the scene once and checks each station's light probes give the lighting its name promises
    public class LightingTestSceneBakeTests
    {
        [OneTimeSetUp]
        public void BuildAndBake()
        {
            LightingTestScene.EnterTestScene(LightingTestMainScene.Open());
            // CI machines have no GPU; the CPU lightmapper also gives the same result on every machine
            Lightmapping.lightingSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            Assert.IsTrue(Lightmapping.Bake(), "Bake failed");
        }

        [OneTimeTearDown]
        public void RemoveGeneratedAssets() => LightingTestMainScene.RemoveGenerated();

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
            Assert.Greater(Front(7).grayscale, dark * 3f, "H: baked lamp should be much brighter than D");
        }

        [Test]
        public void ReflectionStationHasABakedReflection()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Baking a reflection probe needs a graphics device");
            Assert.IsNotNull(Object.FindObjectOfType<ReflectionProbe>().bakedTexture, "H: the reflection probe has no baked cubemap");
        }

        [Test]
        public void RealtimeLampsAreNotBakedIntoProbes()
        {
            // B, C and G get their lamp at runtime only, so their probes hold the same ambient as D.
            // This also catches light leaking in from the neighbouring stations.
            float dark = Front(3).grayscale;
            Assert.Less(Front(1).grayscale, dark * 1.5f + 1e-4f, "B");
            Assert.Less(Front(2).grayscale, dark * 1.5f + 1e-4f, "C");
            Assert.Less(Front(6).grayscale, dark * 1.5f + 1e-4f, "G");
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

        [Test]
        public void BacklightStationIsLitFromBehindAndBelow()
        {
            var towardsLamp = new Vector3(0f, -1f, -1f).normalized;
            float back = Irradiance(8, towardsLamp).grayscale, front = Irradiance(8, -towardsLamp).grayscale;
            Assert.Greater(back, Front(3).grayscale * 3f, "I: baked lamp should be much brighter than D");
            Assert.Greater(back, front * 2f, "I: the side facing back and down should be the bright one");
        }

        [Test]
        public void BakeIsKeptWhenSwitchingToTheMainSceneAndBack()
        {
            var original = LightingTestScene.ReturnToMainScene();
            Assert.AreEqual(LightingTestMainScene.Path, SceneManager.GetActiveScene().path);

            // Leaves the test scene open and baked again for the other tests
            LightingTestScene.EnterTestScene(original);
            Assert.IsNotNull(Lightmapping.lightingDataAsset, "The test scene lost its bake");
            Assert.Greater(Front(0).grayscale, Front(3).grayscale * 3f, "A's probes should still hold the baked lamp");
        }
    }
}
