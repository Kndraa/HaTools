// Lighting Test Scene: a window that takes a copy of an avatar into a baked scene of VRChat-style lighting stations and back to your own scene. Full docs: CLAUDE.md > Tools > Lighting Test Scene.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HaTools
{
    public class LightingTestScene : EditorWindow
    {
        const string Title = "Lighting Test Scene";

        internal const string ParentFolder = "Assets/HaTools";
        internal const string Folder = ParentFolder + "/LightingTestScene";
        internal const string ScenePath = Folder + "/LightingTest.unity";
        internal const string SettingsPath = Folder + "/LightingTestSettings.lighting";
        internal const string StationsRootName = "Lighting Test Stations";
        internal const string CopySuffix = " (Lighting Test Copy)";
        // Per-project setting holding what to return to (see ReturnState)
        internal const string ReturnKey = "HaTools.LightingTestScene.Return";
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

        // What to look for at each station. lilToon's defaults: CLAUDE.md > Lighting Test Scene > Reading the results.
        static readonly string[] StationNotes =
        {
            "The avatar is lit only through light probes, as in most VRChat worlds: there is no realtime light to give the shader a direction.",
            "A vertex light. lilToon ignores vertex lights by default (Vertex Light Strength 0), so it looks like station D here.",
            "A pixel light: the shader gets the light's direction and colour. lilToon caps bright light at Light Max Limit (1).",
            "No lamp, only dim ambient light: shows the shader's minimum brightness (lilToon: Light Min Limit).",
            "The same as A with a white lamp: the colour reference for A.",
            "Red light from the avatar's left, blue from its right, both baked. A shader that keeps the direction of probe light shows a red side " +
            "and a blue side; one that averages it (lilToon) shows a mix.",
        };

        static readonly Color Warm = new Color(1f, 0.7f, 0.35f);
        static readonly Color Red = new Color(1f, 0.15f, 0.1f);
        static readonly Color Blue = new Color(0.15f, 0.3f, 1f);

        internal static Vector3 StationPos(int i) => new Vector3(i * Spacing, 0f, 0f);

        [MenuItem(HaToolsMenu.Root + Title)]
        static void Open() => GetWindow<LightingTestScene>(Title);

        // ---------------------------------------------------------------- Window

        [SerializeField] GameObject avatar;
        [SerializeField] int station;

        static bool InTestScene => SceneManager.GetActiveScene().path == ScenePath;

        void OnEnable()
        {
            if (avatar == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
                avatar = Selection.activeGameObject;
        }

        // Keeps the bake status and the buttons up to date while baking and after scene changes
        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            // Unity can't switch, save or bake scenes while playing
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            if (InTestScene) TestSceneGUI(playing);
            else MainSceneGUI(playing);
        }

        void MainSceneGUI(bool playing)
        {
            EditorGUILayout.HelpBox("Shows how the avatar's shaders react to VRChat world lighting, in a separate baked scene. " +
                                    "A copy of the avatar goes into the test scene; your own scene is closed and reopened when you return.", MessageType.None);

            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            bool valid = avatar != null && !EditorUtility.IsPersistent(avatar);
            if (avatar != null && !valid)
                EditorGUILayout.HelpBox("Pick the avatar in the scene, not a prefab asset.", MessageType.Warning);

            // Scene changes and dialogs run after OnGUI, not in the middle of it
            bool exists = File.Exists(ScenePath);
            using (new EditorGUI.DisabledScope(!valid || playing))
                if (GUILayout.Button(exists ? "Open Test Scene" : "Create Test Scene and Bake", GUILayout.Height(28)))
                    EditorApplication.delayCall += Enter;
            using (new EditorGUI.DisabledScope(!exists || playing))
                if (GUILayout.Button("Delete Test Scene", GUILayout.Height(28)))
                    EditorApplication.delayCall += () => Leave(true);
            if (playing) EditorGUILayout.HelpBox("Exit Play mode to open or delete the test scene.", MessageType.Info);
        }

        void TestSceneGUI(bool playing)
        {
            // Tools that rebuild the avatar in Play mode can drop the reference: fall back to the copy
            if (avatar == null) avatar = Copies(SceneManager.GetActiveScene()).FirstOrDefault();
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            if (avatar != null && avatar.scene.path != ScenePath)
                EditorGUILayout.HelpBox("Pick an object inside the test scene.", MessageType.Warning);

            if (Lightmapping.isRunning)
                EditorGUILayout.HelpBox("Baking (progress bar at the bottom right). Stations A, E and F are only lit once it finishes; " +
                                        "wait for it before entering Play mode.", MessageType.Info);
            else if (Lightmapping.lightingDataAsset == null)
            {
                EditorGUILayout.HelpBox("The scene isn't baked: stations A, E and F are unlit.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(playing))
                    if (GUILayout.Button("Bake")) Lightmapping.BakeAsync();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Station", EditorStyles.boldLabel);
            for (int i = 0; i < StationNames.Length; i++)
            {
                // Clicking the current station again brings the avatar and the view back to it
                bool current = station == i;
                if (GUILayout.Toggle(current, StationNames[i], "Button") != current) GoToStation(i);
            }
            EditorGUILayout.HelpBox(StationNotes[station], MessageType.None);

            if (!playing)
                EditorGUILayout.HelpBox("The test is more accurate to VRChat in Play mode: tools such as Modular Avatar and VRCFury are applied " +
                                        "as they are at upload, and Gesture Manager or Av3 Emulator can run the avatar's toggles.", MessageType.Warning);

            EditorGUILayout.Space();
            if (string.IsNullOrEmpty(EditorUserSettings.GetConfigValue(ReturnKey)))
                EditorGUILayout.HelpBox("This scene wasn't opened from this window, so there is no scene to return to: returning opens a new empty scene.", MessageType.Info);
            using (new EditorGUI.DisabledScope(playing))
                if (GUILayout.Button("Return to Main Scene", GUILayout.Height(28)))
                    EditorApplication.delayCall += () => Leave(false);
            if (playing) EditorGUILayout.HelpBox("Exit Play mode to return or bake.", MessageType.Info);
        }

        void Enter()
        {
            if (avatar == null || InTestScene) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)))
            {
                EditorUtility.DisplayDialog(Title, "Save your scene to a file first (File > Save As).\n\n" +
                                                   "The test scene takes the place of the open scene, and only a saved scene can be reopened afterwards.", "OK");
                return;
            }

            avatar = EnterTestScene(avatar);
            GoToStation(station);
            // A test scene kept from last time is already baked
            if (Lightmapping.lightingDataAsset == null) Lightmapping.BakeAsync();
        }

        void GoToStation(int i)
        {
            station = i;
            if (avatar != null) MoveToStation(avatar.transform, i);
            FrameStation(i);
        }

        // Goes back to the main scene when the test scene is open, then deletes the test scene if asked to
        void Leave(bool delete)
        {
            if (delete && !EditorUtility.DisplayDialog(Title, "Delete the test scene and its baked lighting?\n\n" +
                                                              "It is built and baked again the next time you create it.", "Delete", "Cancel"))
                return;
            if (InTestScene) avatar = ReturnToMainScene();
            if (delete) DeleteTestScene();
        }

        static void FrameStation(int i)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            // Look at head height from the front (+Z side), like facing the avatar
            sv.LookAt(StationPos(i) + Vector3.up * 1.4f, Quaternion.Euler(5f, 180f, 0f), 2.5f);
            sv.sceneLighting = true;
        }

        // ---------------------------------------------------------------- Switching scenes

        // What ReturnToMainScene puts back: the scenes that were open, the Scene view camera and the original avatar
        [Serializable]
        class ReturnState
        {
            public List<SceneEntry> Scenes = new List<SceneEntry>();
            public string Avatar; // GlobalObjectId: the object reference itself is lost when its scene closes
            public bool HasView, Ortho;
            public Vector3 Pivot;
            public Quaternion Rotation;
            public float Size;
        }

        [Serializable]
        struct SceneEntry
        {
            public string Path;
            public bool Loaded, Active;
        }

        internal static IEnumerable<GameObject> Copies(Scene scene) => scene.GetRootGameObjects().Where(g => g.name.EndsWith(CopySuffix));

        // Opens the test scene (building it first if there is none) with a copy of the avatar in it, closes the other scenes
        // without saving them and remembers them for ReturnToMainScene. No dialogs, no bake. Every open scene must be saved to a file.
        internal static GameObject EnterTestScene(GameObject avatar)
        {
            var active = SceneManager.GetActiveScene();
            var open = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s.path != ScenePath).ToArray();

            var state = new ReturnState { Avatar = GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString() };
            foreach (var s in open) state.Scenes.Add(new SceneEntry { Path = s.path, Loaded = s.isLoaded, Active = s == active });
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                state.HasView = true;
                state.Pivot = sv.pivot;
                state.Rotation = sv.rotation;
                state.Size = sv.size;
                state.Ortho = sv.orthographic;
            }
            EditorUserSettings.SetConfigValue(ReturnKey, JsonUtility.ToJson(state));

            // Opened beside the avatar's scene, so the avatar can be copied across before that scene closes
            var scene = File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) : BuildScene();
            SceneManager.SetActiveScene(scene);
            // A copy from last time is only still here if the test scene was saved by hand
            foreach (var old in Copies(scene).ToArray()) DestroyImmediate(old);

            // The copy keeps its scripts, so build tools and emulators treat it as the avatar in Play mode
            var copy = Instantiate(avatar);
            SceneManager.MoveGameObjectToScene(copy, scene);
            copy.name = avatar.name + CopySuffix;
            copy.transform.localScale = avatar.transform.lossyScale;
            copy.SetActive(true);

            foreach (var s in open) EditorSceneManager.CloseScene(s, true);
            return copy;
        }

        // Saves the test scene without the avatar copy (keeping its bake) and reopens the scenes that were open before.
        // Returns the original avatar, or null when it can't be found. No dialogs.
        internal static GameObject ReturnToMainScene()
        {
            var test = SceneManager.GetSceneByPath(ScenePath);
            if (test.isLoaded)
            {
                if (Lightmapping.isRunning) Lightmapping.Cancel();
                foreach (var copy in Copies(test).ToArray()) DestroyImmediate(copy);
                EditorSceneManager.SaveScene(test);
            }

            var state = JsonUtility.FromJson<ReturnState>(EditorUserSettings.GetConfigValue(ReturnKey) ?? "") ?? new ReturnState();
            EditorUserSettings.SetConfigValue(ReturnKey, "");

            var scenes = state.Scenes.Where(s => File.Exists(s.Path)).ToList();
            if (scenes.Count == 0)
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            else
            {
                // Unity wants exactly one active scene and it has to be loaded (the old one may have been deleted meanwhile)
                int active = scenes.FindIndex(s => s.Active);
                if (active < 0) active = Mathf.Max(0, scenes.FindIndex(s => s.Loaded));
                EditorSceneManager.RestoreSceneManagerSetup(scenes
                    .Select((s, i) => new SceneSetup { path = s.Path, isLoaded = s.Loaded || i == active, isActive = i == active }).ToArray());
            }

            var sv = SceneView.lastActiveSceneView;
            if (state.HasView && sv != null) sv.LookAt(state.Pivot, state.Rotation, state.Size, state.Ortho, true);

            return GlobalObjectId.TryParse(state.Avatar ?? "", out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject : null;
        }

        // Deletes everything the tool generated: the scene, its materials, lighting settings and bake. The test scene must not be open.
        internal static void DeleteTestScene()
        {
            AssetDatabase.DeleteAsset(Folder);
            if (Directory.Exists(ParentFolder) && !Directory.EnumerateFileSystemEntries(ParentFolder).Any())
                AssetDatabase.DeleteAsset(ParentFolder);
        }

        // ---------------------------------------------------------------- Build

        // Creates the scene beside the open ones, makes it the active scene and saves it with its materials and lighting settings.
        // No dialogs, no bake. Every open scene must be saved to a file (Unity can't add a scene next to an untitled one).
        internal static Scene BuildScene()
        {
            if (!AssetDatabase.IsValidFolder(ParentFolder)) AssetDatabase.CreateFolder("Assets", "HaTools");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(ParentFolder, "LightingTestScene");

            // Environment and lighting settings belong to the active scene, and new objects are created in it
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

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
                DestroyImmediate(sphere.GetComponent<Collider>());

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
            return scene;
        }

        // ---------------------------------------------------------------- Stations

        // Moves an object that is inside the test scene to a station, facing +Z, with Undo. Returns false for objects in other scenes.
        internal static bool MoveToStation(Transform t, int i)
        {
            if (t.gameObject.scene.path != ScenePath) return false;

            Undo.RecordObject(t, "Move to lighting station");
            t.SetPositionAndRotation(StationPos(i), Quaternion.identity);
            return true;
        }

        // ---------------------------------------------------------------- Helpers

        static Material CreateMat(string name, Color color, string shader = "Standard")
        {
            // CreateAsset replaces an asset left at the path by an earlier build
            var mat = new Material(Shader.Find(shader)) { color = color };
            AssetDatabase.CreateAsset(mat, $"{Folder}/{name}.mat");
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
            DestroyImmediate(bulb.GetComponent<Collider>());
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
