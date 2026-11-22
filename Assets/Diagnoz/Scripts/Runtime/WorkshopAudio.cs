using UnityEngine;

namespace Diagnoz
{
    public enum Sfx { Click, Confirm, Error, Clunk, Machine, Spin }

    // Все звуки синтезируются в коде при запуске: внешних аудиофайлов и лицензий нет.
    // Это простые служебные сигналы и фон, а не полноценное звуковое оформление.
    public sealed class WorkshopAudio : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource ui, ambient;
        private AudioClip[] clips;

        private void Awake()
        {
            ui = gameObject.AddComponent<AudioSource>(); ui.playOnAwake = false; ui.spatialBlend = 0;
            ambient = gameObject.AddComponent<AudioSource>(); ambient.playOnAwake = false; ambient.loop = true;
            ambient.spatialBlend = 0; ambient.volume = .11f;
            clips = new[] { Click(), Confirm(), Error(), Clunk(), Motor("Machine", 1.6f, 92f, 0f), Motor("Spin", 2.2f, 260f, 1f) };
            ambient.clip = RoomTone(); ambient.Play();
        }

        public void Play(Sfx sfx, float volume = 1f)
        {
            if (clips == null) return;
            float v = sfx == Sfx.Click ? .35f : sfx == Sfx.Machine || sfx == Sfx.Spin ? .45f : .6f;
            ui.PlayOneShot(clips[(int)sfx], v * volume);
        }

        private static AudioClip Make(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);

        private static AudioClip Click() => Make("Click", .04f, t => Sin(1700, t) * Mathf.Exp(-t * 120f) * .8f);

        private static AudioClip Confirm() => Make("Confirm", .26f, t =>
            (t < .11f ? Sin(660, t) : Sin(990, t)) * Mathf.Exp(-(t < .11f ? t : t - .11f) * 18f) * .6f);

        private static AudioClip Error() => Make("Error", .22f, t =>
            Mathf.Sign(Sin(196, t)) * .25f * Mathf.Exp(-t * 9f));

        private static AudioClip Clunk()
        {
            var rnd = new System.Random(7);
            float lp = 0;
            return Make("Clunk", .3f, t =>
            {
                lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * .08f;
                return (Sin(85, t) * .8f + lp * 1.6f) * Mathf.Exp(-t * 16f);
            });
        }

        private static AudioClip Motor(string name, float seconds, float hz, float rise)
        {
            var rnd = new System.Random(11);
            float lp = 0;
            return Make(name, seconds, t =>
            {
                float env = Mathf.Clamp01(t / .15f) * Mathf.Clamp01((seconds - t) / .3f);
                float f = hz * (1f + rise * Mathf.Clamp01(t / (seconds * .6f)));
                lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * .05f;
                float saw = 2f * Mathf.Repeat(f * t, 1f) - 1f;
                return (saw * .18f + Sin(f * 2f, t) * .12f + lp * .5f) * env;
            });
        }

        private static AudioClip RoomTone()
        {
            const float seconds = 6f;
            int n = (int)(seconds * Rate);
            var data = new float[n];
            var rnd = new System.Random(3);
            float brown = 0;
            for (int i = 0; i < n; i++)
            {
                brown = (brown + ((float)rnd.NextDouble() * 2f - 1f) * .02f) * .995f;
                data[i] = brown * 2.2f + Sin(50f, i / (float)Rate) * .012f;
            }
            int fade = Rate / 2;   // сшивка конца с началом для бесшовного цикла
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] = data[i] * k + data[n - fade + i] * (1f - k);
            }
            AudioClip clip = AudioClip.Create("RoomTone", n - fade, 1, Rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, n - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }
    }
}
