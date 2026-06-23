# Inkform — 技术设计文档（TDD）：关键系统架构与技术选型

> 配套文档：`GDD_inkform_3D_Puzzle.md`（玩法定义） / `GDD_inkform_3D_Puzzle_EN.md`（英文版）
> 工程：Unity **6000.4.10f1** + URP **17.4**，项目根 `Inkform_3D/`
> 状态：v0.1（架构定稿前的设计提案） · 面向单关垂直切片 Demo

---

## 0. 文档定位

本文档把 GDD 描述的**玩法**翻译成可落地的**系统架构与技术选型**，是程序实现的依据。

- **与 GDD 的关系**：GDD 回答"做什么"（玩法、能力、威胁、流程），本 TDD 回答"怎么搭"（系统划分、类/接口、数据结构、第三方选型、里程碑落地顺序）。术语与里程碑（M1/M2/M3）与 GDD §8、§12 保持一致。
- **组织方式**：先**均衡概览**所有系统（第 1–2 章），再**按优先级分层深化**——
  - **第一批（本版深化，第 3 章）**：扫描—模仿能力、威胁—潜行、检查点—进度，三者构成 M1 最小可玩闭环。
  - **第二批（本版列计划，第 4 章）**：镜头、机关/交互、谜题编排、UI、音频、关卡流程、表现层、存档等，给出概览 + 接口占位 + 深化时要回答的问题。
- **适用范围**：单关 Demo。所有设计以"可演进到 M3"为约束，但不为远期需求过度设计。
- **设计取向**：**数据驱动**（ScriptableObject 配置）+ **事件解耦**（事件总线）+ **成熟开源库优先**（Cinemachine / UniTask）。

---

## 1. 技术选型总览（Tech Stack）

| 领域 | 选型 | 状态 | 里程碑 | 理由摘要 |
|------|------|------|--------|----------|
| 引擎 / 渲染 | Unity 6000.4.10f1 + **URP** 17.4 | 已装 | M1 | 沿用工程版本；URP 后处理（Bloom/Vignette/颜色分级）契合冷色调强光影 |
| 输入 | **Input System** 1.19（复用 `InputSystem_Actions`） | 已装 | M1 | 已有 Player/UI 两套 Action Map，直接扩展 |
| 镜头 | **Cinemachine 3.x** | **需添加** | M1→M2 | 固定/轨道机位、机位间混合切换，贴合 GDD 电影化镜头；自写成本高 |
| 异步 / 时序 | **UniTask** | **需添加** | M1 | 冷却、扫描周期、淡入淡出、重生时序；零 GC、可取消，优于裸协程 |
| 事件 | **轻量自写事件总线 + ScriptableObject Event Channel** | 自写 | M1 | 沿用 GDD `S_GameEvent` 思想；解耦威胁/机关/检查点广播 |
| 配置数据 | **ScriptableObject**（`S_*` 前缀） | 自写 | M1 | 能力/关卡/扫描参数数据化，策划可调、便于复用 |
| 状态机 | **轻量自写 FSM**（泛型） | 自写 | M1→M2 | 玩家形态、清扫者行为、巡逻者警觉；需求简单不引重型行为树 |
| UI / HUD | **uGUI**（极简 HUD） | 已装 | M2→M3 | HUD 近乎为零（能力图标/交互提示/死亡淡入）；uGUI 最快。UI Toolkit 留给菜单/编辑器工具 |
| 音频 | **原生 AudioMixer + AudioManager**；M3 评估 FMOD | 原生已装 | M2→M3 | Demo 用原生足够；清扫者低频氛围/自适应混音在 M3 评估 FMOD |
| 物理 | 内置 **3D Physics**（Rigidbody） | 已装 | M1 | 重量/浮力/磁性/气流均为物理交互，统一用刚体 |
| 导航 | **AI Navigation** 2.0（NavMesh） | 已装 | M2（可选） | 仅"可选巡逻者"需要 |
| 过场编排 | **Timeline** 1.8 | 已装 | M2→M3 | 清扫者掠过、结尾镜头 |
| 程序集 | **asmdef 分层** | 自写 | M1 | 编译隔离、强制单向依赖、加快迭代 |
| 测试 | **Unity Test Framework** 1.6 | 已装 | 持续 | 对纯逻辑（能力状态、检查点）做 EditMode 测试 |
| 版本控制 | Git + **Git LFS**（已配） | 已配 | 持续 | 二进制资源走 LFS，YAML 走 UnityYAMLMerge |

