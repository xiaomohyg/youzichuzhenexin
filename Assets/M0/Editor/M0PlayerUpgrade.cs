#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace M0.Editor
{
    public static class M0PlayerUpgrade
    {
        static Material sleeve, skin, leather, edge, steel, charcoal, trim, eyeWhite;
        [MenuItem("M0/Upgrade Player Presentation")]
        public static void Upgrade()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            var p = Object.FindAnyObjectByType<M0Player>();
            if (p == null) throw new System.InvalidOperationException("Open M0Sandbox first.");
            Build(p); EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            EditorSceneManager.SaveScene(p.gameObject.scene); AssetDatabase.SaveAssets();
        }
        public static void Build(M0Player p)
        {
            Undo.RegisterFullObjectHierarchyUndo(p.gameObject, "Upgrade M0 player rig");
            if (p.WeaponRoot != null) Undo.DestroyObjectImmediate(p.WeaponRoot.gameObject);
            if (p.Rig != null)
            {
                if (p.Rig.Body != null) Undo.DestroyObjectImmediate(p.Rig.Body.gameObject);
                Undo.DestroyObjectImmediate(p.Rig);
            }
            sleeve = PixelMaterial("Pixel sleeve", new Color(0.10f, 0.28f, 0.39f));
            skin = PixelMaterial("Pixel skin", new Color(0.72f, 0.47f, 0.28f));
            leather = PixelMaterial("Pixel leather", new Color(0.26f, 0.15f, 0.10f));
            charcoal = PixelMaterial("Pixel charcoal", new Color(0.045f, 0.08f, 0.12f));
            steel = PixelMaterial("Pixel steel", new Color(0.54f, 0.68f, 0.72f));
            edge = PixelMaterial("Pixel edge", new Color(0.82f, 0.94f, 0.94f));
            trim = PixelMaterial("Pixel gold", new Color(0.82f, 0.53f, 0.14f));
            eyeWhite = PixelMaterial("Pixel eye white", new Color(0.88f, 0.94f, 0.91f));
            p.gameObject.layer = 2;
            var rig = p.gameObject.AddComponent<M0PlayerRig>(); p.Rig = rig;
            rig.Root = Pivot("Articulated voxel hands", p.View.transform, Vector3.zero); p.WeaponRoot = rig.Root;
            rig.RightHand = Pivot("Right wrist / weapon pivot", rig.Root, new Vector3(0.36f, -0.38f, 0.62f));
            rig.LeftHand = Pivot("Left wrist / guard support", rig.Root, new Vector3(-0.36f, -0.42f, 0.55f));
            Hand(rig.RightHand); Hand(rig.LeftHand);
            rig.RightUpper = Box("Right upper sleeve", rig.Root, Vector3.zero, Vector3.one, sleeve);
            rig.RightForearm = Box("Right forearm", rig.Root, Vector3.zero, Vector3.one, skin);
            rig.LeftUpper = Box("Left upper sleeve", rig.Root, Vector3.zero, Vector3.one, sleeve);
            rig.LeftForearm = Box("Left forearm", rig.Root, Vector3.zero, Vector3.one, skin);
            ArmRest(rig.RightUpper, rig.RightForearm, 1); ArmRest(rig.LeftUpper, rig.LeftForearm, -1);
            p.Blade = Sword(rig.RightHand, "Stepped pixel steel sword");
            p.Gun = Pivot("Pixel pulse sidearm", rig.RightHand, Vector3.zero);
            Box("Receiver", p.Gun, new Vector3(0, 0.10f, 0.12f), new Vector3(0.13f, 0.12f, 0.32f), charcoal);
            Box("Slide", p.Gun, new Vector3(0, 0.18f, 0.13f), new Vector3(0.12f, 0.06f, 0.36f), steel);
            Box("Grip", p.Gun, new Vector3(0, -0.015f, 0.01f), new Vector3(0.10f, 0.17f, 0.12f), leather);
            Box("Sight", p.Gun, new Vector3(0, 0.23f, 0.22f), new Vector3(0.04f, 0.04f, 0.04f), trim);
            p.Gun.gameObject.SetActive(false);
            var trailRoot = Pivot("Active swing trail", p.Blade, new Vector3(0, 0.61f, 0));
            rig.SwordTrail = trailRoot.gameObject.AddComponent<TrailRenderer>();
            rig.SwordTrail.time = 0.055f; rig.SwordTrail.widthMultiplier = 0.07f; rig.SwordTrail.minVertexDistance = 0.04f;
            rig.SwordTrail.emitting = false; rig.SwordTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            const string trailPath = "Assets/M0/Materials/Translucent sword trail.mat";
            var trailMaterial = AssetDatabase.LoadAssetAtPath<Material>(trailPath);
            if (trailMaterial == null)
            {
                trailMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                trailMaterial.name = "Translucent sword trail"; trailMaterial.SetFloat("_Surface", 1);
                trailMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                trailMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                trailMaterial.SetFloat("_ZWrite", 0); trailMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                trailMaterial.SetColor("_BaseColor", new Color(0.6f, 0.95f, 1, 0.22f)); trailMaterial.renderQueue = 3000;
                AssetDatabase.CreateAsset(trailMaterial, trailPath);
            }
            rig.SwordTrail.sharedMaterial = trailMaterial;
            rig.SwordTrail.startColor = new Color(0.6f, 0.95f, 1, 0.35f); rig.SwordTrail.endColor = new Color(0.6f, 0.95f, 1, 0);
            rig.Body = Pivot("Third person voxel character", p.transform, Vector3.zero);
            rig.Torso = Box("Jacket", rig.Body, new Vector3(0, 1.12f, 0), new Vector3(0.48f, 0.60f, 0.26f), sleeve);
            Box("Belt", rig.Body, new Vector3(0, 0.82f, 0), new Vector3(0.49f, 0.08f, 0.28f), leather);
            Box("Buckle", rig.Body, new Vector3(0, 0.82f, 0.147f), new Vector3(0.1f, 0.07f, 0.02f), trim);
            rig.Head = Pivot("Head pivot", rig.Body, new Vector3(0, 1.53f, 0));
            Box("Head", rig.Head, Vector3.zero, new Vector3(0.34f, 0.36f, 0.32f), skin);
            Box("Hair", rig.Head, new Vector3(0, 0.13f, -0.02f), new Vector3(0.36f, 0.13f, 0.34f), charcoal);
            foreach (float x in new[] { -0.085f, 0.085f })
            {
                Box("Eye sclera", rig.Head, new Vector3(x, 0.01f, 0.163f), new Vector3(0.062f, 0.055f, 0.025f), eyeWhite);
                Box("Eye pupil", rig.Head, new Vector3(x, 0.01f, 0.181f), new Vector3(0.026f, 0.038f, 0.012f), charcoal);
            }
            rig.LeftLeg = Limb("Left thigh", rig.Body, new Vector3(-0.13f, 0.78f, 0), new Vector3(0.20f, 0.36f, 0.24f), charcoal);
            rig.RightLeg = Limb("Right thigh", rig.Body, new Vector3(0.13f, 0.78f, 0), new Vector3(0.20f, 0.36f, 0.24f), charcoal);
            rig.LeftKnee = Limb("Left knee", rig.LeftLeg, Vector3.down * 0.36f, new Vector3(0.20f, 0.38f, 0.24f), charcoal);
            rig.RightKnee = Limb("Right knee", rig.RightLeg, Vector3.down * 0.36f, new Vector3(0.20f, 0.38f, 0.24f), charcoal);
            rig.BodyLeftArm = Limb("Left shoulder", rig.Body, new Vector3(-0.34f, 1.37f, 0), new Vector3(0.17f, 0.56f, 0.20f), sleeve);
            rig.BodyRightArm = Limb("Right shoulder", rig.Body, new Vector3(0.34f, 1.37f, 0), new Vector3(0.17f, 0.56f, 0.20f), sleeve);
            Box("Third person right hand", rig.BodyRightArm, new Vector3(0, -0.59f, 0), new Vector3(0.17f, 0.14f, 0.20f), skin);
            Box("Third person left hand", rig.BodyLeftArm, new Vector3(0, -0.59f, 0), new Vector3(0.17f, 0.14f, 0.20f), skin);
            rig.BodySword = Sword(rig.BodyRightArm, "Equipped sword");
            rig.BodySword.localPosition = new Vector3(0, -0.59f, 0.04f); rig.BodySword.localRotation = Quaternion.Euler(90, 0, 0);
            rig.BodyGun = Object.Instantiate(p.Gun, rig.BodyRightArm); rig.BodyGun.name = "Equipped sidearm";
            rig.BodyGun.localPosition = new Vector3(0, -0.59f, 0.04f); rig.BodyGun.localRotation = Quaternion.Euler(-90, 0, 0);
            rig.Body.gameObject.SetActive(false);
            p.LightRecovery = 0.46f; p.HeavyRecovery = 0.78f;
            p.BlockWindup = 0; p.PerfectWindow = 0.16f;
            var sandbox = Object.FindAnyObjectByType<M0Sandbox>();
            var slowMotion = sandbox == null ? null : sandbox.GetComponent<M0DodgeEffect>();
            if (sandbox != null && slowMotion == null) slowMotion = Undo.AddComponent<M0DodgeEffect>(sandbox.gameObject);
            if (slowMotion != null) { slowMotion.Duration = 1.18f; slowMotion.MinimumTimeScale = 0.22f; EditorUtility.SetDirty(slowMotion); }
            rig.RightHand.localRotation = Quaternion.Euler(-12, 0, -18);
            EditorUtility.SetDirty(p);
        }
        static void Hand(Transform wrist)
        {
            Box("Block palm", wrist, new Vector3(0, -0.03f, -0.025f), new Vector3(0.15f, 0.14f, 0.17f), skin);
            Box("Leather cuff", wrist, new Vector3(0, -0.065f, -0.14f), new Vector3(0.16f, 0.14f, 0.07f), leather);
            Box("Thumb", wrist, new Vector3(-0.09f, -0.02f, 0.02f), new Vector3(0.045f, 0.075f, 0.09f), skin);
        }
        static void ArmRest(Transform upper, Transform fore, float side)
        {
            Vector3 shoulder = new Vector3(side * 0.38f, -0.35f, -0.08f), wrist = new Vector3(side * 0.36f, -0.40f, 0.58f);
            Vector3 elbow = Vector3.Lerp(shoulder, wrist, 0.48f) + new Vector3(side * 0.09f, -0.16f, -0.04f);
            SegmentRest(upper, shoulder, elbow, 0.17f); SegmentRest(fore, elbow, wrist, 0.14f);
        }
        static void SegmentRest(Transform part, Vector3 a, Vector3 b, float width)
        { part.localPosition = (a + b) * 0.5f; part.localRotation = Quaternion.FromToRotation(Vector3.up, b - a); part.localScale = new Vector3(width, (b - a).magnitude, width); }
        static Transform Sword(Transform parent, string name)
        {
            var root = Pivot(name, parent, Vector3.zero);
            // Every row is a pixel in a custom stepped silhouette, extruded to a solid blade.
            string[] rows = { "...e...", "..ese..", "..ese..", "..ese..", "..ese..", "..ese..", "..ese..", "..ese..", "..ese..", "..ese..", "..ese..", "..ese..", ".ggggg.", "gg.g.gg", "...l...", "...l...", "...l...", "...l...", "..ggg.." };
            float pixel = 0.037f;
            var groups = new Dictionary<char, List<CombineInstance>>();
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[y].Length; x++)
            {
                char c = rows[y][x]; if (c == '.') continue;
                if (!groups.ContainsKey(c)) groups[c] = new List<CombineInstance>();
                groups[c].Add(new CombineInstance { mesh = cubeMesh, transform = Matrix4x4.TRS(new Vector3((x - 3) * pixel, (15 - y) * pixel, 0.025f), Quaternion.identity, new Vector3(pixel, pixel, 0.044f)) });
            }
            Object.DestroyImmediate(primitive);
            foreach (var group in groups)
            {
                string path = "Assets/M0/Models/Sword-" + group.Key + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh { name = "Sword pixels " + group.Key }; mesh.CombineMeshes(group.Value.ToArray()); AssetDatabase.CreateAsset(mesh, path); }
                var child = Pivot("Sword " + group.Key, root, Vector3.zero);
                child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.gameObject.AddComponent<MeshRenderer>().sharedMaterial = group.Key == 'e' ? edge : group.Key == 's' ? steel : group.Key == 'g' ? trim : leather;
            }
            return root;
        }
        internal static Material PixelMaterial(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder("Assets/M0/Models")) AssetDatabase.CreateFolder("Assets/M0", "Models");
            string path = "Assets/M0/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path); if (mat != null) return mat;
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { name = name + " pixels", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            { float shade = 0.87f + ((x * 17 + y * 31 + x * y * 7) % 7) * 0.035f; texture.SetPixel(x, y, new Color(color.r * shade, color.g * shade, color.b * shade)); }
            texture.Apply(); AssetDatabase.CreateAsset(texture, "Assets/M0/Models/" + name + ".asset");
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetTexture("_BaseMap", texture); mat.SetFloat("_Smoothness", 0.1f); AssetDatabase.CreateAsset(mat, path); return mat;
        }
        static Transform Pivot(string name, Transform parent, Vector3 at)
        { var go = new GameObject(name); go.layer = 2; go.transform.SetParent(parent, false); go.transform.localPosition = at; return go.transform; }
        static Transform Box(string name, Transform parent, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.layer = 2; go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = mat; Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
        }
        static Transform Limb(string name, Transform parent, Vector3 pivot, Vector3 size, Material mat)
        { var root = Pivot(name, parent, pivot); Box(name + " mesh", root, Vector3.down * size.y * 0.5f, size, mat); return root; }
    }
}
#endif
