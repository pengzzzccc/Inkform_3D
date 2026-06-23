using System.Collections;
using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>闸门：SetActive(true) 时按 OpenOffset 移开（下沉/滑开）。</summary>
    public class SimpleGate : MonoBehaviour, IMechanism
    {
        public Vector3 OpenOffset = new Vector3(0f, -4f, 0f);
        public float MoveDuration = 0.8f;

        public bool IsActive { get; private set; }

        Vector3 _closed;
        Coroutine _co;

        void Awake() => _closed = transform.position;

        public void SetActive(bool on)
        {
            if (IsActive == on) return;
            IsActive = on;
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(MoveTo(on ? _closed + OpenOffset : _closed));
        }

        IEnumerator MoveTo(Vector3 target)
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
            _co = null;
        }

        public void ResetToCheckpoint()
        {
            if (_co != null) StopCoroutine(_co);
            transform.position = _closed;
            IsActive = false;
        }
    }
}