### 1.1 需要新增的包（待执行）
- `com.unity.cinemachine`（3.x）— Package Manager 安装。
- **UniTask** — 经 OpenUPM 或 git url（`com.cysharp.unitask`）安装。
- （远期，M3 评估）FMOD for Unity。

> 选型原则：能用 Unity 官方/主流稳定库解决的不自写；强耦合游戏规则的核心逻辑（能力、威胁判定、事件契约）自写以保持掌控。

---

## 2. 总体架构概览

### 2.1 分层架构

```
┌──────────────────────────────────────────────────────────────┐
│  Bootstrap / Managers   ManagerRoot(DontDestroyOnLoad)         │
│                         GameStateMachine · InputLock · EventBus│
├──────────────────────────────────────────────────────────────┤
│  Systems (玩法系统)                                            │
│   AbilitySystem · ThreatSystem · CheckpointSystem ·            │
│   CameraDirector · InteractionSystem · AudioManager · HUD      │
├──────────────────────────────────────────────────────────────┤
│  Entities / Abilities (场景实体)                               │
│   PlayerActor · Ability(遥控/船锚/传送…) · ScanField ·          │
│   Sweeper · CheckpointVolume · Mechanism(水闸/吊臂…)            │
├──────────────────────────────────────────────────────────────┤
│  Data (ScriptableObject)                                       │
│   S_AbilityConfig · S_ScanPattern · S_LevelConfig · S_GameEvent│
├──────────────────────────────────────────────────────────────┤
│  Presentation (表现层)                                         │
│   View/VFX(变身/墨水) · URP 后处理 · Cinemachine 机位 · 音源     │
└──────────────────────────────────────────────────────────────┘
```

**依赖方向自上而下**：上层可引用下层接口；下层通过**事件**向上广播，不反向直接引用。表现层只订阅状态、不写规则。

### 2.2 运行时骨架

- **`ManagerRoot`**：场景启动时实例化的根容器，`DontDestroyOnLoad`，持有/初始化各 Manager（事件总线、输入锁、游戏状态机、各 System 的单例入口）。提供有序初始化与跨场景持久化。
- **`GameStateMachine`**：全局状态 `Boot → Playing → Paused → Respawning → Cutscene`。状态切换驱动输入锁与镜头/UI 行为。
- **`InputLock`**：引用计数式输入锁（重生、过场、读取线索时锁定玩家输入）。请求/释放成对，计数归零才解锁。
- **`EventBus`**：见 2.3。

### 2.3 事件系统（`S_GameEvent`）

两种互补形态：

1. **C# 强类型事件总线**（代码内高频、带 payload）：`EventBus.Publish(new PlayerKilled{...})` / `EventBus.Subscribe<PlayerKilled>(...)`。用于系统间逻辑通信。
2. **ScriptableObject Event Channel**（编辑器可连线、表现/音频订阅）：`S_GameEvent` 资产 + `GameEventListener` 组件，便于美术/策划在 Inspector 把"被发现"接到音效/后处理，无需改代码。

> 约定：**规则用 C# 事件；表现用 SO Event Channel**。两者通过一个适配层互转（核心系统发 C# 事件，必要时转发到对应 SO 资产）。

### 2.4 数据驱动与命名约定

- ScriptableObject 一律 **`S_` 前缀**（沿用 GDD `S_GameEvent`/`S_LevelConfig`）：`S_AbilityConfig`、`S_ScanPattern`、`S_LevelConfig`、`S_GameEvent`。
- 接口 `I` 前缀（`IMimicForm`/`IThreatSource`/`IInteractable`/`IMechanism`）。
- 运行时系统以 `System`/`Director`/`Manager` 结尾按职责命名。

### 2.5 全系统清单总览

