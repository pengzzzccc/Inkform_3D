using System.Collections.Generic;
using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 谜题编排：监听一组机关，全部 IsActive 时广播 PuzzleSolved（供音效/提示）。
    /// 通行本身由机关物理结果实现（水位降 / 桥搭成）。
    /// </summary>
    public class PuzzleController : MonoBehaviour, IResettable
    {
        public int PuzzleId;

        [Tooltip("需要全部激活才算解开的机关（实现了 IMechanism 的组件）")]
        public List<MonoBehaviour> Mechanisms = new List<MonoBehaviour>();

        bool _solved;

        void Update()
        {
            if (_solved || Mechanisms == null || Mechanisms.Count == 0) return;

            foreach (var mb in Mechanisms)
            {
                if (mb is IMechanism m)
                {
                    if (!m.IsActive) return;
                }
            }
            _solved = true;
            EventBus.Publish(new PuzzleSolved { Id = PuzzleId });
        }

        public void ResetSolved() => _solved = false;

        public void ResetToCheckpoint() => _solved = false;
    }
}
