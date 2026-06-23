using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Splines;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using Inkform.Core;
using Inkform.Data;
using Inkform.Gameplay;
using Inkform.UI;
using Inkform.GameCamera;
using Inkform.Audio;

namespace Inkform.EditorTools
{
    /// <summary>
    /// 程序化搭建 M1 验证场景并自动连线。菜单：Inkform/M1/Build M1 Test Scene。
    ///
    /// 含可玩性打磨：明显的红色危险区、交互提示 UI、形态视觉(颜色+体型)、
    /// 检查点运行时标记、EXIT 到达即通关。
    /// </summary>
    public static class M1SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/M1_TestScene.unity";
        const string DataFolder = "Assets/Data";
        const string AnchorPath = DataFolder + "/S_AnchorForm.asset";
        const string LightPath = DataFolder + "/S_LightForm.asset";
        const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";

        static readonly Color CoreColor = new Color(0.10f, 0.10f, 0.14f);

        [MenuItem("Inkform/M1/Build M1 Test Scene")]
        public static void Build()
        {
            int playerLayer = EnsureLayer("Player");
            int coverLayer = EnsureLayer("Cover");

            var clips = PlaceholderAudioGenerator.EnsureAll();

            var anchorCfg = EnsureForm(AnchorPath, FormId.Anchor, "船锚",
                new MovementProfile { MoveSpeedMul = 0.45f, MassMul = 3f, JumpHeightMul = 0.3f, Buoyancy = -8f, Drag = 0.4f, CanJump = true },
                new Color(1f, 0.55f, 0.2f), new Vector3(1.3f, 0.6f, 1.3f));
            var lightCfg = EnsureForm(LightPath, FormId.Light, "轻形态",
                new MovementProfile { MoveSpeedMul = 1.4f, MassMul = 0.5f, JumpHeightMul = 1.8f, Buoyancy = 0f, Drag = 0f, CanJump = true },
                new Color(0.4f, 0.8f, 1f), new Vector3(0.8f, 1.4f, 0.8f));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 灯光（降低环境光，让红光对比更强）
            var sun = new GameObject("Directional Light");
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 0.7f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 地面 + 背墙
            MakeBox("Ground", new Vector3(27f, -0.5f, 0f), new Vector3(64f, 1f, 8f), 0, new Color(0.18f, 0.2f, 0.24f));
            MakeBox("BackWall", new Vector3(27f, 3f, 4.6f), new Vector3(64f, 8f, 0.4f), 0, new Color(0.12f, 0.13f, 0.16f));
            MakeBox("FrontWall", new Vector3(27f, 3f, -4.6f), new Vector3(64f, 8f, 0.4f), 0, new Color(0.12f, 0.13f, 0.16f)); // 防 WASD 前后走出地面

            // A 段：扫描目标（颜色取自形态）+ 矮坎
            MakeScanTarget("ScanTarget_Light", new Vector3(8f, 0.6f, 0f), lightCfg);
            MakeScanTarget("ScanTarget_Anchor", new Vector3(13f, 0.6f, 0f), anchorCfg);
            MakeBox("Ledge", new Vector3(19f, 0.5f, 0f), new Vector3(1.2f, 1.0f, 6f), 0, new Color(0.3f, 0.32f, 0.36f));

            // B 段：沿样条移动的探照灯（整组）+ 致死区 + 红色危险视觉 + 掩体棚
            // 样条路径（走廊上方来回）
            var pathGo = new GameObject("ScanLight_Path");
            var spline = pathGo.AddComponent<SplineContainer>();
            spline.Spline.Add(new BezierKnot(new float3(33f, 8f, 0f)));
            spline.Spline.Add(new BezierKnot(new float3(52f, 8f, 0f)));

            // 移动根（沿样条 PingPong）
            var root = new GameObject("ScanLight_Root");
            root.transform.position = new Vector3(33f, 8f, 0f);
            var mover = root.AddComponent<SplinePathMover>();
            mover.Spline = spline;
            mover.Speed = 0.22f;
            mover.Mode = SplinePathMover.LoopMode.PingPong;
            // 探照灯环境低频（GDD 听觉雷达）
            var amb = root.AddComponent<AudioSource>();
            amb.clip = clips.Ambient; amb.loop = true; amb.playOnAwake = true;
            amb.spatialBlend = 1f; amb.volume = 0.5f; amb.minDistance = 4f; amb.maxDistance = 30f;

            // 子：聚光灯（朝下）
            var spot = new GameObject("Spot");
            spot.transform.SetParent(root.transform, false);
            spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var spotLight = spot.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.range = 18f; spotLight.spotAngle = 55f; spotLight.intensity = 14f;
            spotLight.color = new Color(1f, 0.25f, 0.25f);

            // 子：致死光斑（随根移动）
            var killGo = new GameObject("ScanField_KillZone");
            killGo.transform.SetParent(root.transform, false);
            killGo.transform.localPosition = new Vector3(0f, -5.5f, 0f); // 根 y=8 → 致死区中心 y=2.5
            var killCol = killGo.AddComponent<BoxCollider>();
            killCol.isTrigger = true;
            killCol.size = new Vector3(6f, 5f, 6f);
            var scan = killGo.AddComponent<ScanField>();
            scan.LightOrigin = root.transform;
            scan.RotateSpeed = 0f;
            scan.CoverMask = 1 << coverLayer;
            scan.Source = "Searchlight";
            scan.Spotlight = spotLight;
            scan.LightColor = new Color(1f, 0.25f, 0.25f);
            scan.LightIntensity = 14f;
            scan.LightRange = 18f;

            // 子：红色脉动光柱（随根移动，显示当前扫描位置）
            var beam = MakeDanger("DangerBeam", Vector3.zero, new Vector3(2f, 8f, 2f));
            beam.transform.SetParent(root.transform, false);
            beam.transform.localPosition = new Vector3(0f, -4f, 0f);

            // 静态地面警示带：标示整条危险走廊
            MakeDanger("DangerStrip", new Vector3(42.5f, 0.06f, 0f), new Vector3(19f, 0.12f, 6f));

            // 掩体棚（Cover 层）：玩家在棚下被遮挡 → 豁免
            MakeBox("CoverCanopy", new Vector3(46f, 3.5f, 0f), new Vector3(9f, 0.4f, 4f), coverLayer, new Color(0.25f, 0.5f, 0.3f));

            // 终点（到达即通关）
            BuildExit(new Vector3(46f, 0.6f, 0f));

            // 检查点（含编辑器 Gizmos + 运行时标记）
            MakeCheckpoint("Checkpoint_0", 0, new Vector3(2f, 1f, 0f));
            MakeCheckpoint("Checkpoint_1", 1, new Vector3(24f, 1f, 0f));
            MakeCheckpoint("Checkpoint_2", 2, new Vector3(36f, 1f, 0f));

            // 玩家（根不缩放，子 Body 承担形态视觉缩放）
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (input == null) Debug.LogWarning($"[M1SceneBuilder] 未找到输入资产 {InputAssetPath}，请手动给 InputReader.Actions 赋值。");
            var player = BuildPlayer(playerLayer, input);
            player.GetComponent<PlayerMotor>().FootstepSource.clip = clips.Footstep;

            // 事件音效管理 + 占位音
            var audioGo = new GameObject("AudioManager");
            var sfx = audioGo.AddComponent<AudioSource>();
            sfx.playOnAwake = false; sfx.spatialBlend = 0f;
            var am = audioGo.AddComponent<AudioManager>();
            am.SfxSource = sfx;
            am.ScanClip = clips.Scan; am.RevertClip = clips.Revert; am.AbilityClip = clips.Ability;
            am.JumpClip = clips.Jump; am.LandClip = clips.Land; am.DeathClip = clips.Death;
            am.RespawnClip = clips.Respawn; am.CheckpointClip = clips.Checkpoint; am.CompleteClip = clips.Complete;

            // 管理根
            new GameObject("ManagerRoot").AddComponent<ManagerRoot>();

            // UI：淡入淡出 + 调试 HUD + 交互提示 + 通关面板
            var fader = BuildScreenFader();
            BuildDebugHud();
            BuildInteractionPrompt();
            BuildLevelCompleteUI();

            var cpSysGo = new GameObject("CheckpointSystem");
            var cpSys = cpSysGo.AddComponent<CheckpointSystem>();
            cpSys.Player = player.transform;
            cpSys.Fader = fader;
            cpSys.FadeDuration = 0.35f;

            BuildCamera(player.transform);

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[M1SceneBuilder] 已生成并打开 {ScenePath}。");
            EditorUtility.DisplayDialog("Inkform M1",
                "M1 测试场景已重建：\n" + ScenePath +
                "\n\nA/D 移动、Space 跳、E 在目标旁单击=扫描 / 远离=还原。\n" +
                "橙=船锚(矮胖·重·跳矮)、蓝=轻形态(细高·快·跳高)；\n" +
                "红色脉动区=致死，躲到绿色掩体棚下豁免；走到绿色 EXIT=通关。",
                "OK");
        }