| # | 系统 | 职责 | 里程碑 | 深化状态 |
|---|------|------|--------|----------|
| 1 | 扫描—模仿能力系统 | 角色控制 + 扫描/重组/还原 + 能力执行（移动随形态变） | M1（基础）→M2/M3（各形态） | ★ 本版深化（§3.1） |
| 2 | 威胁—潜行系统 | 扫描体/即死判定/清扫者/巡逻者 | M1（固定扫描）→M2（清扫者） | ★ 本版深化（§3.2） |
| 3 | 检查点—进度系统 | 分布式检查点/死亡重生/关卡分段 | M1 | ★ 本版深化（§3.3） |
| 4 | 镜头系统 | Cinemachine 固定/轨道机位切换 | M1（基础）→M2 | 计划中（§4.1） |
| 5 | 机关/交互系统 | 水闸/吊臂/传送带/重量板/供电/单向门 | M2→M3 | 计划中（§4.2） |
| 6 | 谜题编排 | 关卡内谜题状态与解算条件 | M2 | 计划中（§4.3） |
| 7 | UI / HUD | 能力图标/交互提示/死亡淡入淡出 | M2→M3 | 计划中（§4.4） |
| 8 | 音频系统 | 环境音/清扫者线索/被发现反馈 | M2→M3 | 计划中（§4.5） |
| 9 | 关卡流程 / 场景管理 | 分段加载、流程推进 | M1→M2 | 计划中（§4.6） |
| 10 | 表现层（VFX/渲染） | 墨水/变身/扫描光/URP 后处理 | M2→M3 | 计划中（§4.7） |
| 11 | 存档 / 设置 | 选项、（远期）存档 | 远期 | 计划中（§4.8） |
| — | 基础设施 | 事件总线/输入锁/状态机/程序集 | M1 | 已纳入架构（§2） |

---

## 3. 三大核心系统深化（第一批）★

> 三者构成 M1 闭环：**移动 → 扫描模仿获得能力（并改变移动手感）→ 用能力规避一个扫描威胁 → 被照到即死 → 回到检查点**。本章给到"可据此动手写代码"的深度：接口/类、SO 数据、事件契约、扩展点。

### 3.1 扫描—模仿能力系统（含角色控制基础）

> **设计变更（v0.2）**：核心机制由"吞噬物体"改为"**扫描模仿**"。Inkform 是由无数纳米机器人组成的个体；它不吞下物体，而是**扫描目标、读取其形态蓝图（Schematic）并重组自身去模仿它**，从而获得对应形态与能力。**同一时刻只能缓存/模仿一个目标**——扫描新目标会替换当前蓝图。这一变更也强化了下文"移动玩法随形态而变"的设计。

#### 3.1.1 职责
角色基础移动（始终可用）+ 扫描场景中的可模仿目标 + 重组为该形态并执行其能力 + 还原回基态。对应 GDD §4、§5（机制由吞噬改为扫描）。

#### 3.1.2 角色设定与表现形式（纳米机器人）
- **本体**：无数纳米机器人组成的集群个体。**基态（Core Form）** 推荐表现为"**半流体内核 + 硬表面板片外壳**"的混合质感：深色半透明的流体核心（史莱姆 / 水银感）外覆一层会流动重排的微小金属板片（nanite panels），泛冷光——既有史莱姆的可塑流动，又有硬表面的工业冷峻，契合冷色调实验室基调，且比纯史莱姆更"讲得通"纳米机器人设定。
- **扫描（Scan）**：向目标投射扫描光 / 网格，读取其"蓝图"（形态轮廓 + 功能特性），纳米机器人缓存该 `Schematic`。
- **变身（Materialize / 膨胀重组）**：纳米机器人从基态**向外膨胀、增殖并重排板片**，自组装出目标轮廓（船锚 / 遥控器…），呈现"零件自装配"的硬表面装配感，同时保留 inkform 的流动底色——是"仿造体"而非真实物体（用户决策："从初始状态膨胀成扫描目标的形态"）。
- **还原（Dissolve）**：板片解体、形态塌缩回基态。
> 备选表现：纯流体史莱姆（更软萌，偏离冷峻基调）/ 纯硬表面体素重组（更机械，失去墨水流动感）。**推荐"流体内核 + 硬表面板片"的混合**。表现细节在 §4.7 深化。

#### 3.1.3 角色控制器选型：**Rigidbody（推荐，本次变更进一步强化）**
"**模仿不同形态 → 改变基础移动玩法**"是本系统核心乐趣（用户决策）：变船锚就更沉、走更慢、跳更矮；变气球更轻、可飘浮。这类"质量 / 浮力 / 推力"差异用物理表达最自然。

| 方案 | 优势 | 劣势 | 结论 |
|------|------|------|------|
| `CharacterController` | 移动精确、易做台阶 | 不吃力学，浮力/重量/磁吸/气流要全手写 | 不选 |
| **`Rigidbody` + 自定义 `PlayerMotor`** | 重量(船锚)、浮力(气球)、磁吸、气流推力天然由物理表达；形态切换=换一组移动参数 | 需调参、贴面控制略复杂 | **采用** |

