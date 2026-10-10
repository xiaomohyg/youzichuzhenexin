#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using M0.Networking;

namespace M0.Editor
{
    public static class M0SceneBuilder
    {
        public const string ScenePath = "Assets/M0/Scenes/M0Sandbox.unity";
        static Material dark, floor, metal, cyan, orange, enemyMaterial, skin;
        [MenuItem("M0/Create Combat Sandbox")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode before creating the sandbox.");
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("An existing scene has unsaved changes; save it before creating M0.");
            Directory.CreateDirectory("Assets/M0/Scenes"); Directory.CreateDirectory("Assets/M0/Materials"); Directory.CreateDirectory("Assets/M0/Prefabs");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            dark = Material("Graphite", new Color(0.035f, 0.055f, 0.075f));
            floor = Material("Arena concrete", new Color(0.17f, 0.23f, 0.27f));
            metal = Material("Steel", new Color(0.55f, 0.65f, 0.71f));
            cyan = Material("Cyan signal", new Color(0.12f, 0.8f, 0.87f), true);
            orange = Material("Weakpoint orange", new Color(1, 0.36f, 0.08f), true);
            enemyMaterial = Material("Enemy teal", new Color(0.22f, 0.42f, 0.44f));
            skin = Material("Glove", new Color(0.12f, 0.19f, 0.24f));
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.56f, 0.64f, 0.72f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.07f, 0.1f, 0.14f); RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.011f;
            var light = new GameObject("Key Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.8f; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(48, -28, 0);
            var arena = new GameObject("Arena / Collision and training markers").transform;
            Cube("Floor", arena, new Vector3(0, -0.25f, 4), new Vector3(36, 0.5f, 40), floor);
            Cube("Back wall", arena, new Vector3(0, 2.5f, 24), new Vector3(36, 5, 0.6f), dark);
            Cube("Entry wall", arena, new Vector3(0, 2.5f, -16), new Vector3(36, 5, 0.6f), dark);
            Cube("Left wall", arena, new Vector3(-18, 2.5f, 4), new Vector3(0.6f, 5, 40), dark);
            Cube("Right wall", arena, new Vector3(18, 2.5f, 4), new Vector3(0.6f, 5, 40), dark);
            for (int x = -16; x <= 16; x += 4) Cube("Floor grid", arena, new Vector3(x, 0.005f, 4), new Vector3(0.025f, 0.008f, 39), metal, false);
            for (int z = -14; z <= 22; z += 4) Cube("Floor grid", arena, new Vector3(0, 0.006f, z), new Vector3(35, 0.008f, 0.025f), metal, false);
            Cube("Rear signal strip", arena, new Vector3(0, 0.35f, 23.65f), new Vector3(35, 0.08f, 0.04f), cyan, false);
            Cube("Left signal strip", arena, new Vector3(-17.65f, 0.35f, 4), new Vector3(0.04f, 0.08f, 39), cyan, false);
            Cube("Right signal strip", arena, new Vector3(17.65f, 0.35f, 4), new Vector3(0.04f, 0.08f, 39), cyan, false);
            foreach (float x in new[] { -9f, 9f })
            {
                Cube("Cover pillar", arena, new Vector3(x, 1.25f, 7), new Vector3(2.2f, 2.5f, 2.2f), floor);
                Cube("Pillar cap", arena, new Vector3(x, 2.52f, 7), new Vector3(2.3f, 0.1f, 2.3f), orange, false);
                Cube("Step", arena, new Vector3(x, 0.2f, -2), new Vector3(3, 0.4f, 2), dark);
            }
            Sign(arena, new Vector3(0, 3.1f, 23.64f), "M0 / COMBAT LAB", 0.18f, cyan.color);
            Sign(arena, new Vector3(0, 2.15f, 23.63f), "01  MELEE     /     02  PRECISION", 0.065f, Color.white);
            Sign(arena, new Vector3(0, 1.5f, 23.62f), "ORANGE = WEAKPOINT   |   RED = INCOMING ATTACK", 0.045f, orange.color);
            var root = new GameObject("M0 Sandbox"); var sandbox = root.AddComponent<M0Sandbox>();
            sandbox.Network = root.AddComponent<M0NetworkSession>();
            sandbox.Fx = Feedback(root.transform);
            sandbox.Player = Player();
            var spawns = new GameObject("Enemy spawn pads").transform;
            var locations = new[] { new Vector3(-3, 0.1f, 3), new Vector3(3, 0.1f, 6), new Vector3(0, 0.1f, 11), new Vector3(-5, 0.1f, 14), new Vector3(5, 0.1f, 18), new Vector3(-12, 0.1f, 16), new Vector3(12, 0.1f, 16) };
            sandbox.SpawnPoints = new Transform[locations.Length];
            for (int i = 0; i < locations.Length; i++)
            {
                var pad = new GameObject("Spawn " + (i + 1)).transform; pad.SetParent(spawns); pad.position = locations[i]; sandbox.SpawnPoints[i] = pad;
                Cube("Pad marking", spawns, new Vector3(locations[i].x, 0.011f, locations[i].z), new Vector3(1.5f, 0.01f, 1.5f), dark, false);
            }
            var enemies = new GameObject("Enemy pool / 26 reusable ragdolls").transform;
            var template = Enemy(enemies); template.Id = 100;
            PrefabUtility.SaveAsPrefabAsset(template.gameObject, "Assets/M0/Prefabs/TrainingEnemy.prefab");
            sandbox.Enemies.Add(template);
            for (int i = 1; i < 26; i++)
            {
                var clone = Object.Instantiate(template, enemies); clone.name = "Training Enemy " + i; clone.Id = 100 + i; sandbox.Enemies.Add(clone);
            }
            for (int i = 0; i < sandbox.Enemies.Count; i++)
            {
                sandbox.Enemies[i].transform.position = locations[i % locations.Length]; sandbox.Enemies[i].gameObject.SetActive(i < sandbox.DesiredEnemies);
            }
            EditorSceneManager.SaveScene(scene, ScenePath);
            var builds = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            builds.Add(new EditorBuildSettingsScene(ScenePath, true));
            foreach (var entry in EditorBuildSettings.scenes) if (entry.path != ScenePath) builds.Add(entry);
            EditorBuildSettings.scenes = builds.ToArray(); AssetDatabase.SaveAssets();
            Selection.activeGameObject = sandbox.Player.gameObject;
            SceneView.lastActiveSceneView?.LookAt(new Vector3(0, 1, 5), Quaternion.Euler(40, 0, 0), 28);
            Debug.Log("M0 ready: " + ScenePath + ". Press Play; Esc opens bindings and comfort settings.");
        }
        static Material Material(string name, Color color, bool emission = false)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.name = name; mat.color = color;
            if (emission) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * 1.3f); }
            AssetDatabase.CreateAsset(mat, "Assets/M0/Materials/" + name + ".mat"); return mat;
        }
        static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material mat, bool collide = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collide) Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
        }
        static void Sign(Transform parent, Vector3 position, string text, float size, Color color)
        {
            var go = new GameObject("Sign / " + text); go.transform.SetParent(parent); go.transform.position = position;
            var mesh = go.AddComponent<TextMesh>(); mesh.text = text; mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center; mesh.characterSize = size; mesh.fontSize = 64; mesh.color = color;
        }
        static M0Player Player()
        {
            var go = new GameObject("Player / FPV controller"); go.transform.position = new Vector3(0, 0.1f, -8);
            var controller = go.AddComponent<CharacterController>(); controller.height = 1.8f; controller.radius = 0.32f; controller.center = Vector3.up * 0.9f; controller.stepOffset = 0.35f; controller.skinWidth = 0.03f;
            var player = go.AddComponent<M0Player>();
            var cameraObject = new GameObject("First Person Camera"); cameraObject.tag = "MainCamera"; cameraObject.transform.SetParent(go.transform, false); cameraObject.transform.localPosition = new Vector3(0, 1.54f, 0.17f);
            var camera = cameraObject.AddComponent<Camera>(); camera.fieldOfView = 94; camera.nearClipPlane = 0.04f; camera.farClipPlane = 120; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.07f, 0.1f, 0.14f);
            cameraObject.AddComponent<AudioListener>(); player.View = camera;
            var arms = new GameObject("First person hands and weapon").transform; arms.SetParent(camera.transform, false); arms.localPosition = new Vector3(0.32f, -0.32f, 0.62f); player.WeaponRoot = arms;
            Cube("Right glove", arms, new Vector3(0, -0.17f, -0.12f), new Vector3(0.14f, 0.15f, 0.5f), skin, false);
            Cube("Left glove", arms, new Vector3(-0.42f, -0.2f, -0.1f), new Vector3(0.14f, 0.15f, 0.42f), skin, false);
            var blade = new GameObject("Training blade").transform; blade.SetParent(arms, false); player.Blade = blade;
            Cube("Grip", blade, Vector3.zero, new Vector3(0.08f, 0.32f, 0.09f), dark, false);
            Cube("Guard", blade, new Vector3(0, 0.18f, 0), new Vector3(0.32f, 0.05f, 0.12f), metal, false);
            Cube("Blade", blade, new Vector3(0, 0.48f, 0), new Vector3(0.085f, 0.58f, 0.06f), metal, false);
            Cube("Blade signal edge", blade, new Vector3(-0.05f, 0.48f, 0), new Vector3(0.018f, 0.58f, 0.065f), cyan, false);
            var gun = new GameObject("Pulse sidearm").transform; gun.SetParent(arms, false); gun.localPosition = new Vector3(0, -0.02f, 0.1f); player.Gun = gun;
            Cube("Receiver", gun, new Vector3(0, 0.03f, 0.08f), new Vector3(0.14f, 0.17f, 0.4f), metal, false);
            Cube("Grip", gun, new Vector3(0, -0.1f, -0.03f), new Vector3(0.1f, 0.21f, 0.12f), dark, false);
            Cube("Sight", gun, new Vector3(0, 0.15f, 0.12f), new Vector3(0.05f, 0.06f, 0.04f), cyan, false); gun.gameObject.SetActive(false);
            M0PlayerUpgrade.Build(player);
            return player;
        }
        static M0Feedback Feedback(Transform parent)
        {
            var go = new GameObject("Combat presentation / sound and sparks"); go.transform.SetParent(parent); go.AddComponent<AudioSource>(); var fx = go.AddComponent<M0Feedback>();
            var particles = new GameObject("Pooled hit particles").AddComponent<ParticleSystem>(); particles.transform.SetParent(go.transform); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = 0.3f; main.startSpeed = 3; main.startSize = 0.055f; main.gravityModifier = 0.4f; main.maxParticles = 512;
            var emission = particles.emission; emission.enabled = false; var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.08f;
            var unlit = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); unlit.name = "Hit particle"; unlit.SetColor("_BaseColor", Color.white); AssetDatabase.CreateAsset(unlit, "Assets/M0/Materials/Hit particle.mat");
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = unlit; fx.Particles = particles; fx.TracerMaterial = cyan; return fx;
        }
        static M0Enemy Enemy(Transform parent)
        {
            var go = new GameObject("Training Enemy 0"); go.transform.SetParent(parent);
            var controller = go.AddComponent<CharacterController>(); controller.height = 1.4f; controller.center = Vector3.up * 0.7f; controller.radius = 0.32f; controller.stepOffset = 0.25f; controller.skinWidth = 0.025f;
            var enemy = go.AddComponent<M0Enemy>();
            var body = Bone("Torso", go.transform, new Vector3(0, 0.94f, 0), new Vector3(0.58f, 0.78f, 0.38f), enemyMaterial, null); enemy.BodyRenderer = body.GetComponent<Renderer>();
            var headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere); headGo.name = "Weakpoint head"; headGo.transform.SetParent(go.transform, false); headGo.transform.localPosition = new Vector3(0, 1.59f, 0); headGo.transform.localScale = Vector3.one * 0.48f; headGo.GetComponent<Renderer>().sharedMaterial = orange;
            var head = headGo.AddComponent<Rigidbody>(); head.mass = 3; head.isKinematic = true; head.interpolation = RigidbodyInterpolation.Interpolate; Joint(head, body); enemy.Head = head.transform;
            enemy.LeftArm = Bone("Left arm", go.transform, new Vector3(-0.43f, 0.98f, 0), new Vector3(0.19f, 0.7f, 0.21f), metal, body).transform;
            enemy.RightArm = Bone("Right arm", go.transform, new Vector3(0.43f, 0.98f, 0), new Vector3(0.19f, 0.7f, 0.21f), metal, body).transform;
            Bone("Left leg", go.transform, new Vector3(-0.17f, 0.32f, 0), new Vector3(0.22f, 0.63f, 0.26f), dark, body);
            Bone("Right leg", go.transform, new Vector3(0.17f, 0.32f, 0), new Vector3(0.22f, 0.63f, 0.26f), dark, body);
            M0EnemyUpgrade.Build(enemy);
            return enemy;
        }
        static Rigidbody Bone(string name, Transform parent, Vector3 position, Vector3 scale, Material mat, Rigidbody connected)
        {
            var go = Cube(name, parent, position, scale, mat); var rb = go.AddComponent<Rigidbody>(); rb.mass = connected == null ? 12 : 4; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (connected != null) Joint(rb, connected); return rb;
        }
        static void Joint(Rigidbody bone, Rigidbody connected)
        {
            var joint = bone.gameObject.AddComponent<CharacterJoint>(); joint.connectedBody = connected; joint.enableCollision = false; joint.enableProjection = true; joint.projectionDistance = 0.12f;
            // Joint anchor is halfway between the part centers, in the bone's local coordinates.
            joint.anchor = bone.transform.InverseTransformPoint((bone.position + connected.position) * 0.5f);
            joint.lowTwistLimit = new SoftJointLimit { limit = -25 }; joint.highTwistLimit = new SoftJointLimit { limit = 25 };
            joint.swing1Limit = new SoftJointLimit { limit = 45 }; joint.swing2Limit = new SoftJointLimit { limit = 35 };
        }
    }
}
#endif
