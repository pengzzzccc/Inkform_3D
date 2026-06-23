using System.Collections;
using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 可遥控集装箱：被遥控形态 Operate() 时在 原位↔搭桥位 间移动（Lerp）。
    /// 到达搭桥位即 IsActive=true（形成可走的桥）。
    /// </summary>
    public class MovableContainer : MonoBehaviour, IRemoteControllable, IMechanism
    {
        [Tooltip("搭桥位（相对原位的偏移）")]
        public Vector3 BridgeOffset = new Vector3(0f, 0f, 0f);

        public float MoveDuration = 1.0f;
        public AudioSource Sfx;       // 占位搭桥音

        public bool IsActive { get; private set; } // true = 已到搭桥位

        Vector3 _home;
        Coroutine _co;

        void Awake() => _home = transform.position;

        public void Operate() => SetActive(!IsActive);

        public void SetActive(bool on)
        {
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(MoveTo(on ? _home + BridgeOffset : _home, on));
            if (Sfx != null) Sfx.Play();
        }

        IEnumerator MoveTo(Vector3 target, bool active)
        {
            Vector3 from = transform.position;
            float t = 0f;
            while (t < MoveDuration)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(from, target, Mathf.Clamp01(t / MoveDuration));
                yield return null;
            }
            transform.position = target;
            IsActive = active;
            _co = null;
        }

        public void ResetToCheckpoint()
        {
            if (_co != null) StopCoroutine(_co);
            transform.position = _home;
            IsActive = false;
        }
    }
}