        // ───────────────────────── 玩家 ─────────────────────────

        static GameObject BuildPlayer(int playerLayer, InputActionAsset input)
        {
            var player = new GameObject("Player") { tag = "Player", layer = playerLayer };
            player.transform.position = new Vector3(2f, 1.5f, 0f);

            var col = player.AddComponent<CapsuleCollider>();
            col.height = 2f; col.radius = 0.5f; col.center = Vector3.zero;

            var rb = player.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // 子视觉体（缩放它不影响根 collider）
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(player.transform, false);
            SetColor(body, CoreColor);

            var motor = player.AddComponent<PlayerMotor>();
            motor.GroundMask = 1 << 0;
            player.AddComponent<AbilitySystem>();
            var reader = player.AddComponent<InputReader>();
            reader.Actions = input;

            var visual = player.AddComponent<PlayerFormVisual>();
            visual.BodyRenderer = body.GetComponent<Renderer>();
            visual.BodyRoot = body.transform;
            visual.CoreColor = CoreColor;

            // 脚步循环音源（clip 由 Build() 赋占位）
            var foot = player.AddComponent<AudioSource>();
            foot.loop = true; foot.playOnAwake = false; foot.spatialBlend = 0f; foot.volume = 0.4f;
            motor.FootstepSource = foot;

            var actor = player.AddComponent<PlayerActor>();
            actor.Input = reader;
            return player;
        }

