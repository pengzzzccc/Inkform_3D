using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Inkform.Gameplay;
using Inkform.Nanobots;

namespace Inkform.EditorTools
{
    /// <summary>
    /// 程序化搭建纳米机器人附身系统的验证沙盒。菜单：Inkform/Sandbox/Build Nanobot Sandbox。
    /// 含：地面、玩家(WASD)、几个可附身物体(落地/悬空/隔挡)、swarm 根(bot 积分+粒子渲染+编排)。
    /// 操作：WASD 移动、雷达自动检测、1/2 选候选、E 附身、鼠标左键脱离。
    /// </summary>
    public static class NanobotSandboxBuilder
    {
        const string ScenePath = "Assets/Scenes/NanobotSandbox.unity";
        const string PossessableMatPath = "Assets/Settings/M_Possessable.mat";
        const string MetacubeMatPath = "Assets/Settings/M_Metacube.mat";
        const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";
        const string UntitledModelPath = "Assets/Model/Untitled.fbx";

        [MenuItem("Inkform/Sandbox/Build Nanobot Sandbox")]
        public static void Build()
        {
            int possessableLayer = EnsureLayer("Possessable");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 灯光 ──
            var sun = new GameObject("Directional Light");
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.1f;
            sunLight.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ── 地面（Default 层 = groundMask）──
            MakeBox("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 24f), 0,
                new Color(0.18f, 0.2f, 0.24f));

            // ── 材质 ──
            var possMat = EnsurePossessableMaterial();
            var cubeMat = EnsureMetacubeMaterial();

            // ── 可附身物体 ──
            // ① 落地可附身（正常）
            MakePossessable("Crate_OK", new Vector3(4f, 1f, 0f), new Vector3(2f, 2f, 2f),
                possessableLayer, possMat);
            // ② 落地可附身（球，验证非立方体表面采样）
            MakePossessableSphere("Sphere_OK", new Vector3(7f, 1f, 3f), 2.2f, possessableLayer, possMat);
            // ③ 高处目标（架在台子上）：F 落台顶、P 在其底，演示蔓延后竖直立起一大段。
            MakePossessable("Crate_OnLedge", new Vector3(-6f, 3.5f, 0f), new Vector3(2f, 2f, 2f),
                possessableLayer, possMat);
            MakeBox("Ledge", new Vector3(-6f, 1.25f, 0f), new Vector3(3f, 2.5f, 3f), 0,
                new Color(0.3f, 0.32f, 0.36f)); // 台子：目标 F 落在台顶
            // ④ 不可附身反例：悬在地面 X 范围(±20)之外，正下方无地 → ResolveFootAndContact 失败。
            MakePossessable("Crate_NoGround", new Vector3(26f, 2f, 0f), new Vector3(2f, 2f, 2f),
                possessableLayer, possMat);
            // ⑤ FBX 模型（验证非 Primitive 表面采样 + 附身）
            MakePossessableFromFBX("Untitled", new Vector3(-3f, 2f, 5f), Vector3.one * 50f,
                possessableLayer, possMat, UntitledModelPath);

            // ── 玩家 ──
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (input == null)
                Debug.LogWarning($"[NanobotSandboxBuilder] 未找到 {InputAssetPath}，请手动给 InputReader.Actions 赋值。");
            var player = BuildPlayer(input);

            // ── Metacube 根（方块池：积分 + 脉冲 + 实例化绘制 + 编排）──
            var cubeGo = new GameObject("NanobotMetacubes");
            cubeGo.transform.position = player.transform.position;
            var cubes = cubeGo.AddComponent<MetacubeSystem>();
            cubes.Count = 1000;            // 固定总量的 metacube
            cubes.CubeMaterial = cubeMat;
            cubes.SmoothTime = 0.25f;
            cubes.MinCubeSize = 0.06f;
            cubes.MaxCubeSize = 0.18f;
            cubes.PulseFreq = 1.5f;
            cubes.PulseFreqJitter = 0.4f;
            cubes.SizeJitter = 0.3f;
            cubes.SpinSpeed = 18f;

            // 常态本体即 metacube 半球团 → 隐藏玩家胶囊视觉体。
            var body = player.transform.Find("Body");
            if (body != null) body.gameObject.SetActive(false);

            var director = cubeGo.AddComponent<PossessionDirector>();
            director.Cubes = cubes;
            director.Player = player.transform;
            director.Input = player.GetComponent<InputReader>();
            director.ScanRadius = 8f;
            director.PossessableMask = 1 << possessableLayer;
            director.GroundMask = 1 << 0; // Default
            // 常态半球（含 聚合/离散 子态）
            director.BlobRadius = 1.1f;
            director.BlobSpeed = 1f;
            director.BlobFlow = 0.5f;
            director.BlobWobble = 0.18f;
            director.IdleDispersion = 0.55f;
            director.IdleBreathe = 0.12f;
            director.DispersionLerpRate = 2.5f;
            // 蔓延触手（梳状侧分支，全程贴面）
            director.LeafCount = 64;
            director.SurfaceSubdiv = 2;
            director.GroundSamplesPerUnit = 2f;
            director.GroundClearance = 0.12f;
            director.DepartMin = 0.2f;
            director.DepartMax = 0.8f;
            director.BranchThickness = 0.25f;
            director.Trail = 1.5f;
            director.SpreadSpeed = 0.5f;
            director.SourcePointCount = 4;
            // 附身/脱离
            director.DetachGroundRadius = 2.5f;
            director.PlayerGroundOffset = 1.1f;
            director.CoverNoiseScale = 0.6f;
            director.CoverNoiseContrast = 1.5f;

            // ── 反射探针：高金属度的洪流需要环境反射，否则发黑 ──
            var probeGo = new GameObject("Reflection Probe");
            probeGo.transform.position = new Vector3(0f, 4f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.EveryFrame;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(60f, 30f, 40f);
            probe.boxProjection = false;

            // ── 相机（Cinemachine 跟随）──
            BuildCamera(player.transform);

            // ── 保存 ──
            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[NanobotSandboxBuilder] 已生成并打开 {ScenePath}。");
            EditorUtility.DisplayDialog("Inkform — Nanobot Sandbox",
                "纳米机器人附身沙盒已生成：\n" + ScenePath +
                "\n\n操作：\n  WASD 移动（Space 跳）\n  雷达自动检测附身物（检测到闪一下）\n" +
                "  1 / 2 = 选上 / 下一个候选\n  E = 附身选中目标\n  鼠标左键 = 脱离（附身时）\n\n" +
                "观察：聚合贴地半球(脉冲呼吸,离散↔聚合) → 选中按 E 贴地伸出梳状触手、侧分支离干平行 →\n" +
                "各分支爬上目标表面随机点全覆盖铺满 → 附身后随驾驶整体跟随(可跳) → 脱离贴面收回成半球。",
                "OK");
        }

        // ───────────────────────── 玩家 ─────────────────────────

        static GameObject BuildPlayer(InputActionAsset input)
        {
            var player = new GameObject("Player") { tag = "Player" };
            player.transform.position = new Vector3(0f, 1.5f, -6f);

            var col = player.AddComponent<CapsuleCollider>();
            col.height = 2f; col.radius = 0.5f;

            var rb = player.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // 视觉体
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(player.transform, false);
            SetColor(body, new Color(0.1f, 0.1f, 0.14f));

            var motor = player.AddComponent<PlayerMotor>();
            motor.GroundMask = 1 << 0;

            var foot = player.AddComponent<AudioSource>();
            foot.loop = true; foot.playOnAwake = false; foot.spatialBlend = 0f; foot.volume = 0.3f;
            motor.FootstepSource = foot;

            var reader = player.AddComponent<InputReader>();
            reader.Actions = input;

            var sandboxInput = player.AddComponent<SandboxPlayerInput>();
            sandboxInput.Input = reader;

            return player;
        }

        // ───────────────────────── 相机 ─────────────────────────

        static void BuildCamera(Transform target)
        {
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
            camGo.AddComponent<CinemachineBrain>();
            camGo.transform.position = new Vector3(0f, 6f, -14f);

            var vcamGo = new GameObject("CM Player Cam");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcamGo.transform.position = new Vector3(0f, 6f, -14f);
            vcam.Follow = target;
            vcam.Priority = 10;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 3f, -14f);
        }

