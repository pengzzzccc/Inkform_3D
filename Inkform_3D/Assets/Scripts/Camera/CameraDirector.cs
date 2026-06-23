using UnityEngine;
using Unity.Cinemachine;

namespace Inkform.GameCamera
{
    /// <summary>
    /// M1 基础镜头封装：用 Cinemachine 跟随玩家。
    /// 把常用镜头参数（机位偏移 / 视野）暴露在此，方便直接在 Inspector 调，
    /// 无需深入 CinemachineCamera / CinemachineFollow。运行时改这两个值也会实时应用。
    /// </summary>
    [ExecuteAlways]
    public class CameraDirector : MonoBehaviour
    {
        public CinemachineCamera Vcam;
        public CinemachineFollow Follow;
        public Transform Target;

        [Header("可调镜头参数")]
        [Tooltip("机位相对玩家的偏移（拉远/抬高/侧移）")]
        public Vector3 FollowOffset = new Vector3(0f, 3f, -12f);

        [Tooltip("视野角度 FOV")]
        [Range(20f, 90f)]
        public float FieldOfView = 50f;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Bind(Transform target)
        {
            Target = target;
            Apply();
        }

        public void Apply()
        {
            if (Vcam == null) return;
            if (Follow == null) Follow = Vcam.GetComponent<CinemachineFollow>();

            if (Target != null)
            {
                Vcam.Follow = Target;
                Vcam.LookAt = Target;
            }
            if (Follow != null) Follow.FollowOffset = FollowOffset;

            var lens = Vcam.Lens;
            lens.FieldOfView = FieldOfView;
            Vcam.Lens = lens;
        }
    }
}
