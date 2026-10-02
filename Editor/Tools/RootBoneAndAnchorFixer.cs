// Root Bone and Anchor Fixer: checks that every renderer of an avatar shares one root bone and one light anchor, and sets them all to the same ones. Full docs: CLAUDE.md > Tools > Root Bone and Anchor Fixer.
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        [SerializeField] string result;
        Vector2 scroll;

        void OnEnable()
        {
            if (avatar == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
                avatar = Selection.activeGameObject;
        }

        void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) result = null;
            bool validAvatar = avatar != null && !EditorUtility.IsPersistent(avatar);
            if (avatar != null && !validAvatar)
                EditorGUILayout.HelpBox("Pick the avatar in the scene, not a prefab asset.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!validAvatar))
                if (GUILayout.Button("Check Root Bones and Light Anchors", GUILayout.Height(28)))
                    result = Check(avatar).Text;
            if (!string.IsNullOrEmpty(result))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(220));
                EditorGUILayout.HelpBox(result, MessageType.None);
                EditorGUILayout.EndScrollView();
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
                    result = $"Changed {changed} renderer(s).\n\n" + Check(avatar).Text;
                }
        }

        // ---------------------------------------------------------------- Check

        internal class Report
        {
            public bool RootBonesMatch, AnchorsMatch;
            public string Text;
        }

        // Mesh and skinned mesh renderers, including ones on disabled objects (toggles)
        internal static Renderer[] Renderers(GameObject avatar) =>
            avatar.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToArray();

        internal static Report Check(GameObject avatar)
        {
            var renderers = Renderers(avatar);
            var skinned = renderers.OfType<SkinnedMeshRenderer>().ToArray();
            var report = new Report();
            var sb = new StringBuilder();

            // A missing root bone or anchor means each renderer uses its own transform/bounds, so it never counts as shared
            report.RootBonesMatch = skinned.Length > 0 && skinned.All(r => r.rootBone != null && r.rootBone == skinned[0].rootBone);
            report.AnchorsMatch = renderers.Length > 0 && renderers.All(r => r.probeAnchor != null && r.probeAnchor == renderers[0].probeAnchor);

            sb.AppendLine(skinned.Length == 0 ? "Root bones: no skinned mesh renderers."
                : report.RootBonesMatch ? $"OK: all {skinned.Length} skinned mesh renderers use root bone '{Path(skinned[0].rootBone, avatar)}'."
                : $"[!] Root bones differ across {skinned.Length} skinned mesh renderers:\n" + Groups(skinned, r => ((SkinnedMeshRenderer)r).rootBone, avatar, "none (its own transform)"));
            sb.AppendLine();
            sb.Append(renderers.Length == 0 ? "Light anchors: no mesh renderers."
                : report.AnchorsMatch ? $"OK: all {renderers.Length} renderers use light anchor '{Path(renderers[0].probeAnchor, avatar)}'."
                : $"[!] Light anchors differ across {renderers.Length} renderers:\n" + Groups(renderers, r => r.probeAnchor, avatar, "none (its own bounds centre)"));

            report.Text = sb.ToString();
            return report;
        }

        // One line per distinct value: "  Hips (3): Body, Hair, Shirt"
        static string Groups(IEnumerable<Renderer> renderers, System.Func<Renderer, Transform> value, GameObject avatar, string none) =>
            string.Join("\n", renderers.GroupBy(value).OrderByDescending(g => g.Count())
                .Select(g => $"  {(g.Key == null ? none : Path(g.Key, avatar))} ({g.Count()}): {string.Join(", ", g.Select(r => r.name))}"));

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
