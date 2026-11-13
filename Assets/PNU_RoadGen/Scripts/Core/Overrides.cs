// PNU 도로 생성기 — 사람이 고친 값(덮어쓰기). 다시 만들어도 유지되게 노드링크 ID 로 된 고정 키로 찾음
//   도로 키 = "R:" + 첫 링크 ID, 교차로 키 = "J:" + 묶인 노드 중 가장 작은 ID
using System;
using System.Collections.Generic;
using System.Linq;

namespace PNU.RoadGen
{
    [Serializable]
    public class RoadOverride
    {
        public string key;
        public int heightMode;              // 0 자동(지형·도로유형), 1 직접(heightOffset 만큼 올리고 내림)
        public double heightOffset;         // m, + 올림(많이 올리면 고가: 땅은 그대로), − 내림(주변 땅도 같이 깎임)
        public double rampLength = 60;      // 양 끝에서 이 길이에 걸쳐 서서히
        public int sidewalkLeft = -1, sidewalkRight = -1;   // -1 자동, 0 없음, 1 있음(진행 방향 기준 왼/오른)
        public int lanesFwd, lanesBwd;      // 0 = 데이터대로
        public int edgeLine = -1;           // 끝선: -1 자동(GenParams 간선/그 밖), 0 흰 실선, 1 황색 점선, 2 황색 실선, 3 황색 복선, 4 없음
        public int speedMark = -1;          // 제한속도 숫자: -1 자동(GenParams.speedMarks), 0 없음, 1 있음
        public List<RoadTextMark> texts = new List<RoadTextMark>();
    }

    /// <summary>도로 위 글자 하나(차로마다 또는 한 차로) — 문구는 법에 있는 것만 쓸 것(천천히, 어린이보호구역, 방면·시설명, 00앞 …)</summary>
    [Serializable]
    public class RoadTextMark
    {
        public string text = "천천히";      // '/' 로 줄 나눔(예: 어린이/보호구역)
        public int dir = 1;                 // +1 도로 정방향(시작→끝)으로 가는 차가 읽음, -1 역방향
        public double at = 30;              // 그 방향 들어온 쪽 끝(횡단보도 지나서)에서 글자 시작(운전자 쪽)까지 m. 음수면 앞쪽 정지선에서 거꾸로 잰 거리(글자 끝까지)
        public int lane;                    // 0 모든 차로, k = k번째 차로(1 = 중앙선 쪽)
    }

    /// <summary>끝선 종류(인도 있는 쪽): 0 흰 실선(505), 1 황색 점선(515 주차금지), 2 황색 실선(516 정차·주차금지), 3 황색 복선(516의2), 4 없음</summary>
    public static class EdgeLine { public const int Auto = -1, White = 0, YellowDash = 1, Yellow = 2, YellowDouble = 3, None = 4; }

    /// <summary>횡단보도 종류: -1 자동, 0 없음, 1 일반, 2 방향 분리(2단, 화살표), 3 교통섬 이단(준비 중 → 지금은 일반)</summary>
    public static class CrosswalkKind { public const int Auto = -1, None = 0, Standard = 1, Split = 2, Island = 3; }

    [Serializable]
    public class ArmOverride
    {
        public string roadKey; public bool atStart;        // 어느 도로의 어느 끝인지(고정 키)
        public int crosswalk = -1;                         // CrosswalkKind
        public int yellow = -1;                            // -1 자동, 0 흰색, 1 노란색(어린이보호구역)
    }

    [Serializable]
    public class JunctionOverride
    {
        public string key;
        public int crosswalks = -1;         // 교차로 전체: -1 자동, 0 없음, 1 있음
        public double cornerRadius;         // 0 = 자동(간선 10m, 이면 6m)
        public int diagonal;                // 1 = 대각선 횡단보도
        public int signalMode;              // 0 = 규칙(GenParams.signalMode), 1 제1주만, 2 제1주+제2주, 3 정지선 부근+제2주, 4 건너편만
        public List<ArmOverride> arms = new List<ArmOverride>();
        public ArmOverride Arm(Arm a) { var k = Keys.Road(a.road); foreach (var x in arms) if (x.roadKey == k && x.atStart == a.atStart) return x; return null; }
    }

    /// <summary>손으로 이은 차로 연결 하나(차로 연결 편집기): from = 들어오는 차로 끝, to = 나가는 차로 끝, via = 지나갈 점들</summary>
    [Serializable]
    public class LaneLinkOverride { public string from, to; public List<V2> via = new List<V2>(); }

    /// <summary>도로 편집기에서 고친 도로 하나: 정점(ctrl, 그 점들을 지나는 곡선 = 새 중심선) 또는 중심선(center) 그대로, 구간별 차로 수(t = 도로 길이 비율 0~1, 시작→끝)</summary>
    public class LaneSection { public double t0, t1; public int f, b; }
    public class RoadEdit { public string key, jStart, jEnd; public List<V2> center, ctrl; public List<LaneSection> lanes = new List<LaneSection>(); public bool geomDone;
        /// <summary>방향: 0 양방향 · 1 일방(시작→끝) · −1 일방(끝→시작) · −9 그대로</summary>
        public int dir = -9; }

