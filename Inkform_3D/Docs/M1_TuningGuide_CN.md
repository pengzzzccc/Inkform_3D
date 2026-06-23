# Inkform M1 — 调参与使用指南

> 面向 `M1_TestScene`。改完参数后：编辑器里的值会**实时生效**（部分需进入 Play）。
> 改了 `M1SceneBuilder` 才需要重跑菜单 `Inkform/M1/Build M1 Test Scene` 重建场景；**只调下面这些组件参数则不必重建**。

---

## 1. 相机（`CameraDirector` 物体）

在 Hierarchy 选中 **`CameraDirector`**，Inspector 直接调：

| 参数 | 含义 | 调法 |
|------|------|------|
| **FollowOffset** | 机位相对玩家的偏移 | 拉远：z 更负(如 -16)；抬高：y 更大；侧移：改 x |
| **FieldOfView** | 视野角度(20–90) | 越大越广角、画面更"远"；侧卷轴常用 40–55 |

更细的镜头手感在 **`CM Player Cam`** 上：
- `CinemachineFollow` → **FollowOffset**（同上）、**TrackerSettings → Position Damping**（跟随阻尼，越大越"拖"、越平滑）。
- `CinemachineCamera` → **Lens → Field Of View / Near-Far Clip**。
- 想要"固定机位"而非跟随：禁用 `CinemachineFollow` 组件即可。

> `CameraDirector` 上改 FollowOffset/FieldOfView 会覆盖写回 vcam，方便你不进 vcam 也能调。

---

## 2. 扫描威胁 / 探照灯（`ScanField_KillZone` 物体）

选中 **`ScanField_KillZone`**（挂 `ScanField`）：

| 参数 | 含义 | 备注 |
|------|------|------|
| **LightOrigin** | 遮挡判定的"光源点"+ 旋转枢轴 | 默认指向 `ScanLight_Origin`(45,8,0) |
| **RotateSpeed** | 探照灯旋转速度(度/秒) | M1 默认 0(静止)；设 30~60 让它转 |
| **CoverMask** | 哪些层算"掩体"（遮挡=豁免） | 默认 `Cover` 层 |
| **Source** | 死亡来源名（显示在 HUD） | — |
| **Spotlight** | 关联的聚光灯 | 已连到 `Spot` |
| **LightColor / LightIntensity / LightRange** | 灯的颜色/强度/照射距离 | 在此一处即可调灯，不必进 Light 组件 |

- **致死范围**：由 `ScanField_KillZone` 的 **BoxCollider(Is Trigger) Size** 决定（默认覆盖 x:38~52）。
- **判定逻辑**：玩家在致死区内、且"光源点→玩家"之间无 Cover 遮挡 = 即死。所以**移动掩体 / 改 LightOrigin 高度**都会改变安全区。

### 红色危险视觉（`DangerStrip` / `DangerBeam`，挂 `DangerPulse`）
| 参数 | 含义 |
|------|------|
| **BaseColor** | 危险色（默认红） |
| **Speed** | 脉动快慢 |
| **MinAlpha / MaxAlpha** | 脉动透明度范围（越大越刺眼） |
> 这两个物体**只是视觉提示**，不参与判定；想让危险区更/不显眼就调它们。

---

## 3. 掩体棚（`CoverCanopy` 物体）
- 在 **`Cover`** 层，是遮挡来源。移动它的位置/大小即可改变"安全通道"。
- 想新增掩体：复制它、保持在 `Cover` 层即可被 `ScanField` 当作遮挡。

---

## 4. 玩家移动（`Player` 物体 → `PlayerMotor`）

| 参数 | 含义 |
|------|------|
| **BaseSpeed** | 基态移动速度 |
| **BaseJumpHeight** | 基态跳跃高度（决定能否跳过矮坎/台阶） |
| **BaseMass** | 基础质量 |
| **GroundCheckDistance** | 地面检测距离（胶囊半高≈1，默认 1.1） |
| **GroundMask** | 哪些层算地面（默认 Default） |

> 各形态在此基础上**乘以倍率**（见下）。基态值是所有形态的"底"。

---

## 5. 形态参数（数据资产 `Assets/Data/S_AnchorForm.asset` / `S_LightForm.asset`）

选中资产即可调（**数据驱动，改资产不改代码**）：

| 字段 | 含义 | 船锚默认 | 轻形态默认 |
|------|------|---------|-----------|
| **Movement.MoveSpeedMul** | 速度倍率 | 0.45 | 1.4 |
| **Movement.MassMul** | 质量倍率 | 3 | 0.5 |
| **Movement.JumpHeightMul** | 跳跃倍率 | 0.3 | 1.8 |
| **Movement.Buoyancy** | 浮力(>0浮/<0沉) | -8 | 0 |
| **Movement.Drag** | 阻尼 | 0.4 | 0 |
| **Movement.CanJump** | 能否跳 | true | true |
| **BodyColor** | 变身后身体颜色 | 橙 | 蓝 |
| **BodyScale** | 变身后体型缩放 | (1.3,0.6,1.3)矮胖 | (0.8,1.4,0.8)细高 |
| **DisplayName** | 提示里显示的名字 | 船锚 | 轻形态 |

> 新增形态：复制一个 `S_AbilityConfig` 资产、设好 `Form`/参数，放一个 `ScanTarget` 引用它即可（`AbilitySystem` 已支持 Anchor/Light，新枚举值需在 `AbilitySystem.Create` 加一条）。

---

## 6. 检查点 / 重生
- **`Checkpoint_0/1/2`**（`CheckpointVolume`）：`Id` 决定顺序；`RespawnPoint` 可指定重生位置（不填用自身）。
- 编辑器场景视图可见**蓝框+绿点**(Gizmos)；游戏内有**蓝光柱**，激活转绿(`CheckpointMarker`)。
- 重生淡入淡出时长：`CheckpointSystem` 的 **FadeDuration**。

---

## 7. UI
- **HudCanvas → Label**：左上调试信息。
- **PromptCanvas**：底部 `[E] 扫描…` 提示 + 中上 toast（`InteractionPromptUI.ToastDuration` 调 toast 停留）。
- **LevelCompleteCanvas → Panel**：通关面板（默认隐藏，到达 EXIT 显示）。

---

## 常用操作回顾
- A/D 移动、Space 跳、**单击 E** 在目标旁扫描 / 远离时还原。
- 红色脉动区=致死；绿色掩体棚下=豁免；绿色 EXIT=通关。
- 只调参数 → 直接改 Inspector / 资产；改了构建逻辑 → 重跑 `Inkform/M1/Build M1 Test Scene`。
