using UnityEngine;

namespace Inkform.Core
{
    /// <summary>
    /// 跨场景根容器：持有全局子系统（输入锁、游戏状态机），DontDestroyOnLoad。
    /// M1 单场景，直接放在场景中即可；重复实例自销毁。
    /// </summary>
    public class ManagerRoot : MonoBehaviour
    {
        public static ManagerRoot Instance { get; private set; }

        public InputLock InputLock { get; private set; }
        public GameStateMachine State { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InputLock = new InputLock();
            State = new GameStateMachine();
            State.Set(GameState.Playing);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
