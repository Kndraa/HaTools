// Lighting Test Scene: builds a scene of VRChat-style lighting stations to check avatar shaders without running VRChat. Full docs: CLAUDE.md > Tools > Lighting Test Scene.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kndra.Tools
{
    public static class LightingTestScene
    {
        const string Menu = KndraMenu.Root + "Lighting Test Scene/";
        const string MenuMoveA = Menu + "Move Selection to Station A (baked lamp)";
        const string MenuMoveB = Menu + "Move Selection to Station B (vertex light)";
        const string MenuMoveC = Menu + "Move Selection to Station C (pixel light)";
        const string MenuMoveD = Menu + "Move Selection to Station D (dark ambient)";
        const string MenuMoveE = Menu + "Move Selection to Station E (white lamp)";
        const string MenuMoveF = Menu + "Move Selection to Station F (red and blue lamps)";

        internal const string ParentFolder = "Assets/Kndra tools";
        internal const string Folder = ParentFolder + "/LightingTestScene";
        internal const string ScenePath = Folder + "/LightingTest.unity";
        internal const string SettingsPath = Folder + "/LightingTestSettings.lighting";
        internal const string StationsRootName = "Lighting Test Stations";
        const float Spacing = 15f;

        internal static readonly string[] StationNames =
        {
            "A - Baked warm lamp (light probes only)",
            "B - Realtime warm lamp, Not Important (vertex light)",
            "C - Realtime warm lamp, Important (pixel light)",
            "D - Dark world, ambient only",
            "E - Neutral white baked lamp",
            "F - Red and blue baked lamps (split lighting)",
        };

        static readonly Color Warm = new Color(1f, 0.7f, 0.35f);
        static readonly Color Red = new Color(1f, 0.15f, 0.1f);
        static readonly Color Blue = new Color(0.15f, 0.3f, 1f);

        internal static Vector3 StationPos(int i) => new Vector3(i * Spacing, 0f, 0f);

        // ---------------------------------------------------------------- Build

        [MenuItem(Menu + "Build Scene and Bake", priority = 0)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Lighting Test Scene",
                    "A lighting test scene already exists. Rebuild it?\n(Your avatar and its scene are not touched.)",
                    "Rebuild", "Cancel"))
                return;

            // A bake still running for the old scene would write into the files we're about to replace
            if (Lightmapping.isRunning) Lightmapping.Cancel();

            BuildScene();
            Lightmapping.BakeAsync();
            FrameStation(0);

            EditorUtility.DisplayDialog("Lighting Test Scene",
                "Scene built and baking has started (see the progress bar at the bottom right).\n\n" +
                "Next: drag your avatar into this scene, select it, and use\n" +
                "Tools > Kndra tools > Lighting Test Scene > Move Selection to Station ...", "OK");
        }

        // Creates and saves the scene, materials and lighting settings. No dialogs, no bake.
        internal static void BuildScene()
        {
            if (!AssetDatabase.IsValidFolder(ParentFolder)) AssetDatabase.CreateFolder("Assets", "Kndra tools");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(ParentFolder, "LightingTestScene");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Dark world environment: no skybox, dim flat ambient
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.07f);

            var floorMat = CreateMat("Floor", new Color(0.22f, 0.22f, 0.22f));
            var wallMat = CreateMat("Wall", new Color(0.32f, 0.30f, 0.28f));
            var refMat = CreateMat("ReferenceSphere", new Color(0.8f, 0.8f, 0.8f));
            var warmBulb = CreateMat("BulbWarm", Warm, "Unlit/Color");
            var whiteBulb = CreateMat("BulbWhite", Color.white, "Unlit/Color");
            var redBulb = CreateMat("BulbRed", Red, "Unlit/Color");
            var blueBulb = CreateMat("BulbBlue", Blue, "Unlit/Color");

            var root = new GameObject(StationsRootName);

            for (int i = 0; i < StationNames.Length; i++)
            {
                var st = new GameObject(StationNames[i]).transform;
                st.SetParent(root.transform, false);
                st.position = StationPos(i);

                // Floor (10 x 10 m) and back wall, both static so they take part in the bake
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Floor";
                floor.transform.SetParent(st, false);
                floor.GetComponent<Renderer>().sharedMaterial = floorMat;
                GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.ContributeGI);

                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Back Wall";
                wall.transform.SetParent(st, false);
                wall.transform.localPosition = new Vector3(0f, 2f, -3.5f);
                wall.transform.localScale = new Vector3(8f, 4f, 0.2f);
                wall.GetComponent<Renderer>().sharedMaterial = wallMat;
                GameObjectUtility.SetStaticEditorFlags(wall, StaticEditorFlags.ContributeGI);

                // Dynamic reference sphere: lit the same way an avatar is (probes + realtime lights)
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Reference Sphere (dynamic)";
                sphere.transform.SetParent(st, false);
                sphere.transform.localPosition = new Vector3(-1.3f, 1.4f, 0f);
                sphere.transform.localScale = Vector3.one * 0.4f;
                sphere.GetComponent<Renderer>().sharedMaterial = refMat;
                Object.DestroyImmediate(sphere.GetComponent<Collider>());

                AddLabel(st, StationNames[i]);
                AddProbes(st);

                var lampPos = new Vector3(1.5f, 1.8f, 1.2f);
                switch (i)
                {
                    case 0: AddLamp(st, "Warm Lamp (Baked)", lampPos, Warm, 2.5f, LightmapBakeType.Baked, LightRenderMode.Auto, warmBulb); break;
                    case 1: AddLamp(st, "Warm Lamp (Realtime, Not Important)", lampPos, Warm, 2.5f, LightmapBakeType.Realtime, LightRenderMode.ForceVertex, warmBulb); break;
                    case 2: AddLamp(st, "Warm Lamp (Realtime, Important)", lampPos, Warm, 2.5f, LightmapBakeType.Realtime, LightRenderMode.ForcePixel, warmBulb); break;
                    case 3: break; // ambient only
                    case 4: AddLamp(st, "White Lamp (Baked)", lampPos, Color.white, 2f, LightmapBakeType.Baked, LightRenderMode.Auto, whiteBulb); break;
                    case 5: // avatar's left (-X) red, right (+X) blue: shows whether a shader keeps the light's direction
                        AddLamp(st, "Red Lamp (Baked)", new Vector3(-2f, 1.6f, 1.5f), Red, 2.5f, LightmapBakeType.Baked, LightRenderMode.Auto, redBulb);
                        AddLamp(st, "Blue Lamp (Baked)", new Vector3(2f, 1.6f, 1.5f), Blue, 2.5f, LightmapBakeType.Baked, LightRenderMode.Auto, blueBulb);
                        break;
                }
            }

            // Optional realtime sun, off by default (turning it on lights every station)
            var sunGO = new GameObject("Optional Sun (Realtime) - disabled");
            sunGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            sunGO.SetActive(false);

            // Fast, low-resolution bake settings (we only care about light probes)
            AssetDatabase.DeleteAsset(SettingsPath);
            var ls = new LightingSettings();
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.autoGenerate = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 4f;
            ls.lightmapMaxSize = 512;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 128;
            ls.environmentSampleCount = 64;
            AssetDatabase.CreateAsset(ls, SettingsPath);
            Lightmapping.lightingSettings = ls;

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ---------------------------------------------------------------- Move avatar between stations

        [MenuItem(MenuMoveA, priority = 20)] static void MoveA() => MoveTo(0);
        [MenuItem(MenuMoveB, priority = 21)] static void MoveB() => MoveTo(1);
        [MenuItem(MenuMoveC, priority = 22)] static void MoveC() => MoveTo(2);
        [MenuItem(MenuMoveD, priority = 23)] static void MoveD() => MoveTo(3);
        [MenuItem(MenuMoveE, priority = 24)] static void MoveE() => MoveTo(4);
        [MenuItem(MenuMoveF, priority = 25)] static void MoveF() => MoveTo(5);

        [MenuItem(MenuMoveA, true)]
        [MenuItem(MenuMoveB, true)]
        [MenuItem(MenuMoveC, true)]
        [MenuItem(MenuMoveD, true)]
        [MenuItem(MenuMoveE, true)]
        [MenuItem(MenuMoveF, true)]
        static bool HasSelection() => Selection.activeTransform != null;

        static void MoveTo(int i)
        {
            if (MoveToStation(Selection.transforms, i) == 0)
            {
                EditorUtility.DisplayDialog("Lighting Test Scene",
                    "Select an object inside the lighting test scene.\n\n" +
                    "Build it with Tools > Kndra tools > Lighting Test Scene > Build Scene and Bake, " +
                    "then drag your avatar into it.", "OK");
                return;
            }
            FrameStation(i);
        }

        // Moves the objects that are inside the test scene (never the avatar in the user's own scene).
        // Returns how many were moved.
        internal static int MoveToStation(IEnumerable<Transform> objects, int i)
        {
            var targets = objects.Where(t => t.gameObject.scene.path == ScenePath).ToArray();
            if (targets.Length == 0) return 0;

            Undo.RecordObjects(targets, "Move to lighting station");
            foreach (var t in targets)
            {
                t.position = StationPos(i);
                t.rotation = Quaternion.identity;
            }
            return targets.Length;
        }

        static void FrameStation(int i)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            // Look at head height from the front (+Z side), like facing the avatar
            sv.LookAt(StationPos(i) + Vector3.up * 1.4f, Quaternion.Euler(5f, 180f, 0f), 2.5f);
            sv.sceneLighting = true;
        }

        // ---------------------------------------------------------------- Avatar check

        [MenuItem(Menu + "Check Selected Avatar Renderers", priority = 40)]
        static void CheckAvatar()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("Check Avatar", "Select your avatar's root object first.", "OK");
                return;
            }

            var report = AnalyseRenderers(go);
            if (report.Renderers == 0)
            {
                EditorUtility.DisplayDialog("Check Avatar: " + go.name, "No mesh renderers found under this object.", "OK");
                return;
            }

            Debug.Log("[Lighting Test] Renderer report for " + go.name + "\n" + report.Details);

            var summary = new StringBuilder();
            summary.AppendLine(report.SamplePoints > 1
                ? $"[!] Renderers sample lighting from {report.SamplePoints} different points ({report.Unanchored} without an Anchor Override). Parts of the avatar can look mismatched. Set every renderer's Anchor Override to the same bone (e.g. Chest or Hips)."
                : "OK: all renderers sample lighting from the same point.");
            summary.AppendLine(report.ProbeWarnings > 0
                ? $"[!] {report.ProbeWarnings} renderer(s) do not use 'Blend Probes' and will ignore light probes."
                : "OK: all renderers use Blend Probes.");
            summary.AppendLine("\nFull per-renderer list is in the Console.");
            EditorUtility.DisplayDialog("Check Avatar: " + go.name, summary.ToString(), "OK");
        }

        internal class RendererReport
        {
            public int Renderers, SamplePoints, Unanchored, ProbeWarnings;
            public string Details;
        }

        internal static RendererReport AnalyseRenderers(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true)
                .Where(r => !(r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer))
                .ToArray();

            // Renderers without an Anchor Override each sample from their own bounds centre,
            // so every one of them counts as a separate sample point
            var anchors = new HashSet<Transform>();
            var report = new RendererReport { Renderers = renderers.Length };
            var sb = new StringBuilder();

            foreach (var r in renderers)
            {
                if (r.probeAnchor != null) anchors.Add(r.probeAnchor);
                else report.Unanchored++;
                string shaders = string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => m.shader.name).Distinct());
                string anchor = r.probeAnchor != null ? r.probeAnchor.name : "NONE (uses its own bounds centre)";
                bool badProbes = r.lightProbeUsage != LightProbeUsage.BlendProbes;
                if (badProbes) report.ProbeWarnings++;
                sb.AppendLine($"{(badProbes ? "[!] " : "")}{r.name}: probes={r.lightProbeUsage}, anchor={anchor}, shaders={shaders}");
            }

            report.SamplePoints = anchors.Count + report.Unanchored;
            report.Details = sb.ToString();
            return report;
        }

        // ---------------------------------------------------------------- Helpers

        static Material CreateMat(string name, Color color, string shader = "Standard")
        {
            string path = $"{Folder}/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            var mat = new Material(Shader.Find(shader)) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void AddLamp(Transform parent, string name, Vector3 localPos, Color color, float intensity,
                            LightmapBakeType bake, LightRenderMode mode, Material bulbMat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = 6f;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = bake;
            l.renderMode = mode;

            // Small glowing bulb so you can see where the lamp is (not part of the bake)
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb (visual only)";
            bulb.transform.SetParent(go.transform, false);
            bulb.transform.localScale = Vector3.one * 0.15f;
            Object.DestroyImmediate(bulb.GetComponent<Collider>());
            var br = bulb.GetComponent<Renderer>();
            br.sharedMaterial = bulbMat;
            br.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void AddProbes(Transform parent)
        {
            var go = new GameObject("Light Probes");
            go.transform.SetParent(parent, false);
            var lpg = go.AddComponent<LightProbeGroup>();

            var positions = new List<Vector3>();
            float[] heights = { 0.2f, 0.9f, 1.6f, 2.3f, 3.0f };
            for (float x = -3f; x <= 3.01f; x += 1.5f)
                foreach (float y in heights)
                    for (float z = -3f; z <= 3.01f; z += 1.5f)
                        positions.Add(new Vector3(x, y, z));
            lpg.probePositions = positions.ToArray();
        }

        static void AddLabel(Transform parent, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 3.4f, -3.35f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // readable from the front

            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = text;
            tm.fontSize = 48;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }
}
