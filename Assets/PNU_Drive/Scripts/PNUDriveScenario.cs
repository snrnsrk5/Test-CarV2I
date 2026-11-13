// 5/6 스토리보드 흐름(104~117) — 화면(PNUDriveHud)·매핑(PNUDriveOverlay)·비(PNUDriveWeather)·음성(PNUDriveSound sim_1~8)을
// 시나리오 계산(PNUScenarioCore, 시험 프로그램과 같은 코드)에 맞춰 움직임
//   104 운전석 1인칭 전환(sim_1) → 105~106 목적지 설정(sim_2) → 107 경로·주변 환경 매핑(sim_3) → [주행 시작]
//   → 108 교차로 통과(sim_4) → 109 차선 변경 보류(sim_5) → 110~112 사각지대 보행자(sim_6) → 113~115 악천후(sim_7) → 116~117 도착·결과(sim_8)
// 메뉴: 학습 종료 · 공간 나가기 · 조작안내 · 주행 시작/일시 정지 · 시점 변경 · 고정해제 · 학습 변경(상황으로 이동)
using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PNU.Drive
{
    public class PNUDriveScenario : MonoBehaviour
    {
        public static PNUDriveScenario Instance;
        public TextAsset scenarioJson;
        public PNUDriveEgo ego; public PNUDriveNav nav; public PNUDriveSound sound; public PNUDriveTraffic traffic;
        public PNUDriveHud hud; public PNUDriveOverlay overlay; public PNUDriveWeather weather;
        [Header("스토리보드 음성(sim_1~8, 빌더가 채움)")]
        public string[] storyKeys = new string[0]; public AudioClip[] storyClips = new AudioClip[0];
        public bool scenarioOn = true;
        public bool skipIntro = false;                     // 켜면 바로 주행(시험용)

        public enum Phase { Intro, Destination, Route, Driving, Arrived }
        public Phase phase { get; private set; }
        public ScenarioCore Core { get; private set; }
        public ScenarioData Data { get; private set; }
        float phaseT; bool introVoiced; float arriveT = -1f; bool arriveShown;
        PNUDriveNav.ZoneItem[] zones;
        readonly Dictionary<string, AudioClip> story = new Dictionary<string, AudioClip>();
        float panelHideAt = -1f; ScEvent panelEv; bool ownEs;

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        /// <summary>주행 전(인트로·목적지·경로)에는 Space 를 시나리오가 씀</summary>
        public bool HandlesInput { get { return scenarioOn && phase != Phase.Driving; } }
        public bool Active { get { return scenarioOn && isActiveAndEnabled; } }

        void Start()
        {
            if (ego == null) ego = FindObjectOfType<PNUDriveEgo>();
            if (nav == null && ego != null) nav = ego.GetComponent<PNUDriveNav>();
            if (sound == null && ego != null) sound = ego.GetComponent<PNUDriveSound>();
            if (traffic == null) traffic = FindObjectOfType<PNUDriveTraffic>();
            for (int i = 0; i < Mathf.Min(storyKeys.Length, storyClips.Length); i++) if (storyClips[i] != null) story[storyKeys[i]] = storyClips[i];
            if (scenarioJson != null)
            {
                try { Data = JsonUtility.FromJson<ScenarioData>(scenarioJson.text); } catch (Exception e) { Debug.LogWarning("[PNU Drive] scenario.json 읽기 실패 " + e.Message); }
            }
            try { if (ego != null && ego.pathJson != null) zones = JsonUtility.FromJson<ZonesOnly>(ego.pathJson.text).zones; } catch { }
            if (hud != null) { hud.ego = ego; hud.onButton = OnButton; hud.Build(); }
            if (overlay != null) { overlay.ego = ego; overlay.traffic = traffic; }
            if (weather != null) weather.ego = ego;
            if (!scenarioOn || Data == null) { enabled = scenarioOn && Data != null; if (hud != null) hud.gameObject.SetActive(false); return; }
            ego.showGui = false;
            Debug.Log("[PNU Drive] 시나리오(5/6): 음성 " + story.Count + "/8, 상황 5개, 목적지 " + (Data.dest != null ? Data.dest.name : "?"));
            if (skipIntro) { EnterDriving(); return; }
            EnterIntro();
        }
#pragma warning disable 0649
        [Serializable] class ZonesOnly { public PNUDriveNav.ZoneItem[] zones; }
#pragma warning restore 0649

        // ------------------------------------------------------------------ 흐름
        void EnterIntro()
        {
            phase = Phase.Intro; phaseT = 0f; introVoiced = false; arriveT = -1f; arriveShown = false;
            ego.playing = false; ego.camMode = PNUDriveEgo.CamMode.Driver; ego.ResetView();
            if (hud != null)
            {
                hud.FadeNow(1f); hud.Fade(0f, 1.6f);
                hud.ShowMenu(true); hud.SetDrive(false, false); hud.ShowDest(false, null, 0); hud.ShowRoute(false, null, 0); hud.ShowResult(false);
                hud.ShowPanel(false); hud.ShowWarn(false); hud.ShowObjects(false); hud.ShowHelp(false); hud.ShowSwitch(false); hud.HideBanner();
            }
            if (overlay != null) overlay.on = false;
        }
        void EnterDestination()
        {
            phase = Phase.Destination; phaseT = 0f;
            if (hud != null) { hud.ShowDest(true, Data.dest, PlanMinutes()); hud.ShowRoute(false, null, 0); }
            Say("sim_2");
        }
        void EnterRoute()
        {
            phase = Phase.Route; phaseT = 0f;
            if (hud != null) { hud.ShowDest(false, null, 0); hud.ShowRoute(true, Data.dest, PlanMinutes()); hud.ShowObjects(true); }
            if (overlay != null) overlay.on = true;
            Say("sim_3");
        }
        void EnterDriving()
        {
            phase = Phase.Driving; phaseT = 0f;
            if (hud != null) { hud.ShowDest(false, null, 0); hud.ShowRoute(false, null, 0); hud.ShowObjects(true); hud.SetDrive(true, true); }
            if (overlay != null) overlay.on = true;
            ego.playing = true;
            if (sound != null) sound.Play(PNUDriveSound.Fx.Engage);
        }
        void EnterArrived()
        {
            phase = Phase.Arrived; phaseT = 0f; arriveT = Time.time; arriveShown = false;
            if (hud != null) { hud.ShowPanel(false); hud.ShowTitle("목적지 도착", 3.2f); hud.SetDrive(false, true); }
            if (sound != null) sound.Play(PNUDriveSound.Fx.Arrive);
        }
        float PlanMinutes() { return ego != null && ego.Length > 0 ? Mathf.Max(1f, ego.Length / 1000f / 52f * 60f + 1.2f) : 13f; }

        void BackToMain()
        {
            if (hud != null) hud.Fade(1f, 0.5f);
            Invoke("DoMain", 0.55f);
        }
        void DoMain()
        {
            ego.JumpTo(0f); OnJump(0f);
            if (Core != null) { Core.Reset(0f); Core.cues.Clear(); }
            EnterIntro();
        }

        void Say(string key, float delay = 0.35f)
        {
            if (sound == null) return;
            AudioClip c; if (!story.TryGetValue(key, out c)) { Debug.Log("[PNU Drive] 음성 없음: " + key); return; }
            sound.PlayStory(c, delay);
        }

        // ------------------------------------------------------------------ 버튼·키
        void OnButton(string id)
        {
            switch (id)
            {
                case "end": if (phase == Phase.Driving) { ShowResultNow("학습 결과(중간 종료)"); } else BackToMain(); break;
                case "space": BackToMain(); break;
                case "help": hud.ShowHelp(!hud.HelpOn); hud.ShowSwitch(false); break;
                case "drive":
                    if (phase == Phase.Driving) { ego.playing = !ego.playing; hud.SetDrive(ego.playing, true); }
                    else if (phase == Phase.Arrived) BackToMain();
                    else EnterDriving();
                    break;
                case "view": ego.ToggleCam(); break;
                case "lock": hud.SetLocked(!hud.locked); break;
                case "switch": hud.ShowSwitch(!hud.SwitchOn); hud.ShowHelp(false); break;
                case "dest_go": EnterRoute(); break;
                case "route_go": EnterDriving(); break;
                case "route_back": EnterDestination(); break;
                case "result_end": BackToMain(); break;
                case "go_junction": JumpToEvent(0); break;
                case "go_lane": JumpToEvent(1); break;
                case "go_ped": JumpToEvent(2); break;
                case "go_rain": JumpToEvent(3); break;
                case "go_arrive": JumpToEvent(4); break;
                case "go_start": hud.ShowSwitch(false); BackToMain(); break;
            }
        }

        /// <summary>학습 변경: 상황 조금 앞으로 이동해 바로 주행</summary>
        public void JumpToEvent(int k)
        {
            if (Data == null) return;
            float s = 0f;
            switch (k)
            {
                case 0: s = Data.junction.s0 - 3f; break;
                case 1: s = Data.lanechange.sTrig - 200f; break;
                case 2: s = Data.blindped.sBus - 300f; break;
                case 3: s = Data.rain.s0 - 260f; break;
                case 4: s = Data.arrive.s - 330f; break;
            }
            if (hud != null) { hud.ShowSwitch(false); hud.ShowResult(false); hud.ShowPanel(false); hud.ShowWarn(false); hud.HideBanner(); hud.FadeNow(1f); hud.Fade(0f, 0.8f); }
            ego.JumpTo(Mathf.Max(0f, s));
            EnterDriving();
        }

        void Keys()
        {
            if (KeyDown(KeyCode.Space) || KeyDown(KeyCode.Return))
            {
                if (phase == Phase.Intro) EnterDestination();
                else if (phase == Phase.Destination) EnterRoute();
                else if (phase == Phase.Route) EnterDriving();
                else if (phase == Phase.Arrived) BackToMain();
            }
            for (int k = 0; k < 5; k++) if (KeyDown(KeyCode.Alpha1 + k)) JumpToEvent(k);
        }

        // ------------------------------------------------------------------ 자차 작은 걸음마다(PNUDriveEgo): 차·사람 걸음 뒤, 자차 걸음 앞
        public void Tick(float h, float t)
        {
            if (!Active) return;
            EnsureCore();
            if (Core == null) return;
            if (ownEs)
            {
                var es = Core.es; var d = ego.Driver; es.valid = true; es.s = d.s; es.speed = d.v; var p = ego.PosAt(d.s); es.pos = new V3(p.x, p.y, p.z);
                Vector3 a = ego.PosAt(d.s - 2.5f), b = ego.PosAt(d.s + 2.5f), f = b - a; f.y = 0f; f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; es.fwd = new V3(f.x, 0f, f.z);
            }
            Core.Step(h, t);
        }

        public void OnJump(float s)
        {
            if (Core != null) Core.Reset(s);
            panelEv = null; panelHideAt = -1f;
            if (hud != null) { hud.ShowPanel(false); hud.ShowWarn(false); }
            if (overlay != null) { overlay.hiCar = overlay.hiCar2 = overlay.radarCar = null; overlay.hiPed = null; overlay.v2iOn = false; overlay.bandStop = -1f; overlay.mode = 0; }
        }

        void EnsureCore()
        {
            if (Core != null || Data == null || ego == null || ego.Driver == null) return;
            var tr = traffic != null && traffic.isActiveAndEnabled ? traffic : null;
            if (tr != null) tr.EnsureLoaded();
            var sig = PNUDriveSignals.Instance;
            var es = tr != null && tr.Core != null ? tr.Es : new EgoState { len = 4.7f };
            ownEs = !(tr != null && tr.Core != null);
            Core = new ScenarioCore
            {
                D = Data, tr = tr != null ? tr.Core : null, pd = tr != null ? tr.Peds : null, drv = ego.Driver, es = es,
                posAt = s => { var p = ego.PosAt(s); return new V3(p.x, p.y, p.z); }, pathLen = ego.Length,
                egoState = sig != null ? (Func<int, int, string, float, int>)sig.EgoState : null, pedModel = tr != null ? tr.ScriptedPedModel : 0
            };
            Core.Init(); Core.Reset(ego.Dist); Core.cues.Clear();
        }

        // ------------------------------------------------------------------ 화면
        void Update()
        {
            if (!Active || ego == null || !ego.Ready) return;
            phaseT += Time.deltaTime;
            Keys();
            if (phase == Phase.Intro)
            {
                ego.playing = false;
                if (!introVoiced && phaseT > 1.0f) { introVoiced = true; Say("sim_1", 0f); }
                if (introVoiced && phaseT > 2.5f && (sound == null || !sound.StoryBusy) && phaseT > 4f) EnterDestination();
            }
            else if (phase == Phase.Destination || phase == Phase.Route) ego.playing = false;
            if (Core == null) EnsureCore();
            if (Core != null) { HandleCues(); UpdateEventUi(); }
            UpdateWeather();
            UpdateObjectsList();
            // v17: V2I 판은 경로 안내·주행·도착 내내(결과판·조작 안내가 열렸을 때만 숨김)
            if (hud != null)
            {
                bool v2i = (phase == Phase.Route || phase == Phase.Driving || phase == Phase.Arrived) && !hud.ResultOn && !hud.HelpOn;
                hud.ShowV2i(v2i);
                if (v2i && Core == null) V2iNext(null);
            }
            if (phase == Phase.Arrived && !arriveShown && Time.time - arriveT > 3.0f)
            {
                arriveShown = true;
                if (hud != null) hud.ShowBanner("목적지에 도착했습니다.\n결과를 확인하고 학습을 종료하세요.", 7f);
                Say("sim_8", 0.2f); ShowResultNow("주행 결과");
            }
        }

        void HandleCues()
        {
            if (Core.cues.Count == 0) return;
            foreach (var c in Core.cues)
            {
                switch (c)
                {
                    case "J:start": OpenPanel(Core.J); Say("sim_4"); break;
                    case "J:done": ClosePanelLater(Core.J, 2.5f); break;
                    case "L:start": hud.ShowTitle("차선 변경 상황", 2.6f); OpenPanel(Core.L); Say("sim_5", 0.6f); break;
                    case "L:hold": if (sound != null) sound.Play(PNUDriveSound.Fx.Stop, 0.7f); break;
                    case "L:done": ClosePanelLater(Core.L, 2.5f); break;
                    case "B:detect":
                        hud.ShowTitle("사각지대 보행자 감지", 2.2f); hud.ShowBanner("사각지대 보행자를 감지했습니다.\n속도를 줄이고 정지 가능 여부를 확인하세요.", 7f);
                        if (sound != null) sound.Play(PNUDriveSound.Fx.Warn); OpenPanel(Core.B); Say("sim_6", 0.5f); break;
                    case "B:done": ClosePanelLater(Core.B, 2.5f); break;
                    case "R:start": hud.ShowTitle("악천후 주행", 2.6f); hud.ShowWarn(true, "카메라 인식 주의", "시야 저하"); OpenPanel(Core.R); break;
                    case "R:voice": hud.ShowBanner("악천후로 가시성이 낮습니다.\n거리와 속도 정보를 비교하며 주의 주행하세요.", 7f); Say("sim_7", 0.2f); break;
                    case "R:end": hud.ShowWarn(false); ClosePanelLater(Core.R, 1.5f); break;
                    case "A:arrive": EnterArrived(); break;
                    case "X:reset": break;
                }
            }
            Core.cues.Clear();
        }

        void OpenPanel(ScEvent e) { panelEv = e; panelHideAt = -1f; }
        void ClosePanelLater(ScEvent e, float secs) { if (panelEv == e) panelHideAt = Time.time + secs; }

        static readonly string[] IconsJ = { "signal", "car", "ped", "lane" }, IconsL = { "carstop", "car", "ruler", "lane" };

        void UpdateEventUi()
        {
            if (hud == null) return;
            if (panelEv != null && panelHideAt > 0f && Time.time > panelHideAt) { panelEv = null; panelHideAt = -1f; }
            var e = panelEv;
            if (e == null) hud.ShowPanel(false);
            else if (e == Core.J) hud.ShowPanel(true, e, false, IconsJ, null);
            else if (e == Core.L) hud.ShowPanel(true, e, false, IconsL, null);
            else if (e == Core.B) hud.ShowPanel(true, e, false, null, "위험 지속 시 차량이 자동으로 정지합니다.");
            else if (e == Core.R) hud.ShowPanel(true, e, true, null, null);
            if (overlay == null) return;
            // 매핑 강조
            overlay.hiCar = overlay.hiCar2 = overlay.radarCar = null; overlay.hiPed = null; overlay.v2iOn = false; overlay.bandStop = -1f;
            var drv = Core.drv;
            if (drv != null && drv.hazStop >= 0f && drv.hazStop - drv.s < 60f) overlay.bandStop = drv.hazStop - 0.5f;
            else if (drv != null && drv.wait > 0 && drv.wait <= 5 && drv.waitDist < 70f) overlay.bandStop = drv.s + drv.waitDist;
            // v17: V2I 는 늘 — 앞(또는 지금 지나는) 신호 교차로의 신호·남은 초를 주행 내내 보여줌
            V2iNext(drv);
            if (Core.L.state == 1)
            {
                if (Core.truck != null && !Core.truck.done) { overlay.hiCar = Core.truck; overlay.hiCarColor = PNUDriveOverlay.COrange; overlay.hiCarText = "전방 차량 정차\n거리: " + Mathf.Max(0f, Core.truckDist).ToString("0") + "m"; }
                if (Core.rear != null && !Core.rear.done && Core.lcRearGap < 60f)
                {
                    overlay.hiCar2 = Core.rear; bool bad = Core.lcHold;
                    overlay.hiCar2Color = bad ? PNUDriveOverlay.CRed : PNUDriveOverlay.CBlue;
                    overlay.hiCar2Text = Core.lcRearGap < 0f ? "후측방 차량\n옆 차로 나란히" : (bad ? "후측방 차량 접근 중\n거리: " : "후측방 차량\n거리: ") + Mathf.Max(0f, Core.lcRearGap).ToString("0.0") + "m";
                }
            }
            if (Core.B.state == 1 && Core.ped != null && Core.pedStage >= 1)
            {
                overlay.hiPed = Core.ped; overlay.hiPedColor = Core.pedStage >= 2 && Core.pedStage < 4 ? PNUDriveOverlay.CRed : PNUDriveOverlay.COrange;
                overlay.hiPedText = "사각지대 보행자 감지\n" + Mathf.Max(0f, Core.pedDist).ToString("0.0") + " m";
            }
            overlay.mode = Core.rainW > 0.35f ? 1 : 0;
            if (overlay.mode == 1 && Core.radarDist >= 0f)
            {
                // 레이더가 잡은 앞차: 자차 경로 위 가장 가까운 차
                overlay.radarCar = LeadCar();
                overlay.radarText = "전방 차량(레이더)\n거리 " + Core.radarDist.ToString("0.0") + "m · 상대 속도 " + (Core.radarRel >= 0f ? "+" : "") + Core.radarRel.ToString("0") + "km/h";
            }
        }

        TCar LeadCar()
        {
            if (Core.tr == null) return null;
            TCar best = null; float bd = 1e9f; var es = Core.es;
            foreach (var c in Core.tr.cars)
            {
                if (!c.active || c.done || !c.hasPose) continue;
                V3 d = c.pos - es.pos; float along = d.x * es.fwd.x + d.z * es.fwd.z; if (along < 2f || along > 160f) continue;
                for (float x = 2f; x <= 160f; x += 3f)
                {
                    var p = Core.posAt(Math.Min(Core.pathLen, es.s + x));
                    if (V3.Dist2DSq(p, c.pos) < 4f) { if (x < bd) { bd = x; best = c; } break; }
                }
            }
            return best;
        }

        void UpdateWeather()
        {
            float w = Core != null ? Core.rainW : 0f;
            if (Core == null && Data != null && Data.rain != null && ego != null)
            {
                float s = ego.Dist, d0 = Data.rain.s0, f = Data.rain.full, s1 = Data.rain.s1, c = Data.rain.clear;
                float q = s <= d0 ? 0f : s < f ? (s - d0) / (f - d0) : s <= s1 ? 1f : s < c ? 1f - (s - s1) / (c - s1) : 0f; w = q * q * (3f - 2f * q);
            }
            if (weather != null)
            {
                weather.w = w;
                if (!weather.HasBlockers)          // 비 막는 구역(상자)이 없을 때만 예전처럼 경로 구간으로
                {
                    bool cov = false;
                    if (zones != null) foreach (var z in zones) if (ego.Dist >= z.s0 && ego.Dist < z.s1 && z.kind != "bridge") { cov = true; break; }
                    weather.covered = cov;
                }
            }
            if (sound != null) sound.SetRain(w, weather != null && weather.covered);
        }

        /// <summary>V2I(늘): 자차 경로의 다음 신호 정지선(지나도 교차로를 빠져나갈 때까지) — 왼쪽 위 V2I 판에 늘 신호·남은 초·거리,
        /// 교차로가 300m 안이면 교차로 가운데 위에도 글자</summary>
        void V2iNext(EgoDriver drv)
        {
            var gray = new Color(0.7f, 0.76f, 0.86f);
            if (drv == null || drv.stopS == null || Core == null || Core.tr == null || Core.egoState == null)
            {
                if (hud != null) hud.SetV2i("신호 정보 받는 중", "V2I 신호 정보", gray);
                return;
            }
            var J = Core.tr.J; float s = drv.s;
            for (int k = 0; k < drv.stopS.Length; k++)
            {
                int j = drv.stopJ[k]; if (j < 0 || j >= J.Length || J[j] == null || !J[j].sig || !J[j].hasPlan) continue;
                float exitS = (J[j].es >= 0f ? J[j].es : drv.stopS[k] + 12f) + J[j].r;     // 교차로를 빠져나가는 자리
                if (exitS < s) continue;
                int g = drv.stopG != null && k < drv.stopG.Length ? drv.stopG[k] : 0;
                string mv = drv.stopMv != null && k < drv.stopMv.Length && !string.IsNullOrEmpty(drv.stopMv[k]) ? drv.stopMv[k] : "S";
                int st = Core.egoState(j, g, mv, Core.now); float rem = Core.SigRemain(j, g, Core.now);
                bool arrow = mv == "L" && drv.leftProt != null && drv.leftProt(j) && g == 0;
                string light = st == 2 ? (arrow ? "좌회전 신호" : "녹색") : st == 1 ? "황색" : st == 4 ? "적색 · 우회전 가능" : (arrow ? "적색 · 좌회전 대기" : "적색");
                float dist = drv.stopS[k] - s;
                string secs = rem > 0f ? " · " + Mathf.CeilToInt(rem) + "초" : "";
                string far = dist > 1000f ? (dist / 1000f).ToString("0.0") + "km 앞" : dist > 3f ? Mathf.RoundToInt(dist) + "m 앞" : "교차로 통과 중";
                var col = st == 2 ? new Color(0.2f, 0.75f, 0.4f) : st == 1 ? new Color(1f, 0.75f, 0.1f) : new Color(1f, 0.3f, 0.25f);
                if (hud != null) hud.SetV2i(light + secs, "V2I 신호 정보 · " + far, st == 2 ? new Color(0.36f, 0.92f, 0.52f) : st == 1 ? new Color(1f, 0.82f, 0.25f) : new Color(1f, 0.45f, 0.4f));
                if (overlay != null && dist < 300f)
                {
                    var jc = J[j].c;
                    overlay.v2iOn = true; overlay.v2iPos = new Vector3(jc.x, jc.y + 6.5f, jc.z);
                    overlay.v2iText = "신호 상태(V2I)\n" + light + secs + (dist > 3f ? " · " + Mathf.RoundToInt(dist) + "m" : "");
                    overlay.v2iColor = col;
                }
                return;
            }
            if (hud != null) hud.SetV2i("앞 신호 교차로 없음", "V2I 신호 정보", gray);
        }

        void UpdateObjectsList()
        {
            if (hud == null || overlay == null) return;
            for (int i = 0; i < 3; i++)
            {
                if (i < overlay.nearest.Count)
                {
                    var kv = overlay.nearest[i]; string ic = kv.Key == "ped" ? "ped" : kv.Key == "bus" ? "bus" : "car";
                    string nm = kv.Key == "ped" ? "보행자" : kv.Key == "bus" ? "버스" : "차량";
                    hud.SetObject(i, ic, nm + "\n" + Mathf.RoundToInt(kv.Value) + "m", Color.white);
                }
                else hud.SetObject(i, null, null, Color.white);
            }
        }

        // ------------------------------------------------------------------ 결과판(117)
        void ShowResultNow(string title)
        {
            if (hud == null || Core == null) return;
            float t = Core.driveTime, dist = Core.arrived ? ego.Length : ego.Dist;
            int nStop = Core.stops.Count;
            string[] nm = { "교차로 통과", "차선 변경 보류", "사각지대 보행자", "악천후 센서 보완" };
            var ev = new[] { Core.J, Core.L, Core.B, Core.R };
            var res = new string[4]; var st = new int[4];
            for (int i = 0; i < 4; i++)
            {
                var e = ev[i]; st[i] = e.state;
                res[i] = e.state == 2 ? e.result : e.state == 1 ? "진행 중" : e.state == 3 ? "건너뜀(이동)" : "아직 안 지남";
            }
            var sb = new System.Text.StringBuilder(); int n = 0;
            foreach (var r in Core.stops)
            {
                if (n >= 6) { sb.Append("… 외 " + (Core.stops.Count - 6) + "번"); break; }
                sb.Append(Clock(r.t)).Append("  ").Append(ego.Label(r.s)).Append(" · ").Append(r.why).Append(" · ").Append(r.dur.ToString("0")).Append("초\n"); n++;
            }
            if (n == 0) sb.Append("정지 없음");
            float avg = t > 1f ? dist / t * 3.6f : 0f;
            hud.ShowResult(true, title, Clock(t), (dist / 1000f).ToString("0.0") + "km", avg.ToString("0") + "km/h", nStop + "회", nm, res, st, sb.ToString());
            hud.ShowPanel(false); panelEv = null;
            if (phase == Phase.Driving) { ego.playing = false; hud.SetDrive(false, true); }
        }
        static string Clock(float t) { int s = Mathf.Max(0, Mathf.RoundToInt(t)); return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }

        static bool KeyDown(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; if (kb == null) return false;
            switch (k)
            {
                case KeyCode.Space: return kb.spaceKey.wasPressedThisFrame;
                case KeyCode.Return: return kb.enterKey.wasPressedThisFrame;
                case KeyCode.Alpha1: return kb.digit1Key.wasPressedThisFrame;
                case KeyCode.Alpha2: return kb.digit2Key.wasPressedThisFrame;
                case KeyCode.Alpha3: return kb.digit3Key.wasPressedThisFrame;
                case KeyCode.Alpha4: return kb.digit4Key.wasPressedThisFrame;
                case KeyCode.Alpha5: return kb.digit5Key.wasPressedThisFrame;
            }
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(k);
#else
            return false;
#endif
        }
    }
}
