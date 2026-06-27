# Feature Plan — Nanobot 视觉系统重构（Metacube）

> 状态：Step 0–6 已实现（待 Unity 内验证调参） · 分支 `feature/metanano`
> 关联设计：见 [Docs/nanobot.md](nanobot.md)（三态玩法设计原文）

## 1. 背景与目标

当前 nanobot 视觉系统把蜂群画成「沿速度方向拉丝的金属丝条」（`NanobotRenderer` 用拉长的
capsule 实例）。本次重构把视觉本体换成 **metacube**：约 **1000 个方形小立方体**，各自按周期相位
**逐渐变大变小（脉冲）**，共同构成一个有呼吸感的金属质量体。

整个 nanobot 是一个**围绕附身玩法**的视觉系统，生命周期为三态循环：

| 状态 | 含义 | 形态 | 子态 |
| --- | --- | --- | --- |
| **聚合** Idle | 玩家未附身 | 贴地半球团跟随玩家 | 聚合 / 离散 |
| **蔓延** Spreading | 从一物体到另一目标的中间态 | 管道 / 触手 | （过渡） |
| **附身** Possessed | 附在目标上 | **全覆盖**（先做）/ 固定轨迹捆绑（未来） | 进入 / 退出 |

### 已确认决策

1. **Metacube = 离散脉冲方块**：GPU 实例化绘制 1000 个独立 cube，各自按相位脉冲；**不做真 metaball**。
2. **范围 = 全部重写** `Assets/Scripts/Nanobots/` 整层（含状态机）；proven 算法以新代码形式承接。
3. **附身 = 先做全覆盖**；strap（固定轨迹捆绑）作为未来子形态预留接口。

现有架构分层很干净，新系统**沿用同样的分层形状**（形态 → 积分 → 渲染 → 编排），只是语义与渲染换成 metacube。

## 2. 目标架构

```
Assets/Scripts/Nanobots/
  MetacubeSystem.cs      ← 核心：1000 cube 池 + SmoothDamp 积分 + 脉冲缩放 + DrawMeshInstanced 绘制
  IMetacubeForm.cs       ← 形态接口 SampleTarget(i,count,t) / IsComplete(t)（+ 确定性 Hash 工具）
  AggregateForm.cs       ← 聚合：贴地半球，dispersion 参数驱动 聚合↔离散 子态
  SpreadForm.cs          ← 蔓延：沿 SpreadPathSolver 预解算路径生长（含 reverse 收回）
  PossessForm.cs         ← 附身：全覆盖（局部空间表面点跟随移动目标）；预留 strap 模式 enum
  SpreadPathSolver.cs    ← 蔓延几何：源→贴地主干→侧分支→爬面到目标表面点（重写自 TentacleSolver）
  Possessable.cs         ← 目标：表面采样 / 投影 / 高亮闪烁（重写，承接 proven 采样）
  PossessionDirector.cs  ← 三态状态机 + 雷达扫描 + 选择 + 控制转移（重写）
  SandboxPlayerInput.cs  ← 输入适配（重写薄封装）
```

### 核心新机制 — 脉冲（pulse）

在 `MetacubeSystem` 每帧绘制时计算，各 cube 独立脉冲、确定性相位（不抖动）：

```
phase[i]  = Hash.Unit(i, salt) * TAU                       // 每 cube 确定性相位
pulse01   = 0.5 + 0.5 * sin(Time.time * PulseFreq * freqJit[i] + phase[i])
size      = Lerp(MinCubeSize, MaxCubeSize, pulse01)        // 逐渐变大变小
matrix[i] = TRS(pos[i], spin[i], size)                     // spin = 每 cube 慢速确定性旋转
DrawMeshInstanced(cubeMesh, mat, matrices, N)              // N=1000 ≤ 1023，单批
```

### 承接的 proven 资产（以新代码重写，不直接保留旧文件）

- 确定性伪随机 `Hash.Unit / Hash.Direction`（原 `SwarmFormations.cs:23-42`）。
- `Vector3.SmoothDamp` 逐 cube 追目标的积分手感（原 `NanobotSwarm.cs:58-77`）。
- 蔓延 4 段路径几何（原 `TentacleSolver`）、目标表面均匀采样与投影
  （原 `Possessable.GetEvenSurfaceSamples / GetSurfaceProjector`）。
- 控制转移 / raycast 可附身闸门 / 雷达扫描（原 `PossessionDirector` 中
  `AttachControlTo`、`ResolveFootAndContact`、`GroundSnap`、`ScanTick`）。

## 3. 删除清单

- `Assets/Scripts/Nanobots/NanobotSwarm.cs`(+ .meta)
- `Assets/Scripts/Nanobots/NanobotRenderer.cs`(+ .meta)
- `Assets/Scripts/Nanobots/SwarmFormations.cs`(+ .meta)
- `Assets/Scripts/Nanobots/TentacleSolver.cs`(+ .meta)
- 已 staged 删除的 `NanobotTree.cs` / `NanobotTubeRenderer.cs`（确认提交删除）
- 自定义 flow shader 与旧 bot 材质：`Assets/Shaders/NanobotFlow.shader`(+ .meta)、
  `Assets/Settings/M_NanobotBot.mat`(+ .meta) —— 删前确认无其它引用；新 cube 用普通 URP Lit 金属材质 `M_Metacube.mat`。
- `Possessable.cs` / `PossessionDirector.cs` / `SandboxPlayerInput.cs` 原地重写（保持 namespace `Inkform.Nanobots`）。

## 4. 实现步骤（每步可独立验证）

