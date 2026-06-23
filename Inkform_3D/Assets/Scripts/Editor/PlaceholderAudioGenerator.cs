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
            EnsureAll();
            AssetDatabase.Refresh();
            Debug.Log($"[PlaceholderAudio] 占位音频已生成于 {Folder}");
        }

        /// <summary>确保全部占位音存在并返回引用（不存在才生成）。</summary>
        public static Clips EnsureAll()
        {
            return new Clips
            {
                Scan       = EnsureClip("sfx_scan", 880f, 0.18f),
                Revert     = EnsureClip("sfx_revert", 440f, 0.16f),
                Ability    = EnsureClip("sfx_ability", 660f, 0.15f),
                Jump       = EnsureClip("sfx_jump", 520f, 0.12f),
                Land       = EnsureClip("sfx_land", 180f, 0.12f),
                Death      = EnsureClip("sfx_death", 120f, 0.5f),
                Respawn    = EnsureClip("sfx_respawn", 700f, 0.3f),
                Checkpoint = EnsureClip("sfx_checkpoint", 990f, 0.2f),
                Complete   = EnsureClip("sfx_complete", 1320f, 0.6f),
                Footstep   = EnsureClip("sfx_footstep", 300f, 0.12f),
                Ambient    = EnsureClip("amb_searchlight", 70f, 2f),
                Valve      = EnsureClip("sfx_valve", 240f, 0.4f),
                Bridge     = EnsureClip("sfx_bridge", 200f, 0.5f),
                Puzzle     = EnsureClip("sfx_puzzle", 1100f, 0.4f),
                Teleport   = EnsureClip("sfx_teleport", 1500f, 0.2f),
                Sweeper    = EnsureClip("amb_sweeper", 50f, 2.5f),
                Alarm      = EnsureClip("sfx_alarm", 1700f, 0.5f),
            };
        }

        static AudioClip EnsureClip(string name, float freq, float dur)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/{name}.wav";
            if (!File.Exists(path))
            {
                WriteWav(path, Tone(freq, dur));
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static float[] Tone(float freq, float dur)
        {
            int n = Mathf.Max(1, (int)(dur * SampleRate));
            var s = new float[n];
            int fade = Mathf.Max(1, (int)(n * 0.08f));
            for (int i = 0; i < n; i++)
            {
                float a = 0.3f * Mathf.Sin(2f * Mathf.PI * freq * i / SampleRate);
                float env = 1f;
                if (i < fade) env = (float)i / fade;
                else if (i > n - fade) env = (float)(n - i) / fade;
                s[i] = a * env;
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
