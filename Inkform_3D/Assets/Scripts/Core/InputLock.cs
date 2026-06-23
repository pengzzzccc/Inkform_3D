namespace Inkform.Core
{
    /// <summary>
    /// 引用计数式输入锁：重生 / 过场 / 读线索时锁定玩家输入。
    /// Acquire 与 Release 必须成对；计数归零才解锁。
    /// </summary>
    public class InputLock
    {
        int _count;

        public bool IsLocked => _count > 0;
        public int Count => _count;

        public void Acquire() => _count++;
        public void Release() { if (_count > 0) _count--; }
        public void Reset() => _count = 0;
    }
}