### Step 0 — 拆除
删除「删除清单」全部文件。此时项目不可编译（builder / scene 引用待后续补齐），是预期中间态。

### Step 1 — Metacube 核心 + 脉冲 + 聚合半球（最小可见里程碑）
- `IMetacubeForm.cs`（接口 + Hash 工具）、`AggregateForm.cs`（贴地半球，先忽略 dispersion）、
  `MetacubeSystem.cs`（池、SmoothDamp 积分、脉冲、`DrawMeshInstanced` cube）。
- `MetacubeSystem` 公开：`Count(=1000)`、`SetForm(IMetacubeForm, speed)`、`Centroid`、`Progress`、
  `FormComplete`、脉冲参数（`MinCubeSize/MaxCubeSize/PulseFreq`）。
- 临时在 `Start()` 里 `SetForm(new AggregateForm(...))` 自测。
- **验证**：场景里出现一坨贴地、呼吸脉动的金属方块半球。

### Step 2 — 状态机 + 输入 + 目标（聚合态打通）
- 重写 `Possessable.cs`（`Bounds` / `GetEvenSurfaceSamples` / `GetSurfaceProjector` / `Highlighted` /
  `Flash` / `OnPossessed` / `SetCollidersEnabled`）。
- 重写 `PossessionDirector.cs`：先实现 `State.Idle` + 雷达 `ScanTick` + 1/2 选择 + 高亮 +
  `EnterIdle` 设 `AggregateForm` 跟随玩家脚下地面点。
- 重写 `SandboxPlayerInput.cs`；接 `InputReader` 事件（`SelectPrev/Next`、`Interact`、`Use`）。
- 子态 *聚合/离散*：`AggregateForm` 加 `Dispersion(0..1)` 半径倍率，director 据有无选中目标 lerp。
- **验证**：WASD 移动半球跟随；靠近可附身物闪一下、1/2 切换高亮；选中时半球收紧（聚合）。

### Step 3 — 蔓延（Spreading）
- `SpreadPathSolver.cs`（4 段路径：源→贴地→侧分支→爬面到 `GetEvenSurfaceSamples` 叶点；
  含 `GroundSnap/SurfaceProject` 回调注入）。
- `SpreadForm.cs`（统一生长前沿 + 每 cube lag 拉散；`reverse` 用于收回）。
- `PossessionDirector.Possess()`：`ResolveFootAndContact` 闸门 → 解算路径 → `SetForm(SpreadForm)`
  → 等 `FormComplete`。
- **验证**：E 附身时方块从半球贴地伸出、爬上目标表面随机点铺开。

### Step 4 — 附身（Possessed，全覆盖）+ 脱离
- `PossessForm.cs`：全覆盖 = 目标局部空间表面点 + 极小 idle 摆动，随目标移动整体跟随；
  `enum Mode { Cover, Strap }`，Strap 先占位。
- director：蔓延末态叶子反变换为局部点 → `AttachControlTo`（parent 到玩家、关碰撞、
  玩家安全落位、解冻 + 附身 MovementProfile 可跳）。
- `Detach()` → `DetachRoutine`：`SpreadForm(reverse:true)` 收回到旁侧地面点 → `EnterIdle`。
- **验证**：附身后 WASD 驾驶目标（可跳），方块贴满随动；左键脱离收回半球回常态；
  case B（附身→附身）从当前物体表面取源点。

### Step 5 — 打磨
- 脉冲调参（size/freq/振幅、每 cube 频率抖动）、cube 旋转手感、`M_Metacube.mat` 金属度/反射。
- 进入/退出附身的过渡节奏（SpreadSpeed、settle）。聚合/离散触发条件细化。

### Step 6 — Builder + 场景重建
- 更新 `Assets/Scripts/Editor/NanobotSandboxBuilder.cs`：
  - `swarmGo` 改挂 `MetacubeSystem`（`Count=1000`、脉冲参数、`M_Metacube.mat`），
    删去 `NanobotRenderer` / `EnsureBotMaterial`(NanobotFlow) 相关；
  - 新增 `EnsureMetacubeMaterial()`（URP Lit，`enableInstancing=true`，金属度/光滑度）；
  - director 字段名按重写后的 API 对齐。
- 菜单 `Inkform/Sandbox/Build Nanobot Sandbox` 重新生成 `Assets/Scenes/NanobotSandbox.unity`。

## 5. 未来预留（不在本次实现）

- **Strap 固定轨迹捆绑**：`PossessForm.Mode.Strap` —— cube 沿目标若干环绕样条排布。接口已留，逻辑后补。
- **真 metaball**：若日后要融合表面，`MetacubeSystem` 的绘制段可替换为 raymarch/marching-cubes，不动 form / director。

## 6. 端到端验证

Unity 编辑器内：
1. 菜单 `Inkform/Sandbox/Build Nanobot Sandbox` 重建场景 → 进 Play。
2. **聚合**：方块半球贴地、脉冲呼吸、跟随 WASD；移动/无目标离散、选中目标聚合收紧。
3. **检测/选择**：靠近 `Crate_OK / Sphere_OK / Crate_OnLedge / Untitled(FBX)` 闪光，1/2 切换高亮；
   `Crate_NoGround` 不可附身（E 无效并打印原因）。
4. **蔓延**：E → 方块贴地伸触手爬上目标铺满。
5. **附身**：铺满后 WASD 驾驶目标 + Space 跳；左键脱离 → 收回半球回常态。
6. **性能**：1000 cube 单批 `DrawMeshInstanced`，确认无明显掉帧（必要时降到 ~512 或后续上
   Jobs/Burst，接口不变）。