`PlayerMotor` 基础移动用速度控制，当前形态通过 `MovementProfile`（见 3.1.5）修改质量/阻力/浮力/跳跃，实现"形态改变手感"。2.5D：约束主平面，局部纵深通道临时放开 Z。

#### 3.1.4 形态与状态
玩家两态：`Core(基态)` ↔ `Mimic(模仿态)`。轻量 FSM 管理；进入 `Mimic` 触发"膨胀重组"表现（§3.1.2/§4.7）并套用该形态的移动参数。规则（GDD §4，机制改扫描）：**同一时刻仅模仿一个目标**；扫描新目标即替换当前蓝图；可主动还原回基态。目标固定分布在场景中，保留 GDD 的"能力可达性"空间约束（须移动到目标处才能扫描）。

#### 3.1.5 核心类与接口

```csharp
// 一个“可模仿形态”= 一种能力策略（扫描遥控器→遥控形态…）
public interface IMimicForm {
    FormId Id { get; }
    MovementProfile Movement { get; }       // 该形态的移动玩法参数
    void OnMaterialize(PlayerContext ctx);  // 膨胀重组完成、参数生效
    void OnDissolve(PlayerContext ctx);     // 还原回基态
    void OnUse(PlayerContext ctx);          // “使用能力”输入触发
    void Tick(PlayerContext ctx, float dt); // 持续效果（按需）
}

public abstract class MimicFormBase : IMimicForm {
    protected S_AbilityConfig Config;       // 数据驱动（含 MovementProfile）
    public MovementProfile Movement => Config.Movement;
    // 通用冷却/计时/数值修正，子类只写差异
}

// 扫描得到的形态蓝图——同一时刻只缓存一个
public struct Schematic { public FormId Form; /* 扫描来源等 */ }

// 持有当前模仿形态，驱动 扫描/重组/还原，转发输入
public class AbilitySystem : MonoBehaviour {
    IMimicForm _current;                     // null = 基态
    Schematic?  _cached;                     // 当前缓存的唯一蓝图
    public void Scan(ScanTarget target);     // 扫描 → 缓存蓝图 → 膨胀重组
    public void RevertToCore();              // 还原基态
    public void UseAbility();                // 绑定输入
}

// 把玩家可被形态操作的部件聚合给形态使用（避免直接找组件）
public class PlayerContext { /* Motor, Rigidbody, Transform, Events… */ }

// 移动玩法参数：随形态切换，实现“形态改变手感”
[System.Serializable]
public struct MovementProfile {
    public float MoveSpeedMul;   // 移动速度倍率（船锚 <1）
    public float MassMul;        // 质量倍率（船锚 >1，影响下沉）
    public float JumpHeightMul;  // 跳跃高度倍率（船锚 <1）
    public float Buoyancy;       // 浮力（气球 >0 可飘，船锚 <0 下沉）
    public float Drag;           // 阻力
    public bool  CanJump;        // 船锚可设 false
}
```

**场景目标**：`ScanTarget`（替代原 `SwallowPoint`）固定分布场景中，携带 `FormId`；玩家朝它执行 `Interact`（扫描）即缓存蓝图并重组。GDD 的"物品固定位置 = 能力可达性约束"在此体现为"扫描目标固定位置"。

#### 3.1.6 数据（ScriptableObject）
`S_AbilityConfig`：`formId`、**`MovementProfile`（移动玩法参数，本次新增重点）**、冷却、持续时长、能力专属参数、变身视觉引用（基态→该形态的膨胀重组表现 / 材质 / 板片）、能力图标（HUD 用）。**新增形态 / 调平衡只改资产，不改代码**。

#### 3.1.7 三个核心模仿形态（GDD §5 核心三）
| 扫描目标 | 形态 / 能力关键实现 | 移动玩法变化（MovementProfile） | 与其它系统接口 |
|----------|---------------------|--------------------------------|----------------|
| **遥控器** | 进入"操控模式"：把输入转发给选中的 `IMechanism`（吊臂/闸门/灯塔） | 操控时静止（GDD：操控期间不可移动） | 机关系统（§4.2）、威胁（关/转灯塔） |
| **船锚** | 增大质量、负浮力，可沉入水体 | 速度↓、质量↑、跳跃↓或禁跳、`Buoyancy<0` 下沉 | 水体触发、重量机关（§4.2） |
| **传送标记** | `Place()` 存标记 Transform；`UseAbility`=瞬移回标记 + 冷却（UniTask） | 基本不变，瞬移=位移能力 | 单向断点（§4.3） |

