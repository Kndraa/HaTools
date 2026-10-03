// Bounds Fixer: moves a hidden copy of an avatar through extreme poses to find skinned meshes that leave their bounds, and grows those bounds to fit. Full docs: CLAUDE.md > Tools > Bounds Fixer.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HaTools
{
    public class BoundsFixer : EditorWindow
    {
        const string Title = "Bounds Fixer";
        internal const float Tolerance = 0.001f; // metres a mesh may stick out before its bounds count as too small
        internal const float Margin = 0.05f;     // room added on every side when growing, as a fraction of the box's longest side

        [MenuItem(HaToolsMenu.Root + Title)]
        static void Open() => GetWindow<BoundsFixer>(Title);

        // ---------------------------------------------------------------- Window

        [SerializeField] GameObject avatar;
        [SerializeField] List<Entry> entries = new List<Entry>();
        [SerializeField] bool hasResult, humanoid;
        [SerializeField] bool showGreen = true, showRed = true, showYellow = true;
        Vector2 scroll;

        void OnEnable()
        {
            if (avatar == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
                avatar = Selection.activeGameObject;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += Repaint;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= Repaint;
            SceneView.RepaintAll();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("A skinned mesh disappears in VRChat when its bounds leave the view, even while the mesh itself is still in view. " +
                                    "The check moves a hidden copy of the avatar through extreme poses and finds the meshes that leave their bounds.", MessageType.None);

            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                entries.Clear();
                hasResult = false;
                SceneView.RepaintAll();
            }
            bool valid = avatar != null && !EditorUtility.IsPersistent(avatar);
            if (avatar != null && !valid)
                EditorGUILayout.HelpBox("Pick the avatar in the scene, not a prefab asset.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button("Check Bounds", GUILayout.Height(28)))
                {
                    humanoid = IsHumanoid(avatar);
                    entries = Check(avatar);
                    hasResult = true;
                    SceneView.RepaintAll();
                }
            if (!hasResult) return;

            if (!humanoid)
                EditorGUILayout.HelpBox("This avatar has no humanoid Animator, so only its current pose was checked.", MessageType.Warning);
            var live = entries.Where(e => e.Renderer != null).OrderByDescending(e => e.Overshoot).ToList();
            if (live.Count == 0)
            {
                EditorGUILayout.HelpBox("No skinned mesh renderers found.", MessageType.Info);
                return;
            }

            // A box is drawn in the Scene view when its renderer's checkbox and its colour's checkbox are both on
            EditorGUI.BeginChangeCheck();
            showGreen = EditorGUILayout.ToggleLeft("Show green bounds (large enough)", showGreen);
            showRed = EditorGUILayout.ToggleLeft("Show red bounds (too small)", showRed);
            showYellow = EditorGUILayout.ToggleLeft("Show yellow bounds (what growing sets)", showYellow);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var e in live)
            {
                EditorGUILayout.BeginHorizontal();
                e.Show = GUILayout.Toggle(e.Show, new GUIContent("", "Show this renderer's bounds in the Scene view"), GUILayout.Width(16));
                // Read-only: clicking the field shows the renderer in the Hierarchy
                EditorGUILayout.ObjectField(e.Renderer, typeof(SkinnedMeshRenderer), true, GUILayout.Width(170));
                EditorGUILayout.LabelField((e.TooSmall ? $"Too small: sticks out {e.Overshoot:0.00} m ({e.Pose})" : "OK") +
                                           (e.Renderer.updateWhenOffscreen ? ". Update When Offscreen is on" : ""), EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

            int flagged = live.Count(e => e.TooSmall);
            using (new EditorGUI.DisabledScope(flagged == 0))
                if (GUILayout.Button(flagged == 0 ? "All Bounds Are Large Enough" : $"Grow {flagged} Bounds to Fit", GUILayout.Height(28)))
                {
                    Grow(entries);
                    SceneView.RepaintAll();
                }
        }

        // Unity only draws the bounds of the selected renderer: draw every one that is switched on
        void OnSceneGUI(SceneView sv)
        {
            foreach (var e in entries)
            {
                if (e.Renderer == null || !e.Show) continue;
                Handles.matrix = Root(e.Renderer).localToWorldMatrix;
                bool tooSmall = e.TooSmall;
                if (tooSmall ? showRed : showGreen)
                {
                    var bounds = Stored(e.Renderer);
                    Handles.color = tooSmall ? Color.red : Color.green;
                    Handles.DrawWireCube(bounds.center, bounds.size);
                }
                if (!tooSmall || !showYellow) continue;
                var grown = e.Grown;
                Handles.color = Color.yellow;
                Handles.DrawWireCube(grown.center, grown.size);
            }
            Handles.matrix = Matrix4x4.identity;
        }

        // ---------------------------------------------------------------- Test poses

        // Each pose overrides some humanoid muscles (-1 to 1) of the avatar's current pose. A muscle is named by the
        // end of its name in HumanTrait.MuscleName, so "Arm Down-Up" sets both arms and "Chest Front-Back" also sets the upper chest.
        // The arm values were found by trying them on a humanoid: in T-pose Down-Up is 0.4 and Front-Back 0.3.
        static readonly (string, float)[] ArmsUp = { ("Arm Down-Up", 1f), ("Arm Front-Back", 0f), ("Shoulder Down-Up", 1f), ("Forearm Stretch", 1f) };

        internal static readonly (string name, (string muscle, float value)[] muscles)[] Poses =
        {
            ("current pose", new (string, float)[0]),
            ("arms up", ArmsUp),
            ("arms down", new[] { ("Arm Down-Up", -1f), ("Arm Front-Back", 0.1f), ("Shoulder Down-Up", -1f), ("Forearm Stretch", 1f) }),
            ("arms forward", new[] { ("Arm Down-Up", 0.2f), ("Arm Front-Back", -0.6f), ("Shoulder Front-Back", -1f), ("Forearm Stretch", 1f) }),
            ("arms back", new[] { ("Arm Down-Up", 0f), ("Arm Front-Back", 1f), ("Shoulder Front-Back", 1f), ("Forearm Stretch", 1f) }),
            ("legs apart", new[] { ("Leg In-Out", 1f), ("Leg Stretch", 1f) }),
            ("legs forward", new[] { ("Leg Front-Back", -1f), ("Leg Stretch", 1f) }),
            ("legs back", new[] { ("Leg Front-Back", 1f), ("Leg Stretch", 1f) }),
            ("bent forward, arms up", ArmsUp.Concat(Torso("Front-Back", "Nod Down-Up", -1f)).ToArray()),
            ("bent back, arms up", ArmsUp.Concat(Torso("Front-Back", "Nod Down-Up", 1f)).ToArray()),
            ("leaning left, arms up", ArmsUp.Concat(Torso("Left-Right", "Tilt Left-Right", -1f)).ToArray()),
            ("leaning right, arms up", ArmsUp.Concat(Torso("Left-Right", "Tilt Left-Right", 1f)).ToArray()),
            ("turned left", Torso("Twist Left-Right", "Turn Left-Right", -1f)),
            ("turned right", Torso("Twist Left-Right", "Turn Left-Right", 1f)),
        };

        // Spine, chest, upper chest, neck and head all bent or turned the same way
        static (string, float)[] Torso(string spine, string head, float value) =>
            new[] { ("Spine " + spine, value), ("Chest " + spine, value), (head, value) };

        internal static bool IsHumanoid(GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        // ---------------------------------------------------------------- Check

        [Serializable]
        internal class Entry
        {
            public SkinnedMeshRenderer Renderer;
            public Bounds Needed; // box around the mesh over every test pose, in the root bone's space
            public string Pose;   // the pose that stuck out the furthest
            public bool Show;     // draw this renderer's bounds in the Scene view. A check turns it on for the ones that are too small.

            // Compared with the renderer's bounds as they are now, so growing and Undo show up without a new check
            public float Overshoot => BoundsFixer.Overshoot(Stored(Renderer), Needed, Root(Renderer).lossyScale);
            public bool TooSmall => Overshoot > Tolerance;

            // The bounds growing sets: the current ones plus the needed box with a margin. Never smaller than the current ones.
            public Bounds Grown
            {
                get
                {
                    var b = Stored(Renderer);
                    var margin = Vector3.one * (Margin * Mathf.Max(Needed.size.x, Needed.size.y, Needed.size.z));
                    b.Encapsulate(Needed.min - margin);
                    b.Encapsulate(Needed.max + margin);
                    return b;
                }
            }
        }

        // Bounds are stored relative to the root bone, or to the renderer itself without one
        static Transform Root(SkinnedMeshRenderer r) => r.rootBone != null ? r.rootBone : r.transform;

        // With Update When Offscreen on, localBounds returns the box Unity recalculates every frame instead of the stored one
        internal static Bounds Stored(SkinnedMeshRenderer r) =>
            r.updateWhenOffscreen ? new SerializedObject(r).FindProperty("m_AABB").boundsValue : r.localBounds;

        // Skinned mesh renderers with a mesh, including ones on disabled objects (toggles)
        static SkinnedMeshRenderer[] Renderers(GameObject avatar) =>
            avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).ToArray();

        // Poses a hidden copy of the avatar and measures where each skinned mesh ends up. One entry per renderer.
        // The avatar and its scene are not touched.
        internal static List<Entry> Check(GameObject avatar)
        {
            var entries = Renderers(avatar).Select(r => new Entry { Renderer = r, Pose = Poses[0].name }).ToList();
            var worst = new float[entries.Count];
            var baked = new Mesh();
            var vertices = new List<Vector3>();
            // A preview scene keeps the copy out of the user's scenes
            var scene = EditorSceneManager.NewPreviewScene();
            HumanPoseHandler handler = null;
            try
            {
                // Under a disabled parent nothing on the copy wakes up (VRChat SDK, PhysBones, build tools, ...)
                var holder = new GameObject("HaTools Bounds Fixer");
                SceneManager.MoveGameObjectToScene(holder, scene);
                holder.SetActive(false);
                var copy = Instantiate(avatar, holder.transform);
                var renderers = Renderers(copy);

                var rest = new HumanPose();
                if (IsHumanoid(avatar))
                {
                    handler = new HumanPoseHandler(avatar.GetComponent<Animator>().avatar, copy.transform);
                    handler.GetHumanPose(ref rest);
                }

                foreach (var (name, muscles) in Poses)
                {
                    if (muscles.Length > 0)
                    {
                        if (handler == null) break;
                        var pose = new HumanPose { bodyPosition = rest.bodyPosition, bodyRotation = rest.bodyRotation, muscles = (float[])rest.muscles.Clone() };
                        foreach (var (muscle, value) in muscles)
                            for (int m = 0; m < pose.muscles.Length; m++)
                                if (HumanTrait.MuscleName[m].EndsWith(muscle)) pose.muscles[m] = value;
                        handler.SetHumanPose(ref pose);
                    }

                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        var box = MeshBox(renderers[i], baked, vertices);
                        if (name == Poses[0].name) e.Needed = box;
                        else e.Needed.Encapsulate(box);

                        float over = Overshoot(Stored(e.Renderer), box, Root(e.Renderer).lossyScale);
                        if (over <= worst[i]) continue;
                        worst[i] = over;
                        e.Pose = name;
                    }
                }
            }
            finally
            {
                handler?.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
                DestroyImmediate(baked);
            }
            foreach (var e in entries) e.Show = e.TooSmall;
            return entries;
        }

        // Box around the renderer's mesh as it is skinned right now, in its root bone's space
        static Bounds MeshBox(SkinnedMeshRenderer r, Mesh baked, List<Vector3> vertices)
        {
            r.BakeMesh(baked, true);
            baked.GetVertices(vertices);
            if (vertices.Count == 0) return Stored(r);
            var toRoot = Root(r).worldToLocalMatrix * r.transform.localToWorldMatrix;
            Vector3 min = toRoot.MultiplyPoint3x4(vertices[0]), max = min;
            foreach (var v in vertices)
            {
                var p = toRoot.MultiplyPoint3x4(v);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var box = new Bounds();
            box.SetMinMax(min, max);
            return box;
        }

        // How far the needed box sticks out of the bounds on its worst side, in metres (scale: the root bone's world scale). 0 when it fits.
        internal static float Overshoot(Bounds bounds, Bounds needed, Vector3 scale)
        {
            var over = Vector3.Max(Vector3.Max(needed.max - bounds.max, bounds.min - needed.min), Vector3.zero);
            return Mathf.Max(over.x * Mathf.Abs(scale.x), over.y * Mathf.Abs(scale.y), over.z * Mathf.Abs(scale.z));
        }

        // ---------------------------------------------------------------- Grow

        // Grows the bounds of every renderer that is too small so they cover its test poses plus a margin.
        // Returns how many renderers changed. Registers Undo.
        internal static int Grow(IEnumerable<Entry> entries)
        {
            var flagged = entries.Where(e => e.Renderer != null && e.TooSmall).ToArray();
            if (flagged.Length == 0) return 0;

            Undo.RecordObjects(flagged.Select(e => (Object)e.Renderer).ToArray(), "Grow bounds");
            foreach (var e in flagged) e.Renderer.localBounds = e.Grown;
            return flagged.Length;
        }
    }
}