    public class Overrides
    {
        public Dictionary<string, RoadOverride> roads = new Dictionary<string, RoadOverride>();
        public Dictionary<string, JunctionOverride> junctions = new Dictionary<string, JunctionOverride>();
        /// <summary>교차로 키 → 손으로 이은 연결(있으면 그 교차로는 자동 연결 대신 이것만, 0개도 가능)</summary>
        public Dictionary<string, List<LaneLinkOverride>> laneLinks = new Dictionary<string, List<LaneLinkOverride>>();
        /// <summary>도로 편집기 자동 연결을 그대로 받은 교차로(손 연결 아님 — 면은 보통 교차로처럼)</summary>
        public HashSet<string> edAuto = new HashSet<string>();
        public RoadOverride Road(Road r) { RoadOverride o; return roads.TryGetValue(Keys.Road(r), out o) ? o : null; }
        public JunctionOverride Junction(Junction j) { JunctionOverride o; return j != null && junctions.TryGetValue(Keys.Junction(j), out o) ? o : null; }
        public List<LaneLinkOverride> LaneLinks(Junction j) { List<LaneLinkOverride> o; return j != null && laneLinks.TryGetValue(Keys.Junction(j), out o) ? o : null; }
        /// <summary>도로 편집기에서 고친 도로들(pnu_road_edits.txt)</summary>
        public List<RoadEdit> roadEdits = new List<RoadEdit>();

        /// <summary>도로 고친 값 파일 읽기. 줄: "E 도로키 시작교차로키|- 끝교차로키|-" 로 도로 하나 시작 →
        /// "V x z x z …" 정점(시작→끝, 지나는 곡선 = 새 중심선) / "C x z …" 중심선 그대로 / "N t0 t1 정방향차로 역방향차로" 구간 차로 수(t = 길이 비율, 0 = 그대로)</summary>
        public int ReadRoadEdits(IEnumerable<string> lines)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; RoadEdit cur = null; int n = 0;
            foreach (var raw in lines)
            {
                if (string.IsNullOrEmpty(raw) || raw[0] == '#') continue;
                var a = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); if (a.Length == 0) continue;
                if (a[0] == "E" && a.Length >= 4) { cur = new RoadEdit { key = a[1], jStart = a[2], jEnd = a[3] }; roadEdits.Add(cur); n++; continue; }
                if (cur == null) continue;
                if (a[0] == "C" || a[0] == "V")
                {
                    var c = new List<V2>(); for (int i = 1; i + 1 < a.Length; i += 2) c.Add(new V2(double.Parse(a[i], ci), double.Parse(a[i + 1], ci)));
                    if (c.Count >= 2) { if (a[0] == "C") cur.center = c; else cur.ctrl = c; }
                }
                else if (a[0] == "D" && a.Length >= 2) { int d; if (int.TryParse(a[1], out d) && d >= -1 && d <= 1) cur.dir = d; }
                else if (a[0] == "N" && a.Length >= 5)
                    cur.lanes.Add(new LaneSection { t0 = double.Parse(a[1], ci), t1 = double.Parse(a[2], ci), f = int.Parse(a[3]), b = int.Parse(a[4]) });
            }
            return n;
        }

        /// <summary>차로 연결 파일(pnu_lane_links.txt) 읽기. 줄: "J 교차로키" = 손으로 고친 교차로(연결 0개여도) /
        /// "L 교차로키 from to [x z x z …]" = 연결 하나(끝 ID = 도로키|s·e|i·o|차로번호, 지나갈 점은 지역 좌표 m)</summary>
        public int ReadLaneLinks(IEnumerable<string> lines)
        {
            int n = 0; var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var raw in lines)
            {
                if (string.IsNullOrEmpty(raw) || raw[0] == '#') continue;
                var a = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (a.Length >= 2 && a[0] == "J") { if (!laneLinks.ContainsKey(a[1])) laneLinks[a[1]] = new List<LaneLinkOverride>(); continue; }
                if (a.Length < 4 || a[0] != "L") continue;
                List<LaneLinkOverride> l; if (!laneLinks.TryGetValue(a[1], out l)) laneLinks[a[1]] = l = new List<LaneLinkOverride>();
                var o = new LaneLinkOverride { from = a[2], to = a[3] };
                for (int i = 4; i + 1 < a.Length; i += 2) o.via.Add(new V2(double.Parse(a[i], ci), double.Parse(a[i + 1], ci)));
                l.Add(o); n++;
            }
            return n;
        }
    }

    public static class Keys
    {
        public static string Road(Road r)
        {
            if (r.keyOv != null) return r.keyOv;
            if (r.fwdLinks.Count > 0) return "R:" + r.fwdLinks[0];
            if (r.bwdLinks.Count > 0) return "R:" + r.bwdLinks[0];
            return "R#" + r.id;
        }
        public static string Junction(Junction j)
        {
            if (j.keyOv != null) return j.keyOv;
            if (j.nodes.Count == 0) return "J#" + j.id;
            string m = j.nodes[0]; foreach (var n in j.nodes) if (string.CompareOrdinal(n, m) < 0) m = n;
            return "J:" + m;
        }
    }
}