#### 3.1.8 输入映射（复用 `InputSystem_Actions` 的 Player Map）
| GDD 行为 | 现有 Action | 备注 |
|----------|-------------|------|
| 移动 | `Move` | 直接用（受当前 `MovementProfile` 调制） |
| 扫描 / 互动 / 读线索 | `Interact` | 复用为"扫描目标 / 交互" |
| 使用能力 | `Attack` | 复用为"使用当前形态能力"（后续可重命名 `UseAbility`） |
| 躲入掩体 | `Crouch` | 贴靠 / 钻入掩体 |
| 还原基态 | （新增）`Revert` 或长按 `Interact` | 待定 |

> `Jump/Sprint/Previous/Next/Look` 暂保留；2.5D 下 `Look` 多由 Cinemachine 接管。

#### 3.1.9 事件契约
- 发出：`TargetScanned{formId}`、`FormMaterialized{formId}`、`FormDissolved{formId}`、`AbilityUsed{formId}`。
- 订阅：HUD（更新形态图标）、表现层（膨胀重组 / 解体 VFX）、音频（扫描 / 重组 / 使用音）。

#### 3.1.10 扩展点
新增形态 = 新建 `XxxForm : MimicFormBase` + 一个含 `MovementProfile` 的 `S_AbilityConfig` 资产 + 在场景放 `ScanTarget`。无需改 `AbilitySystem`。段落形态（灯泡 / 磁铁 / 气球）按此在 M3 接入。

---

### 3.2 威胁—潜行系统（扫描 / 即死判定 / 清扫者）

#### 3.2.1 职责
提供"被光照到=即死"的环境压力（GDD §6）。三类威胁源：固定扫描装置、清扫者、可选巡逻者。统一抽象、数据驱动周期。

#### 3.2.2 威胁抽象与判定

```csharp
public interface IThreatSource {
    ThreatId Id { get; }
    bool IsLethalAt(Vector3 worldPos); // 当前帧该点是否致命
}
```

- **扫描体 `ScanField`**：用 Trigger（光锥/激光栅/摄像头视锥）表示危险区。玩家在区内时，做一次**遮挡 Raycast**（玩家↔扫描源），无遮挡才判定"被照到"——既符合"掩体可挡光"，又避免穿墙误杀。
- **即死判定**：`ScanField.OnTriggerStay` → 若 `IsLethalAt(player)` 为真且当前未被掩体/能力豁免 → 发 `PlayerKilled`。Layer：`Player` / `Cover`(遮挡) / `ScanField`，用 `LayerMask` 控制 Raycast 与 Trigger 交互，避免逐帧全场扫描，保证性能。
- **豁免**：船锚下潜（水下层）、磁铁掩体（动态遮挡体）、气球高处死角——都通过"遮挡/层切换"而非特例代码实现，保持统一。

#### 3.2.3 扫描周期（数据驱动）
`S_ScanPattern`：旋转速度/角度范围、激光横扫节奏、明灭周期、相位偏移。运行时用 **UniTask** 或 `AnimationCurve` 驱动，使玩家可"观察周期、卡间隙穿过"（GDD §6 节奏规避）。

#### 3.2.4 清扫者（Sweeper）—— Demo 高潮
- **行为 FSM**：`Approach(远处逼近) → Scan(进入并扫描) → Leave(离开)` 循环（GDD §6）。
- **路径与节奏**：用 **Timeline** 编排掠过轨迹与扫描节拍，关键节点广播事件给玩家压力点。
- **扫描**：清扫者携带一个大型 `ScanField`，复用 §3.2.2 判定。
- **听觉雷达**：`Approach` 阶段触发低频逼近音（GDD §11），由音频系统（§4.5）响应事件播放。

#### 3.2.5 可选巡逻者（M2，可裁）
`Sensor`（视野/感知）+ **巡逻-警觉 FSM**（`Patrol → Alert → Search`）+ NavMesh 移动。GDD §6：面朝红光=即死（红光是 `ScanField`），感知范围更大（仅触发警觉/转向）。

#### 3.2.6 事件契约
- 发出：`PlayerSpotted{source}`、`PlayerKilled{source, position}`、`SweeperPhaseChanged{phase}`。
- 订阅：检查点系统（触发重生）、镜头（被发现冲击）、音频（刺耳反馈/低频）、表现层（屏幕冲击）。

