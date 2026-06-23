using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 关卡终点：玩家进入即"通关"——锁输入、切到过场状态、广播 LevelCompleted。
    /// M1 不退出程序，到达 EXIT = 完成关卡。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ExitGoal : MonoBehaviour
    {
        bool _done;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (_done || !other.CompareTag("Player")) return;
            _done = true;

            var root = ManagerRoot.Instance;
            root?.InputLock.Acquire();
            root?.State.Set(GameState.Cutscene);
            EventBus.Publish(new LevelCompleted());
        }
    }
}
