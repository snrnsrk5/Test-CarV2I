// 부산대 → 광안대교 → 부경대 주행 재생 (런타임)
// - Data/pnu_drive.json, Data/npc_tracks.json 을 읽어 자차·카메라·신호·NPC·가로수/가로등을 재생
// - 메뉴 "PNU Map/Build Drive Scene (v3)" 가 씬을 만들고 이 컴포넌트를 붙여 줌
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU
{
    /// <summary>신호등 모델(사용자 TrafficLight)의 On_/Off_ 자식을 켜고 끔</summary>
    public class PNUSignalLight : MonoBehaviour
    {
        public string junction; public int arm; public int kind; public string style;
        GameObject[] on = new GameObject[4], off = new GameObject[4];   // 차량: R, Y, L, G
        GameObject pedRedOn, pedRedOff, pedGreenOn, pedGreenOff;
        GameObject[,] segOn = new GameObject[2, 7], segOff = new GameObject[2, 7];  // [0]=일의 자리(1), [1]=십의 자리(2)
        GameObject[] barOn = new GameObject[9], barOff = new GameObject[9];
        string last = ""; int lastNum = -2; int lastBars = -1;
        static readonly string[] SEG = { "t", "lt", "rt", "c", "lu", "ru", "u" };
        static readonly bool[][] DIG = {
            new[]{true,true,true,false,true,true,true}, new[]{false,false,true,false,false,true,false}, new[]{true,false,true,true,true,false,true},
            new[]{true,false,true,true,false,true,true}, new[]{false,true,true,true,false,true,false}, new[]{true,true,false,true,false,true,true},
            new[]{true,true,false,true,true,true,true}, new[]{true,false,true,false,false,true,false}, new[]{true,true,true,true,true,true,true},
            new[]{true,true,true,true,false,true,true} };

        static GameObject Find(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var tr in all) if (tr.name == name) return tr.gameObject;
            // 잘라 만든 모델은 블렌더 이름 뒤에 .001 같은 번호가 붙음(보행 숫자 꺼짐 부품 .001은 제외)
            if (!name.Contains("HN_"))
                foreach (var tr in all) if (tr.name.StartsWith(name + ".")) return tr.gameObject;
            return null;
        }
        void Awake() { Bind(); }
        public void Bind()
        {
            string[] k = { "Red", "Yellow", "Left", "Green" };
            for (int i = 0; i < 4; i++) { on[i] = Find(transform, "On_" + k[i]); off[i] = Find(transform, "Off_" + k[i]); }
            pedRedOn = Find(transform, "On_RedHN") ?? Find(transform, "On_RedH");
            pedRedOff = Find(transform, "Off_RedHN") ?? Find(transform, "Off_RedH");
            pedGreenOn = Find(transform, "On_GreenHN") ?? Find(transform, "On_GreenH");
            pedGreenOff = Find(transform, "Off_GreenHN") ?? Find(transform, "Off_GreenH");
            for (int d = 0; d < 2; d++)
                for (int s = 0; s < 7; s++)
                {
                    segOn[d, s] = Find(transform, "On_GreenHN_" + (d + 1) + SEG[s]);
                    segOff[d, s] = Find(transform, "On_GreenHN_" + (d + 1) + SEG[s] + ".001");
                }
            for (int b = 0; b < 9; b++) { barOn[b] = Find(transform, "On_Green_T" + (b + 1)); barOff[b] = Find(transform, "Off_Green_T" + (b + 1)); }
        }
        static void Set(GameObject g, bool v) { if (g != null && g.activeSelf != v) g.SetActive(v); }
        public void Apply(float t)
        {
            if (kind == 0)
            {
                string st = SignalPlans.ArmState(junction, arm, t);
                if (st == last) return; last = st;
                bool r = st == "R" || st == "L" || st == "Ly", y = st == "Gy" || st == "Ly", l = st == "L", g = st == "G";
                bool[] lit = { r, y, l, g };
                for (int i = 0; i < 4; i++) { Set(on[i], lit[i]); Set(off[i], !lit[i]); }
            }
            else
            {
                float rem, frac; bool walk = SignalPlans.PedState(junction, arm, t, out rem, out frac);
                bool blink = walk && rem < 6f && Mathf.FloorToInt(t * 2f) % 2 == 0;
                Set(pedRedOn, !walk); Set(pedRedOff, walk);
                Set(pedGreenOn, walk && !blink); Set(pedGreenOff, !(walk && !blink));
                int num = walk ? Mathf.Min(99, Mathf.CeilToInt(rem)) : -1;
                if (num != lastNum)
                {
                    lastNum = num;
                    for (int d = 0; d < 2; d++)
                    {
                        int digit = num < 0 ? -1 : (d == 0 ? num % 10 : num / 10);
                        bool show = digit >= 0 && !(d == 1 && digit == 0);
                        for (int s = 0; s < 7; s++) { bool v = show && DIG[digit < 0 ? 0 : digit][s]; Set(segOn[d, s], v); Set(segOff[d, s], !v); }
                    }
                }
                int bars = walk ? Mathf.Clamp(Mathf.CeilToInt(frac * 9f), 0, 9) : 0;
                if (bars != lastBars)
                {
                    lastBars = bars;
                    for (int b = 0; b < 9; b++) { Set(barOn[b], b < bars); Set(barOff[b], b >= bars); }
                }
            }
        }
    }
}