#### 3.2.7 扩展点
新增威胁类型实现 `IThreatSource` + `S_ScanPattern` 资产即可接入即死判定与事件流；无需改判定核心。

---

### 3.3 检查点—进度系统

#### 3.3.1 职责
分布式隐式检查点 + 死亡→重生流程 + 关卡分段进度（GDD §6 检查点、§8 流程、§12 Sections/`S_LevelConfig`）。

#### 3.3.2 检查点
- **`CheckpointVolume`**：场景中的隐式触发体，玩家进入即把它登记为"最近检查点"（记录重生 Transform + 必要的可恢复状态：当前段落、已开启的不可逆机关等）。
- **`CheckpointSystem`**：持有当前激活检查点；提供 `GetRespawn()`。多个检查点按进入顺序覆盖（GDD：回到最近一处）。

#### 3.3.3 死亡 → 重生流程（订阅 `PlayerKilled`）
```
PlayerKilled
  → GameState=Respawning, InputLock+1
  → 屏幕淡出（UniTask, 表现层/HUD）
  → 玩家移回 CheckpointSystem.GetRespawn()，重置形态/速度/能力为该点状态
  → 复位该段落可重置元素（扫描相位、可重置机关）
  → 屏幕淡入
  → InputLock-1, GameState=Playing
  → 广播 OnRespawn
```
全流程用 UniTask 串联，可被取消（如连续死亡）。

#### 3.3.4 关卡分段与配置
- **Sections**：关卡切成 A–H 段（GDD §8），每段含其检查点、威胁、机关、谜题引用。
- **`S_LevelConfig`**：关卡级配置（段落列表、起始检查点、各段参数引用）。便于调试时从任意段开始（开发用）。

#### 3.3.5 进度持久化
Demo 期：**内存级**（运行时进度，重启从头）。预留 `IProgressStore` 接口，远期接本地存档（§4.8），当前不实现。

#### 3.3.6 事件契约
- 发出：`CheckpointReached{id}`、`OnRespawn`、`SectionEntered{section}`。
- 订阅：音频（轻提示）、表现层（淡入淡出）、关卡流程（推进）。

#### 3.3.7 扩展点
"可重置元素"实现统一的 `IResettable.ResetToCheckpoint()`，重生时由系统批量调用；新机关/威胁只要实现该接口即纳入重生复位，无需改重生流程。

---

## 4. 其余系统概览 + 后续深化计划（第二批）

> 以下系统本版只给**职责 / 推荐方案 / 关键接口占位 / 里程碑 / 与核心系统衔接点 / 深化时要回答的问题**，留待各自里程碑深化。

### 4.1 镜头系统（M1 基础 → M2 深化）
- **职责**：电影化固定/轨道机位，按场景切换，保证威胁段"看清扫描光与掩体"的可读性（GDD §4）。
- **方案**：**Cinemachine 3.x**。每个机位 = 一个 `CinemachineCamera`；用 `CinemachineCameraTrigger`/优先级切换；被发现时叠加冲击（Impulse）。镜头听 `SweeperPhaseChanged`/`PlayerSpotted`。
- **接口占位**：`CameraDirector.SwitchTo(cameraId)`、`Shake(intensity)`。
- **衔接**：威胁系统（被发现冲击）、关卡分段（进段切机位）。
- **深化要回答**：固定机位 vs 轨道跟随的边界？2.5D 纵深通道如何换镜不晕？

### 4.2 机关 / 交互系统（M2 → M3）
- **职责**：可交互物与机关：水闸阀、吊臂/传送带、重量板、供电点/光敏开关、单向门、出口门（GDD §7、§9）。
- **方案**：`IInteractable`（玩家可交互）与 `IMechanism`（可被能力/信号驱动，含 `Activate/Deactivate/SetSignal`）。机关状态走事件，可被遥控器/磁铁/电池驱动。
- **接口占位**：`IInteractable.Interact(PlayerContext)`、`IMechanism.SetSignal(float)`、`IResettable`（重生复位）。
- **衔接**：能力系统（遥控/磁铁/电池操控）、谜题编排、检查点（复位）。
- **深化要回答**：机关与能力的耦合用直连还是信号总线？哪些机关不可逆（不参与复位）？

