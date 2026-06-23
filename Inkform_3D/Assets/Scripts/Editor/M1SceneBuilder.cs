using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using Inkform.Core;
using Inkform.Data;
using Inkform.Gameplay;
using Inkform.UI;
using Inkform.GameCamera;

namespace Inkform.EditorTools
{
    /// <summary>
    /// 程序化搭建 M1 验证场景，并自动连线所有引用。
    /// 菜单：Inkform/M1/Build M1 Test Scene。
    ///
    /// 场景布局（侧向 2.5D，X 为前进方向）：
    ///   A 段(学习区)：起点 + Light/Anchor 两个扫描目标 + 一道矮坎(验证跳跃随形态变)
    ///   B 段(扫描走廊)：固定探照灯致死区 + 掩体棚(验证遮挡豁免) + 终点
    ///   分布式检查点 cp0/cp1/cp2，死亡回最近点。
    /// </summary>
    public static class M1SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/M1_TestScene.unity";
        const string DataFolder = "Assets/Data";
        const string AnchorPath = DataFolder + "/S_AnchorForm.asset";
        const string LightPath = DataFolder + "/S_LightForm.asset";
        const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";

        [MenuItem("Inkform/M1/Build M1 Test Scene")]
        public static void Build()
        {
            int playerLayer = EnsureLayer("Player");
            int coverLayer = EnsureLayer("Cover");

            var anchorCfg = EnsureForm(AnchorPath, FormId.Anchor, "Anchor", new MovementProfile
            {
                MoveSpeedMul = 0.45f, MassMul = 3f, JumpHeightMul = 0.3f,
                Buoyancy = -8f, Drag = 0.4f, CanJump = true
            });
            var lightCfg = EnsureForm(LightPath, FormId.Light, "Light", new MovementProfile
            {
                MoveSpeedMul = 1.4f, MassMul = 0.5f, JumpHeightMul = 1.8f,
                Buoyancy = 0f, Drag = 0f, CanJump = true
            });

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 灯光 ──
            var sun = new GameObject("Directional Light");
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.0f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ── 地面 ──
            MakeBox("Ground", new Vector3(27f, -0.5f, 0f), new Vector3(64f, 1f, 8f), 0, new Color(0.18f, 0.2f, 0.24f));
            // 背墙（视觉纵深参照）
            MakeBox("BackWall", new Vector3(27f, 3f, 4.6f), new Vector3(64f, 8f, 0.4f), 0, new Color(0.12f, 0.13f, 0.16f));

            // ── A 段：扫描目标 + 矮坎 ──
            MakeScanTarget("ScanTarget_Light", new Vector3(8f, 0.6f, 0f), lightCfg, new Color(0.4f, 0.8f, 1f));
            MakeScanTarget("ScanTarget_Anchor", new Vector3(13f, 0.6f, 0f), anchorCfg, new Color(1f, 0.55f, 0.2f));
            // 矮坎：高 1.0，Light/Core 跳得过、Anchor 跳不过
            MakeBox("Ledge", new Vector3(19f, 0.5f, 0f), new Vector3(1.2f, 1.0f, 6f), 0, new Color(0.3f, 0.32f, 0.36f));

            // ── B 段：固定探照灯致死区 + 掩体棚 ──
            var lightOrigin = new GameObject("ScanLight_Origin");
            lightOrigin.transform.position = new Vector3(45f, 8f, 0f);
            var spot = new GameObject("Spot (visual)");
            spot.transform.SetParent(lightOrigin.transform, false);
            var spotLight = spot.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.range = 16f; spotLight.spotAngle = 55f; spotLight.intensity = 8f;
            spotLight.color = new Color(1f, 0.3f, 0.3f);
            spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 朝下

            // 致死触发区（覆盖 x:38~52）
            var killGo = new GameObject("ScanField_KillZone");
            killGo.transform.position = new Vector3(45f, 2.5f, 0f);
            var killCol = killGo.AddComponent<BoxCollider>();
            killCol.isTrigger = true;
            killCol.size = new Vector3(14f, 5f, 6f);
            var scan = killGo.AddComponent<ScanField>();
            scan.LightOrigin = lightOrigin.transform;
            scan.RotateSpeed = 0f;               // M1 不旋转，判定纯靠遮挡
            scan.CoverMask = 1 << coverLayer;
            scan.Source = "Searchlight";

            // 掩体棚（Cover 层）：玩家在棚下被遮挡 → 豁免
            MakeBox("CoverCanopy", new Vector3(46f, 3.5f, 0f), new Vector3(9f, 0.4f, 4f), coverLayer, new Color(0.25f, 0.5f, 0.3f));

            // 终点（棚下安全处，视觉标记）
            MakeBox("EXIT", new Vector3(46f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f), 0, new Color(0.3f, 1f, 0.4f));

            // ── 检查点 ──
            MakeCheckpoint("Checkpoint_0", 0, new Vector3(2f, 1f, 0f));
            MakeCheckpoint("Checkpoint_1", 1, new Vector3(24f, 1f, 0f));
            MakeCheckpoint("Checkpoint_2", 2, new Vector3(36f, 1f, 0f)); // 致死区前，反复尝试用

            // ── 玩家 ──
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (input == null) Debug.LogWarning($"[M1SceneBuilder] 未找到输入资产 {InputAssetPath}，请手动给 InputReader.Actions 赋值。");

            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.tag = "Player";
            player.layer = playerLayer;
            player.transform.position = new Vector3(2f, 1.5f, 0f);
            SetColor(player, new Color(0.1f, 0.1f, 0.14f));
            var rb = player.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var motor = player.AddComponent<PlayerMotor>();
            motor.GroundMask = 1 << 0; // Default 层为地面
            var ability = player.AddComponent<AbilitySystem>();
            var reader = player.AddComponent<InputReader>();
            reader.Actions = input;
            var actor = player.AddComponent<PlayerActor>();
            actor.Input = reader;

            // ── 管理根 + 检查点系统 ──
            var managers = new GameObject("ManagerRoot");
            managers.AddComponent<ManagerRoot>();

            // ── UI：淡入淡出 + 调试 HUD ──
            var fader = BuildScreenFader(out _);
            var hud = BuildDebugHud();

            var cpSysGo = new GameObject("CheckpointSystem");
            var cpSys = cpSysGo.AddComponent<CheckpointSystem>();
            cpSys.Player = player.transform;
            cpSys.Fader = fader;
            cpSys.FadeDuration = 0.35f;

            // ── 镜头（Cinemachine 侧视跟随）──
            BuildCamera(player.transform);

            // 保存
            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[M1SceneBuilder] 已生成并打开 {ScenePath}。等待编译完成后按 Play 验证。");
            EditorUtility.DisplayDialog("Inkform M1",
                "M1 测试场景已生成：\n" + ScenePath +
                "\n\n按 Play 后：A/D 移动、Space 跳、E 在目标旁扫描/还原。\n" +
                "靠近橙色=船锚(重/慢/跳矮)、蓝色=轻形态(快/跳高)；\n走入红光致死区会被即死并在最近检查点重生，躲到绿色掩体棚下可豁免。",
                "OK");
        }

