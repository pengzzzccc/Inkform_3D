using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>光敏开关：范围内出现灯泡形态的玩家（自身发光）即激活，开启关联闸门。</summary>
    public class LightSensor : MonoBehaviour, IMechanism
    {
        public SimpleGate Gate;
        public float Range = 4f;
        public AudioSource Sfx;

        public bool IsActive { get; private set; }

        Transform _player;

        void Update()
        {
            if (IsActive) return;
            if (_player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go == null) return;
                _player = go.transform;
            }
            if ((_player.position - transform.position).sqrMagnitude > Range * Range) return;

            var ability = _player.GetComponent<AbilitySystem>();
            if (ability != null && ability.CurrentForm == FormId.Bulb) SetActive(true);
        }

        public void SetActive(bool on)
        {
            if (IsActive == on) return;
            IsActive = on;
            if (on)
            {
                Gate?.SetActive(true);
                if (Sfx != null) Sfx.Play();
            }
        }

        public void ResetToCheckpoint() => IsActive = false;
    }
}
