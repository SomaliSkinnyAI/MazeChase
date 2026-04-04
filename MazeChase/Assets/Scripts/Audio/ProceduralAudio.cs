using UnityEngine;

namespace MazeChase.Audio
{
    /// <summary>
    /// Generates simple audio clips procedurally using AudioClip.Create.
    /// Provides game-ready placeholder sounds without external assets.
    /// </summary>
    public static class ProceduralAudio
    {
        private const int SampleRate = 44100;

        /// <summary>Short click/blip for pellet collection.</summary>
        public static AudioClip CreatePelletEat(float pitch = 1.0f)
        {
            float duration = 0.05f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("PelletEat", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            float freq = 800f * pitch;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float env = 1f - (float)i / samples; // decay
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.3f;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Deeper tone for energizer pickup.</summary>
        public static AudioClip CreateEnergizerEat()
        {
            float duration = 0.3f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("EnergizerEat", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float env = 1f - (float)i / samples;
                float freq = 200f + 400f * t / duration; // rising
                data[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) * 0.3f +
                           Mathf.Sin(2f * Mathf.PI * freq * 0.5f * t) * 0.2f) * env;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Satisfying crunch for ghost eating.</summary>
        public static AudioClip CreateGhostEat()
        {
            float duration = 0.2f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("GhostEat", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float env = 1f - (float)i / samples;
                float freq = 600f - 400f * t / duration; // descending
                data[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) * 0.25f +
                           (Random.value * 2f - 1f) * 0.05f * env) * env; // noise
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Descending tone for player death.</summary>
        public static AudioClip CreateDeath()
        {
            float duration = 1.0f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("Death", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float progress = t / duration;
                float env = 1f - progress;
                float freq = 800f * (1f - progress * 0.8f); // descending
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.35f;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Ascending arpeggio for round clear.</summary>
        public static AudioClip CreateRoundClear()
        {
            float duration = 0.8f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("RoundClear", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f }; // C5, E5, G5, C6
            float noteLen = duration / notes.Length;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                int noteIdx = Mathf.Min((int)(t / noteLen), notes.Length - 1);
                float noteT = t - noteIdx * noteLen;
                float env = Mathf.Clamp01(1f - noteT / noteLen);
                data[i] = Mathf.Sin(2f * Mathf.PI * notes[noteIdx] * t) * env * 0.3f;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Fruit collection jingle.</summary>
        public static AudioClip CreateFruitCollect()
        {
            float duration = 0.15f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("FruitCollect", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float env = 1f - (float)i / samples;
                data[i] = (Mathf.Sin(2f * Mathf.PI * 1200f * t) * 0.2f +
                           Mathf.Sin(2f * Mathf.PI * 1500f * t) * 0.15f) * env;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Continuous siren tone (loopable).</summary>
        public static AudioClip CreateSiren(float baseFreq = 300f, float modDepth = 50f)
        {
            float duration = 2f; // loop length
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("Siren", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float freq = baseFreq + Mathf.Sin(2f * Mathf.PI * 2f * t) * modDepth;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.15f;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Frightened mode warbly sound (loopable).</summary>
        public static AudioClip CreateFrightenedLoop()
        {
            float duration = 1f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("Frightened", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float freq = 150f + Mathf.Sin(2f * Mathf.PI * 8f * t) * 50f;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.12f;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Short UI click.</summary>
        public static AudioClip CreateUIClick()
        {
            float duration = 0.03f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("UIClick", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float env = 1f - (float)i / samples;
                data[i] = Mathf.Sin(2f * Mathf.PI * 1000f * t) * env * 0.2f;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Game start jingle.</summary>
        public static AudioClip CreateGameStart()
        {
            float duration = 1.5f;
            int samples = (int)(SampleRate * duration);
            var clip = AudioClip.Create("GameStart", samples, 1, SampleRate, false);
            float[] data = new float[samples];
            // Simple ascending melody
            float[] notes = { 261.6f, 329.6f, 392f, 523.3f, 659.3f, 784f };
            float noteLen = duration / notes.Length;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                int noteIdx = Mathf.Min((int)(t / noteLen), notes.Length - 1);
                float noteT = t - noteIdx * noteLen;
                float env = Mathf.Clamp01(1f - noteT / noteLen * 0.7f);
                data[i] = (Mathf.Sin(2f * Mathf.PI * notes[noteIdx] * t) * 0.25f +
                           Mathf.Sin(2f * Mathf.PI * notes[noteIdx] * 2f * t) * 0.1f) * env;
            }
            clip.SetData(data, 0);
            return clip;
        }
    }
}
