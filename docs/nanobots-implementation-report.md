# Nanobots 模块实现逻辑报告

> Inkform_3D 项目 · 分支 `feature/nanobot-possession`
> 文档生成日期：2026-06-26

---

## 目录

1. [模块概览](#1-模块概览)
2. [架构分层](#2-架构分层)
3. [核心接口与数据结构](#3-核心接口与数据结构)
4. [NanobotSwarm — 身体层](#4-nanobotswarm--身体层)
5. [SwarmFormations — 形态层](#5-swarmformations--形态层)
6. [NanobotTree — 预解算蔓延树](#6-nanobottree--预解算蔓延树)
7. [NanobotRenderer — 点粒渲染层](#7-nanobotrenderer--点粒渲染层)
8. [NanobotTubeRenderer — 方管渲染层](#8-nanobottuberenderer--方管渲染层)
9. [PossessionDirector — 编排层](#9-possessiondirector--编排层)
10. [NanobotSandboxBuilder — 编辑器沙盒](#10-nanobotsandboxbuilder--编辑器沙盒)
11. [Shader 实现](#11-shader-实现)
12. [设计铁律与约束](#12-设计铁律与约束)
13. [文件清单](#13-文件清单)

---

## 1. 模块概览

Nanobots 模块实现了 Inkform 3D 中纳米机器人附身系统的完整管线：从玩家扫描目标、蜂群蔓延生长、方管金属管网渲染，到附身驾驶物体、脱离回收的全生命周期。

核心玩法：玩家控制一团由纳米机器人组成的液态生物，可以扫描环境中的物体并"附身"——蜂群从玩家身体出发，沿地面爬行到目标物体正下方，然后沿物体表面递归分叉蔓延、包裹整个物体，最终控制该物体移动。

**设计哲学**：纯逻辑与渲染分离、确定性伪随机、无 GC 运行时分配、几何即谜题规则。

---

## 2. 架构分层

```
┌─────────────────────────────────────────────────┐
│  编排层 PossessionDirector                       │
│  (状态机 + 玩家控制 + 输入 + 生命周期)              │
├─────────────────────────────────────────────────┤
│  形态层 SwarmFormations                          │
│  (ISwarmFormation 接口 + 各形态实现)               │
├──────────────┬──────────────────────────────────┤
│  身体层       │  渲染层                           │
│  NanobotSwarm │  NanobotRenderer (点粒)           │
│  (运动积分)    │  NanobotTubeRenderer (方管)       │
├──────────────┴──────────────────────────────────┤
│  几何层 NanobotTree + BotPath                     │
│  (预解算蔓延树 + 弧长采样)                         │
├─────────────────────────────────────────────────┤
│  工具层 Hash + PolylinePath                       │
│  (确定性伪随机 + 折线路径)                         │
└─────────────────────────────────────────────────┘
```

**依赖关系**（asmdef）：
- `Inkform.Nanobots` → `Inkform.Core`, `Inkform.Gameplay`, `Inkform.Data`, `Unity.InputSystem`

---

## 3. 核心接口与数据结构

### 3.1 ISwarmFormation 接口

```csharp
public interface ISwarmFormation
{
    Vector3 SampleTarget(int i, int count, float t);
    bool IsComplete(float t);
}
```

- `SampleTarget`：返回第 `i` 个 bot（共 `count` 个）在进度 `t`（0→1）时的世界目标点
- `IsComplete`：形态是否到达终态（idle 类常驻形态恒 `false`）

所有形态实现此接口，NanobotSwarm 只负责平滑追到目标点，不关心形态语义。

### 3.2 PolylinePath — 折线路径

按弧长参数化的折线采样器：
- 构造时缓存累计弧长 `_cumLen[]`
- `PointAt01(s)`：归一弧长 `s∈[0,1]` 处的世界点（线性插值）
- `TangentAt01(s)`：归一弧长处的前进切线方向

### 3.3 Hash — 确定性伪随机

避免使用 `UnityEngine.Random`（会导致稳定形态每帧跳变），全部基于整数哈希：

- `Unit(i, salt)` → `[0,1)` 确定性浮点
- `Direction(i)` → 球面上确定性方向
- `ValueNoise(p, salt)` → 格点哈希 + 三线性插值的 3D 值噪声
- `Fbm3(p, octaves)` → 多倍频分形噪声向量位移 `[-1,1]³`

---

## 4. NanobotSwarm — 身体层

**文件**：`Assets/Scripts/Nanobots/NanobotSwarm.cs` (79 行)

运动积分器：每帧用 `Vector3.SmoothDamp` 把每个 bot 追到当前形态的目标点。

### 核心逻辑

```
Update() {
    _t += _speed * dt;  // 推进形态进度
    for each bot i:
        target = _formation.SampleTarget(i, Count, _t);
        _current[i] = SmoothDamp(_current[i], target, smoothTime, maxSpeed);
    Centroid = average(_current[]);
    OnPositionsUpdated?.Invoke(_current[]);
}
```

### 关键设计

- **手感旋钮**：`SmoothTime`（小=机械硬、大=液态黏）统一控制所有形态质感
- **事件驱动渲染**：`OnPositionsUpdated` 事件让渲染层订阅，身体层不知道渲染存在
- **形态切换**：`SetFormation(formation, speed)` 重置进度到 0，开始新形态

### 属性

| 属性 | 说明 |
|------|------|
| `Count` | bot 数量（验证阶段 256，上千需改 Jobs+Burst） |
| `SmoothTime` | SmoothDamp 平滑时间 |
| `MaxSpeed` | 单 bot 最大速度 (m/s) |
| `Centroid` | 当前质心位置 |
| `Progress` | 形态进度 0→1 |
| `FormationComplete` | 形态是否完成 |

---

## 5. SwarmFormations — 形态层

**文件**：`Assets/Scripts/Nanobots/SwarmFormations.cs` (402 行)

### 5.1 BlobFormation — 呼吸团（idle 游荡态）

绕 `center`（玩家位置）的呼吸团，常驻跟随玩家。

- 方向：`Hash.Direction(i)` 确定性球面方向
- 半径：确定性基底 + 低频 Perlin 噪声时间扰动 → 缓慢起伏
- `IsComplete` 恒 `false`（常驻形态）

### 5.2 PathFlowFormation — 蔓延+立起形态

bot 沿折线 `A→F→P` 流动，途中分裂成多条支流（河流三角洲），接近目标处收束汇合。

**核心机制**：
- **滞后拉散**：每个 bot 有确定性 `lag = Hash.Unit(i,5) * trail`，队伍在路上拉成一条
- **支流分叉**：`i % branchCount` 分配支流，沿路径横向（地面平面）拉开，`sin(π·s)` 包络让两端收束
- **分形抖动**：确定性相位 + 时间漂移的 fBm 噪声
- **收敛铁律**：所有横向/分形偏移在 `s→1` 处归零 → `t=1` 全员汇聚到终点 P

### 5.3 SurfaceWrapFormation — 包裹形态

bot 散布到目标表面采样点。`i % points.Length` 映射，密度随点数变化。

### 5.4 WrapFollowFormation — 附身稳态形态

bot 贴在**移动中的**附身物体表面：
- 表面点以物体**局部空间**存储，每帧 `obj.TransformPoint` 还原 → 随驾驶移动
- 叠加极小确定性 idle 摆动让蜂群"活"着
- `IsComplete` 恒 `false`（常驻形态）
- 局部点用蔓延末态的同一批表面点反变换 → 附身瞬间与蔓延无缝衔接

### 5.5 TreeBranchFormation — 树状分形分支形态

bot 沿预解算的 `NanobotTree` 生长，是蔓延管线的核心形态。

**生长前沿机制**：
```
front = t * (MaxLen + maxLag)    // 统一物理推进弧长
arc = Clamp(front - lag, 0, path.Length)  // 每个 bot 的弧长位置
path.Sample(arc) → pos, height, tangent
```

**分支体积**：沿切线法平面用确定性方向散开，`envelope` 包络在两端归零
**分形抖动**：随离面高度 `airT` 衰减 → 表面段几乎不抖（蔓延非缠绕）
**反向模式**：`reverse=true` 时从叶收敛回起点（脱离聚合成团）

---

## 6. NanobotTree — 预解算蔓延树

**文件**：`Assets/Scripts/Nanobots/NanobotTree.cs` (367 行)

### 6.1 设计理念

预解算的纳米机器人"生长树"：触手伸出前一次性算好的树状分形结构。

```
起点 A → 共享树干(trunk) → 递归二分分叉 → 表面均匀叶子末梢
```

**设计铁律**：
1. **只发散不汇聚**：每条 root→叶 路径一旦分开，绝不再共享同一点
2. **不穿模**：递归沿最长轴按中位数二分 → 兄弟子树占据不相交的空间半区
3. **纯逻辑、确定性、无 Unity 运行时依赖** → 可 EditMode 单测

### 6.2 Build() 构建流程

```csharp
NanobotTree.Build(
    start,           // 起点
    leafPoints,      // 表面均匀采样点（叶子末梢）
    surfaceCenter,   // 物体中心
    airBranchDepth,  // 空中分叉深度（0=全贴面）
    outwardLift,     // 抬出高度基准
    surfaceSubdiv,   // 表面段细分
    projectToSurface,// 表面投影委托
    trunkPath        // 共享主干折线（贴地爬行段）
)
```

1. **主干折线**：`trunkPath`（蜂群→接触点 P 的贴地折线），每条 bot 路径都以此开头
2. **递归二分**：`BuildNode()` 对 `indices[lo,hi)` 子集沿最长轴中位二分
   - 分叉点 = 子集 medoid（离质心最近的表面点）
   - 浅层向外抬出（空中分支），深层 `lift=0`（贴面）
3. **DFS 收集**：`Collect()` 把 root→叶 的位置/高度栈固化成 `BotPath`
4. **表面细分投影**：`BuildPath()` 对两端都贴面的段细分并投影回表面

### 6.3 BotPath — bot 路径

```csharp
public sealed class BotPath
{
    Vector3[] Points;  // 路径点
    float[] Cum;       // 累计弧长
    float[] Height;    // 每点离面高度（表面段=0）
    float Length;      // 总弧长
    
    void Sample(float dist, out Vector3 pos, out float height, out Vector3 tangent);
}
```

`Sample()` 按绝对弧长采样位置、离面高度、前进切线（二分查找 + 线性插值）。

### 6.4 GetBranchPolylines() — 分支抽取

把树拆成**不重叠**的分支中心线，供方管渲染：
- 每条 = 父节点→子节点 的一条边
- 表面边按 `surfaceSubdiv` 细分投影 → 贴面不穿模
- 输出每条的**根弧长偏移** `startArc`（该边起点离总根 A 的累计弧长）
- 第一条永远是共享主干折线 + P→root，`startArc=0`

---

## 7. NanobotRenderer — 点粒渲染层

**文件**：`Assets/Scripts/Nanobots/NanobotRenderer.cs` (128 行)

用 `DrawMeshInstanced` 把每个 bot 画成沿运动方向拉丝的金属丝条。

### 核心机制

- **速度派生**：位置差分自派生 `vel = (pos - prev) / dt`，不改 Swarm API
- **拉丝效果**：速度 > `MinSpeedForStretch` 时，沿速度方向拉长 `lenMul = 1 + StretchAmount * speed01`
- **静止退化**：低速时退化为球（避免抖）
- **大小抖动**：`_sizeMul[i] = 1 + (Hash.Unit(i,20) - 0.5) * 2 * SizeJitter`
- **分批绘制**：单批上限 1023，超出自动分批

### 属性

| 属性 | 说明 |
|------|------|
| `BotRadius` | 单个 bot 基础半径 (m) |
| `SizeJitter` | 大小随机抖动 (0=全一致, 0.3=±30%) |
| `StretchAmount` | 最大拉长倍数 |
| `StretchSpeedRef` | 满拉长所需速度 (m/s) |
| `MinSpeedForStretch` | 静止阈值 |

---

## 8. NanobotTubeRenderer — 方管渲染层

**文件**：`Assets/Scripts/Nanobots/NanobotTubeRenderer.cs` (342 行)

把各支流中心线挤成方截面金属管，按生长进度向前延伸，管头发光做生长前沿。替代点粒渲染。

### 8.1 两种模式

1. **普通模式**（`SetBranches(centerlines, radiusMul, grows)`）：
   - 每条按自身比例生长，`grows[i]=false` 的条恒满渲染
2. **前沿模式**（`SetBranches(centerlines, radiusMul, startArcs)`）：
   - 全局生长前沿 `front = Growth * maxTotalArc`
   - 每条只画 `[drainFront, front]` 这一段 → 主干先长、子枝按深度依次冒头

### 8.2 方管挤出 AppendTube()

沿中心线挤一根方管，渲染弧长区间 `[fromFrac, toFrac]·总长`：

1. **弧长采样**：按 `RingsPerUnit` 采样环，每环 4 顶点（方截面）
2. **平行传输**：`up` 向量沿切线投影避免截面突变扭转
3. **分形扭动**：`Hash.Fbm3()` 在法平面小幅偏移
4. **硬表面分节**：
   - `SegmentsPerUnit` 控制节缝密度
   - `SegmentTwist` 每节绕切线拧接旋转（交替方向更"机械"）
   - `SegmentRidge` 节缝处粗细跳变 → 一节节台阶
5. **管头变细**：`TipThickness` 控制末端收细
6. **管头发光**：顶点色 `a=1` 标记生长前沿（shader 中用）

### 8.3 排空机制 Drain

`Drain` 从根排空进度：`drainFront = Drain * maxTotalArc`，每条只画 `[drainFront, startArc+len]` → 离根近的 trunk 段先被吃掉。

---

## 9. PossessionDirector — 编排层

**文件**：`Assets/Scripts/Nanobots/PossessionDirector.cs` (550 行)

纳米机器人生命周期状态机 + 玩家主控角色。三状态：游荡 ↔ 延伸 ↔ 附身。

### 9.1 状态机

```
┌─────────┐   扫描+确认   ┌─────────┐   生长完成   ┌─────────┐
│  Wander  │ ──────────▶ │  Extend  │ ──────────▶ │ Possess │
│  游荡    │             │  延伸    │             │  附身    │
└─────────┘             └─────────┘             └─────────┘
     ▲                       │                       │
     │       脱离完成         │                       │
     └───────────────────────┴───────────────────────┘
```

### 9.2 游荡态 Wander

- 蜂群成团跟随玩家（`BlobFormation`）
- 玩家可 WASD 移动、不可跳
- Shift 扫描范围内可附身物体并高亮

### 9.3 延伸态 Extend（核心流程）

`ExtendToPossessRoutine()` 协程：

```
① 预解算蔓延树
   - BuildGroundCrawl(): 贴地主干折线（蜂群→foot→contact）
   - target.GetEvenSurfaceSamples(): 表面均匀采样叶子
   - NanobotTree.Build(): 全程贴面、不离地

② 驱动蜂群沿树生长
   - TreeBranchFormation(tree, ...)
   - PathSpeed 控制推进速度

③ 驱动方管
   - tree.GetBranchPolylines() → 不重叠分支中心线
   - Tubes.SetBranches(branches, radii, startArcs)

④ 表面上色辅助
   - target.BeginWrapShader(contact)

⑤ 等待生长完成
   - while (!Swarm.FormationComplete): Tubes.Growth = Swarm.Progress

⑥ 控制转移
   - target.OnPossessed()
   - AttachControlTo(target): 玩家贴合到物体，蜂群转 WrapFollow

⑦ 空中主干流空
   - DrainTrunkRoutine(): Tubes.Drain 0→1，收净后清管
```

### 9.4 附身态 Possess

- 控制体贴合到物体，物体 parent 到玩家（随驾驶移动）
- 物体碰撞关闭，玩家视觉体隐藏
- 蜂群转 `WrapFollowFormation` 贴附物体表面
- 可跳（`PossessProfile`）

### 9.5 脱离态 Detach

`DetachRoutine()` 协程：

```
① 预解算「收束树」（同蔓延树，反向播放）
② 蜂群反向收束：TreeBranchFormation(reverse=true)
③ 方管同步退潮：Growth = 1 - Progress
④ 落地成团：转 BlobFormation，玩家传送到地面点
```

### 9.6 几何即谜题规则

`ResolveFootAndContact()` 的 raycast 失败 = 不可附身：
- F：目标中心朝下命中地面 → 失败 = 悬空
- P：从 F 朝上命中目标表面 → 失败 = 被遮挡
- 命中的必须是目标本身（不是别的可附身物挡在前面）

### 9.7 贴地主干生成

`BuildGroundCrawl()`：
- 从蜂群质心到 foot 按 `GroundSamplesPerUnit` 细分
- 每个中间点朝下 raycast 贴地面（+ `GroundClearance` 离地高度）
- 失败退化线性插值
- 末点接 contact（物体表面接触点）

---

## 10. NanobotSandboxBuilder — 编辑器沙盒

**文件**：`Assets/Scripts/Editor/NanobotSandboxBuilder.cs` (335 行)

菜单 `Inkform/Sandbox/Build Nanobot Sandbox` 程序化搭建验证沙盒。

### 沙盒内容

- 地面（40×24m）
- 玩家（WASD 移动、Space 跳、CapsuleCollider + Rigidbody）
- 4 个可附身物体：
  - `Crate_OK`：落地立方体（正常）
  - `Sphere_OK`：落地球体（验证非立方体表面采样）
  - `Crate_OnLedge`：架在台子上的立方体（演示竖直蔓延）
  - `Crate_NoGround`：悬空（不可附身反例）
- NanobotSwarm 根（身体 + 方管渲染 + 编排）
- 反射探针（高金属度需要环境反射）
- Cinemachine 跟随相机

---

## 11. Shader 实现

### 11.1 NanobotFlow — 液金属洪流

**文件**：`Assets/Shaders/NanobotFlow.shader` (205 行)

配合 `NanobotRenderer` / `NanobotTubeRenderer`，给方管/丝条高金属镜面效果。

**视觉层次**：
1. **头亮尾暗渐变**：`lerp(_TailColor, _HeadColor, axis01)` 沿丝条长轴
2. **流动扫线**：`frac(axis01 * _ScanFreq - _Time.y * _ScanSpeed)` 窄亮带，像电流在丝里淌
3. **硬表面面板线**：
   - 节缝：`segPhase` 接近 0/1 处亮
   - 沿管细缝：密度 `_PanelLengthwise`
   - 绕截面棱：4 条边界处亮 + 密度 `_PanelAround`
   - 缝处更暗哑（金属分块感）
4. **生长前沿**：顶点色 `a=1` 标记管头 → `_TipColor * tip * _TipGlow`
5. **表面微噪**：`vnoise(positionWS * 8)` 避免塑料感

**技术要点**：
- URP ForwardLit + GPU Instancing
- ShadowCaster + DepthOnly 复用 URP/Lit 的 pass
- 3D 值噪声（无纹理依赖）

### 11.2 NanobotWrap — 包裹 shader

**文件**：`Assets/Shaders/NanobotWrap.shader` (163 行)

在物体表面建"到入射点 P 的距离场"，阈值 `_Grow` 0→1 往外扫。

**三态渲染**：
1. **已包裹**（`d < grow`）：`_PossessedColor`，更金属
2. **生长前沿**（`d ≈ grow`）：`_EdgeColor * 3.0` 发光
3. **原表面**（`d > grow`）：`_BaseColor`

**距离场设计**：
```
d = (length(delta) + horiz * _HorizPenalty) / _MaxDist
```
- `horiz = length(delta.xz)`：水平距离
- `_HorizPenalty`：水平惩罚 → 阈值面优先竖着爬（从底往上裹）
- 叠 3D 值噪声打碎前沿 → 不规则细胞状而非光滑球面

---

## 12. 设计铁律与约束

### 12.1 树只发散不汇聚

每条 root→叶 路径一旦在分叉点与兄弟分开，之后绝不再共享同一点。横向偏移/抖动包络在末梢归零 → 终点精确落在均匀叶子上。

### 12.2 不穿模

递归沿最长轴按中位数二分 → 兄弟子树占据不相交的空间半区。表面段细分投影回最近表面点。

### 12.3 收敛性铁律

PathFlowFormation / TreeBranchFormation 中，所有横向/分形偏移在 `s→1` 处归零 → `t=1` 全员汇聚到终点。

### 12.4 确定性伪随机

同一 `i` 永远得到同一组值，避免 idle 抖动。切勿用 `UnityEngine.Random` 替代。

### 12.5 纯逻辑与渲染分离

NanobotTree 纯逻辑、无 Unity 运行时依赖 → 可 EditMode 单测。渲染层订阅 `OnPositionsUpdated` 自行绘制。

### 12.6 几何即谜题规则

`ResolveFootAndContact` 的任一 raycast 失败 = 不可附身（悬空/底下没地/被遮挡），不另写可附身判定。

---

## 13. 文件清单

| 文件 | 行数 | 说明 |
|------|------|------|
| `Assets/Scripts/Nanobots/NanobotSwarm.cs` | 79 | 身体层：运动积分器 |
| `Assets/Scripts/Nanobots/SwarmFormations.cs` | 402 | 形态层：ISwarmFormation + 5 种形态 + Hash + PolylinePath |
| `Assets/Scripts/Nanobots/NanobotTree.cs` | 367 | 几何层：预解算蔓延树 + BotPath |
| `Assets/Scripts/Nanobots/NanobotRenderer.cs` | 128 | 渲染层：点粒拉丝 DrawMeshInstanced |
| `Assets/Scripts/Nanobots/NanobotTubeRenderer.cs` | 342 | 渲染层：方管金属管网 |
| `Assets/Scripts/Nanobots/PossessionDirector.cs` | 550 | 编排层：状态机 + 玩家控制 |
| `Assets/Scripts/Editor/NanobotSandboxBuilder.cs` | 335 | 编辑器：程序化沙盒搭建 |
| `Assets/Shaders/NanobotFlow.shader` | 205 | Shader：液金属洪流 |
| `Assets/Shaders/NanobotWrap.shader` | 163 | Shader：包裹生长前沿 |
| `Assets/Scripts/Nanobots/Inkform.Nanobots.asmdef` | 19 | 程序集定义 |

**总计**：约 2,590 行代码（C# + HLSL）
