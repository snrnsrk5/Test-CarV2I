// PNU 도로 생성기 — 입력 읽기(탭 구분 텍스트: N 노드, L 링크, T 회전정보, G 높이 격자)
//   N id type name x z
//   L id from to name rank type connect lanes speed n x0 z0 x1 z1 ...
//   T node fromLink toLink type
//   G x0 zTop step nx nz h...   (행 0 = 북쪽)
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PNU.RoadGen
{
    public class InputData
    {
        public List<InNode> nodes = new List<InNode>();
        public List<InLink> links = new List<InLink>();
        public List<InTurn> turns = new List<InTurn>();
        public HeightGrid dem;
    }

    public static class InputReader
    {
        static double D(string s) { return double.Parse(s, CultureInfo.InvariantCulture); }
        public static InputData Parse(IEnumerable<string> lines)
        {
            var d = new InputData(); var layers = new Dictionary<string, int>();
            foreach (var raw in lines)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var line = raw.TrimEnd('\r');
                var a = line.Split('\t');
                if (a[0] == "N") d.nodes.Add(new InNode { id = a[1], type = a[2], name = a[3], p = new V2(D(a[4]), D(a[5])) });
                else if (a[0] == "L")
                {
                    int n = int.Parse(a[10]); var pts = new List<V2>(n);
                    for (int i = 0; i < n; i++) pts.Add(new V2(D(a[11 + 2 * i]), D(a[12 + 2 * i])));
                    d.links.Add(new InLink { id = a[1], f = a[2], t = a[3], name = a[4], rank = a[5], type = a[6], connect = a[7], lanes = int.Parse(a[8]), spd = int.Parse(a[9]), pts = pts });
                }
                else if (a[0] == "T") d.turns.Add(new InTurn { node = a[1], from = a[2], to = a[3], type = a[4] });
                else if (a[0] == "S" && a.Length >= 3) { int lv; if (int.TryParse(a[2], out lv)) layers[a[1]] = lv; }   // S 링크 OSM층
                else if (a[0] == "G")
                {
                    var g = new HeightGrid { x0 = D(a[1]), z1 = D(a[2]), step = D(a[3]), nx = int.Parse(a[4]), nz = int.Parse(a[5]) };
                    g.h = new float[g.nx * g.nz];
                    for (int i = 0; i < g.h.Length; i++) g.h[i] = (float)D(a[6 + i]);
                    d.dem = g;
                }
            }
            if (layers.Count > 0) foreach (var l in d.links) { int lv; if (layers.TryGetValue(l.id, out lv)) l.layer = lv; }
            return d;
        }
        public static InputData ParseText(string text) { return Parse(text.Split('\n')); }
    }
}
