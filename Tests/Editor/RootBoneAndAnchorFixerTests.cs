// Root Bone and Anchor Fixer tests. Full docs: CLAUDE.md > Tools > Root Bone and Anchor Fixer.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HaTools.Tests
{
    public class RootBoneAndAnchorFixerTests
    {
        readonly List<GameObject> created = new List<GameObject>();
        GameObject avatar;
        Transform hips, chest;
        SkinnedMeshRenderer body, hair;
        MeshRenderer glasses;

        // Avatar with Hips > Chest bones, two skinned meshes (one on a disabled toggle) and a static mesh
        [SetUp]
        public void CreateAvatar()
        {
            avatar = new GameObject("Avatar");
            created.Add(avatar);
            hips = Child("Hips", avatar.transform, new Vector3(0f, 1f, 0f));
            chest = Child("Chest", hips, new Vector3(0f, 0.3f, 0f));
            body = Child("Body", avatar.transform, Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();
            var toggle = Child("Hair", avatar.transform, Vector3.zero);
            hair = toggle.gameObject.AddComponent<SkinnedMeshRenderer>();
            toggle.gameObject.SetActive(false);
            glasses = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshRenderer>();
            glasses.name = "Glasses";
            glasses.transform.SetParent(avatar.transform, false);
        }

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var go in created) if (go != null) Object.DestroyImmediate(go);
            created.Clear();
        }

        static Transform Child(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        void SetAll(Transform root, Transform probeAnchor)
        {
            body.rootBone = hair.rootBone = root;
            body.probeAnchor = hair.probeAnchor = glasses.probeAnchor = probeAnchor;
        }

        // ---------------------------------------------------------------- Check

        [Test]
        public void MatchingRootBonesAndAnchorsAreReported()
        {
            SetAll(hips, chest);
            var report = RootBoneAndAnchorFixer.Check(avatar);
            Assert.IsTrue(report.RootBonesMatch);
            Assert.IsTrue(report.AnchorsMatch);
            StringAssert.Contains("Hips", report.Text);
            StringAssert.Contains("Hips/Chest", report.Text);
        }

        [Test]
        public void DifferentRootBonesAndAnchorsListEachRenderer()
        {
            SetAll(hips, chest);
            hair.rootBone = chest;
            glasses.probeAnchor = null;

            var report = RootBoneAndAnchorFixer.Check(avatar);
            Assert.IsFalse(report.RootBonesMatch);
            Assert.IsFalse(report.AnchorsMatch);
            StringAssert.Contains("Hair", report.Text);
            StringAssert.Contains("Glasses", report.Text);
            StringAssert.Contains("own bounds centre", report.Text);
        }

        [Test]
        public void MissingAnchorsNeverCountAsShared()
        {
            SetAll(hips, null);
            Assert.IsFalse(RootBoneAndAnchorFixer.Check(avatar).AnchorsMatch);
        }

        [Test]
        public void IgnoresParticleRenderers()
        {
            SetAll(hips, chest);
            Child("Sparkles", avatar.transform, Vector3.zero).gameObject.AddComponent<ParticleSystem>();
            Assert.IsTrue(RootBoneAndAnchorFixer.Check(avatar).AnchorsMatch);
            Assert.AreEqual(3, RootBoneAndAnchorFixer.Renderers(avatar).Length);
        }

        // ---------------------------------------------------------------- Override

        [Test]
        public void OverrideSetsEveryRendererIncludingDisabledOnesWithUndo()
        {
            SetAll(chest, null);
            Undo.IncrementCurrentGroup();

            Assert.AreEqual(3, RootBoneAndAnchorFixer.Apply(avatar, hips, chest));
            Assert.AreSame(hips, body.rootBone);
            Assert.AreSame(hips, hair.rootBone);
            Assert.IsTrue(new Renderer[] { body, hair, glasses }.All(r => r.probeAnchor == chest));
            var report = RootBoneAndAnchorFixer.Check(avatar);
            Assert.IsTrue(report.RootBonesMatch && report.AnchorsMatch);

            Undo.PerformUndo();
            Assert.AreSame(chest, body.rootBone);
            Assert.IsNull(glasses.probeAnchor);
        }

        [Test]
        public void EmptyFieldLeavesThatSettingAlone()
        {
            SetAll(chest, hips);
            RootBoneAndAnchorFixer.Apply(avatar, null, chest);
            Assert.AreSame(chest, body.probeAnchor);
            Assert.AreSame(chest, body.rootBone, "Root bone should be untouched");

            RootBoneAndAnchorFixer.Apply(avatar, hips, null);
            Assert.AreSame(hips, body.rootBone);
            Assert.AreSame(chest, body.probeAnchor, "Anchor should be untouched");
        }

        [Test]
        public void NothingToChangeReturnsZero()
        {
            SetAll(hips, chest);
            Assert.AreEqual(0, RootBoneAndAnchorFixer.Apply(avatar, hips, chest));
        }

        [Test]
        public void ChangingRootBoneKeepsBoundsInTheSamePlace()
        {
            SetAll(chest, chest);
            body.localBounds = new Bounds(new Vector3(0f, 0.2f, 0.1f), new Vector3(0.6f, 0.8f, 0.4f));
            var worldCenter = chest.TransformPoint(body.localBounds.center);

            RootBoneAndAnchorFixer.Apply(avatar, hips, null);
            Assert.AreSame(hips, body.rootBone);
            Assert.Less(Vector3.Distance(worldCenter, hips.TransformPoint(body.localBounds.center)), 1e-4f);
            Assert.Less(Vector3.Distance(new Vector3(0.6f, 0.8f, 0.4f), body.localBounds.size), 1e-4f);
        }

        [Test]
        public void MoveBoundsCoversRotatedBox()
        {
            var from = Child("From", avatar.transform, Vector3.zero);
            var to = Child("To", avatar.transform, new Vector3(1f, 0f, 0f));
            to.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var moved = RootBoneAndAnchorFixer.MoveBounds(new Bounds(Vector3.zero, new Vector3(2f, 1f, 4f)), from, to);
            // Rotated 90 degrees around Y: X and Z sizes swap, and the box still sits at the world origin
            Assert.Less(Vector3.Distance(new Vector3(4f, 1f, 2f), moved.size), 1e-4f);
            Assert.Less(Vector3.Distance(Vector3.zero, to.TransformPoint(moved.center)), 1e-4f);
        }
    }
}
