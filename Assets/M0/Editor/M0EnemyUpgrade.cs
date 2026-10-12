#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace M0.Editor
{
    public static class M0EnemyUpgrade
    {
        static Material armor, dark, iron, orange, cueMaterial;
        [MenuItem("M0/Upgrade Enemy Models and Attacks")]
        public static void Upgrade()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            var sandbox = Object.FindAnyObjectByType<M0Sandbox>();
            if (sandbox == null) throw new System.InvalidOperationException("Open M0Sandbox first.");
            foreach (var enemy in sandbox.Enemies) { Undo.RegisterFullObjectHierarchyUndo(enemy.gameObject, "Upgrade enemy presentation"); Build(enemy); }
            const string path = "Assets/M0/Prefabs/TrainingEnemy.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try { Build(prefab.GetComponent<M0Enemy>()); PrefabUtility.SaveAsPrefabAsset(prefab, path); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            EditorSceneManager.MarkSceneDirty(sandbox.gameObject.scene); EditorSceneManager.SaveScene(sandbox.gameObject.scene); AssetDatabase.SaveAssets();
            Debug.Log("Enemy upgrade saved: all 26 pool entries and reusable prefab. Gameplay systems preserved.");
        }
        public static void Build(M0Enemy enemy)
        {
            armor = M0PlayerUpgrade.PixelMaterial("Enemy moss armor", new Color(0.26f, 0.43f, 0.39f));
            dark = M0PlayerUpgrade.PixelMaterial("Enemy obsidian", new Color(0.065f, 0.09f, 0.11f));
            iron = M0PlayerUpgrade.PixelMaterial("Enemy worn iron", new Color(0.49f, 0.56f, 0.55f));
            orange = AssetDatabase.LoadAssetAtPath<Material>("Assets/M0/Materials/Weakpoint orange.mat");
            var rig = enemy.GetComponent<M0EnemyRig>(); if (rig == null) rig = enemy.gameObject.AddComponent<M0EnemyRig>(); enemy.Rig = rig;
            var oldCue = enemy.transform.Find("Attack warning ring"); if (oldCue != null) Object.DestroyImmediate(oldCue.gameObject);
            rig.TelegraphRing = null;
            rig.Torso = enemy.transform.Find("Torso"); rig.Head = enemy.Head;
            rig.LeftShoulder = enemy.LeftArm; rig.RightShoulder = enemy.RightArm;
            rig.LeftLeg = enemy.transform.Find("Left leg"); rig.RightLeg = enemy.transform.Find("Right leg");
            Prepare(rig.Torso, new Vector3(0, 1.01f, 0), new Vector3(0.69f, 0.72f, 0.44f), Vector3.zero);
            Prepare(rig.Head, new Vector3(0, 1.64f, 0.015f), new Vector3(0.46f, 0.44f, 0.43f), Vector3.zero);
            foreach (var arm in new[] { rig.LeftShoulder, rig.RightShoulder })
                Prepare(arm, new Vector3(arm == rig.LeftShoulder ? -0.49f : 0.49f, 1.4f, 0), new Vector3(0.24f, 0.93f, 0.27f), Vector3.down * 0.46f);
            foreach (var leg in new[] { rig.LeftLeg, rig.RightLeg })
                Prepare(leg, new Vector3(leg == rig.LeftLeg ? -0.21f : 0.21f, 0.67f, 0), new Vector3(0.27f, 0.67f, 0.32f), Vector3.down * 0.335f);
            var chest = Detail(rig.Torso);
            enemy.BodyRenderer = Box("Armored chest", chest, Vector3.zero, new Vector3(0.69f, 0.62f, 0.44f), armor).GetComponent<Renderer>();
            Box("Shoulder ridge", chest, new Vector3(0, 0.31f, 0), new Vector3(0.77f, 0.14f, 0.47f), dark);
            Box("Waist", chest, new Vector3(0, -0.36f, 0), new Vector3(0.52f, 0.14f, 0.35f), dark);
            for (int i = 0; i < 3; i++) Box("Segmented chest plate", chest, new Vector3(0, 0.15f - i * 0.16f, 0.244f), new Vector3(0.48f - i * 0.05f, 0.09f, 0.075f), iron);
            Box("Core slit", chest, new Vector3(0, 0.18f, 0.29f), new Vector3(0.09f, 0.12f, 0.04f), orange);
            var head = Detail(rig.Head);
            Box("Block weakpoint skull", head, Vector3.zero, new Vector3(0.43f, 0.4f, 0.40f), orange);
            Box("Helmet cap", head, new Vector3(0, 0.19f, -0.015f), new Vector3(0.48f, 0.10f, 0.45f), dark);
            Box("Heavy brow", head, new Vector3(0, 0.09f, 0.21f), new Vector3(0.47f, 0.09f, 0.06f), armor);
            Box("Jaw guard", head, new Vector3(0, -0.18f, 0.07f), new Vector3(0.46f, 0.10f, 0.30f), iron);
            foreach (float side in new[] { -1f, 1f })
            {
                Box("Helmet side", head, new Vector3(side * 0.22f, 0, -0.04f), new Vector3(0.08f, 0.36f, 0.33f), armor);
                Box("Inset eye", head, new Vector3(side * 0.095f, 0.018f, 0.211f), new Vector3(0.068f, 0.055f, 0.028f), dark);
                var horn = Box("Stepped horn base", head, new Vector3(side * 0.24f, 0.25f, -0.055f), new Vector3(0.11f, 0.15f, 0.12f), iron);
                horn.localRotation = Quaternion.Euler(0, 0, -side * 20);
                Box("Horn tip", head, new Vector3(side * 0.28f, 0.35f, -0.055f), new Vector3(0.06f, 0.10f, 0.07f), dark);
            }
            rig.LeftElbow = Arm(rig.LeftShoulder, false, rig); rig.RightElbow = Arm(rig.RightShoulder, true, rig);
            foreach (var leg in new[] { rig.LeftLeg, rig.RightLeg })
            {
                var root = Detail(leg);
                Box("Armored thigh", root, new Vector3(0, -0.17f, 0), new Vector3(0.27f, 0.33f, 0.28f), armor);
                Box("Shin", root, new Vector3(0, -0.46f, 0), new Vector3(0.23f, 0.25f, 0.26f), dark);
                Box("Knee plate", root, new Vector3(0, -0.35f, 0.14f), new Vector3(0.29f, 0.17f, 0.11f), iron);
                Box("Heavy boot", root, new Vector3(0, -0.59f, 0.07f), new Vector3(0.29f, 0.16f, 0.42f), dark);
            }
            foreach (var bone in enemy.GetComponentsInChildren<Rigidbody>(true))
            {
                var joint = bone.GetComponent<CharacterJoint>(); if (joint == null) continue;
                joint.autoConfigureConnectedAnchor = false;
                Vector3 anchor = bone.transform == rig.Head ? Vector3.down * 0.23f : bone.transform == rig.LeftShoulder || bone.transform == rig.RightShoulder ? Vector3.zero : Vector3.zero;
                joint.anchor = anchor; joint.connectedAnchor = joint.connectedBody.transform.InverseTransformPoint(bone.transform.TransformPoint(anchor));
            }
            rig.TelegraphRing = BuildTelegraphRing(enemy.transform);
            enemy.SenseRange = 28f; enemy.AttackRange = 2.0f; enemy.LockRange = 8.0f; EditorUtility.SetDirty(enemy);
        }
        static void Prepare(Transform bone, Vector3 at, Vector3 size, Vector3 center)
        {
            bone.localPosition = at; bone.localRotation = Quaternion.identity; bone.localScale = Vector3.one;
            bone.GetComponent<Renderer>().enabled = false;
            var sphere = bone.GetComponent<SphereCollider>(); if (sphere != null) Object.DestroyImmediate(sphere);
            var box = bone.GetComponent<BoxCollider>(); if (box == null) box = bone.gameObject.AddComponent<BoxCollider>(); box.size = size; box.center = center;
        }
        static Transform Detail(Transform parent)
        {
            var old = parent.Find("Voxel detail"); if (old != null) Object.DestroyImmediate(old.gameObject);
            return Pivot("Voxel detail", parent, Vector3.zero);
        }
        static Transform Arm(Transform shoulder, bool strike, M0EnemyRig rig)
        {
            var detail = Detail(shoulder);
            Box("Pauldron", detail, new Vector3(0, -0.075f, 0), new Vector3(0.36f, 0.27f, 0.38f), armor);
            Box("Upper arm", detail, new Vector3(0, -0.3f, 0), new Vector3(0.23f, 0.34f, 0.25f), dark);
            var elbow = Pivot("Elbow pivot", detail, Vector3.down * 0.47f);
            Box("Elbow rivet", elbow, Vector3.zero, new Vector3(0.27f, 0.14f, 0.28f), iron);
            Box("Forearm plates", elbow, new Vector3(0, -0.22f, 0), new Vector3(0.28f, 0.38f, 0.29f), armor);
            var fist = Box(strike ? "Striking iron gauntlet / exact damage shape" : "Iron gauntlet", elbow, new Vector3(0, -0.46f, 0.02f), new Vector3(0.34f, 0.30f, 0.36f), iron);
            for (int i = -1; i <= 1; i++) Box("Knuckle stud", elbow, new Vector3(i * 0.1f, -0.48f, 0.218f), new Vector3(0.065f, 0.11f, 0.07f), dark);
            if (strike)
            {
                rig.StrikeShape = fist.gameObject.AddComponent<BoxCollider>(); rig.StrikeShape.isTrigger = true;
                rig.StrikeShape.size = Vector3.one * 1.45f;
                var trail = Pivot("Short active punch trail", elbow, new Vector3(0, -0.46f, 0.05f));
                rig.StrikeTrail = trail.gameObject.AddComponent<TrailRenderer>(); rig.StrikeTrail.time = 0.085f;
                rig.StrikeTrail.widthMultiplier = 0.12f; rig.StrikeTrail.emitting = false;
                rig.StrikeTrail.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/M0/Materials/Translucent sword trail.mat");
                rig.StrikeTrail.startColor = new Color(1, 0.45f, 0.18f, 0.4f); rig.StrikeTrail.endColor = new Color(1, 0.25f, 0.05f, 0);
            }
            return elbow;
        }
        static LineRenderer BuildTelegraphRing(Transform enemy)
        {
            if (cueMaterial == null)
            {
                const string materialPath = "Assets/M0/Materials/Enemy attack cue.mat";
                cueMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (cueMaterial == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit");
                    cueMaterial = new Material(shader) { name = "Enemy attack cue" };
                    cueMaterial.SetColor("_BaseColor", Color.white);
                    cueMaterial.SetFloat("_Surface", 1);
                    cueMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    cueMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    cueMaterial.SetFloat("_ZWrite", 0);
                    cueMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    cueMaterial.renderQueue = 3000;
                    AssetDatabase.CreateAsset(cueMaterial, materialPath);
                }
            }
            var go = new GameObject("Attack warning ring");
            go.transform.SetParent(enemy, false);
            go.transform.localPosition = new Vector3(0, 1.05f, 0);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.loop = false; line.positionCount = 65;
            line.sharedMaterial = cueMaterial; line.startWidth = line.endWidth = 0.06f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            line.sortingOrder = 5; line.enabled = false;
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / (line.positionCount - 1);
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0));
            }
            return line;
        }
        static Transform Pivot(string name, Transform parent, Vector3 at)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = at; return go.transform; }
        static Transform Box(string name, Transform parent, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
        }
    }
}
#endif
