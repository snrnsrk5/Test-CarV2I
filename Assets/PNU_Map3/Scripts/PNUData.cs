// 부산대 → 광안대교 → 부경대 주행 재생 (런타임)
// - Data/pnu_drive.json, Data/npc_tracks.json 을 읽어 자차·카메라·신호·NPC·가로수/가로등을 재생
// - 메뉴 "PNU Map/Build Drive Scene (v3)" 가 씬을 만들고 이 컴포넌트를 붙여 줌
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU
{
    [Serializable] public class StopD { public float s; public float t0; public float t1; public string label; public string hint; }
    [Serializable] public class LabelD { public float s0; public float s1; public string label; public string hint; }
    [Serializable] public class HyperD { public float s0; public float s1; public float rate; public string name; }
    [Serializable] public class ZoneD { public float s0; public float s1; public float kmh; }
    [Serializable] public class RangeD { public float s0; public float s1; }
    [Serializable] public class EgoD { public float[] s; public float[] x; public float[] y; public float[] z; public float[] t; }
    [Serializable] public class SignalD { public int kind; public float x; public float y; public float z; public float yawDeg; public string junction; public int arm; public int heads; public string style; }
    [Serializable] public class PhaseD { public string kind; public float start; public float green; }
    [Serializable] public class PlanD { public string junction; public float cycle; public float offset; public int[] mainArms; public PhaseD[] phases; }
    [Serializable]
    public class DriveData
    {
        public float total; public StopD[] stops; public LabelD[] labels; public HyperD[] hyper; public ZoneD[] zones; public RangeD[] tunnels;
        public float rainS0; public float rainS1; public float sS2; public float sS3;
        public EgoD ego; public SignalD[] signals; public PlanD[] plans; public float[] trees; public float[] lamps;
    }
    [Serializable] public class NpcD { public int kind; public string color; public float t0; public float[] path; public float[] s; }
    [Serializable] public class NpcData { public float dt; public NpcD[] npcs; }

    /// <summary>신호 주기 계산(웹 미리보기와 같은 규칙)</summary>
    public static class SignalPlans
    {
        public const float Yellow = 3f, AllRed = 1f;
        static Dictionary<string, PlanD> plans = new Dictionary<string, PlanD>();
        public static void Load(PlanD[] list)
        {
            plans.Clear();
            if (list == null) return;
            foreach (var p in list) if (p != null && p.junction != null) plans[p.junction] = p;
        }
        static bool IsMain(PlanD p, int arm)
        {
            if (p.mainArms == null) return false;
            foreach (var a in p.mainArms) if (a == arm) return true;
            return false;
        }
        /// <returns>"G","Gy","L","Ly","R"</returns>
        public static string ArmState(string junction, int arm, float t)
        {
            PlanD p;
            if (junction == null || !plans.TryGetValue(junction, out p) || p.cycle <= 0) return "G";
            float tc = Mathf.Repeat(t - p.offset, p.cycle);
            bool main = IsMain(p, arm);
            foreach (var ph in p.phases)
            {
                if (tc >= ph.start && tc < ph.start + ph.green + Yellow + AllRed)
                {
                    float dt = tc - ph.start; string k = ph.kind;
                    bool mine = (k == "A" || k == "AL") ? main : ((k == "B" || k == "BL") ? !main : false);
                    if (!mine) return "R";
                    bool left = k.EndsWith("L");
                    if (dt < ph.green) return left ? "L" : "G";
                    if (dt < ph.green + Yellow) return left ? "Ly" : "Gy";
                    return "R";
                }
            }
            return "R";
        }
        /// <summary>보행 신호: walk 여부, 남은 초, 남은 비율</summary>
        public static bool PedState(string junction, int arm, float t, out float remain, out float frac)
        {
            remain = 0; frac = 0;
            PlanD p;
            if (junction == null || !plans.TryGetValue(junction, out p) || p.cycle <= 0) return false;
            float tc = Mathf.Repeat(t - p.offset, p.cycle);
            bool main = IsMain(p, arm);
            string want = "A";
            if (main)
            {
                want = "P";
                foreach (var ph in p.phases) if (ph.kind == "B") { want = "B"; break; }
            }
            foreach (var ph in p.phases)
            {
                if (ph.kind == want && tc >= ph.start && tc < ph.start + ph.green)
                {
                    remain = ph.start + ph.green - tc; frac = remain / Mathf.Max(0.1f, ph.green); return true;
                }
            }
            return false;
        }
    }
    [Serializable] public class SignEntry { public int index; public int name = -1; public int guide = -1; public int[] regs; }
    [Serializable] public class SignsData { public string atlas; public SignCell[] cells; public SignEntry[] signals; }
}
