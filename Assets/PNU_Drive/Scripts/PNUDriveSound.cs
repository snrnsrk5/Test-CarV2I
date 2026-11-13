// PNU_Drive 자차 소리(런타임) — 소리 파일 없이 코드로 만들어 틂(음성 안내 wav 만 빌더가 윈도 음성으로 만듦)
// - 전기차 모터(v11 고급형): 낮고 부드러운 사인 화음(밑옥타브 + 두 겹 살짝 어긋남), 속도 따라 91→420Hz, 가속·회생제동 때만 조금 커짐
//   날카로운 고음 배음·기어 윙은 없앰. 노면은 낮은 웅웅(타이어 공명 220Hz), 바람은 150~1200Hz 부드러운 띠
// - 저속 보행자 경고음(AVAS, 20km/h 까지 또렷, 30km/h 에서 사라짐; 밖에서 볼 때 더 크게)
// - 노면(타이어) 소음·바람 소리(속도 따라), 터널·지하차도 안에서는 울림, 다리 들어가고 나올 때 이음매 "덜컹"
// - 알림음: 자율주행 시작·도착, 신호 정지·초록불 출발, 길안내 "띵동", 깜빡이 "똑딱"
// PNUDriveNav 가 언제 무엇을 틀지 정하고, 여기는 소리를 만들고 섞음. 2D 소리(카메라의 AudioListener 로 들음)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU.Drive
{
    public class PNUDriveSound : MonoBehaviour
    {
        public PNUDriveEgo ego;
        [Range(0f, 1f)] public float master = 0.8f;
        public bool muted = false;
        [Header("크기")]
        [Range(0f, 1f)] public float motorVol = 0.22f;
        [Range(0f, 1f)] public float avasVol = 0.4f;
        [Range(0f, 1f)] public float roadVol = 0.42f;
        [Range(0f, 1f)] public float windVol = 0.26f;
        [Range(0f, 1f)] public float chimeVol = 0.45f;
        [Range(0f, 1f)] public float voiceVol = 1f;
        [Header("음성 안내(빌더가 채움: path.json voice 의 key → wav)")]
        public string[] voiceKeys = new string[0];
        public AudioClip[] voiceClips = new AudioClip[0];

        public enum Fx { Engage, Disengage, Stop, Green, Nav, Arrive, Tick, Tock, Thump, Warn }
        [Range(0f, 1f)] public float storyVol = 1f;
        [Range(0f, 1f)] public float rainVol = 0.38f;

        const int SR = 44100;
        AudioSource sWhine, sGear, sAvas, sRoad, sWind, sFx, sVoice, sThump, sStory, sRain, sHiss;
        float rainW; bool rainCovered;
        AudioReverbFilter rev;
        readonly Dictionary<Fx, AudioClip> fx = new Dictionary<Fx, AudioClip>();
        readonly Dictionary<string, AudioClip> voice = new Dictionary<string, AudioClip>();
        float vPrev, aSm, vSm, tunnelMix, bridgeMix, tunTarget, brTarget, thumpAt = -1f;
        bool inited;
        System.Random rng;

        public bool HasVoice(string key) { Init(); return key != null && voice.ContainsKey(key); }
        public int VoiceCount { get { Init(); return voice.Count; } }

        void Start() { Init(); }

        void Init()
        {
            if (inited) return; inited = true;
            rng = new System.Random(20261112);
            if (ego == null) ego = GetComponent<PNUDriveEgo>();
            if (ego == null) ego = FindObjectOfType<PNUDriveEgo>();
            for (int i = 0; i < Mathf.Min(voiceKeys.Length, voiceClips.Length); i++) if (voiceClips[i] != null && !string.IsNullOrEmpty(voiceKeys[i])) voice[voiceKeys[i]] = voiceClips[i];
            // 계속 도는 소리(2초 고리): 모터 440Hz 바탕(배음), 기어 2200Hz, AVAS 110Hz 두 겹, 노면·바람 잡음
            sWhine = Loop("모터", Warm(140f));
            sGear = Loop("모터 고음(아주 작게)", Tone(560f, new[] { 1f }, new[] { 1f }, 0f));
            sAvas = Loop("보행자 경고음", Avas());
            sRoad = Loop("노면", Road());
            sWind = Loop("바람", Wind());
            rev = sRoad.gameObject.AddComponent<AudioReverbFilter>();
            rev.reverbPreset = AudioReverbPreset.User;
            rev.dryLevel = 0f; rev.room = -10000f; rev.roomHF = -500f; rev.decayTime = 2.3f; rev.decayHFRatio = 0.55f;
            rev.reflectionsLevel = -500f; rev.reflectionsDelay = 0.02f; rev.reverbLevel = -200f; rev.reverbDelay = 0.04f; rev.diffusion = 100f; rev.density = 100f;
            sFx = OneShot("알림음"); sVoice = OneShot("음성 안내"); sThump = OneShot("이음매"); sStory = OneShot("스토리보드 음성");
            sRain = Loop("빗소리", RainNoise()); sHiss = Loop("젖은 노면", Hiss());
            // 알림음
            // 알림음(v11): 한 옥타브 낮춘 부드러운 마림바풍(C5~C6), 고음 배음 줄임
            fx[Fx.Engage] = Clip("자율주행 시작", Bells(new[] { 523.25f, 783.99f }, new[] { 0f, 0.14f }, 0.32f));
            fx[Fx.Disengage] = Clip("자율주행 끝", Bells(new[] { 783.99f, 523.25f }, new[] { 0f, 0.14f }, 0.32f));
            fx[Fx.Stop] = Clip("신호 정지", Bells(new[] { 587.33f }, new[] { 0f }, 0.4f));
            fx[Fx.Green] = Clip("초록불", Bells(new[] { 659.25f, 880f }, new[] { 0f, 0.1f }, 0.26f));
            fx[Fx.Nav] = Clip("길안내", Bells(new[] { 783.99f, 659.25f }, new[] { 0f, 0.2f }, 0.4f));
            fx[Fx.Arrive] = Clip("도착", Bells(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, new[] { 0f, 0.13f, 0.26f, 0.39f }, 0.42f));
            fx[Fx.Tick] = Clip("똑", Click(1450f, 560f));
            fx[Fx.Tock] = Clip("딱", Click(1120f, 430f));
            fx[Fx.Thump] = Clip("덜컹", Thump());
            fx[Fx.Warn] = Clip("경고", Bells(new[] { 1318.5f, 1318.5f, 1318.5f }, new[] { 0f, 0.16f, 0.32f }, 0.07f));
        }

        // ---------------------------------------------------------------- 밖에서 부르는 것
        public void Play(Fx f, float vol = 1f)
        {
            Init(); if (muted) return;
            AudioClip c; if (!fx.TryGetValue(f, out c)) return;
            float v = chimeVol * master * vol;
            if (f == Fx.Tick || f == Fx.Tock) v = master * 0.5f * vol;
            if (f == Fx.Thump) { sThump.PlayOneShot(c, master * 0.7f * vol); return; }
            sFx.PlayOneShot(c, v);
        }

        /// <summary>음성 안내: delay 초 뒤(알림음 다음). 없는 key 면 false</summary>
        public bool PlayVoice(string key, float delay)
        {
            Init(); AudioClip c;
            if (muted || key == null || !voice.TryGetValue(key, out c)) return false;
            sVoice.Stop(); sVoice.clip = c; sVoice.volume = voiceVol * master; sVoice.PlayDelayed(Mathf.Max(0f, delay));
            return true;
        }
        public void StopVoice() { if (sVoice != null) sVoice.Stop(); if (sStory != null) sStory.Stop(); }

        /// <summary>스토리보드 음성(sim_1~8): 길안내 음성보다 먼저(길안내는 끝날 때까지 기다림)</summary>
        public void PlayStory(AudioClip c, float delay)
        {
            Init(); if (c == null || muted) return;
            if (sVoice != null && sVoice.isPlaying) sVoice.Stop();
            sStory.Stop(); sStory.clip = c; sStory.volume = storyVol * master; sStory.PlayDelayed(Mathf.Max(0f, delay));
        }
        public bool StoryBusy { get { return sStory != null && sStory.isPlaying; } }
        public bool VoiceBusy { get { return sVoice != null && sVoice.isPlaying; } }
        /// <summary>비(0~1)와 지붕 아래(터널) 여부</summary>
        public void SetRain(float w, bool covered) { rainW = w; rainCovered = covered; }

        /// <summary>지금 터널(또는 지하차도) 안인지, 다리 위인지</summary>
        public void SetZones(bool tunnel, bool bridge) { tunTarget = tunnel ? 1f : 0f; brTarget = bridge ? 1f : 0f; }

        /// <summary>이음매: 앞바퀴 지금, 뒷바퀴는 축거/속도 뒤</summary>
        public void Joint(float speed, float wheelBase)
        {
            if (muted || speed < 1f) return;
            Play(Fx.Thump, Mathf.Clamp01(0.4f + speed / 25f));
            thumpAt = Time.time + Mathf.Clamp(wheelBase / Mathf.Max(1f, speed * Mathf.Max(0.25f, ego != null ? ego.rate : 1f)), 0.04f, 0.6f);
        }

        void Update()
        {
            Init();
            if (ego == null || !ego.Ready) return;
            float dt = Time.deltaTime; if (dt <= 0f) return;
            if (thumpAt > 0f && Time.time >= thumpAt) { thumpAt = -1f; Play(Fx.Thump, 0.8f); }
            bool run = ego.playing && !muted;
            float v = ego.Speed;
            float simDt = dt * Mathf.Max(0.05f, ego.rate);
            float a = run ? (v - vPrev) / Mathf.Max(1e-4f, simDt) : 0f; vPrev = v;
            aSm = Mathf.Lerp(aSm, Mathf.Clamp(a, -4f, 4f), 1f - Mathf.Exp(-dt * 4f));
            vSm = Mathf.Lerp(vSm, v, 1f - Mathf.Exp(-dt * 8f));
            float kmh = vSm * 3.6f, g = run ? master : 0f;
            bool drv = ego.camMode == PNUDriveEgo.CamMode.Driver;
            tunnelMix = Mathf.MoveTowards(tunnelMix, tunTarget, dt * 0.9f);
            bridgeMix = Mathf.MoveTowards(bridgeMix, brTarget, dt * 0.5f);
            // 모터: 부드러운 화음이 속도 따라 올라감(0~108km/h → 91~420Hz), 가속·회생 때 조금 커짐
            float load = Mathf.Clamp01(Mathf.Abs(aSm) / 2.0f), mv = Mathf.Clamp01(vSm / 2.5f);
            float sp = Mathf.Clamp01(vSm / 30f);
            sWhine.pitch = 0.65f + 2.3f * sp;
            sWhine.volume = g * motorVol * mv * (0.35f + 0.65f * load) * (drv ? 1f : 0.7f);
            sGear.pitch = 0.65f + 2.3f * sp;
            sGear.volume = g * motorVol * 0.07f * mv * load;
            // 보행자 경고음(AVAS)
            float av = 1f - Mathf.Clamp01((kmh - 20f) / 10f);
            sAvas.pitch = 0.85f + Mathf.Clamp01(kmh / 30f) * 0.5f;
            sAvas.volume = g * avasVol * av * (v > 0.05f ? 1f : 0.3f) * (drv ? 0.15f : 1f);   // 실내에서는 아주 작게
            // 노면·바람
            float rv = Mathf.Pow(Mathf.Clamp01(vSm / 30f), 0.8f);
            sRoad.pitch = 0.85f + 0.3f * Mathf.Clamp01(vSm / 30f);
            sRoad.volume = g * roadVol * rv * (1f + 0.55f * tunnelMix) * (drv ? 1f : 0.8f);
            float wv = Mathf.Clamp01((vSm - 6f) / 24f); wv *= wv;
            sWind.pitch = 0.85f + 0.3f * Mathf.Clamp01(vSm / 30f);
            sWind.volume = g * windVol * wv * (1f + 0.5f * bridgeMix) * (drv ? 0.8f : 1.2f);
            if (rev != null) rev.room = tunnelMix <= 0.001f ? -10000f : Mathf.Lerp(-3000f, -650f, tunnelMix);
            // 비: 빗소리(지붕·유리에 떨어짐, 터널 안에선 멀리), 젖은 노면 쉬익(속도 따라)
            float rw = muted ? 0f : rainW;
            sRain.volume = master * rainVol * rw * (rainCovered ? 0.18f : 1f) * (drv ? 1f : 0.8f);
            sHiss.pitch = 0.8f + 0.5f * Mathf.Clamp01(vSm / 25f);
            sHiss.volume = master * rainVol * 0.9f * rw * Mathf.Clamp01(vSm / 14f) * (ego.playing ? 1f : 0f);
            if (!run && sVoice != null && sVoice.isPlaying && muted) sVoice.Stop();
        }

        // ---------------------------------------------------------------- 소리 만들기
        AudioSource Loop(string name, float[] data)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.clip = Clip(name, data); s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f; s.dopplerLevel = 0f;
            s.Play(); return s;
        }
        AudioSource OneShot(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0f; s.dopplerLevel = 0f; return s;
        }
        static AudioClip Clip(string name, float[] d) { var c = AudioClip.Create(name, d.Length, 1, SR, false); c.SetData(d, 0); return c; }

        /// <summary>배음 고리(2초, 모든 주파수가 2초에 딱 맞게 돌아 이음매 없음)</summary>
        float[] Tone(float f0, float[] harm, float[] amp, float noise)
        {
            int n = SR * 2; var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SR, x = 0;
                for (int k = 0; k < harm.Length; k++) x += amp[k] * Math.Sin(2 * Math.PI * f0 * harm[k] * t);
                d[i] = (float)(x + noise * (rng.NextDouble() * 2 - 1));
            }
            return Norm(d, 0.8f);
        }

        /// <summary>보행자 경고음: 110Hz 톱니(부드럽게) + 다섯째음 + 살짝 어긋난 110.5Hz(2초에 한 번 일렁임)</summary>
        float[] Avas()
        {
            int n = SR * 2; var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SR, x = 0;
                for (int k = 1; k <= 8; k++)
                {
                    double a = 1.0 / (k * k * 0.7 + 0.3);
                    x += a * (Math.Sin(2 * Math.PI * 110 * k * t) + 0.8 * Math.Sin(2 * Math.PI * 110.5 * k * t));
                    if (k <= 4) x += 0.45 * a * Math.Sin(2 * Math.PI * 165 * k * t);
                }
                x *= 0.85 + 0.15 * Math.Sin(2 * Math.PI * 1.0 * t);
                d[i] = (float)x;
            }
            return Norm(d, 0.7f);
        }

        /// <summary>모터(고급형): f0 사인 + 밑옥타브 + 2배음 조금 + 0.5Hz 어긋난 두 번째 겹(코러스) — 2초에 모두 딱 맞게 돌아 이음매 없음</summary>
        float[] Warm(float f0)
        {
            int n = SR * 2; var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SR;
                double x = 1.0 * Math.Sin(2 * Math.PI * f0 * t) + 0.38 * Math.Sin(2 * Math.PI * f0 * 0.5 * t) + 0.16 * Math.Sin(2 * Math.PI * f0 * 2 * t)
                         + 0.55 * Math.Sin(2 * Math.PI * (f0 + 0.5) * t + 1.3) + 0.2 * Math.Sin(2 * Math.PI * (f0 + 0.5) * 0.5 * t + 0.4);
                d[i] = (float)x;
            }
            return Norm(d, 0.7f);
        }

        // 두 극 걸러내기(biquad): 띠(band) / 낮은 쪽(low)
        struct Bq { public double b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public double F(double x) { double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; } }
        static Bq Band(double f, double q) { double w = 2 * Math.PI * f / SR, al = Math.Sin(w) / (2 * q), a0 = 1 + al; return new Bq { b0 = al / a0, b1 = 0, b2 = -al / a0, a1 = -2 * Math.Cos(w) / a0, a2 = (1 - al) / a0 }; }
        static Bq Low(double f, double q) { double w = 2 * Math.PI * f / SR, al = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + al; return new Bq { b0 = (1 - c) / 2 / a0, b1 = (1 - c) / a0, b2 = (1 - c) / 2 / a0, a1 = -2 * c / a0, a2 = (1 - al) / a0 }; }
        static Bq High(double f, double q) { double w = 2 * Math.PI * f / SR, al = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + al; return new Bq { b0 = (1 + c) / 2 / a0, b1 = -(1 + c) / a0, b2 = (1 + c) / 2 / a0, a1 = -2 * c / a0, a2 = (1 - al) / a0 }; }

        /// <summary>노면(고급형): 낮은 웅웅(갈색 잡음 → 400Hz 아래) + 타이어 공명 220Hz 띠 + 아주 작은 결, 끝·처음을 섞어 고리</summary>
        float[] Road()
        {
            int n = SR * 3, m = SR / 4; var w = new float[n + m];
            double y = 0; var lo = Low(400, 0.7); var lo2 = Low(400, 0.7); var cav = Band(220, 2.2); var tex = Band(700, 0.9); var dc = High(35, 0.7);
            for (int i = 0; i < w.Length; i++)
            {
                double r = rng.NextDouble() * 2 - 1;
                y = 0.985 * y + 0.05 * r;
                double rumble = lo2.F(lo.F(y));
                w[i] = (float)dc.F(rumble * 1.0 + cav.F(r) * 0.55 + tex.F(r) * 0.06);
            }
            return Norm(LoopFade(w, n, m), 0.8f);
        }

        /// <summary>바람(고급형): 150~1200Hz 부드러운 띠 잡음 + 느린 흔들림(쉬익 소리 없음)</summary>
        float[] Wind()
        {
            int n = SR * 3, m = SR / 4; var w = new float[n + m];
            var hp = High(150, 0.7); var lp = Low(1200, 0.7); var lp2 = Low(1500, 0.7);
            for (int i = 0; i < w.Length; i++)
            {
                double r = rng.NextDouble() * 2 - 1;
                double t = (double)(i % n) / SR;
                w[i] = (float)(lp2.F(lp.F(hp.F(r))) * (0.8 + 0.2 * Math.Sin(2 * Math.PI * t * 2.0 / 3.0)));
            }
            return Norm(LoopFade(w, n, m), 0.7f);
        }

        static float[] LoopFade(float[] w, int n, int m)
        {
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = w[i];
            for (int i = 0; i < m; i++) { float k = (float)i / m; d[i] = w[i] * k + w[n + i] * (1f - k); }
            return d;
        }

        /// <summary>종소리(알림음): 주파수·시작 시각마다 맑은 음(배음 조금, 지수 감쇠)</summary>
        static float[] Bells(float[] f, float[] t0, float tau)
        {
            float len = t0[t0.Length - 1] + tau * 4.5f; int n = (int)(len * SR); var d = new float[n];
            for (int k = 0; k < f.Length; k++)
            {
                int i0 = (int)(t0[k] * SR);
                for (int i = i0; i < n; i++)
                {
                    double t = (double)(i - i0) / SR, att = Math.Min(1.0, t / 0.007), e = Math.Exp(-t / tau);
                    d[i] += (float)(att * e * (Math.Sin(2 * Math.PI * f[k] * t) + 0.16 * Math.Exp(-t / (tau * 0.3)) * Math.Sin(2 * Math.PI * f[k] * 2.0 * t) + 0.04 * Math.Exp(-t / (tau * 0.15)) * Math.Sin(2 * Math.PI * f[k] * 4.0 * t)));
                }
            }
            return Norm(d, 0.8f);
        }

        /// <summary>깜빡이 릴레이 "똑/딱": 짧은 잡음 + 높은 울림 + 낮은 울림</summary>
        float[] Click(float fHi, float fLo)
        {
            int n = (int)(0.05f * SR); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SR;
                d[i] = (float)(0.22 * Math.Exp(-t / 0.0015) * (rng.NextDouble() * 2 - 1) + 0.45 * Math.Exp(-t / 0.006) * Math.Sin(2 * Math.PI * fHi * t) + 0.5 * Math.Exp(-t / 0.011) * Math.Sin(2 * Math.PI * fLo * t));
            }
            return Norm(d, 0.9f);
        }

        /// <summary>빗소리: 분홍빛 잡음(높은 쪽 부드럽게) + 빗방울 톡톡(무작위 짧은 울림), 3초 고리</summary>
        float[] RainNoise()
        {
            int n = SR * 3, m = SR / 4; var w = new float[n + m];
            var hp = High(300, 0.7); var lp = Low(5200, 0.7); double b0 = 0, b1 = 0, b2 = 0;
            for (int i = 0; i < w.Length; i++)
            {
                double r = rng.NextDouble() * 2 - 1;
                b0 = 0.99765 * b0 + r * 0.099; b1 = 0.963 * b1 + r * 0.2965; b2 = 0.57 * b2 + r * 1.0526;     // 분홍 잡음
                w[i] = (float)lp.F(hp.F(b0 + b1 + b2 + r * 0.18)) * 0.6f;
            }
            for (int k = 0; k < 900; k++)
            {
                int i0 = rng.Next(w.Length - 400); double f = 1800 + rng.NextDouble() * 2600, a = 0.2 + rng.NextDouble() * 0.5;
                for (int i = 0; i < 380; i++) { double t = (double)i / SR; w[i0 + i] += (float)(a * Math.Exp(-t / 0.0022) * Math.Sin(2 * Math.PI * f * t)); }
            }
            return Norm(LoopFade(w, n, m), 0.7f);
        }

        /// <summary>젖은 노면: 높은 띠 잡음(쉬익)</summary>
        float[] Hiss()
        {
            int n = SR * 3, m = SR / 4; var w = new float[n + m];
            var hp = High(1500, 0.7); var lp = Low(7000, 0.7);
            for (int i = 0; i < w.Length; i++) { double r = rng.NextDouble() * 2 - 1; w[i] = (float)lp.F(hp.F(r)); }
            return Norm(LoopFade(w, n, m), 0.6f);
        }

        /// <summary>다리 이음매: 낮은 "둥" + 짧은 잡음</summary>
        float[] Thump()
        {
            int n = (int)(0.3f * SR); var d = new float[n]; double lp = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SR; lp = lp + 0.08 * ((rng.NextDouble() * 2 - 1) - lp);
                d[i] = (float)(Math.Exp(-t / 0.05) * Math.Sin(2 * Math.PI * (52 + 30 * Math.Exp(-t / 0.03)) * t) + 1.2 * Math.Exp(-t / 0.012) * lp);
            }
            return Norm(d, 0.9f);
        }

        static float[] Norm(float[] d, float peak)
        {
            float m = 1e-6f; foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
            float k = peak / m; for (int i = 0; i < d.Length; i++) d[i] *= k;
            return d;
        }
    }
}
