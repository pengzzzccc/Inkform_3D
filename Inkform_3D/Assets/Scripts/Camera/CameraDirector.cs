using UnityEngine;
using Unity.Cinemachine;

namespace Inkform.GameCamera
{
    /// <summary>
    /// M1 基础镜头封装：用 Cinemachine 跟随玩家。
    /// 预留 SwitchTo / Shake 供 M2 的多机位与被发现冲击使用。
    /// 命名空间用 GameCamera 以避免与 UnityEngine.Camera 混淆。
    /// </summary>
    public class CameraDirector : MonoBehaviour
    {
        public CinemachineCamera Vcam;
        public Transform Target;

        void Start() => Bind(Target);

        public void Bind(Transform target)
        {
            Target = target;
            if (Vcam == null || target == null) return;
            Vcam.Follow = target;
            Vcam.LookAt = target;
        }
    }
}