### 4.3 谜题编排（M2）
- **职责**：把"机关状态组合 = 解谜成功"编排出来（GDD §7 三个核心谜题：水闸/吊臂搭桥/断点错位）。
- **方案**：`PuzzleController` 监听若干 `IMechanism` 状态，满足条件广播 `PuzzleSolved` → 开门/开路。数据化解谜条件。
- **衔接**：机关系统、关卡流程、检查点。
- **深化要回答**：解谜条件用可视化连线（SO）还是脚本？失败可回退吗？

### 4.4 UI / HUD（M2 → M3）
- **职责**：极简近零 HUD（GDD §10）：当前能力图标、交互提示高亮、死亡淡入淡出。
- **方案**：**uGUI**。`HudController` 订阅 `AbilityEquipped`（换图标，取自 `S_AbilityConfig`）、`Interactable` 进入/离开（提示）、`PlayerKilled/OnRespawn`（全屏淡入淡出 `CanvasGroup`，UniTask 驱动）。
- **深化要回答**：能力状态是否完全靠变身剪影表达、HUD 是否进一步精简到只剩淡入淡出？

### 4.5 音频系统（M2 → M3）
- **职责**：环境音主导、强空间感；清扫者低频"听觉雷达"；被发现刺耳反馈（GDD §11）。
- **方案**：原生 **AudioMixer**（Ambient/SFX/Sting 分组）+ `AudioManager`（事件 → 播放/混音快照切换）。订阅 `SweeperPhaseChanged`（逼近低频）、`PlayerSpotted`（刺耳）、`AbilityUsed`。M3 评估是否换 **FMOD**（自适应混音、参数化氛围更强）。
- **深化要回答**：是否值得引入 FMOD？空间音用内置 spatializer 还是第三方？

### 4.6 关卡流程 / 场景管理（M1 → M2）
- **职责**：按 `S_LevelConfig` 加载分段、推进流程、开发期跳段。
- **方案**：单场景分段（Demo 体量）+ `LevelFlow` 状态推进；如需异步加载用 UniTask + Addressables（远期再评估，Demo 可不引）。
- **衔接**：检查点（段落）、镜头（进段切镜）。

### 4.7 表现层（VFX / 渲染）（M2 → M3）
- **职责**：纳米机器人基态质感（半流体内核 + 硬表面板片，§3.1.2）、**扫描 → 膨胀重组 → 解体还原**的变身表现、扫描光高亮、被发现屏幕冲击（GDD §11、§2）。
- **方案**：URP 后处理（Bloom/Vignette/Color Grading 做冷色调强光影）、Shader Graph（流体内核 + 板片冷光）、VFX Graph / 粒子（纳米板片膨胀增殖与重排）；表现只**订阅事件与状态**（`TargetScanned/FormMaterialized/FormDissolved`），不写规则。
- **深化要回答**：膨胀重组用 Blend Shape / 顶点动画 / 粒子蜂群 / 软体近似？基态→目标轮廓如何过渡（板片增殖 vs 形变）？扫描光用真实 Light 还是后处理体积？硬表面板片用真实网格还是法线/视差贴花？

### 4.8 存档 / 设置（远期）
- **职责**：画质/音量等设置；（远期）存档。
- **方案**：`IProgressStore` + `ISettingsStore`，Demo 仅做内存与基本设置，存档留接口不实现。

---

## 5. 里程碑落地路线（M1 / M2 / M3）

对齐 GDD §12。每个里程碑标注涉及系统、交付物、依赖。

| 里程碑 | 涉及系统 | 交付物 | 依赖 |
|--------|----------|--------|------|
| **M1** 技术验证闭环 | 基础设施(§2) + 扫描-模仿(基础, §3.1) + 威胁-潜行(1 个固定扫描, §3.2) + 检查点(§3.3) + 镜头(基础, §4.1) | 角色可移动；扫描模仿切换形态且移动手感随形态变；1 个固定扫描威胁可即死；死亡回最近检查点；A、B 段可玩 | Cinemachine、UniTask 安装到位；事件总线/输入锁/状态机就绪 |
| **M2** 核心三形态 + 高潮 | 形态(遥控/船锚/传送) + 机关/交互(§4.2) + 谜题编排(§4.3) + 清扫者(§3.2.4) + 镜头切换(§4.1) | C(水闸)、D(吊臂搭桥)、F(断点错位+清扫者) 段可玩 | M1 骨架；机关/谜题接口；Timeline 编排 |
| **M3** 段落形态 + 氛围 | 段落形态(灯泡/磁铁/气球) + UI(§4.4) + 音频(§4.5) + 表现层(膨胀重组 VFX, §4.7) + 结尾 Timeline | E、G、H 段；美术音频氛围；结尾镜头；完整单关 | M2 系统；表现/音频资源；（评估 FMOD） |

