using System;
using System.Collections.Generic;

namespace Inkform.Core
{
    /// <summary>
    /// 静态泛型事件总线：规则层用它解耦通信（发布/订阅）。
    /// M1 用最简单的字典 + 委托实现；PlayMode 退出时记得 Clear()。
    /// </summary>
    public static class EventBus
    {
        static readonly Dictionary<Type, Delegate> _handlers = new();

        public static void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            _handlers.TryGetValue(typeof(T), out var d);
            _handlers[typeof(T)] = (d as Action<T>) + handler;
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            if (_handlers.TryGetValue(typeof(T), out var d))
                _handlers[typeof(T)] = (d as Action<T>) - handler;
        }

        public static void Publish<T>(T evt)
        {
            if (_handlers.TryGetValue(typeof(T), out var d))
                (d as Action<T>)?.Invoke(evt);
        }

        /// <summary>清空所有订阅（测试 / 退出 PlayMode 时调用）。</summary>
        public static void Clear() => _handlers.Clear();
    }
}