        // ───────────────────────── 可附身物体 ─────────────────────────

        static void MakePossessable(string name, Vector3 pos, Vector3 scale, int layer, Material possMat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = layer;
            go.GetComponent<Renderer>().sharedMaterial = possMat;
            go.AddComponent<Possessable>();
        }

        static void MakePossessableSphere(string name, Vector3 pos, float diameter, int layer, Material possMat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * diameter;
            go.layer = layer;
            go.GetComponent<Renderer>().sharedMaterial = possMat;
            go.AddComponent<Possessable>();
        }

        static void MakePossessableFromFBX(string name, Vector3 pos, Vector3 scale,
            int layer, Material possMat, string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[NanobotSandboxBuilder] 未找到 FBX 模型：{modelPath}");
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            SetLayerRecursive(go, layer);

            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = possMat;

            if (go.GetComponentInChildren<Collider>() == null)
            {
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }

            if (go.GetComponent<Possessable>() == null)
                go.AddComponent<Possessable>();
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        // ───────────────────────── 材质 ─────────────────────────

        // 可附身物体材质：标准 URP Lit（带 _EmissionColor，供 Possessable 高亮/闪烁）。不依赖自定义 shader。
        static Material EnsurePossessableMaterial()
        {
            EnsureFolder("Assets/Settings");
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(PossessableMatPath);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, PossessableMatPath);
            }
            else if (mat.shader != sh) mat.shader = sh;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.55f, 0.57f, 0.62f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.4f);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // metacube 材质：普通 URP Lit 金属（开启 GPU 实例化）。无自定义 shader。
        static Material EnsureMetacubeMaterial()
        {
            EnsureFolder("Assets/Settings");
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MetacubeMatPath);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, MetacubeMatPath);
            }
            else if (mat.shader != sh) mat.shader = sh;

            mat.enableInstancing = true;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.7f, 0.78f, 0.9f));
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.85f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.75f);
            // 暗场景里高金属度易发黑，给一点冷色自发光保底（配合场景反射探针）。
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(0.10f, 0.18f, 0.30f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ───────────────────────── helper（移植自 M1SceneBuilder）─────────────────────────

        static GameObject MakeBox(string name, Vector3 pos, Vector3 scale, int layer, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = layer;
            SetColor(go, color);
            return go;
        }

        static void SetColor(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            r.sharedMaterial = mat;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            string leaf = folder.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static int EnsureLayer(string layerName)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset == null || asset.Length == 0) return 0;
            var tagManager = new SerializedObject(asset[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;
            for (int i = 8; i < layers.arraySize; i++)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue))
                {
                    p.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }
            Debug.LogWarning($"[NanobotSandboxBuilder] 无空闲层位可分配给 '{layerName}'。");
            return 0;
        }
    }
}