> 关键路径：**M1 骨架必须先稳**（事件契约、检查点重生、即死判定），后续能力/机关都挂在其上增量接入。

---

## 6. 目录结构与程序集划分建议

```
Assets/Scripts/
├── Core/                 # ManagerRoot, EventBus, InputLock, GameStateMachine, FSM, 工具
│   └── Inkform.Core.asmdef
├── Data/                 # S_AbilityConfig / S_ScanPattern / S_LevelConfig / S_GameEvent
│   └── Inkform.Data.asmdef        (依赖 Core)
├── Gameplay/
│   ├── Player/           # PlayerActor, PlayerMotor, PlayerContext, AbilitySystem
│   ├── Abilities/        # IMimicForm, MimicFormBase, Remote/Anchor/Teleport…
│   ├── Threats/          # IThreatSource, ScanField, Sweeper, Patroller
│   ├── Checkpoint/       # CheckpointVolume, CheckpointSystem, IResettable
│   └── Mechanisms/       # IInteractable, IMechanism, 各机关, PuzzleController
│       └── Inkform.Gameplay.asmdef  (依赖 Core, Data)
├── Camera/               # CameraDirector (Cinemachine 封装)
│   └── Inkform.Camera.asmdef        (依赖 Core)
├── UI/                   # HudController
│   └── Inkform.UI.asmdef            (依赖 Core, Data)
├── Audio/                # AudioManager
│   └── Inkform.Audio.asmdef         (依赖 Core)
└── Editor/               # 自定义 Inspector / 工具
    └── Inkform.Editor.asmdef        (依赖各运行时 asmdef)
```

**依赖方向**：`Core ← Data ← Gameplay/UI/Camera/Audio`，单向；`Core` 不依赖任何上层；系统间通过 `Core` 的事件总线通信，避免横向硬引用。

---

## 7. 风险与待定（TBD）

| 项 | 说明 | 处理 |
|----|------|------|
| 3D 化改造成本 | GDD §12 的 2D 架构思想需重写为 3D；本 Demo 即技术验证 | 以 M1 验证骨架 |
| 扫描可见性判定精度/性能 | Trigger + 遮挡 Raycast 的边界与帧开销 | 限定 LayerMask、按需判定、必要时降频 |
| 能力数值未定 | 速度/冷却/浮力/电量等（GDD §13） | 全部进 `S_AbilityConfig`，留策划调 |
| 是否引入巡逻者 | 可选威胁（GDD §6/§13） | 默认不做，接口预留（§3.2.5） |
| 音频中间件取舍 | 原生 vs FMOD | M3 再决策（§4.5） |
| 角色控制器手感 | Rigidbody 贴面/掩体钻入手感 | M1 早期专门调参验证 |

---

## 8. 附录

### 8.1 技术选型对照表
见 §1 主表。一句话：**Unity6+URP / Input System / Cinemachine / UniTask / 自写事件总线 + ScriptableObject / uGUI / AudioMixer / 3D 物理 / asmdef 分层**。

### 8.2 命名规范
- ScriptableObject：`S_` 前缀（`S_AbilityConfig`…）。
- 接口：`I` 前缀（`IMimicForm`/`IThreatSource`/`IInteractable`/`IMechanism`/`IResettable`）。
- 系统类：`*System` / `*Director` / `*Manager` / `*Controller` 按职责。
- 事件结构体：过去式名词（`PlayerKilled`/`AbilityEquipped`/`CheckpointReached`）。

### 8.3 对标实现要点
- **INSIDE / Somerville**：固定/轨道机位、近零 HUD、分布式隐式检查点、光影叙事 → 直接映射镜头(§4.1)、UI(§4.4)、检查点(§3.3)。
- **风之旅人 隧道龙 / Somerville 飞船**：清扫者周期掠过的庞然压迫 → 清扫者 FSM + Timeline(§3.2.4)。
- **星之卡比 探索发现（塞满嘴变形）**：变身的可爱笨拙感参考 → 现机制为**扫描后纳米机器人膨胀重组**（§3.1.2）+ 表现层(§4.7) + 各形态剪影差异(§3.1.4)。

---

> 本文档为 v0.1 设计提案，随 M1 实现迭代修订。实现时若与本设计冲突，以"事件契约稳定、核心系统解耦"为最高优先级。
