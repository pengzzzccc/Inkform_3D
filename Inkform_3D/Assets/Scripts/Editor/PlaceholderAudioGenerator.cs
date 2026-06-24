using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace Inkform.EditorTools
{
    /// <summary>
    /// 生成占位音效 wav（16-bit mono PCM 短音）写入 Assets/Audio/Placeholders/，
    /// 供 M1SceneBuilder 自动赋值。后续用真实音效替换同名文件即可。
    /// </summary>
    public static class PlaceholderAudioGenerator
    {
        public const string Folder = "Assets/Audio/Placeholders";
        const int SampleRate = 44100;

        public struct Clips
        {
            public AudioClip Scan, Revert, Ability, Jump, Land, Death, Respawn, Checkpoint, Complete, Footstep, Ambient;
            public AudioClip Valve, Bridge, Puzzle, Teleport;
            public AudioClip Sweeper, Alarm;
        }

        [MenuItem("Inkform/M1/Generate Placeholder Audio")]
        public static void GenerateMenu()
        {
            // force=true：按最新参数覆盖同名 wav（保留 GUID，场景引用不断、下次 Play 即生效）
            EnsureAll(true);
            AssetDatabase.Refresh();
            Debug.Log($"[PlaceholderAudio] 占位音频已（重新）生成于 {Folder}");
        }

        /// <summary>确保全部占位音存在并返回引用。force=true 时按最新参数覆盖现有文件。</summary>
        public static Clips EnsureAll(bool force = false)
        {
            // 柔和占位音：低频、低音量、指数衰减（"叮"一声而非持续蜂鸣）；循环音用持续淡入淡出。
            return new Clips
            {
                Scan       = EnsureClip("sfx_scan", 440f, 0.16f, false, force),
                Revert     = EnsureClip("sfx_revert", 294f, 0.16f, false, force),
                Ability    = EnsureClip("sfx_ability", 392f, 0.14f, false, force),
                Jump       = EnsureClip("sfx_jump", 392f, 0.10f, false, force),
                Land       = EnsureClip("sfx_land", 147f, 0.12f, false, force),
                Death      = EnsureClip("sfx_death", 110f, 0.5f, false, force),
                Respawn    = EnsureClip("sfx_respawn", 440f, 0.3f, false, force),
                Checkpoint = EnsureClip("sfx_checkpoint", 523f, 0.22f, false, force),
                Complete   = EnsureClip("sfx_complete", 587f, 0.7f, false, force),
                Footstep   = EnsureClip("sfx_footstep", 150f, 0.10f, false, force),
                Ambient    = EnsureClip("amb_searchlight", 55f, 2f, true, force),
                Valve      = EnsureClip("sfx_valve", 175f, 0.4f, false, force),
                Bridge     = EnsureClip("sfx_bridge", 147f, 0.5f, false, force),
                Puzzle     = EnsureClip("sfx_puzzle", 523f, 0.45f, false, force),
                Teleport   = EnsureClip("sfx_teleport", 622f, 0.2f, false, force),
                Sweeper    = EnsureClip("amb_sweeper", 44f, 2.5f, true, force),
                Alarm      = EnsureClip("sfx_alarm", 440f, 0.4f, false, force),
            };
        }

        static AudioClip EnsureClip(string name, float freq, float dur, bool sustain = false, bool force = false)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/{name}.wav";
            if (force || !File.Exists(path))
            {
                WriteWav(path, Tone(freq, dur, sustain));
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static float[] Tone(float freq, float dur, bool sustain)
        {
            int n = Mathf.Max(1, (int)(dur * SampleRate));
            var s = new float[n];
            int attack = Mathf.Max(1, (int)(SampleRate * 0.006f)); // 6ms 起音，防爆音
            int fade = Mathf.Max(1, (int)(n * 0.12f));
            float decay = 4.5f / Mathf.Max(0.05f, dur);            // 指数衰减到尾端很小
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                // 基频 + 弱二次谐波，柔化音色（纯正弦偏尖锐）
                float wave = Mathf.Sin(2f * Mathf.PI * freq * t)
                           + 0.18f * Mathf.Sin(2f * Mathf.PI * 2f * freq * t);
                float env;
                if (sustain) // 循环音：持续 + 两端淡入淡出
                    env = (i < fade) ? (float)i / fade
                        : (i > n - fade) ? (float)(n - i) / fade : 1f;
                else         // 一次性音：快速起音 + 指数衰减
                    env = (i < attack) ? (float)i / attack : Mathf.Exp(-t * decay);
                s[i] = 0.15f * wave * env;
            }
            return s;
        }

        static void WriteWav(string path, float[] samples)
        {
            const int channels = 1, bits = 16;
            int byteRate = SampleRate * channels * bits / 8;
            int dataSize = samples.Length * 2;

            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);                 // fmt chunk size
            bw.Write((short)1);           // PCM
            bw.Write((short)channels);
            bw.Write(SampleRate);
            bw.Write(byteRate);
            bw.Write((short)(channels * bits / 8)); // block align
            bw.Write((short)bits);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);
            foreach (var v in samples)
                bw.Write((short)(Mathf.Clamp(v, -1f, 1f) * short.MaxValue));
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            string leaf = folder.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
