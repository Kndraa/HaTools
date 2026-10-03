// Root Bone and Anchor Fixer: checks that every renderer of an avatar shares one root bone and one light anchor, and sets them all to the same ones. Full docs: CLAUDE.md > Tools > Root Bone and Anchor Fixer.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HaTools
{
    public class RootBoneAndAnchorFixer : EditorWindow
    {
        const string Title = "Root Bone and Anchor Fixer";

        [MenuItem(HaToolsMenu.Root + Title)]
        static void Open() => GetWindow<RootBoneAndAnchorFixer>(Title);

        // ---------------------------------------------------------------- Window

        [SerializeField] GameObject avatar;
        [SerializeField] Transform rootBone, anchor;
        [SerializeField] Report report;
        [SerializeField] bool hasResult, rootBonesOpen, anchorsOpen;
        Vector2 scroll;

        void OnEnable()
        {
            if (avatar == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
                avatar = Selection.activeGameObject;
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) hasResult = false;
            bool validAvatar = avatar != null && !EditorUtility.IsPersistent(avatar);
            if (avatar != null && !validAvatar)
                EditorGUILayout.HelpBox("Pick the avatar in the scene, not a prefab asset.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!validAvatar))
                if (GUILayout.Button("Check Root Bones and Light Anchors", GUILayout.Height(28)))
                {
                    report = Check(avatar);
                    hasResult = true;
                }
            if (hasResult && validAvatar)
            {
                rootBonesOpen = Foldouts(rootBonesOpen, "Root Bones", report.RootBones);
                anchorsOpen = Foldouts(anchorsOpen, "Light Anchors", report.Anchors);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Override", EditorStyles.boldLabel);
            rootBone = (Transform)EditorGUILayout.ObjectField("Root Bone", rootBone, typeof(Transform), true);
            anchor = (Transform)EditorGUILayout.ObjectField("Light Anchor", anchor, typeof(Transform), true);

            // Targets outside the avatar would be lost on upload
            bool rootOk = rootBone == null || (validAvatar && rootBone.IsChildOf(avatar.transform));
            bool anchorOk = anchor == null || (validAvatar && anchor.IsChildOf(avatar.transform));
            if (!rootOk || !anchorOk)
                EditorGUILayout.HelpBox("The root bone and light anchor must be inside the avatar.", MessageType.Warning);
            EditorGUILayout.HelpBox("An empty field leaves that setting as it is.", MessageType.None);

            using (new EditorGUI.DisabledScope(!validAvatar || !rootOk || !anchorOk || (rootBone == null && anchor == null)))
                if (GUILayout.Button("Override All Renderers", GUILayout.Height(28)))
                {
                    int changed = Apply(avatar, rootBone, anchor);
                    ShowNotification(new GUIContent($"Changed {changed} renderer(s)"));
                    report = Check(avatar);
                    hasResult = true;
                }
            EditorGUILayout.EndScrollView();
        }

        // A foldout holding one foldout per root bone or anchor in use, each listing the renderers that use it
        bool Foldouts(bool open, string title, List<Group> groups)
        {
            string summary = groups.Count == 0 ? "no renderers" : Shared(groups) ? $"OK, shared by all {groups[0].Renderers.Count}" : "not shared";
            open = EditorGUILayout.Foldout(open, $"{title}: {summary}", true);
            if (!open) return false;

            EditorGUI.indentLevel++;
            foreach (var g in groups)
            {
                g.Open = EditorGUILayout.Foldout(g.Open, $"{(g.Value == null ? "None" : Path(g.Value, avatar))} ({g.Renderers.Count})", true);
                if (!g.Open) continue;
                EditorGUI.indentLevel++;
                // Read-only: clicking a field shows the renderer in the Hierarchy
                foreach (var r in g.Renderers) EditorGUILayout.ObjectField(r, typeof(Renderer), true);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            return true;
        }

        // ---------------------------------------------------------------- Check

        [System.Serializable]
        internal class Group
        {
            public Transform Value; // the root bone or anchor; null for the renderers that have none
            public List<Renderer> Renderers;
            public bool Open;
        }

        [System.Serializable]
        internal class Report
        {
            public List<Group> RootBones, Anchors; // one group per value in use, the most used first
            public bool RootBonesMatch => Shared(RootBones);
            public bool AnchorsMatch => Shared(Anchors);
        }

        // A missing root bone or anchor means each renderer uses its own transform/bounds, so it never counts as shared
        static bool Shared(List<Group> groups) => groups.Count == 1 && groups[0].Value != null;

        // Mesh and skinned mesh renderers, including ones on disabled objects (toggles)
        internal static Renderer[] Renderers(GameObject avatar) =>
            avatar.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToArray();

        internal static Report Check(GameObject avatar)
        {
            var renderers = Renderers(avatar);
            return new Report
            {
                RootBones = Groups(renderers.OfType<SkinnedMeshRenderer>(), r => ((SkinnedMeshRenderer)r).rootBone),
                Anchors = Groups(renderers, r => r.probeAnchor),
            };
        }

        static List<Group> Groups(IEnumerable<Renderer> renderers, System.Func<Renderer, Transform> value) =>
            renderers.GroupBy(value).OrderByDescending(g => g.Count())
                .Select(g => new Group { Value = g.Key, Renderers = g.ToList() }).ToList();

        static string Path(Transform t, GameObject avatar)
        {
            if (!t.IsChildOf(avatar.transform)) return t.name + " (outside the avatar)";
            return t == avatar.transform ? t.name : AnimationUtility.CalculateTransformPath(t, avatar.transform);
        }

        // ---------------------------------------------------------------- Override

        // Sets every renderer's root bone (skinned only) and light anchor; null leaves that setting alone.
        // Returns how many renderers changed. Registers Undo.
        internal static int Apply(GameObject avatar, Transform newRootBone, Transform newAnchor)
        {
            var toChange = Renderers(avatar).Where(r =>
                (newAnchor != null && r.probeAnchor != newAnchor) ||
                (newRootBone != null && r is SkinnedMeshRenderer s && s.rootBone != newRootBone)).ToArray();
            if (toChange.Length == 0) return 0;

            Undo.RecordObjects(toChange, "Override root bones and light anchors");
            foreach (var r in toChange)
            {
                if (newAnchor != null) r.probeAnchor = newAnchor;
                if (newRootBone != null && r is SkinnedMeshRenderer s && s.rootBone != newRootBone)
                {
                    // Bounds are stored relative to the root bone: convert them so they cover the same space as before
                    var oldRoot = s.rootBone != null ? s.rootBone : s.transform;
                    s.localBounds = MoveBounds(s.localBounds, oldRoot, newRootBone);
                    s.rootBone = newRootBone;
                }
            }
            return toChange.Length;
        }

        // Re-expresses a local box from one transform's space in another's (the box around its 8 corners)
        internal static Bounds MoveBounds(Bounds local, Transform from, Transform to)
        {
            var result = new Bounds(to.InverseTransformPoint(from.TransformPoint(local.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(to.InverseTransformPoint(from.TransformPoint(corner)));
            }
            return result;
        }
    }
}
