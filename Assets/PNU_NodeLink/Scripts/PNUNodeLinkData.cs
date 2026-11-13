// 표준노드링크 보기 — 데이터 형식·코드표·색 (PNUNodeLinkView 와 함께 씀)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU.NodeLink
{

    [Serializable] public class NLLink { public string id, f, t, name, rank, type, connect, multi, roadNo, roadUse, restVeh; public int lanes, spd; public float len; public float[] x, y, z; }
    [Serializable] public class NLNode { public string id, type, name, turnP; public float x, y, z; }
    [Serializable] public class NLTurn { public string node, frm, to, type, oper; }
    [Serializable] public class NLData { public string source, crs; public double E0, N0; public NLLink[] links; public NLNode[] nodes; public NLTurn[] turns; public float[] routeX, routeY, routeZ; }

    /// <summary>표준노드링크 코드 → 한글</summary>
    public static class NLCodes
    {
        public static string Rank(string c)
        {
            switch (c)
            {
                case "101": return "고속국도"; case "102": return "도시고속국도"; case "103": return "일반국도"; case "104": return "특별·광역시도";
                case "105": return "국가지원지방도"; case "106": return "지방도"; case "107": return "시·군도"; default: return "기타(" + c + ")";
            }
        }
        public static string RoadType(string c)
        {
            switch (c) { case "000": return "일반도로"; case "001": return "고가차도"; case "002": return "지하차도"; case "003": return "교량"; case "004": return "터널"; default: return c; }
        }
        public static string NodeType(string c)
        {
            switch (c)
            {
                case "101": return "교차로"; case "102": return "도로 시·종점"; case "103": return "속성 변화점"; case "104": return "도로 시설물(교량·지하차도·터널 끝)";
                case "105": return "행정 경계"; case "106": return "IC·JC"; case "107": return "최소 노드 배치점"; default: return c;
            }
        }
        public static string Turn(string c)
        {
            switch (c)
            {
                case "001": return "비보호 회전"; case "002": return "버스만 회전"; case "003": return "회전 금지"; case "011": return "U턴"; case "012": return "P턴";
                case "101": return "좌회전 금지"; case "102": return "직진 금지"; case "103": return "우회전 금지"; default: return c;
            }
        }
        public static bool IsConnect(string c) { return !(string.IsNullOrEmpty(c) || c == "0" || c == "000"); }
    }

    /// <summary>색 기준별 색·범례</summary>
    public static class NLPalette
    {
        public static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }
        public static Color LinkColor(PNUNodeLinkView.ColorBy by, PNULinkInfo l)
        {
            switch (by)
            {
                case PNUNodeLinkView.ColorBy.Lanes:
                    return Hex(l.lanes <= 1 ? "#66BB6A" : l.lanes == 2 ? "#FDD835" : l.lanes == 3 ? "#FB8C00" : l.lanes == 4 ? "#E53935" : "#8E24AA");
                case PNUNodeLinkView.ColorBy.RoadType:
                    return Hex(l.roadType == "001" ? "#E53935" : l.roadType == "002" ? "#8E24AA" : l.roadType == "003" ? "#1E88E5" : l.roadType == "004" ? "#6D4C41" : "#B0BEC5");
                case PNUNodeLinkView.ColorBy.Speed:
                    return Hex(l.speed <= 30 ? "#43A047" : l.speed <= 40 ? "#9CCC65" : l.speed <= 50 ? "#FDD835" : l.speed <= 60 ? "#FB8C00" : l.speed <= 70 ? "#E53935" : l.speed <= 80 ? "#8E24AA" : "#3949AB");
                case PNUNodeLinkView.ColorBy.Connect:
                    return Hex(NLCodes.IsConnect(l.connect) ? "#EC407A" : "#B0BEC5");
                default:
                    return Hex(l.rank == "101" ? "#7B1FA2" : l.rank == "102" ? "#C2185B" : l.rank == "103" ? "#E65100" : l.rank == "104" ? "#F9A825" :
                               l.rank == "105" ? "#2E7D32" : l.rank == "106" ? "#00838F" : l.rank == "107" ? "#795548" : "#9E9E9E");
            }
        }
        public static KeyValuePair<string, string>[] Legend(PNUNodeLinkView.ColorBy by)
        {
            switch (by)
            {
                case PNUNodeLinkView.ColorBy.Lanes: return P("#66BB6A", "1차로", "#FDD835", "2차로", "#FB8C00", "3차로", "#E53935", "4차로", "#8E24AA", "5차로 이상");
                case PNUNodeLinkView.ColorBy.RoadType: return P("#B0BEC5", "일반도로", "#E53935", "고가차도", "#8E24AA", "지하차도", "#1E88E5", "교량", "#6D4C41", "터널");
                case PNUNodeLinkView.ColorBy.Speed: return P("#43A047", "30 이하", "#9CCC65", "40", "#FDD835", "50", "#FB8C00", "60", "#E53935", "70", "#8E24AA", "80", "#3949AB", "90 이상");
                case PNUNodeLinkView.ColorBy.Connect: return P("#B0BEC5", "일반 링크", "#EC407A", "연결로(램프)");
                default: return P("#7B1FA2", "고속국도", "#C2185B", "도시고속국도", "#E65100", "일반국도", "#F9A825", "특별·광역시도", "#2E7D32", "국가지원지방도", "#00838F", "지방도", "#795548", "시·군도");
            }
        }
        public static Color NodeColor(string type)
        {
            return Hex(type == "101" ? "#D32F2F" : type == "102" ? "#212121" : type == "103" ? "#FAFAFA" : type == "104" ? "#1E88E5" : type == "105" ? "#43A047" : type == "106" ? "#EC407A" : "#9E9E9E");
        }
        public static KeyValuePair<string, string>[] NodeLegend()
        {
            return P("#D32F2F", "교차로", "#212121", "도로 시·종점", "#FAFAFA", "속성 변화점", "#1E88E5", "시설물 끝(교량·터널)", "#43A047", "행정 경계", "#EC407A", "IC·JC", "#9E9E9E", "최소 노드 배치점");
        }
        public static readonly Color Arrow = new Color(0.08f, 0.08f, 0.08f, 1f);
        public static readonly Color Route = new Color(0f, 0.9f, 1f, 1f);
        static KeyValuePair<string, string>[] P(params string[] a)
        {
            var r = new KeyValuePair<string, string>[a.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new KeyValuePair<string, string>(a[i * 2], a[i * 2 + 1]);
            return r;
        }
    }
}