        // ───────────────────────── helpers ─────────────────────────

        static ScreenFader BuildScreenFader(out Canvas canvas)
        {
            var go = new GameObject("FadeCanvas", typeof(Canvas), typeof(CanvasGroup));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var img = new GameObject("Black", typeof(Image));
            img.transform.SetParent(go.transform, false);
            var rt = img.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            img.GetComponent<Image>().color = Color.black;
            var fader = go.AddComponent<ScreenFader>();
            return fader;
        }

        static DebugHud BuildDebugHud()
        {
            var go = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var textGo = new GameObject("Label", typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -16f);
            rt.sizeDelta = new Vector2(520f, 260f);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;
            text.color = Color.white;
            text.text = "Form: Core";

            var hud = go.AddComponent<DebugHud>();
            hud.Label = text;
            return hud;
        }

        static void BuildCamera(Transform target)
        {
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.GetComponent<Camera>().backgroundColor = new Color(0.03f, 0.04f, 0.06f);
            camGo.AddComponent<CinemachineBrain>();
            camGo.transform.position = new Vector3(2f, 4f, -12f);

            var vcamGo = new GameObject("CM Player Cam");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcamGo.transform.position = new Vector3(2f, 4f, -12f);
            vcam.Follow = target;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 3f, -12f);

            var director = new GameObject("CameraDirector").AddComponent<CameraDirector>();
            director.Vcam = vcam;
            director.Target = target;
        }

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

        static void MakeScanTarget(string name, Vector3 pos, S_AbilityConfig cfg, Color color)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = name;
            visual.transform.position = pos;
            visual.transform.localScale = new Vector3(1f, 1.2f, 1f);
            SetColor(visual, color);
            // 让用作可视化的实体碰撞保留，单独加一个子触发器作为扫描范围
            var trig = new GameObject("Range");
            trig.transform.SetParent(visual.transform, false);
            var bc = trig.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(3.2f, 3f, 3f);
            var st = trig.AddComponent<ScanTarget>();
            st.Config = cfg;
        }

        static void MakeCheckpoint(string name, int id, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(2f, 2.5f, 4f);
            var cp = go.AddComponent<CheckpointVolume>();
            cp.Id = id;
        }

        static void SetColor(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            r.sharedMaterial = mat;
        }

        static S_AbilityConfig EnsureForm(string path, FormId form, string disp, MovementProfile mp)
        {
            EnsureFolder(DataFolder);
            var cfg = AssetDatabase.LoadAssetAtPath<S_AbilityConfig>(path);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<S_AbilityConfig>();
                AssetDatabase.CreateAsset(cfg, path);
            }
            cfg.Form = form;
            cfg.DisplayName = disp;
            cfg.Movement = mp;
            EditorUtility.SetDirty(cfg);
            return cfg;
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
            Debug.LogWarning($"[M1SceneBuilder] 无空闲层位可分配给 '{layerName}'。");
            return 0;
        }
    }
}
