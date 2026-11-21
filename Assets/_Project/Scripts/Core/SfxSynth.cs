using System;

namespace DeckArpg.Core
{
    /// <summary>Every sound the game plays (M4 SFX pass).</summary>
    public enum SfxId
    {
        ClashNeutral,
        CardBreak,
        ZeroBreak,
        Swing,
        Hit,
        Dash,
        ReloadPerfect,
        ReloadMiss,
        BossSleightWindup,
        BossSleightFire,
        BossReload,
        BossExhausted,
        BigDeadlock,
        Victory,
        /// <summary>The player healed (hand channel or Sleight): a short rising chime.</summary>
        Heal
    }

    /// <summary>
    /// Procedural sound effects: each <see cref="SfxId"/> is a short mono clip built from sine sweeps, square buzz and
    /// seeded noise under simple envelopes. Pure and deterministic (no Unity types), so it's EditMode-testable and needs
    /// no audio assets. Samples are in [-1, 1] with the peak normalized to <see cref="Peak"/>.
    /// </summary>
    public static class SfxSynth
    {
        public const float Peak = 0.9f;
        public const int DefaultSampleRate = 44100;

        public static float[] Generate(SfxId id, int sampleRate = DefaultSampleRate)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            var b = new Builder(Duration(id), sampleRate, (int)id * 7919 + 17);
            switch (id)
            {
                case SfxId.ClashNeutral:
                    b.Sine(1200f, 1200f, 0.5f, Decay(18f)); b.Sine(1810f, 1810f, 0.35f, Decay(22f)); b.Noise(0.25f, Decay(60f));
                    break;
                case SfxId.CardBreak:
                    b.Noise(0.6f, Decay(25f)); b.Sine(320f, 110f, 0.7f, Decay(10f)); b.Sine(950f, 700f, 0.25f, Decay(30f));
                    break;
                case SfxId.ZeroBreak:
                    b.Noise(0.5f, Decay(14f)); b.Sine(2600f, 2200f, 0.35f, Decay(9f)); b.Sine(3900f, 3500f, 0.2f, Decay(12f));
                    b.Sine(90f, 50f, 0.8f, Decay(12f));
                    break;
                case SfxId.Swing:
                    b.Noise(0.5f, Bell);
                    break;
                case SfxId.Hit:
                    b.Sine(110f, 60f, 0.9f, Decay(28f)); b.Noise(0.35f, Decay(45f));
                    break;
                case SfxId.Dash:
                    b.Noise(0.45f, Bell); b.Sine(300f, 700f, 0.2f, Bell);
                    break;
                case SfxId.ReloadPerfect:
                    b.Sine(880f, 880f, 0.5f, Window(0f, 0.5f, 12f)); b.Sine(1320f, 1320f, 0.5f, Window(0.4f, 1f, 10f));
                    break;
                case SfxId.ReloadMiss:
                    b.Square(110f, 0.35f, Decay(6f)); b.Square(104f, 0.25f, Decay(6f));
                    break;
                case SfxId.BossSleightWindup:
                    b.Sine(180f, 820f, 0.6f, Rise); b.Square(90f, 0.12f, Rise); b.Noise(0.12f, Rise);
                    break;
                case SfxId.BossSleightFire:
                    b.Sine(70f, 35f, 1f, Decay(5f)); b.Noise(0.6f, Decay(9f)); b.Sine(240f, 120f, 0.3f, Decay(8f));
                    break;
                case SfxId.BossReload:
                    b.Sine(620f, 620f, 0.45f, Window(0f, 0.45f, 14f)); b.Sine(330f, 330f, 0.45f, Window(0.5f, 1f, 10f));
                    break;
                case SfxId.BossExhausted:
                    b.Sine(70f, 66f, 0.8f, Decay(3f)); b.Sine(141f, 136f, 0.4f, Decay(4f)); b.Sine(213f, 205f, 0.25f, Decay(5f));
                    b.Noise(0.15f, Decay(20f));
                    break;
                case SfxId.BigDeadlock:
                    b.Sine(420f, 40f, 0.9f, Decay(2.5f)); b.Noise(0.7f, Decay(4f)); b.Sine(55f, 45f, 0.8f, Decay(3f));
                    break;
                case SfxId.Victory:
                    float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
                    for (int i = 0; i < notes.Length; i++)
                        b.Sine(notes[i], notes[i], 0.45f, Window(i * 0.2f, i == notes.Length - 1 ? 1f : i * 0.2f + 0.3f, 6f));
                    break;
                case SfxId.Heal:
                    b.Sine(520f, 780f, 0.5f, Window(0f, 0.6f, 8f)); b.Sine(780f, 1170f, 0.4f, Window(0.35f, 1f, 9f));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(id));
            }
            return b.Normalized();
        }

        /// <summary>Clip length in seconds.</summary>
        public static float Duration(SfxId id)
        {
            switch (id)
            {
                case SfxId.Swing: return 0.12f;
                case SfxId.Hit: return 0.14f;
                case SfxId.Dash: return 0.18f;
                case SfxId.ClashNeutral: return 0.3f;
                case SfxId.CardBreak: return 0.35f;
                case SfxId.ReloadPerfect: return 0.3f;
                case SfxId.Heal: return 0.3f;
                case SfxId.ReloadMiss: return 0.35f;
                case SfxId.BossReload: return 0.35f;
                case SfxId.ZeroBreak: return 0.5f;
                case SfxId.BossSleightFire: return 0.6f;
                case SfxId.Victory: return 1.1f;
                case SfxId.BossExhausted: return 1.2f;
                case SfxId.BossSleightWindup: return 1.4f;
                case SfxId.BigDeadlock: return 1.5f;
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        // ---- Envelopes: t in [0, 1] over the clip -> gain ----

        private static Func<float, float, float> Decay(float rate) => (t, seconds) => (float)Math.Exp(-rate * t * seconds) * Attack(t, seconds);
        private static readonly Func<float, float, float> Bell = (t, _) => (float)Math.Sin(Math.PI * t);
        private static readonly Func<float, float, float> Rise = (t, seconds) => t * t * Fade(t, seconds);

        /// <summary>On between <paramref name="from"/> and <paramref name="to"/> (fractions of the clip), decaying inside.</summary>
        private static Func<float, float, float> Window(float from, float to, float rate) => (t, seconds) =>
        {
            if (t < from || t > to) return 0f;
            float local = (t - from) * seconds;
            float edge = Math.Min(1f, local / 0.005f) * Math.Min(1f, (to - t) * seconds / 0.01f);
            return (float)Math.Exp(-rate * local) * edge;
        };

        /// <summary>5 ms attack so a clip never starts with a click.</summary>
        private static float Attack(float t, float seconds) => Math.Min(1f, t * seconds / 0.005f);

        /// <summary>10 ms fade-out at the very end.</summary>
        private static float Fade(float t, float seconds) => Math.Min(1f, (1f - t) * seconds / 0.01f);

        private sealed class Builder
        {
            private readonly float[] data;
            private readonly int rate;
            private readonly float seconds;
            private readonly Random rng;

            public Builder(float seconds, int sampleRate, int seed)
            {
                this.seconds = seconds;
                rate = sampleRate;
                data = new float[Math.Max(1, (int)(seconds * sampleRate))];
                rng = new Random(seed);
            }

            /// <summary>A sine sweeping exponentially from <paramref name="f0"/> to <paramref name="f1"/> Hz.</summary>
            public void Sine(float f0, float f1, float gain, Func<float, float, float> env)
            {
                double phase = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)data.Length;
                    double f = f0 * Math.Pow(f1 / f0, t);
                    phase += 2 * Math.PI * f / rate;
                    data[i] += (float)Math.Sin(phase) * gain * env(t, seconds) * Fade(t, seconds);
                }
            }

            public void Square(float f, float gain, Func<float, float, float> env)
            {
                double phase = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)data.Length;
                    phase += f / rate;
                    float s = phase - Math.Floor(phase) < 0.5 ? 1f : -1f;
                    data[i] += s * gain * env(t, seconds) * Fade(t, seconds);
                }
            }

            /// <summary>White noise, lightly low-passed so it reads as a "whoosh"/"crunch" rather than hiss.</summary>
            public void Noise(float gain, Func<float, float, float> env)
            {
                float last = 0f;
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)data.Length;
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    last += 0.35f * (white - last);
                    data[i] += last * 2f * gain * env(t, seconds) * Fade(t, seconds);
                }
            }

            public float[] Normalized()
            {
                float max = 0f;
                foreach (var s in data) max = Math.Max(max, Math.Abs(s));
                if (max <= 0f) return data;
                float k = Peak / max;
                for (int i = 0; i < data.Length; i++) data[i] *= k;
                return data;
            }
        }
    }
}