        // ───────────────────────── 危险视觉 / EXIT ─────────────────────────

        static GameObject MakeDanger(string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // 纯视觉
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = MakeTransparentMat(new Color(1f, 0.15f, 0.15f, 0.35f));
            go.AddComponent<DangerPulse>();
            return go;
        }

        static void BuildExit(Vector3 pos)
        {
            var exit = MakeBox("EXIT", pos, new Vector3(1.2f, 1.2f, 1.2f), 0, new Color(0.3f, 1f, 0.4f));
            var trig = new GameObject("ExitTrigger");
            trig.transform.SetParent(exit.transform, false);
            var bc = trig.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(3f, 3f, 3f);
            trig.AddComponent<ExitGoal>();
        }

        // ───────────────────────── 检查点 ─────────────────────────

        static void MakeCheckpoint(string name, int id, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(2f, 2.5f, 4f);
            var cp = go.AddComponent<CheckpointVolume>();
            cp.Id = id;

            // 运行时低调标记（半透明柱）
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Marker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            marker.transform.localScale = new Vector3(0.5f, 2.2f, 0.5f);
            marker.GetComponent<Renderer>().sharedMaterial = MakeTransparentMat(new Color(0.3f, 0.6f, 1f, 0.22f));
            var cm = go.AddComponent<CheckpointMarker>();
            cm.Id = id;
            cm.Target = marker.GetComponent<Renderer>();
        }

        // ───────────────────────── UI ─────────────────────────

        static ScreenFader BuildScreenFader()
        {
            var go = new GameObject("FadeCanvas", typeof(Canvas), typeof(CanvasGroup));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var img = new GameObject("Black", typeof(Image));
            img.transform.SetParent(go.transform, false);
            StretchFull(img.GetComponent<RectTransform>());
            img.GetComponent<Image>().color = Color.black;
            return go.AddComponent<ScreenFader>();
        }

        static void BuildDebugHud()
        {
            var go = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var text = MakeText(go.transform, "Label", new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(520f, 260f), 20, TextAnchor.UpperLeft);
            text.text = "Form: Core";
            var hud = go.AddComponent<DebugHud>();
            hud.Label = text;
        }

        static void BuildInteractionPrompt()
        {
            var go = new GameObject("PromptCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            // 底部居中提示
            var prompt = MakeText(go.transform, "Prompt", new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(700f, 50f), 26, TextAnchor.LowerCenter);
            prompt.alignment = TextAnchor.LowerCenter;
            // 中上 toast
            var toast = MakeText(go.transform, "Toast", new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(700f, 50f), 28, TextAnchor.UpperCenter);
            toast.alignment = TextAnchor.UpperCenter;
            toast.color = new Color(0.5f, 1f, 0.7f);

            var ui = go.AddComponent<InteractionPromptUI>();
            ui.PromptText = prompt;
            ui.ToastText = toast;
        }

        static void BuildLevelCompleteUI()
        {
            var go = new GameObject("LevelCompleteCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(go.transform, false);
            StretchFull(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            var label = MakeText(panel.transform, "Text", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 160f), 56, TextAnchor.MiddleCenter);
            label.text = "LEVEL COMPLETE";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.4f, 1f, 0.6f);

            var ui = go.AddComponent<LevelCompleteUI>();
            ui.Panel = panel;
        }

        static Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor align)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.color = Color.white; text.alignment = align;
            return text;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // ───────────────────────── 镜头 ─────────────────────────

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
            director.Follow = follow;
            director.Target = target;
            director.FollowOffset = new Vector3(0f, 3f, -12f);
            director.FieldOfView = 50f;
        }

        // ───────────────────────── 基础 helper ─────────────────────────

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

        static void MakeScanTarget(string name, Vector3 pos, S_AbilityConfig cfg)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = name;
            visual.transform.position = pos;
            visual.transform.localScale = new Vector3(1f, 1.2f, 1f);
            SetColor(visual, cfg.BodyColor);

            var trig = new GameObject("Range");
            trig.transform.SetParent(visual.transform, false);
            var bc = trig.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(3.2f, 3f, 3f);
            trig.AddComponent<ScanTarget>().Config = cfg;
        }

        static void SetColor(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.sharedMaterial = MakeOpaqueMat(color);
        }

        static Material MakeOpaqueMat(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        static Material MakeTransparentMat(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            // URP 透明设置
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color); }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        static S_AbilityConfig EnsureForm(string path, FormId form, string disp, MovementProfile mp, Color bodyColor, Vector3 bodyScale)
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
            cfg.BodyColor = bodyColor;
            cfg.BodyScale = bodyScale;
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
