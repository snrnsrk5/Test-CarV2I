// PNU_Drive 길안내(런타임): path.json 의 nav(돌 곳)·voice(음성 안내)·blink(깜빡이)·zones(터널·다리)를 따라
// - 화면 위 길안내 카드(테슬라 화면과 같은 그림 Tesla_Nav.png) + 남은 거리 + 깜빡이 화살표 + 안내 문장 자막
// - 소리(PNUDriveSound): 음성 안내(띵동 + 윈도 음성 wav, 없으면 띵동과 자막만), 깜빡이 똑딱, 신호 정지·초록불, 자율주행 시작, 도착, 다리 이음매, 터널 울림
// - 테슬라 가운데 화면 카드·깜빡이 화살표·차 깜빡이 등은 PNUDriveCockpit 이 여기 값(NextIndex·NextDist·BlinkSide·BlinkLamp)을 읽어 그림
// 키: M 소리 켜기/끄기. 4배 이상 빠르게 볼 때는 음성 대신 띵동만
using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PNU.Drive
{
    [Serializable] public class TeslaAtlasGlyph { public string chars; public float x0, y0, w, h; public float[] adv; }
    [Serializable] public class TeslaAtlasScreen { public float[] card, dist, sigL, sigR; }
    [Serializable] public class TeslaAtlas { public float w = 1024, h = 1024; public float[] cards, sigL, sigR; public TeslaAtlasGlyph glyph; public TeslaAtlasScreen screen; }

    public class PNUDriveNav : MonoBehaviour
    {
        [Serializable] public class NavItem { public float s; public string type, name; }
        [Serializable] public class VoiceItem { public float s; public string key, text, kind; }
        [Serializable] public class BlinkItem { public float s0, s1; public int d; }
        [Serializable] public class ZoneItem { public float s0, s1; public string kind; }
        [Serializable] class PD { public float length; public NavItem[] nav; public VoiceItem[] voice; public BlinkItem[] blink; public ZoneItem[] zones; }
        [Serializable] class DriveJ { public TeslaAtlas atlas; }

        public PNUDriveEgo ego;
        public PNUDriveSound sound;
        public TextAsset driveJson;             // PNU_Drive/Data/tesla_drive.json (글자판 자리)
        public Texture2D navTexture;            // PNU_Drive/Textures/Tesla_Nav.png
        public bool showCard = true;
        public float blinkPeriod = 0.68f;        // 깜빡이 한 번(켜짐+꺼짐) 초 ≈ 분당 88번

        PD P; TeslaAtlas A;
        int vi; float lastS = -1f; bool engaged, arrived;
        int lastWait; float lastSignalDist;
        bool lastLamp; float blinkT0; int lastSide;
        string subtitle = ""; float subtitleUntil;
        VoiceItem pending; float pendingAt;            // 스토리보드 음성이 끝날 때까지 미룬 길안내
        GUIStyle stDist, stSub, stBtn;

        public bool Ready { get { return P != null && ego != null && ego.Ready; } }
        public int NextIndex { get; private set; }
        public float NextDist { get; private set; }
        public int BlinkSide { get; private set; }
        public bool BlinkLamp { get; private set; }
        public TeslaAtlas Atlas { get { return A; } }
        public NavItem Next { get { return P != null && P.nav != null && NextIndex >= 0 && NextIndex < P.nav.Length ? P.nav[NextIndex] : null; } }

        void Start()
        {
            if (ego == null) ego = GetComponent<PNUDriveEgo>();
            if (ego == null) ego = FindObjectOfType<PNUDriveEgo>();
            if (sound == null) sound = GetComponent<PNUDriveSound>();
            if (ego != null && ego.pathJson != null) P = JsonUtility.FromJson<PD>(ego.pathJson.text);
            if (driveJson != null) { var dj = JsonUtility.FromJson<DriveJ>(driveJson.text); if (dj != null) A = dj.atlas; }
            NextIndex = -1;
            if (P != null) Debug.Log("[PNU Drive] 길안내: 돌 곳 " + Len(P.nav) + ", 음성 안내 " + Len(P.voice) + " (wav " + (sound != null ? sound.VoiceCount : 0) + "), 깜빡이 " + Len(P.blink) + ", 터널·다리 " + Len(P.zones));
        }
        static int Len(Array a) { return a == null ? 0 : a.Length; }

        public static string DistText(float d)
        {
            if (d >= 1000f) return (d / 1000f).ToString(d >= 10000f ? "0" : "0.0") + "km";
            int m = Mathf.Max(0, Mathf.RoundToInt(d / 10f) * 10);
            return m + "m";
        }

        void Update()
        {
            if (!Ready) return;
            if (KeyDown()) { if (sound != null) { sound.muted = !sound.muted; if (sound.muted) sound.StopVoice(); } }
            float s = ego.Dist;
            bool jumped = lastS < 0f || Mathf.Abs(s - lastS) > 60f || s < lastS - 2f;
            // 다음 돌 곳
            NextIndex = -1;
            if (P.nav != null) for (int i = 0; i < P.nav.Length; i++) if (P.nav[i].s > s - 3f) { NextIndex = i; break; }
            NextDist = NextIndex >= 0 ? Mathf.Max(0f, P.nav[NextIndex].s - s) : 0f;
            // 음성 안내: 지나친 자리에서 한 번(건너뛰면 조용히 넘김)
            if (P.voice != null)
            {
                if (jumped) { vi = 0; while (vi < P.voice.Length && P.voice[vi].s < s - 0.5f) vi++; if (s < 1f) engaged = false; arrived = s >= ego.Length - 1f && arrived; }
                while (vi < P.voice.Length && P.voice[vi].s <= s)
                {
                    var v = P.voice[vi];
                    if (!ego.playing) break;
                    if (v.kind == "start" && Time.timeSinceLevelLoad < 1.2f) break;          // 처음 화면이 뜨고 조금 뒤
                    Say(v); vi++;
                }
            }
            // 자율주행 시작(처음 움직일 때) — 시작 안내가 없으면 알림음만
            if (!engaged && ego.playing && ego.Speed > 0.4f && s < 60f) { engaged = true; }
            // 도착
            if (!arrived && s >= ego.Length - 0.6f) { arrived = true; }
            // 깜빡이
            int side = 0;
            if (P.blink != null) foreach (var b in P.blink) if (s >= b.s0 && s < b.s1) { side = b.d; break; }
            var scn = PNUDriveScenario.Instance;
            if (scn != null && scn.Active && scn.Core != null && scn.Core.blink != 0) side = scn.Core.blink;      // 5/6: 차선 변경 보류 중에도 깜빡이 유지
            // 미뤄 둔 길안내: 스토리보드 음성이 끝나면(6초 넘게 늦으면 버림)
            if (pending != null && sound != null && !sound.StoryBusy) { if (Time.time - pendingAt < 6f) SayNow(pending); pending = null; }
            if (side != lastSide) { blinkT0 = Time.time; lastSide = side; }
            BlinkSide = side;
            bool lamp = side != 0 && ego.playing && Mathf.Repeat(Time.time - blinkT0, blinkPeriod) < blinkPeriod * 0.5f;
            if (lamp != lastLamp && sound != null && side != 0 && ego.playing) sound.Play(lamp ? PNUDriveSound.Fx.Tick : PNUDriveSound.Fx.Tock);
            BlinkLamp = lamp; lastLamp = lamp;
            // 터널·다리(소리), 다리 들어가고 나올 때 이음매
            bool tun = false, brg = false;
            if (P.zones != null)
                foreach (var z in P.zones)
                {
                    if (s >= z.s0 && s < z.s1) { if (z.kind == "bridge") brg = true; else tun = true; }
                    if (!jumped && z.kind == "bridge" && sound != null && ((lastS < z.s0 && s >= z.s0) || (lastS < z.s1 && s >= z.s1))) sound.Joint(ego.Speed, ego.wheelBase);
                }
            if (sound != null) sound.SetZones(tun, brg);
            // 신호: 서기 시작하면 "띵", 서 있다가 초록불로 떠나면 "띠링"
            int w = ego.SignalWait;
            if (sound != null && ego.playing)
            {
                if (lastWait == 0 && (w == 1 || w == 2) && ego.SignalDist > 6f && ego.Speed > 2f) sound.Play(PNUDriveSound.Fx.Stop);
                if ((lastWait == 1 || lastWait == 2) && w == 0 && ego.Speed < 0.6f && lastSignalDist < 8f) sound.Play(PNUDriveSound.Fx.Green);
            }
            lastWait = w; lastSignalDist = ego.SignalDist;
            lastS = s;
        }

        void Say(VoiceItem v)
        {
            var scn = PNUDriveScenario.Instance;
            if (scn != null && scn.Active && v.kind == "arrive") { if (sound != null) sound.Play(PNUDriveSound.Fx.Arrive); return; }   // 도착 안내는 스토리보드(sim_8)
            if (sound != null && sound.StoryBusy) { pending = v; pendingAt = Time.time; return; }
            SayNow(v);
        }

        void SayNow(VoiceItem v)
        {
            subtitle = v.text; subtitleUntil = Time.time + 5.5f;
            if (sound == null) return;
            var chime = v.kind == "start" ? PNUDriveSound.Fx.Engage : v.kind == "arrive" ? PNUDriveSound.Fx.Arrive : PNUDriveSound.Fx.Nav;
            sound.Play(chime);
            if (ego.rate <= 2.01f) sound.PlayVoice(v.key, v.kind == "arrive" ? 0.85f : 0.6f);
        }

        static bool KeyDown()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; return kb != null && kb.mKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.M);
#else
            return false;
#endif
        }

        // ---------------------------------------------------------------- 화면(PNUDriveEgo 화면 표시를 켰을 때만)
        Rect UV(float x, float y, float w, float h) { float W = A != null ? A.w : 1024f, H = A != null ? A.h : 1024f; return new Rect(x / W, 1f - (y + h) / H, w / W, h / H); }

        void OnGUI()
        {
            if (!Ready || !ego.showGui) return;
            float k = Mathf.Clamp(Screen.height / 900f, 0.8f, 2.5f);
            var keep = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float W = Screen.width / k;
            if (stDist == null)
            {
                stDist = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft }; stDist.normal.textColor = Color.white;
                stSub = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true }; stSub.normal.textColor = Color.white;
                stBtn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            }
            // 소리 단추(오른쪽 위)
            if (sound != null && GUI.Button(new Rect(W - 96, 10, 86, 28), sound.muted ? "소리 끔 (M)" : "소리 켬 (M)", stBtn)) { sound.muted = !sound.muted; if (sound.muted) sound.StopVoice(); }
            // 길안내 카드(가운데 위, 왼쪽 정보 상자와 안 겹치게)
            var n = Next;
            if (showCard && n != null && navTexture != null && A != null && A.cards != null && NextIndex * 4 + 3 < A.cards.Length)
            {
                float sc = Mathf.Min(0.75f, (W - 420f - 110f) / 612f); if (sc < 0.45f) sc = 0.45f;
                float cw = 612f * sc, ch = 98f * sc, x = Mathf.Max(410f, (W - cw) * 0.5f), y = 10f;
                if (x + cw > W - 104f) x = Mathf.Max(410f, W - 104f - cw);
                int c = NextIndex * 4;
                GUI.DrawTextureWithTexCoords(new Rect(x, y, cw, ch), navTexture, UV(A.cards[c], A.cards[c + 1], A.cards[c + 2], A.cards[c + 3]));
                stDist.fontSize = Mathf.RoundToInt(40 * sc);
                GUI.Label(new Rect(x + 104f * sc, y, 180f * sc, ch), DistText(NextDist), stDist);
                // 깜빡이 화살표(카드 양옆)
                if (BlinkLamp && A.sigL != null && A.sigR != null)
                {
                    float a = 34f * sc / 0.75f;
                    if (BlinkSide < 0) GUI.DrawTextureWithTexCoords(new Rect(x - a - 6f, y + (ch - a) * 0.5f, a, a), navTexture, UV(A.sigL[0], A.sigL[1], A.sigL[2], A.sigL[3]));
                    else GUI.DrawTextureWithTexCoords(new Rect(x + cw + 6f, y + (ch - a) * 0.5f, a, a), navTexture, UV(A.sigR[0], A.sigR[1], A.sigR[2], A.sigR[3]));
                }
                if (Time.time < subtitleUntil && !string.IsNullOrEmpty(subtitle))
                    GUI.Label(new Rect(x, y + ch + 4f, cw, 26f), subtitle, stSub);
            }
            GUI.matrix = keep;
        }
    }
}
