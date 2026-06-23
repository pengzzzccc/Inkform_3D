using UnityEngine;
using UnityEngine.Splines;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 沿 Unity Spline 样条移动自身（探照灯整组挂在此，沿轨迹扫动）。
    /// </summary>
    public class SplinePathMover : MonoBehaviour
    {
        public enum LoopMode { PingPong, Loop }

        [Tooltip("要跟随的样条容器")]
        public SplineContainer Spline;

        [Tooltip("沿样条移动速度（归一化 t / 秒）")]
        public float Speed = 0.25f;

        public LoopMode Mode = LoopMode.PingPong;

        float _t;

        void Update()
        {
            if (Spline == null) return;

            _t += Speed * Time.deltaTime;
            float eval = Mode == LoopMode.PingPong ? Mathf.PingPong(_t, 1f) : Mathf.Repeat(_t, 1f);

            transform.position = (Vector3)Spline.EvaluatePosition(eval);
        }
    }
}
