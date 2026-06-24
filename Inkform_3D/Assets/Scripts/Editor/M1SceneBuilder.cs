using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Splines;
using UnityEngine.Timeline;
using UnityEngine.Playables;
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
        const string RemotePath = DataFolder + "/S_RemoteForm.asset";
        const string TeleportPath = DataFolder + "/S_TeleportForm.asset";
        const string BulbPath = DataFolder + "/S_BulbForm.asset";
        const string MagnetPath = DataFolder + "/S_MagnetForm.asset";
        const string BalloonPath = DataFolder + "/S_BalloonForm.asset";
        const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";

        static readonly Color CoreColor = new Color(0.10f, 0.10f, 0.14f);

        [MenuItem("Inkform/Build Full Scene (A-H)")]
        [MenuItem("Inkform/M1/Build M1 Test Scene")]
        [MenuItem("Inkform/M2/Build M2 Scene")]
        public static void Build()
        {
            int playerLayer = EnsureLayer("Player");
            int coverLayer = EnsureLayer("Cover");

            var clips = PlaceholderAudioGenerator.EnsureAll();

            var anchorCfg = EnsureForm(AnchorPath, FormId.Anchor, "船锚",
                new MovementProfile { MoveSpeedMul = 0.45f, MassMul = 3f, JumpHeightMul = 0.3f, Buoyancy = -8f, Drag = 0.4f, CanJump = true },
                new Color(1f, 0.55f, 0.2f), new Vector3(1.3f, 0.6f, 1.3f));
            var remoteCfg = EnsureForm(RemotePath, FormId.Remote, "遥控",
                new MovementProfile { MoveSpeedMul = 1f, MassMul = 1f, JumpHeightMul = 1f, Buoyancy = 0f, Drag = 0f, CanJump = true },
                new Color(0.4f, 0.8f, 1f), new Vector3(1f, 1f, 1f));
            var teleportCfg = EnsureForm(TeleportPath, FormId.Teleport, "传送",
                new MovementProfile { MoveSpeedMul = 1.1f, MassMul = 0.8f, JumpHeightMul = 1.1f, Buoyancy = 0f, Drag = 0f, CanJump = true },
                new Color(0.7f, 0.45f, 1f), new Vector3(0.9f, 1.2f, 0.9f));
            var bulbCfg = EnsureForm(BulbPath, FormId.Bulb, "灯泡·电池",
                new MovementProfile { MoveSpeedMul = 1f, MassMul = 1f, JumpHeightMul = 1f, Buoyancy = 0f, Drag = 0f, CanJump = true },
                new Color(1f, 0.9f, 0.4f), new Vector3(1f, 1f, 1f));
            var magnetCfg = EnsureForm(MagnetPath, FormId.Magnet, "磁铁",
                new MovementProfile { MoveSpeedMul = 0.8f, MassMul = 1.5f, JumpHeightMul = 0.8f, Buoyancy = 0f, Drag = 0.2f, CanJump = true },
                new Color(0.6f, 0.3f, 0.3f), new Vector3(1.1f, 0.9f, 1.1f));
            var balloonCfg = EnsureForm(BalloonPath, FormId.Balloon, "气球",
                new MovementProfile { MoveSpeedMul = 1.1f, MassMul = 0.3f, JumpHeightMul = 2f, Buoyancy = 11f, Drag = 1.2f, CanJump = true },
                new Color(0.95f, 0.5f, 0.7f), new Vector3(1.4f, 1.5f, 1.4f));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 灯光（降低环境光，让红光对比更强）
            var sun = new GameObject("Directional Light");
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 0.7f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 地面（A/B 段一整条）+ 前后墙（延伸覆盖到 D 段）
            MakeBox("Ground_AB", new Vector3(25.5f, -0.5f, 0f), new Vector3(67f, 1f, 8f), 0, new Color(0.18f, 0.2f, 0.24f));
            MakeBox("BackWall", new Vector3(86f, 3f, 4.6f), new Vector3(190f, 8f, 0.4f), 0, new Color(0.12f, 0.13f, 0.16f));
            MakeBox("FrontWall", new Vector3(86f, 3f, -4.6f), new Vector3(190f, 8f, 0.4f), 0, new Color(0.12f, 0.13f, 0.16f)); // 防 WASD 前后走出地面

            // A 段：扫描目标（颜色取自形态）+ 矮坎
            MakeScanTarget("ScanTarget_Anchor", new Vector3(8f, 0.6f, 0f), anchorCfg);
            MakeScanTarget("ScanTarget_Teleport", new Vector3(14f, 0.6f, 0f), teleportCfg);
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

            // C 水闸 + D 吊臂 + F 清扫者 + E 三段落能力 + 结尾(G/H)
            BuildSectionC(anchorCfg, clips);
            BuildSectionD(remoteCfg, clips);
            var fZone = BuildSectionF(teleportCfg, coverLayer, clips);
            BuildSectionE(bulbCfg, magnetCfg, balloonCfg, clips);
            var ending = BuildEnding(clips);
            BuildPostFX(); // URP 后处理氛围（失败可注释）

            // 检查点（含编辑器 Gizmos + 运行时标记）
            MakeCheckpoint("Checkpoint_0", 0, new Vector3(2f, 1f, 0f));
            MakeCheckpoint("Checkpoint_1", 1, new Vector3(24f, 1f, 0f));
            MakeCheckpoint("Checkpoint_2", 2, new Vector3(36f, 1f, 0f));
            MakeCheckpoint("Checkpoint_3", 3, new Vector3(55f, 1f, 0f));
            MakeCheckpoint("Checkpoint_4", 4, new Vector3(71f, 1f, 0f));
            MakeCheckpoint("Checkpoint_5", 5, new Vector3(93f, 1f, 0f));
            MakeCheckpoint("Checkpoint_6", 6, new Vector3(126f, 1f, 0f));
            MakeCheckpoint("Checkpoint_7", 7, new Vector3(158f, 1f, 0f));

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
            am.PuzzleClip = clips.Puzzle; am.TeleportClip = clips.Teleport; am.DeathClip = clips.Death;

            // 管理根
            new GameObject("ManagerRoot").AddComponent<ManagerRoot>();

            // UI：淡入淡出 + 调试 HUD + 交互提示 + 通关面板
            var fader = BuildScreenFader();
            BuildDebugHud();
            BuildInteractionPrompt();
            BuildLevelCompleteUI();
            if (ending != null) ending.Fader = fader;

            var cpSysGo = new GameObject("CheckpointSystem");
            var cpSys = cpSysGo.AddComponent<CheckpointSystem>();
            cpSys.Player = player.transform;
            cpSys.Fader = fader;
            cpSys.FadeDuration = 0.35f;

            var fVcam = BuildCamera(player.transform);
            if (fZone != null) fZone.Cam = fVcam;

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[M1SceneBuilder] 已生成并打开 {ScenePath}。");
            EditorUtility.DisplayDialog("Inkform — Full Scene (A-H)",
                "完整关卡场景已生成：\n" + ScenePath +
                "\n\nWASD 移动、Space 跳、E 在目标旁扫描/远离还原、鼠标左键=用能力。\n" +
                "A 学习 → B 躲移动探照灯 → C 船锚开水闸 → D 遥控搭桥 →\n" +
                "F 传送躲清扫者开门 → E 灯泡供电/磁铁拉门/气球高台开关 → 终点结尾过场。",
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

            // 灯泡形态发光（默认关，BulbForm 持有时开启）
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(player.transform, false);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point; glow.range = 9f; glow.intensity = 2.6f;
            glow.color = new Color(1f, 0.95f, 0.7f);
            glow.enabled = false;

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
            // 操控类能力瞄准提示（在扫描提示上方）
            var abilityPrompt = MakeText(go.transform, "AbilityPrompt", new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(700f, 46f), 24, TextAnchor.LowerCenter);
            abilityPrompt.alignment = TextAnchor.LowerCenter;
            abilityPrompt.color = new Color(1f, 0.85f, 0.4f);

            var ui = go.AddComponent<InteractionPromptUI>();
            ui.PromptText = prompt;
            ui.ToastText = toast;
            ui.AbilityPromptText = abilityPrompt;
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

        /// <summary>建主跟随 vcam + F 大厅拉远 vcam，返回 F vcam 供 CameraZone 切换。</summary>
        static CinemachineCamera BuildCamera(Transform target)
        {
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.GetComponent<Camera>().backgroundColor = new Color(0.03f, 0.04f, 0.06f);
            camGo.GetComponent<Camera>().GetUniversalAdditionalCameraData().renderPostProcessing = true; // 开启 URP 后处理
            camGo.AddComponent<CinemachineBrain>();
            camGo.transform.position = new Vector3(2f, 4f, -12f);

            // 主跟随机位（中等优先级）
            var vcamGo = new GameObject("CM Player Cam");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcamGo.transform.position = new Vector3(2f, 4f, -12f);
            vcam.Follow = target;
            vcam.Priority = 10;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 3f, -12f);

            var director = new GameObject("CameraDirector").AddComponent<CameraDirector>();
            director.Vcam = vcam;
            director.Follow = follow;
            director.Target = target;
            director.FollowOffset = new Vector3(0f, 3f, -12f);
            director.FieldOfView = 50f;

            // F 大厅拉远机位（默认低优先级，由 CameraZone 进区提升）
            var fGo = new GameObject("CM Hall Cam");
            var fVcam = fGo.AddComponent<CinemachineCamera>();
            fGo.transform.position = new Vector3(106f, 7f, -22f);
            fVcam.Follow = target;
            fVcam.Priority = 0;
            var fLens = fVcam.Lens; fLens.FieldOfView = 62f; fVcam.Lens = fLens;
            var fFollow = fGo.AddComponent<CinemachineFollow>();
            fFollow.FollowOffset = new Vector3(0f, 6f, -22f);

            return fVcam;
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

        // ───────────────────────── C / D 谜题段 ─────────────────────────

        static void BuildSectionC(S_AbilityConfig anchorCfg, PlaceholderAudioGenerator.Clips clips)
        {
            // 池底（低于地面）+ 出池地面
            MakeBox("Pool_Floor", new Vector3(63f, -2f, 0f), new Vector3(8f, 1f, 7f), 0, new Color(0.14f, 0.16f, 0.2f));
            MakeBox("Ground_C", new Vector3(68.5f, -0.5f, 0f), new Vector3(4f, 1f, 8f), 0, new Color(0.18f, 0.2f, 0.24f));

            // 水体 + 水面视觉
            var waterGo = new GameObject("WaterVolume");
            waterGo.transform.position = new Vector3(63f, -0.4f, 0f);
            var wcol = waterGo.AddComponent<BoxCollider>();
            wcol.isTrigger = true; wcol.size = new Vector3(8f, 2.4f, 7f);
            var water = waterGo.AddComponent<WaterVolume>();
            water.Buoyancy = 14f; water.DrainDrop = 2.5f; water.DrainDuration = 1.2f;

            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "WaterSurface";
            Object.DestroyImmediate(surface.GetComponent<Collider>());
            surface.transform.SetParent(waterGo.transform, false);
            surface.transform.localPosition = new Vector3(0f, 0.4f, 0f); // 世界 y≈0
            surface.transform.localScale = new Vector3(8f, 0.1f, 7f);
            surface.GetComponent<Renderer>().sharedMaterial = MakeTransparentMat(new Color(0.2f, 0.5f, 0.9f, 0.45f));
            water.WaterSurface = surface.transform;

            // 水闸门（挡对岸出口，valve 激活时下沉移开）
            var gateGo = MakeBox("SluiceGate", new Vector3(66.5f, 1.6f, 0f), new Vector3(0.6f, 3.6f, 7f), 0, new Color(0.3f, 0.34f, 0.4f));
            var gate = gateGo.AddComponent<SimpleGate>();
            gate.OpenOffset = new Vector3(0f, -4f, 0f);

            // 水闸阀（池底，仅船锚沉到此触发）
            var valveGo = new GameObject("SluiceValve");
            valveGo.transform.position = new Vector3(63f, -1.2f, 0f);
            var vcol = valveGo.AddComponent<BoxCollider>();
            vcol.isTrigger = true; vcol.size = new Vector3(6f, 1f, 6f);
            var valve = valveGo.AddComponent<SluiceValve>();
            valve.Water = water; valve.Gate = gate;
            var vsfx = valveGo.AddComponent<AudioSource>();
            vsfx.clip = clips.Valve; vsfx.playOnAwake = false; vsfx.spatialBlend = 1f;
            valve.Sfx = vsfx;

            MakeBox("ValveMark", new Vector3(63f, -1.45f, 0f), new Vector3(1.5f, 0.3f, 1.5f), 0, new Color(1f, 0.8f, 0.2f));

            // 入口处船锚扫描目标
            MakeScanTarget("ScanTarget_Anchor_C", new Vector3(57f, 0.6f, 0f), anchorCfg);

            var puzzle = new GameObject("Puzzle_C").AddComponent<PuzzleController>();
            puzzle.PuzzleId = 100;
            puzzle.Mechanisms.Add(valve);
        }

        static void BuildSectionD(S_AbilityConfig remoteCfg, PlaceholderAudioGenerator.Clips clips)
        {
            MakeBox("Ground_D1", new Vector3(72f, -0.5f, 0f), new Vector3(5f, 1f, 8f), 0, new Color(0.18f, 0.2f, 0.24f));
            MakeBox("Ground_D2", new Vector3(85f, -0.5f, 0f), new Vector3(12f, 1f, 8f), 0, new Color(0.18f, 0.2f, 0.24f));

            // 缺口下方致死区（掉落即死）
            var killGo = new GameObject("FallKill");
            killGo.transform.position = new Vector3(76.5f, -6f, 0f);
            var kcol = killGo.AddComponent<BoxCollider>();
            kcol.isTrigger = true; kcol.size = new Vector3(10f, 4f, 10f);
            killGo.AddComponent<KillVolume>();

            // 可遥控集装箱（侧边待命，遥控 Operate 移到缺口搭桥）
            var cont = MakeBox("MovableContainer", new Vector3(72f, 0.6f, 1.8f), new Vector3(5.5f, 1.2f, 2.4f), 0, new Color(0.6f, 0.5f, 0.25f));
            var mc = cont.AddComponent<MovableContainer>();
            mc.BridgeOffset = new Vector3(4.5f, -0.1f, -1.8f); // 移到缺口中 (76.5,0.5,0)
            mc.MoveDuration = 1.0f;
            var csfx = cont.AddComponent<AudioSource>();
            csfx.clip = clips.Bridge; csfx.playOnAwake = false; csfx.spatialBlend = 1f;
            mc.Sfx = csfx;

            MakeScanTarget("ScanTarget_Remote", new Vector3(71f, 0.6f, 0f), remoteCfg);

            var puzzle = new GameObject("Puzzle_D").AddComponent<PuzzleController>();
            puzzle.PuzzleId = 101;
            puzzle.Mechanisms.Add(mc);
        }

        // E 段（段落三能力串联：灯泡供电 → 磁铁拉门 → 气球高台开关）
        static void BuildSectionE(S_AbilityConfig bulbCfg, S_AbilityConfig magnetCfg, S_AbilityConfig balloonCfg,
            PlaceholderAudioGenerator.Clips clips)
        {
            MakeBox("Ground_E", new Vector3(137f, -0.5f, 0f), new Vector3(34f, 1f, 8f), 0, new Color(0.13f, 0.14f, 0.18f));

            // ① 灯泡供电门
            MakeScanTarget("ScanTarget_Bulb", new Vector3(123f, 0.6f, 0f), bulbCfg);
            var gate1 = MakeBox("Gate_Bulb", new Vector3(130f, 1.75f, 0f), new Vector3(0.6f, 3.5f, 8f), 0, new Color(0.3f, 0.34f, 0.4f)).AddComponent<SimpleGate>();
            var powerGo = MakeBox("PowerNode", new Vector3(127f, 0.8f, 0f), new Vector3(1f, 1.6f, 1f), 0, new Color(1f, 0.85f, 0.3f));
            var power = powerGo.AddComponent<PowerNode>();
            power.Gate = gate1; power.Sfx = AddSfx(powerGo, clips.Power);

            // ② 磁铁金属门
            MakeScanTarget("ScanTarget_Magnet", new Vector3(133f, 0.6f, 0f), magnetCfg);
            var metalGo = MakeBox("MetalDoor", new Vector3(140f, 1.75f, 0f), new Vector3(0.6f, 3.5f, 8f), 0, new Color(0.45f, 0.3f, 0.3f));
            var metal = metalGo.AddComponent<MetalDoor>();
            metal.OpenOffset = new Vector3(0f, -4f, 0f); metal.Sfx = AddSfx(metalGo, clips.Magnet);

            // ③ 气球高台 + 高处开关 → 结尾门
            MakeScanTarget("ScanTarget_Balloon", new Vector3(143f, 0.6f, 0f), balloonCfg);
            MakeBox("Platform_High", new Vector3(148f, 1.5f, 0f), new Vector3(6f, 3f, 8f), 0, new Color(0.2f, 0.22f, 0.26f)); // 顶 y=3，气球才跳得上
            var endGate = MakeBox("Gate_End", new Vector3(152f, 1.75f, 0f), new Vector3(0.6f, 3.5f, 8f), 0, new Color(0.3f, 0.34f, 0.4f)).AddComponent<SimpleGate>();
            var hsGo = new GameObject("HighSwitch");
            hsGo.transform.position = new Vector3(148f, 3.6f, 0f); // 高台顶上
            var hsCol = hsGo.AddComponent<BoxCollider>(); hsCol.isTrigger = true; hsCol.size = new Vector3(5f, 1.2f, 6f);
            var hs = hsGo.AddComponent<HighSwitch>();
            hs.Gate = endGate; hs.Sfx = AddSfx(hsGo, clips.Balloon);

            var pz = new GameObject("Puzzle_E").AddComponent<PuzzleController>();
            pz.PuzzleId = 102;
            pz.Mechanisms.Add(power); pz.Mechanisms.Add(metal); pz.Mechanisms.Add(hs);
        }

        // 结尾段（G 出口 + H 结尾镜头）：到达终点触发结尾过场（脚本 + 可选 Timeline）
        static EndingDirector BuildEnding(PlaceholderAudioGenerator.Clips clips)
        {
            MakeBox("Ground_End", new Vector3(166f, -0.5f, 0f), new Vector3(28f, 1f, 8f), 0, new Color(0.15f, 0.16f, 0.2f));

            // 外界之光
            var lightGo = new GameObject("OutsideLight");
            lightGo.transform.position = new Vector3(176f, 5f, 0f);
            lightGo.transform.rotation = Quaternion.Euler(20f, 200f, 0f);
            var endLight = lightGo.AddComponent<Light>();
            endLight.type = LightType.Spot; endLight.range = 30f; endLight.spotAngle = 70f; endLight.intensity = 0.5f;
            endLight.color = new Color(1f, 0.97f, 0.85f);
            var halo = MakeDanger("EndGlow", new Vector3(176f, 2.5f, 0f), new Vector3(3f, 5f, 3f));
            halo.GetComponent<DangerPulse>().BaseColor = new Color(1f, 0.95f, 0.8f, 1f); // 暖白光晕脉动（非红）

            // 终点触发 + 结尾导演
            var endGo = new GameObject("EndGoal");
            endGo.transform.position = new Vector3(170f, 1.5f, 0f);
            var ec = endGo.AddComponent<BoxCollider>(); ec.isTrigger = true; ec.size = new Vector3(3f, 3f, 8f);
            var ending = endGo.AddComponent<EndingDirector>();
            ending.EndLight = endLight; ending.Duration = 4f;

            // 结尾专属机位（拉远、看终点与光）+ 切换区
            var ecamGo = new GameObject("CM Ending Cam");
            var ecam = ecamGo.AddComponent<CinemachineCamera>();
            ecamGo.transform.position = new Vector3(168f, 7f, -16f);
            ecamGo.transform.LookAt(new Vector3(176f, 3f, 0f));
            ecam.Priority = 0;
            var lens = ecam.Lens; lens.FieldOfView = 55f; ecam.Lens = lens;
            var zoneGo = new GameObject("CameraZone_End");
            zoneGo.transform.position = new Vector3(164f, 2f, 0f);
            var zc = zoneGo.AddComponent<BoxCollider>(); zc.isTrigger = true; zc.size = new Vector3(8f, 6f, 10f);
            var zone = zoneGo.AddComponent<CameraZone>(); zone.Cam = ecam; zone.ActivePriority = 30; zone.InactivePriority = 0;

            // 可选 Timeline（氛围）：激活光晕物体
            BuildEndingTimeline(endGo, ending, halo);
            return ending;
        }

        static void BuildEndingTimeline(GameObject endGo, EndingDirector ending, GameObject haloTarget)
        {
            EnsureFolder("Assets/Timeline");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var track = timeline.CreateTrack<ActivationTrack>();
            var clip = track.CreateDefaultClip(); // ActivationPlayableAsset 是 internal，用默认 clip 工厂
            clip.start = 0; clip.duration = 5;
            AssetDatabase.CreateAsset(timeline, "Assets/Timeline/Ending.playable");

            var director = endGo.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.SetGenericBinding(track, haloTarget);
            ending.Director = director;
        }

        static void BuildPostFX()
        {
            EnsureFolder("Assets/Settings");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.55f); bloom.threshold.Override(1.1f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.32f);
            var col = profile.Add<ColorAdjustments>(true); col.saturation.Override(-18f); col.contrast.Override(8f);
            AssetDatabase.CreateAsset(profile, "Assets/Settings/M2_PostFX.asset");

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true; vol.sharedProfile = profile;
        }

        static AudioSource AddSfx(GameObject go, AudioClip clip)
        {
            var src = go.AddComponent<AudioSource>();
            src.clip = clip; src.playOnAwake = false; src.spatialBlend = 1f;
            return src;
        }

        static CameraZone BuildSectionF(S_AbilityConfig teleportCfg, int coverLayer, PlaceholderAudioGenerator.Clips clips)
        {
            // 大厅地面
            MakeBox("Ground_F", new Vector3(106f, -0.5f, 0f), new Vector3(30f, 1f, 8f), 0, new Color(0.16f, 0.18f, 0.22f));

            // 入口：传送扫描目标 + 安全死角掩体（设标记/躲清扫者）
            MakeScanTarget("ScanTarget_Teleport_F", new Vector3(94f, 0.6f, 0f), teleportCfg);
            MakeBox("SafeCover", new Vector3(98f, 1.5f, 2.5f), new Vector3(2f, 3f, 2f), coverLayer, new Color(0.25f, 0.45f, 0.3f));

            // 清扫者（纯视觉块 + 随行致死 ScanField，周期掠过）
            var sweeper = MakeBox("Sweeper", new Vector3(100f, 3f, 0f), new Vector3(3f, 5f, 8f), 0, new Color(0.07f, 0.05f, 0.1f));
            Object.DestroyImmediate(sweeper.GetComponent<Collider>()); // 纯视觉，致死靠 Hazard

            var hazard = new GameObject("Hazard");
            hazard.transform.SetParent(sweeper.transform, false);
            hazard.transform.localPosition = new Vector3(0f, -2.5f, 0f);
            var hcol = hazard.AddComponent<BoxCollider>();
            hcol.isTrigger = true; hcol.size = new Vector3(4f, 5f, 8f);
            var hscan = hazard.AddComponent<ScanField>();
            hscan.LightOrigin = sweeper.transform; hscan.RotateSpeed = 0f;
            hscan.CoverMask = 1 << coverLayer; hscan.Source = "Sweeper";

            var hdanger = MakeDanger("SweeperDanger", Vector3.zero, new Vector3(4f, 5f, 8f));
            hdanger.transform.SetParent(sweeper.transform, false);
            hdanger.transform.localPosition = new Vector3(0f, -2.5f, 0f);

            var sc = sweeper.AddComponent<SweeperController>();
            sc.StartPos = new Vector3(100f, 3f, 0f);
            sc.EndPos = new Vector3(114f, 3f, 0f);
            sc.SweepDuration = 5.5f; sc.WaitDuration = 3f; sc.Hazard = hazard;
            var ssfx = sweeper.AddComponent<AudioSource>();
            ssfx.clip = clips.Sweeper; ssfx.loop = true; ssfx.playOnAwake = true;
            ssfx.spatialBlend = 1f; ssfx.minDistance = 5f; ssfx.maxDistance = 40f; ssfx.volume = 0f;
            sc.ApproachSfx = ssfx;

            // 出口门（被远端开关打开）
            var gateGo = MakeBox("ExitGate", new Vector3(117f, 1.75f, 0f), new Vector3(0.6f, 3.6f, 8f), 0, new Color(0.3f, 0.34f, 0.4f));
            var gate = gateGo.AddComponent<SimpleGate>();
            gate.OpenOffset = new Vector3(0f, -4f, 0f);

            var switchGo = MakeBox("GoalSwitch", new Vector3(114.5f, 0.6f, 0f), new Vector3(1.5f, 1.2f, 1.5f), 0, new Color(1f, 0.85f, 0.2f));
            var ts = switchGo.AddComponent<TriggerSwitch>();
            ts.Gate = gate;
            var tsfx = switchGo.AddComponent<AudioSource>();
            tsfx.clip = clips.Puzzle; tsfx.playOnAwake = false; tsfx.spatialBlend = 1f;
            ts.Sfx = tsfx;

            var puzzleF = new GameObject("Puzzle_F").AddComponent<PuzzleController>();
            puzzleF.PuzzleId = 102;
            puzzleF.Mechanisms.Add(ts);

            // 镜头切换区（覆盖整个 F 大厅，进区切到拉远机位）
            var zoneGo = new GameObject("CameraZone_F");
            zoneGo.transform.position = new Vector3(106f, 2f, 0f);
            var zcol = zoneGo.AddComponent<BoxCollider>();
            zcol.isTrigger = true; zcol.size = new Vector3(30f, 6f, 10f);
            var zone = zoneGo.AddComponent<CameraZone>();
            zone.ActivePriority = 20; zone.InactivePriority = 0;
            return zone;
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
