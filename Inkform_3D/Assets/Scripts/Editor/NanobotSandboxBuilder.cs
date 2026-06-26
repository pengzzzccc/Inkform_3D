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
    /// 含：地面、玩家(WASD)、几个可附身物体(落地/悬空/隔挡)、swarm 根(身体+渲染+编排)。
    /// 操作：WASD 移动、E 扫描/切候选、鼠标左键确认附身。
    /// </summary>
    public static class NanobotSandboxBuilder
    {
        const string ScenePath = "Assets/Scenes/NanobotSandbox.unity";
        const string WrapMatPath = "Assets/Settings/M_NanobotWrap.mat";
        const string BotMatPath = "Assets/Settings/M_NanobotBot.mat";
        const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";

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
            var wrapMat = EnsureWrapMaterial();
            var botMat = EnsureBotMaterial();

            // ── 可附身物体 ──
            // ① 落地可附身（正常）
            MakePossessable("Crate_OK", new Vector3(4f, 1f, 0f), new Vector3(2f, 2f, 2f),
                possessableLayer, wrapMat);
            // ② 落地可附身（球，验证非立方体表面采样）
            MakePossessableSphere("Sphere_OK", new Vector3(7f, 1f, 3f), 2.2f, possessableLayer, wrapMat);
            // ③ 高处目标（架在台子上）：F 落台顶、P 在其底，演示蔓延后竖直立起一大段。
            MakePossessable("Crate_OnLedge", new Vector3(-6f, 3.5f, 0f), new Vector3(2f, 2f, 2f),
                possessableLayer, wrapMat);
            MakeBox("Ledge", new Vector3(-6f, 1.25f, 0f), new Vector3(3f, 2.5f, 3f), 0,
                new Color(0.3f, 0.32f, 0.36f)); // 台子：目标 F 落在台顶
            // ④ 不可附身反例：悬在地面 X 范围(±20)之外，正下方无地 → ResolveFootAndContact 失败。
            MakePossessable("Crate_NoGround", new Vector3(26f, 2f, 0f), new Vector3(2f, 2f, 2f),
                possessableLayer, wrapMat);

            // ── 玩家 ──
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (input == null)
                Debug.LogWarning($"[NanobotSandboxBuilder] 未找到 {InputAssetPath}，请手动给 InputReader.Actions 赋值。");
            var player = BuildPlayer(input);

            // ── Swarm 根（身体[求质心/进度] + 方管渲染 + 编排）──
            var swarmGo = new GameObject("NanobotSwarm");
            swarmGo.transform.position = player.transform.position;
            var swarm = swarmGo.AddComponent<NanobotSwarm>();
            swarm.Count = 64; // 只用于求质心/形态进度，不再画点粒

            // 方管渲染器（金属管网，替代点粒）。需要 MeshFilter+MeshRenderer。
            // ⚠ mesh 顶点是世界空间 → 该 GO 必须在世界原点、单位变换，否则会被父级位置二次偏移。
            var tubeGo = new GameObject("NanobotTubes", typeof(MeshFilter), typeof(MeshRenderer));
            tubeGo.transform.position = Vector3.zero;
            tubeGo.transform.rotation = Quaternion.identity;
            tubeGo.transform.localScale = Vector3.one;
            var tubes = tubeGo.AddComponent<NanobotTubeRenderer>();
            tubes.TubeMaterial = botMat;
            tubeGo.GetComponent<MeshRenderer>().sharedMaterial = botMat;
            tubes.TubeSize = 0.3f;
            tubes.RingsPerUnit = 12f;
            tubes.SegmentsPerUnit = 3f;
            tubes.SegmentRidge = 0.25f;
            tubes.SegmentTwist = 22f;

            var director = swarmGo.AddComponent<PossessionDirector>();
            director.Swarm = swarm;
            director.Player = player.transform;
            director.Input = player.GetComponent<InputReader>();
            director.Tubes = tubes;
            director.ScanRadius = 8f;
            director.PossessableMask = 1 << possessableLayer;
            director.GroundMask = 1 << 0; // Default
            // 束缚绑定参数
            director.SurfaceOffset = 0.12f;
            director.StrapCount = 4;
            director.StrapTurns = 1f;
            director.SubTentacleCount = 30;
            director.SubTentacleLength = 0.5f;
            director.SubTentacleRadius = 0.35f;

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
                "\n\n操作：\n  WASD 移动（Space 跳）\n  E = 扫描 / 在候选间循环切换\n  鼠标左键 = 确认附身选中目标\n\n" +
                "观察：扫描高亮 → 选定后金属方管从玩家处分叉成几股支流、管头发光地朝目标延伸生长 →\n" +
                "到目标正下方竖直立起接触 → 表面从底向上包裹生长 → 管网消隐。",
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
            follow.FollowOffset = new Vector3(0f, 5f, -14f);
        }

        // ───────────────────────── 可附身物体 ─────────────────────────

        static void MakePossessable(string name, Vector3 pos, Vector3 scale, int layer, Material wrapMat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = layer;
            go.GetComponent<Renderer>().sharedMaterial = wrapMat;
            go.AddComponent<Possessable>();
        }

        static void MakePossessableSphere(string name, Vector3 pos, float diameter, int layer, Material wrapMat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * diameter;
            go.layer = layer;
            go.GetComponent<Renderer>().sharedMaterial = wrapMat;
            go.AddComponent<Possessable>();
        }

        // ───────────────────────── 材质 ─────────────────────────

        static Material EnsureWrapMaterial()
        {
            EnsureFolder("Assets/Settings");
            var sh = Shader.Find("Inkform/NanobotWrap");
            if (sh == null)
            {
                Debug.LogError("[NanobotSandboxBuilder] 找不到 Inkform/NanobotWrap shader，请确认已编译。");
                sh = Shader.Find("Universal Render Pipeline/Lit");
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WrapMatPath);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, WrapMatPath);
            }
            else if (mat.shader != sh) mat.shader = sh;

            // shader 降级为辅助：possessed 反差小(只微变色),边缘发光收紧感保留。
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.55f, 0.57f, 0.62f));
            if (mat.HasProperty("_PossessedColor")) mat.SetColor("_PossessedColor", new Color(0.42f, 0.5f, 0.62f));
            if (mat.HasProperty("_EdgeColor")) mat.SetColor("_EdgeColor", new Color(0.4f, 0.9f, 1f) * 2f);
            if (mat.HasProperty("_EdgeWidth")) mat.SetFloat("_EdgeWidth", 0.05f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material EnsureBotMaterial()
        {
            EnsureFolder("Assets/Settings");
            var sh = Shader.Find("Inkform/NanobotFlow");
            if (sh == null)
            {
                Debug.LogWarning("[NanobotSandboxBuilder] 找不到 Inkform/NanobotFlow shader，回退 URP Lit。");
                sh = Shader.Find("Universal Render Pipeline/Lit");
            }

            // 已存在则确保 shader 指向 NanobotFlow（否则重建场景还是旧材质）。
            var mat = AssetDatabase.LoadAssetAtPath<Material>(BotMatPath);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, BotMatPath);
            }
            else if (mat.shader != sh)
            {
                mat.shader = sh;
            }

            mat.enableInstancing = true;
            if (mat.HasProperty("_HeadColor")) mat.SetColor("_HeadColor", new Color(0.75f, 0.85f, 1f));
            if (mat.HasProperty("_TailColor")) mat.SetColor("_TailColor", new Color(0.1f, 0.2f, 0.4f));
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.9f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
            if (mat.HasProperty("_PanelColor")) mat.SetColor("_PanelColor", new Color(0.3f, 0.7f, 1f) * 2f);
            if (mat.HasProperty("_PanelGlow")) mat.SetFloat("_PanelGlow", 2.2f);
            if (mat.HasProperty("_PanelLengthwise")) mat.SetFloat("_PanelLengthwise", 2f);
            if (mat.HasProperty("_PanelAround")) mat.SetFloat("_PanelAround", 1f);
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
