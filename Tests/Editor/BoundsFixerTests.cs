// Bounds Fixer tests. Full docs: CLAUDE.md > Tools > Bounds Fixer.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HaTools.Tests
{
    public class BoundsFixerTests
    {
        const string CurrentPose = "current pose";
        static readonly Bounds Huge = new Bounds(Vector3.zero, Vector3.one * 20f);

        readonly List<Object> created = new List<Object>();
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        GameObject avatar;

        // Humanoid in T-pose facing +Z, 1.6 m tall, hips at 1 m and hands 0.7 m to each side at 1.4 m
        [SetUp]
        public void CreateAvatar()
        {
            avatar = new GameObject("Avatar");
            created.Add(avatar);
            Bone("Hips", null, new Vector3(0f, 1f, 0f));
            Bone("Spine", "Hips", new Vector3(0f, 1.1f, 0f));
            Bone("Chest", "Spine", new Vector3(0f, 1.25f, 0f));
            Bone("Neck", "Chest", new Vector3(0f, 1.45f, 0f));
            Bone("Head", "Neck", new Vector3(0f, 1.55f, 0f));
            foreach (var (side, x) in new[] { ("Left", -1f), ("Right", 1f) })
            {
                Bone(side + "UpperArm", "Chest", new Vector3(0.2f * x, 1.4f, 0f));
                Bone(side + "LowerArm", side + "UpperArm", new Vector3(0.45f * x, 1.4f, 0f));
                Bone(side + "Hand", side + "LowerArm", new Vector3(0.7f * x, 1.4f, 0f));
                Bone(side + "UpperLeg", "Hips", new Vector3(0.1f * x, 0.95f, 0f));
                Bone(side + "LowerLeg", side + "UpperLeg", new Vector3(0.1f * x, 0.5f, 0f));
                Bone(side + "Foot", side + "LowerLeg", new Vector3(0.1f * x, 0.08f, 0f));
            }

            var description = new HumanDescription
            {
                human = bones.Keys.Select(n => new HumanBone { boneName = n, humanName = n, limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                skeleton = avatar.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone
                    { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
            };
            var rig = AvatarBuilder.BuildHumanAvatar(avatar, description);
            created.Add(rig);
            Assert.IsTrue(rig.isValid && rig.isHuman, "The test rig should be a valid humanoid");
            avatar.AddComponent<Animator>().avatar = rig;
        }

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            bones.Clear();
        }

        void Bone(string name, string parent, Vector3 worldPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent == null ? avatar.transform : bones[parent], false);
            t.position = worldPosition;
            bones[name] = t;
        }

        // A 0.1 m cube skinned to one bone, with bounds that fit it exactly in the current pose
        SkinnedMeshRenderer Skinned(string name, string bone, string rootBone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(avatar.transform, false);
            var centre = bones[bone].position;
            var mesh = new Mesh
            {
                name = name,
                vertices = Enumerable.Range(0, 8).Select(i => centre + 0.05f * new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1)).ToArray(),
                triangles = new[] { 0, 1, 2 },
                boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 8).ToArray(),
                bindposes = new[] { bones[bone].worldToLocalMatrix },
            };
            created.Add(mesh);

            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = mesh;
            r.bones = new[] { bones[bone] };
            r.rootBone = rootBone == null ? null : bones[rootBone];
            var root = r.rootBone != null ? r.rootBone : r.transform;
            r.localBounds = new Bounds(root.InverseTransformPoint(centre), Vector3.one * 0.1f);
            return r;
        }

        // ---------------------------------------------------------------- Measuring

        [Test]
        public void OvershootIsZeroWhenTheBoxFits()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            Assert.AreEqual(0f, BoundsFixer.Overshoot(bounds, bounds, Vector3.one));
            Assert.AreEqual(0f, BoundsFixer.Overshoot(bounds, new Bounds(Vector3.one * 0.5f, Vector3.one), Vector3.one));
        }

        [Test]
        public void OvershootIsTheWorstSideInMetres()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            var needed = new Bounds();
            needed.SetMinMax(new Vector3(-1.5f, -0.5f, -0.5f), new Vector3(0.5f, 1.2f, 0.5f));
            Assert.AreEqual(0.5f, BoundsFixer.Overshoot(bounds, needed, Vector3.one), 1e-5f);
            // A root bone scaled 2x on X makes the same local distance twice as long
            Assert.AreEqual(1f, BoundsFixer.Overshoot(bounds, needed, new Vector3(-2f, 1f, 1f)), 1e-5f);
        }

        [Test]
        public void EveryPoseMuscleExists()
        {
            foreach (var (name, muscles) in BoundsFixer.Poses)
                foreach (var (muscle, value) in muscles)
                {
                    Assert.IsTrue(HumanTrait.MuscleName.Any(m => m.EndsWith(muscle)), $"'{muscle}' in '{name}' is not a humanoid muscle");
                    Assert.That(value, Is.InRange(-1f, 1f));
                }
        }

        // ---------------------------------------------------------------- Check

        [Test]
        public void TightBoundsFarFromTheRootBoneAreTooSmall()
        {
            var glove = Skinned("Glove", "LeftHand", "Hips");
            var entry = BoundsFixer.Check(avatar).Single();

            Assert.AreSame(glove, entry.Renderer);
            Assert.IsTrue(entry.TooSmall);
            Assert.AreNotEqual(CurrentPose, entry.Pose);
            Assert.Greater(entry.Overshoot, 0.3f, "A hand moves a lot further than this from where it is in T-pose");
            // The box covers the T-pose cube it started from
            Assert.AreEqual(0f, BoundsFixer.Overshoot(entry.Needed, glove.localBounds, Vector3.one), 1e-4f);
        }

        [Test]
        public void MeshThatStaysInsideItsBoundsIsFine()
        {
            // The belt follows the root bone, so it never leaves even tight bounds; the glove has room to move
            Skinned("Belt", "Hips", "Hips");
            Skinned("Glove", "LeftHand", "Hips").localBounds = Huge;

            foreach (var entry in BoundsFixer.Check(avatar))
            {
                Assert.IsFalse(entry.TooSmall, entry.Renderer.name);
                Assert.AreEqual(0f, entry.Overshoot, BoundsFixer.Tolerance, entry.Renderer.name);
            }
        }

        [Test]
        public void WorstPoseIsNamed()
        {
            var glove = Skinned("Glove", "LeftHand", "Hips");
            Bounds Open(Vector3 min, Vector3 max)
            {
                var b = new Bounds();
                b.SetMinMax(min, max);
                return b;
            }

            // Room everywhere except above, in front or behind (hips space: the hand rests at y 0.4, z 0)
            glove.localBounds = Open(Vector3.one * -10f, new Vector3(10f, 0.5f, 10f));
            StringAssert.Contains("arms up", BoundsFixer.Check(avatar).Single().Pose);
            glove.localBounds = Open(Vector3.one * -10f, new Vector3(10f, 10f, 0.1f));
            StringAssert.Contains("forward", BoundsFixer.Check(avatar).Single().Pose);
            glove.localBounds = Open(new Vector3(-10f, -10f, -0.1f), Vector3.one * 10f);
            StringAssert.Contains("back", BoundsFixer.Check(avatar).Single().Pose);
        }

        [Test]
        public void LegsAreMovedToo()
        {
            Skinned("Shoe", "LeftFoot", "Hips");
            var entry = BoundsFixer.Check(avatar).Single();
            Assert.IsTrue(entry.TooSmall);
            StringAssert.Contains("legs", entry.Pose);
        }

        [Test]
        public void OnlyTheCurrentPoseIsCheckedWithoutAHumanoidRig()
        {
            Object.DestroyImmediate(avatar.GetComponent<Animator>());
            Assert.IsFalse(BoundsFixer.IsHumanoid(avatar));

            var glove = Skinned("Glove", "LeftHand", "Hips");
            Assert.IsFalse(BoundsFixer.Check(avatar).Single().TooSmall);

            glove.localBounds = new Bounds(glove.localBounds.center, Vector3.one * 0.06f);
            var entry = BoundsFixer.Check(avatar).Single();
            Assert.IsTrue(entry.TooSmall);
            Assert.AreEqual(CurrentPose, entry.Pose);
            Assert.AreEqual(0.02f, entry.Overshoot, 1e-4f);
        }

        [Test]
        public void DisabledRenderersAndMissingRootBonesAreChecked()
        {
            Skinned("Toggle", "LeftHand", "Hips").gameObject.SetActive(false);
            // Without a root bone the bounds are relative to the renderer itself
            Skinned("No Root", "RightHand", null);

            var entries = BoundsFixer.Check(avatar);
            Assert.AreEqual(2, entries.Count);
            Assert.IsTrue(entries.All(e => e.TooSmall));
        }

        [Test]
        public void RenderersWithoutAMeshAreSkipped()
        {
            new GameObject("Empty").AddComponent<SkinnedMeshRenderer>().transform.SetParent(avatar.transform, false);
            Assert.IsEmpty(BoundsFixer.Check(avatar));
        }

        [Test]
        public void CheckLeavesTheAvatarAndSceneAlone()
        {
            var glove = Skinned("Glove", "LeftHand", "Hips");
            var scene = SceneManager.GetActiveScene();
            int roots = scene.rootCount, previewScenes = EditorSceneManager.previewSceneCount;
            bool dirty = scene.isDirty;
            var hand = bones["LeftHand"].position;
            var bounds = glove.localBounds;

            BoundsFixer.Check(avatar);

            Assert.AreEqual(roots, scene.rootCount);
            Assert.AreEqual(previewScenes, EditorSceneManager.previewSceneCount);
            Assert.AreEqual(dirty, scene.isDirty);
            Assert.AreEqual(hand, bones["LeftHand"].position);
            Assert.AreEqual(bounds, glove.localBounds);
        }

        // ---------------------------------------------------------------- Grow

        [Test]
        public void GrowFitsEveryPoseWithUndo()
        {
            var glove = Skinned("Glove", "LeftHand", "Hips");
            var tight = glove.localBounds;
            var entries = BoundsFixer.Check(avatar);
            Undo.IncrementCurrentGroup();

            Assert.AreEqual(1, BoundsFixer.Grow(entries));
            Assert.IsFalse(entries[0].TooSmall, "The entry follows the renderer's bounds");
            Assert.IsFalse(BoundsFixer.Check(avatar).Single().TooSmall, "A new check agrees");
            // Room on every side of the needed box
            var needed = entries[0].Needed;
            float margin = BoundsFixer.Margin * Mathf.Max(needed.size.x, needed.size.y, needed.size.z);
            Assert.Less(Vector3.Distance(needed.max + Vector3.one * margin, glove.localBounds.max), 1e-4f);
            Assert.Less(Vector3.Distance(needed.min - Vector3.one * margin, glove.localBounds.min), 1e-4f);
            Assert.AreEqual(0, BoundsFixer.Grow(entries), "Nothing left to grow");

            Undo.PerformUndo();
            Assert.AreEqual(tight, glove.localBounds);
            Assert.IsTrue(entries[0].TooSmall);
        }

        [Test]
        public void UpdateWhenOffscreenChecksAndGrowsTheStoredBounds()
        {
            // With the option on, localBounds follows the mesh; the stored bounds are what turning it off brings back
            var glove = Skinned("Glove", "LeftHand", "Hips");
            var tight = glove.localBounds;
            glove.updateWhenOffscreen = true;
            Assert.AreEqual(tight, BoundsFixer.Stored(glove));

            var entries = BoundsFixer.Check(avatar);
            Assert.IsTrue(entries[0].TooSmall);
            Assert.AreEqual(1, BoundsFixer.Grow(entries));
            Assert.IsFalse(entries[0].TooSmall);

            glove.updateWhenOffscreen = false;
            Assert.AreEqual(0f, BoundsFixer.Overshoot(glove.localBounds, entries[0].Needed, Vector3.one));
        }

        [Test]
        public void GrowNeverShrinksAndLeavesFineBoundsAlone()
        {
            // Too small above, far too large everywhere else
            var glove = Skinned("Glove", "LeftHand", "Hips");
            var before = new Bounds();
            before.SetMinMax(Vector3.one * -10f, new Vector3(10f, 0.5f, 10f));
            glove.localBounds = before;
            var fine = Skinned("Fine", "RightHand", "Hips");
            fine.localBounds = Huge;

            Assert.AreEqual(1, BoundsFixer.Grow(BoundsFixer.Check(avatar)));
            Assert.AreEqual(Huge, fine.localBounds);
            Assert.AreEqual(before.min, glove.localBounds.min);
            Assert.AreEqual(10f, glove.localBounds.max.x, 1e-4f);
            Assert.Greater(glove.localBounds.max.y, 0.5f);
        }
    }
}
