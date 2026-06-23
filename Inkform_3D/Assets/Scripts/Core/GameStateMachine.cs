using System;

namespace Inkform.Core
{
    /// <summary>全局游戏状态机。状态切换驱动输入锁 / 镜头 / UI 行为。</summary>
    public class GameStateMachine
    {
        public GameState Current { get; private set; } = GameState.Boot;

        /// <summary>(prev, next)</summary>
        public event Action<GameState, GameState> OnChanged;

        public void Set(GameState next)
        {
            if (next == Current) return;
            var prev = Current;
            Current = next;
            OnChanged?.Invoke(prev, next);
        }
    }
}
