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
}
