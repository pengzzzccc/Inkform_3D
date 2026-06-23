using UnityEngine;

namespace Inkform.Core
{
    // ── 扫描—模仿能力系统 ──
    public struct TargetScanned   { public FormId Form; }
    public struct FormMaterialized { public FormId Form; }
    public struct FormDissolved   { public FormId Form; }
    public struct AbilityUsed     { public FormId Form; }

    // ── 威胁—潜行系统 ──
    public struct PlayerKilled    { public Vector3 Position; public string Source; }

    // ── 检查点—进度系统 ──
    public struct CheckpointReached { public int Id; }
    public struct OnRespawn         { public int CheckpointId; }

    // ── 交互提示 / 关卡流程 ──
    // 用 string 传形态显示名，让 UI 层无需依赖 Gameplay/Data。
    public struct NearbyScanTargetChanged { public bool HasTarget; public string FormName; }
    public struct LevelCompleted { }

    // ── 玩家动作（供音效等订阅）──
    public struct Jumped { }
    public struct Landed { }

    // ── 机关 / 谜题 ──
    public struct PuzzleSolved { public int Id; }
}
