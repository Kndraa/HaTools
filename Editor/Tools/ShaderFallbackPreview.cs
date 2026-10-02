// Shader Fallback Preview: shows a temporary copy of an avatar with the shaders VRChat falls back to when shaders are blocked. Full docs: CLAUDE.md > Tools > Shader Fallback Preview.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HaTools
{
    public class ShaderFallbackPreview : EditorWindow
    {
        const string Title = "Shader Fallback Preview";
        internal const string CopySuffix = " (Fallback Preview)";
        internal const string MaterialSuffix = " (HaTools fallback)";

        [MenuItem(HaToolsMenu.Root + Title)]
        static void Open() => GetWindow<ShaderFallbackPreview>(Title);

        // ---------------------------------------------------------------- Window

        [SerializeField] GameObject avatar;
        [SerializeField] GameObject preview;
        [SerializeField] List<Entry> entries = new List<Entry>();
        [SerializeField] bool hasSavedView, savedOrtho;
        [SerializeField] Vector3 savedPivot;
        [SerializeField] Quaternion savedRotation;
        [SerializeField] float savedSize;
        Vector2 scroll;

        void OnEnable()
        {
            if (avatar == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
                avatar = Selection.activeGameObject;
            // The copy is never saved, so take it away before anything that would reload or leave the scene
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneClosing += OnSceneClosing;
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
        }

        // Closing the window removes the preview (OnDestroy isn't called on script reloads)
        void OnDestroy() => RemovePreview();

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) RemovePreview();
        }

        void OnSceneClosing(Scene scene, bool removingScene)
        {
            if (preview != null && preview.scene == scene) RemovePreview();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Shows a copy of the avatar with the shaders VRChat uses when someone has your shaders blocked. " +
                                    "The copy is temporary and is never saved in the scene.", MessageType.None);

            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
            bool valid = avatar != null && !EditorUtility.IsPersistent(avatar) && avatar != preview;
            if (avatar != null && !valid)
                EditorGUILayout.HelpBox("Pick the avatar in the scene (not a prefab asset or the preview copy).", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button(preview == null ? "Create Fallback Preview" : "Recreate Fallback Preview", GUILayout.Height(28)))
                    CreatePreview();
            using (new EditorGUI.DisabledScope(preview == null && !hasSavedView))
                if (GUILayout.Button("Remove Preview and Restore Camera", GUILayout.Height(28)))
                    RemovePreview();

            if (entries.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Materials", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var e in entries)
            {
                EditorGUILayout.LabelField(e.Material, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{e.Shader}  ->  {e.Fallback}", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Preview shader: " + e.PreviewShader, EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(e.Note)) EditorGUILayout.HelpBox(e.Note, MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        void CreatePreview()
        {
            DestroyPreview(preview);
            entries.Clear();
            preview = CreatePreview(avatar, entries);

            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            if (!hasSavedView)
            {
                savedPivot = sv.pivot;
                savedRotation = sv.rotation;
                savedSize = sv.size;
                savedOrtho = sv.orthographic;
                hasSavedView = true;
            }
            FrameFront(sv, preview);
        }

        void RemovePreview()
        {
            DestroyPreview(preview);
            preview = null;
            entries.Clear();

            var sv = SceneView.lastActiveSceneView;
            if (hasSavedView && sv != null) sv.LookAt(savedPivot, savedRotation, savedSize, savedOrtho, true);
            hasSavedView = false;
            Repaint();
        }

        // Puts the Scene view camera straight in front of the avatar (avatars face +Z), showing all of it
        static void FrameFront(SceneView sv, GameObject go)
        {
            var bounds = RendererBounds(go);
            var forward = Vector3.ProjectOnPlane(go.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            sv.LookAt(bounds.center, Quaternion.LookRotation(-forward), bounds.extents.magnitude, false, true);
        }

        // ---------------------------------------------------------------- Preview copy

        [Serializable]
        internal class Entry
        {
            public string Material, Shader, Fallback, PreviewShader, Note;
        }

        // Copies the avatar next to the original and swaps every material for its fallback. No dialogs, no camera.
        // The copy and its materials are DontSave: they never end up in the scene file.
        internal static GameObject CreatePreview(GameObject avatar, List<Entry> entries)
        {
            var src = avatar.transform;
            var copy = Instantiate(avatar, src.parent);
            if (src.parent == null) SceneManager.MoveGameObjectToScene(copy, avatar.scene);
            // Under a disabled parent the copy would stay hidden: move it to the scene root, keeping its world size
            else if (!src.parent.gameObject.activeInHierarchy) copy.transform.SetParent(null, true);
            copy.name = avatar.name + CopySuffix;
            copy.SetActive(true);
            var size = RendererBounds(avatar).size;
            copy.transform.SetPositionAndRotation(src.position - src.right * (Mathf.Max(size.x, size.z) + 0.5f), src.rotation);
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = HideFlags.DontSave;
                RemoveScripts(t.gameObject);
            }

            var fallbacks = new Dictionary<Material, Material>();
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (!fallbacks.TryGetValue(mats[i], out var fb))
                    {
                        var entry = new Entry();
                        fb = fallbacks[mats[i]] = CreateFallbackMaterial(mats[i], entry);
                        entries.Add(entry);
                    }
                    mats[i] = fb;
                }
                r.sharedMaterials = mats;
            }
            return copy;
        }

        // Destroys the copy and the fallback materials made for it (never the avatar's own materials)
        internal static void DestroyPreview(GameObject copy)
        {
            if (copy == null) return;
            var made = copy.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m.hideFlags == HideFlags.DontSave && m.name.EndsWith(MaterialSuffix))
                .Distinct().ToList();
            DestroyImmediate(copy);
            foreach (var m in made) DestroyImmediate(m);
        }

        // Scripts (VRChat SDK, PhysBones, Modular Avatar, ...) would make the copy look like a second avatar to other tools
        static void RemoveScripts(GameObject go)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            var scripts = go.GetComponents<MonoBehaviour>().ToList();
            while (scripts.Count > 0)
            {
                // Remove scripts no other script requires first, so [RequireComponent] never blocks a removal
                var free = scripts.Where(s => !scripts.Any(o => o != s && Requires(o, s))).ToList();
                if (free.Count == 0) free = scripts;
                foreach (var s in free) DestroyImmediate(s);
                scripts = scripts.Except(free).ToList();
            }
        }

        static bool Requires(Component c, Component other) =>
            c.GetType().GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>()
                .Any(r => new[] { r.m_Type0, r.m_Type1, r.m_Type2 }.Any(t => t != null && t.IsInstanceOfType(other)));

        static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position + Vector3.up, Vector3.one * 2f);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // ---------------------------------------------------------------- Fallback rules (VRChat docs: "Shader Blocking and Fallback System")

        internal enum Kind { Keep, Hidden, Standard, Unlit, VertexLit, Toon, MobileToon, ToonStandard, ToonStandardOutline, Particle, Sprite, Matcap }
        internal enum Mode { Opaque, Cutout, Fade, Transparent } // same order as the Standard shader's _Mode

        internal struct Fallback
        {
            public Kind Kind;
            public Mode Mode;
            public bool DoubleSided, Outline, FromTag;

            public override string ToString()
            {
                if (Kind == Kind.Keep) return "kept (VRChat has this built-in shader)";
                string s = Kind.ToString();
                if (Mode != Mode.Opaque) s += " " + Mode;
                if (DoubleSided) s += " DoubleSided";
                if (Outline) s += " Outline";
                return s + (FromTag ? " (from VRCFallback tag)" : " (guessed from shader name)");
            }
        }

        // Shaders VRChat has pre-compiled: materials using them keep their shader and all properties
        static readonly HashSet<string> InternalShaders = new HashSet<string>
        {
            "Standard", "Standard (Specular setup)", "Effects/Rim", "Effects/GlowAdditiveSimple",
            "Legacy Shaders/Bumped Diffuse", "Legacy Shaders/Bumped Specular", "Legacy Shaders/Decal", "Legacy Shaders/Diffuse",
            "Legacy Shaders/Diffuse Detail", "Legacy Shaders/Diffuse Fast", "Legacy Shaders/Lightmapped/Diffuse",
            "Legacy Shaders/Lightmapped/Specular", "Legacy Shaders/Lightmapped/VertexLit", "Legacy Shaders/Parallax Diffuse",
            "Legacy Shaders/Parallax Specular", "Legacy Shaders/Reflective/Bumped Diffuse", "Legacy Shaders/Reflective/Bumped Specular",
            "Legacy Shaders/Reflective/Bumped Unlit", "Legacy Shaders/Reflective/Bumped VertexLit", "Legacy Shaders/Reflective/Diffuse",
            "Legacy Shaders/Reflective/Parallax Diffuse", "Legacy Shaders/Reflective/Parallax Specular", "Legacy Shaders/Reflective/Specular",
            "Legacy Shaders/Reflective/VertexLit", "Legacy Shaders/Self-Illum/Bumped Diffuse", "Legacy Shaders/Self-Illum/Bumped Specular",
            "Legacy Shaders/Self-Illum/Diffuse", "Legacy Shaders/Self-Illum/Parallax Diffuse", "Legacy Shaders/Self-Illum/Parallax Specular",
            "Legacy Shaders/Self-Illum/Specular", "Legacy Shaders/Self-Illum/VertexLit", "Legacy Shaders/Specular",
            "Legacy Shaders/Transparent/Bumped Diffuse", "Legacy Shaders/Transparent/Bumped Specular",
            "Legacy Shaders/Transparent/Cutout/Bumped Diffuse", "Legacy Shaders/Transparent/Cutout/Bumped Specular",
            "Legacy Shaders/Transparent/Cutout/Diffuse", "Legacy Shaders/Transparent/Cutout/Soft Edge Unlit",
            "Legacy Shaders/Transparent/Cutout/Specular", "Legacy Shaders/Transparent/Cutout/VertexLit", "Legacy Shaders/Transparent/Diffuse",
            "Legacy Shaders/Transparent/Parallax Diffuse", "Legacy Shaders/Transparent/Parallax Specular", "Legacy Shaders/Transparent/Specular",
            "Legacy Shaders/Transparent/VertexLit", "Legacy Shaders/VertexLit", "MatCap/Vertex/Textured Lit", "Mobile/Bumped Diffuse",
            "Mobile/Bumped Specular", "Mobile/Bumped Specular (1 Directional Light)", "Mobile/Diffuse", "Mobile/Unlit (Supports Lightmap)",
            "Mobile/Particles/Additive", "Mobile/Particles/Alpha Blended", "Mobile/Particles/Multiply", "Mobile/Particles/VertexLit Blended",
            "Particles/~Additive-Multiply", "Particles/Additive", "Particles/Additive (Soft)", "Particles/Alpha Blended",
            "Particles/Alpha Blended Premultiply", "Particles/Anim Alpha Blended", "Particles/Multiply", "Particles/Multiply (Double)",
            "Particles/VertexLit Blended", "Sprites/Default", "Sprites/Diffuse", "Toon/Lit", "Toon/Lit (Double)", "Toon/Lit Cutout",
            "Toon/Lit Cutout (Double)", "Toon/Lit Outline", "UI/Default", "Unlit/FailShader", "VRChat/UI/Default",
        };

        internal static Fallback Resolve(Material m) =>
            Resolve(m.GetTag("VRCFallback", false, ""), m.shader.name, m.HasProperty("_Ramp"),
                m.IsKeywordEnabled("_ALPHABLEND_ON"), m.IsKeywordEnabled("_ALPHATEST_ON"));

        internal static Fallback Resolve(string tag, string shaderName, bool hasRamp, bool alphaBlend, bool alphaTest)
        {
            var f = new Fallback();
            if (!string.IsNullOrEmpty(tag))
            {
                // New system: the tag decides. toonstandard(outline) can't be combined with anything.
                f.FromTag = true;
                if (tag.Equals("toonstandard", StringComparison.OrdinalIgnoreCase)) { f.Kind = Kind.ToonStandard; return f; }
                if (tag.Equals("toonstandardoutline", StringComparison.OrdinalIgnoreCase)) { f.Kind = Kind.ToonStandardOutline; return f; }
                if (tag.Contains("Hidden")) { f.Kind = Kind.Hidden; return f; }
                f.Kind = tag.Contains("MobileToon") ? Kind.MobileToon
                    : tag.Contains("Unlit") ? Kind.Unlit
                    : tag.Contains("VertexLit") ? Kind.VertexLit
                    : tag.Contains("Toon") ? Kind.Toon
                    : tag.Contains("Particle") ? Kind.Particle
                    : tag.Contains("Sprite") ? Kind.Sprite
                    : tag.Contains("Matcap") ? Kind.Matcap
                    : Kind.Standard;
                f.Mode = tag.Contains("Cutout") ? Mode.Cutout : tag.Contains("Fade") ? Mode.Fade
                    : tag.Contains("Transparent") ? Mode.Transparent : Mode.Opaque;
                f.DoubleSided = f.Kind == Kind.Toon && tag.Contains("DoubleSided");
            }
            else
            {
                // Old system: built-in shaders are kept, others are matched by words in the name (case-sensitive)
                if (InternalShaders.Contains(shaderName)) { f.Kind = Kind.Keep; return f; }
                bool Has(string word) => shaderName.Contains(word);
                f.Kind = Has("Sprite") ? Kind.Sprite
                    : Has("Particle") ? Kind.Particle
                    : Has("MatCap") ? Kind.Matcap
                    : Has("Toon") || hasRamp ? Kind.Toon
                    : Has("Unlit") ? Kind.Unlit
                    : Has("VertexLit") ? Kind.VertexLit
                    : Kind.Standard;
                f.Mode = Has("Cutout") || alphaTest ? Mode.Cutout : Has("Fade") ? Mode.Fade
                    : Has("Transparent") || alphaBlend ? Mode.Transparent : Mode.Opaque;
                f.Outline = f.Kind == Kind.Toon && Has("Outline");
            }
            // There is no transparent Toon fallback: VRChat uses Unlit Transparent instead
            if (f.Kind == Kind.Toon && (f.Mode == Mode.Fade || f.Mode == Mode.Transparent)) f.Kind = Kind.Unlit;
            return f;
        }

        // Standard shader properties copied by the tag system; the old system only copies _MainTex and _Color
        static readonly string[] StandardProperties =
        {
            "_MainTex", "_MetallicGlossMap", "_SpecGlossMap", "_BumpMap", "_ParallaxMap", "_OcclusionMap", "_EmissionMap",
            "_DetailMask", "_DetailAlbedoMap", "_DetailNormalMap", "_Color", "_EmissionColor", "_SpecColor", "_Cutoff",
            "_Glossiness", "_GlossMapScale", "_SpecularHighlights", "_GlossyReflections", "_SmoothnessTextureChannel",
            "_Metallic", "_BumpScale", "_Parallax", "_OcclusionStrength", "_DetailNormalMapScale", "_UVSec",
        };

        // Builds the material VRChat would show instead of src (or returns src when VRChat keeps it) and fills in entry
        internal static Material CreateFallbackMaterial(Material src, Entry entry)
        {
            var f = Resolve(src);
            entry.Material = src.name;
            entry.Shader = src.shader.name;
            entry.Fallback = f.ToString();
            if (f.Kind == Kind.Keep)
            {
                entry.PreviewShader = src.shader.name;
                return src;
            }

            string note = null;
            string shader = PreviewShader(f, ref note);
            var sh = Shader.Find(shader);
            if (sh == null)
            {
                note = $"'{shader}' is not in this project (it comes with the VRChat SDK), so Standard is shown instead.";
                sh = Shader.Find(shader = "Standard");
            }

            var m = new Material(sh) { name = src.name + MaterialSuffix, hideFlags = HideFlags.DontSave };
            if (f.Kind == Kind.ToonStandard || f.Kind == Kind.ToonStandardOutline)
            {
                // Every same-named property and keyword is copied
                CopyProperties(src, m, Enumerable.Range(0, sh.GetPropertyCount()).Select(sh.GetPropertyName));
                foreach (var k in src.shaderKeywords) m.EnableKeyword(k);
            }
            else
            {
                var names = f.FromTag ? StandardProperties : new[] { "_MainTex", "_Color" };
                CopyProperties(src, m, names.Concat(new[] { "_Ramp", "_MatCap" }));
                if (!f.FromTag && !src.HasProperty("_MainTex") && !src.HasProperty("_Color"))
                    note = (note != null ? note + "\n" : "") +
                           "This shader has no _MainTex or _Color: VRChat shows a matcap in the viewer's trust rank colour instead.";
            }

            if (f.Kind == Kind.Hidden) SetHidden(m);
            else if (shader == "Standard") SetStandardMode(m, f.Mode);

            entry.PreviewShader = shader;
            entry.Note = note;
            return m;
        }

        // The Unity shader used to show each fallback. VRChat's own Toon fallbacks aren't available in Unity, so those are approximated.
        static string PreviewShader(Fallback f, ref string note)
        {
            switch (f.Kind)
            {
                case Kind.Unlit:
                    return f.Mode == Mode.Opaque ? "Unlit/Texture" : f.Mode == Mode.Cutout ? "Unlit/Transparent Cutout" : "Unlit/Transparent";
                case Kind.VertexLit:
                    return f.Mode == Mode.Opaque ? "Legacy Shaders/VertexLit"
                        : f.Mode == Mode.Cutout ? "Legacy Shaders/Transparent/Cutout/VertexLit" : "Legacy Shaders/Transparent/VertexLit";
                case Kind.Toon:
                    note = "Approximation: VRChat uses its own Toon Lit shader" +
                           (f.DoubleSided || f.Outline ? ". Double-sided faces and outlines aren't shown here." : ".");
                    return f.Mode == Mode.Cutout ? "Legacy Shaders/Transparent/Cutout/Diffuse" : "VRChat/Mobile/Toon Lit";
                case Kind.MobileToon: return "VRChat/Mobile/Toon Lit";
                case Kind.ToonStandard: return "VRChat/Mobile/Toon Standard";
                case Kind.ToonStandardOutline: return "VRChat/Mobile/Toon Standard (Outline)";
                case Kind.Particle:
                    note = "Approximation: VRChat picks its own particle shader.";
                    return "Legacy Shaders/Particles/Alpha Blended";
                case Kind.Sprite: return "Sprites/Default";
                case Kind.Matcap: return "VRChat/Mobile/Matcap Lit";
                case Kind.Hidden:
                    note = "Hidden: VRChat doesn't draw this material at all.";
                    return "Standard";
                default: return "Standard";
            }
        }

        static void CopyProperties(Material src, Material dst, IEnumerable<string> names)
        {
            foreach (var n in names)
            {
                if (src.HasTexture(n) && dst.HasTexture(n))
                {
                    dst.SetTexture(n, src.GetTexture(n));
                    dst.SetTextureScale(n, src.GetTextureScale(n));
                    dst.SetTextureOffset(n, src.GetTextureOffset(n));
                }
                else if (src.HasColor(n) && dst.HasColor(n)) dst.SetColor(n, src.GetColor(n));
                else if (src.HasVector(n) && dst.HasVector(n)) dst.SetVector(n, src.GetVector(n));
                else if (src.HasFloat(n) && dst.HasFloat(n)) dst.SetFloat(n, src.GetFloat(n));
                else if (src.HasInteger(n) && dst.HasInteger(n)) dst.SetInteger(n, src.GetInteger(n));
            }
        }

        // Same settings the Standard shader's inspector applies for each rendering mode
        internal static void SetStandardMode(Material m, Mode mode)
        {
            bool cutout = mode == Mode.Cutout, fade = mode == Mode.Fade, transparent = mode == Mode.Transparent;
            m.SetFloat("_Mode", (float)mode);
            m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : fade || transparent ? "Transparent" : "");
            m.SetFloat("_SrcBlend", (float)(fade ? BlendMode.SrcAlpha : BlendMode.One));
            m.SetFloat("_DstBlend", (float)(fade || transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetFloat("_ZWrite", fade || transparent ? 0f : 1f);
            SetKeyword(m, "_ALPHATEST_ON", cutout);
            SetKeyword(m, "_ALPHABLEND_ON", fade);
            SetKeyword(m, "_ALPHAPREMULTIPLY_ON", transparent);
            m.renderQueue = cutout ? (int)RenderQueue.AlphaTest : fade || transparent ? (int)RenderQueue.Transparent : -1;
        }

        // Fully transparent cutout: nothing is drawn, not even shadows
        static void SetHidden(Material m)
        {
            SetStandardMode(m, Mode.Cutout);
            m.SetTexture("_MainTex", null);
            m.SetColor("_Color", Color.clear);
            m.SetFloat("_Cutoff", 0.5f);
        }

        static void SetKeyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }
    }
}
