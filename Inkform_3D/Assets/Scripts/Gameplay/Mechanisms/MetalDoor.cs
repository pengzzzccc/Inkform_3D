using System.Collections;
using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>金属门：被磁铁形态 Pull() 拉开（按 OpenOffset 滑动）。</summary>
    public class MetalDoor : MonoBehaviour, IMagnetic, IMechanism
    {
        public Vector3 OpenOffset = new Vector3(0f, -4f, 0f);
        public float MoveDuration = 0.8f;
        public AudioSource Sfx;

        public bool IsActive { get; private set; }

        Vector3 _closed;
        Coroutine _co;

        void Awake() => _closed = transform.position;

        public void Pull(Transform puller) => SetActive(true);

        public void SetActive(bool on)
        {
            if (IsActive == on) return;
            IsActive = on;
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(MoveTo(on ? _closed + OpenOffset : _closed));
            if (on && Sfx != null) Sfx.Play();
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
