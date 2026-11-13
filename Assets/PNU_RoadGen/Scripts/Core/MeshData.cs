// PNU 도로 생성기 — 결과 메쉬(재질별 삼각형) + 높이 격자 + OBJ 쓰기(시험용)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PNU.RoadGen
{
    [Serializable] public struct V3 { public double x, y, z; public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; } }

    /// <summary>도로 하나 또는 교차로 하나의 메쉬. 재질 이름별 삼각형.
    /// 앞면 규칙(유니티): 앞면 법선 = cross(v1-v0, v2-v0). 위에서 볼 때 시계 방향 = 위를 보는 앞면</summary>
    public class MeshData
    {
        public string name, kind; public int refId;
        public List<V3> v = new List<V3>();
        public List<V2> uv = new List<V2>();                         // 기본: 월드 (x, z) 미터 — 도로·교차로 이음매에서 무늬가 이어짐
        public Dictionary<string, List<int>> tris = new Dictionary<string, List<int>>();
        public int V(V2 p, double y) { v.Add(new V3(p.x, y, p.z)); uv.Add(new V2(p.x, p.z)); return v.Count - 1; }
        public int V(double x, double y, double z) { v.Add(new V3(x, y, z)); uv.Add(new V2(x, z)); return v.Count - 1; }
        public int VU(V2 p, double y, double u, double w) { v.Add(new V3(p.x, y, p.z)); uv.Add(new V2(u, w)); return v.Count - 1; }
        List<int> T(string mat) { List<int> l; if (!tris.TryGetValue(mat, out l)) { l = new List<int>(); tris[mat] = l; } return l; }
        /// <summary>위에서 볼 때 반시계(a→b→c)로 준 삼각형을 앞면이 위로 오게 추가</summary>
        public void TriCCW(string mat, int a, int b, int c) { var l = T(mat); l.Add(a); l.Add(c); l.Add(b); }
        /// <summary>순서와 상관없이 위를 보게(꼬인 다각형 부채꼴용)</summary>
        public void TriUp(string mat, int a, int b, int c)
        {
            V3 A = v[a], B = v[b], C = v[c];
            double cr = (B.x - A.x) * (C.z - A.z) - (B.z - A.z) * (C.x - A.x);
            if (Math.Abs(cr) < 1e-9) return;
            if (cr > 0) TriCCW(mat, a, b, c); else TriCCW(mat, a, c, b);
        }
        /// <summary>네모: 진행 방향 왼쪽 선 l0(뒤)·l1(앞), 오른쪽 선 r0(뒤)·r1(앞) — 위로 보이게</summary>
        public void Strip(string mat, int l0, int l1, int r0, int r1) { TriCCW(mat, r0, r1, l1); TriCCW(mat, r0, l1, l0); }
        /// <summary>세운 벽: 아래 a0,a1 / 위 b0,b1 (0→1 이 진행 방향). faceLeft = 진행 방향 왼쪽을 보는 앞면</summary>
        public void Wall(string mat, int a0, int a1, int b0, int b1, bool faceLeft)
        {
            var l = T(mat);
            if (faceLeft) { l.Add(a0); l.Add(a1); l.Add(b1); l.Add(a0); l.Add(b1); l.Add(b0); }
            else { l.Add(a0); l.Add(b1); l.Add(a1); l.Add(a0); l.Add(b0); l.Add(b1); }
        }
        public void Poly(string mat, List<V2> poly, Func<V2, double> y)
        {
            var idx = Geom.Triangulate(poly); int b = v.Count;
            foreach (var p in poly) V(p, y(p));
            var l = T(mat); foreach (var i in idx) l.Add(b + i);
        }
        public int TriCount { get { int n = 0; foreach (var l in tris.Values) n += l.Count / 3; return n; } }
    }

    /// <summary>높이 격자(북쪽 위 행부터): h[row*nx+col], x = x0 + col*step, z = z1 - row*step</summary>
    [Serializable]
    public class HeightGrid
    {
        public double x0, z1, step; public int nx, nz; public float[] h;
        [NonSerialized] public List<V2[]> holes = new List<V2[]>();      // 지형을 뚫을 삼각형들(개착 구간: 옹벽 안쪽)
        [NonSerialized] public bool[] land;                               // 땅으로 볼 칸(지면 도로 둘레) — 물 재질 안 씀
        public double At(double x, double z)
        {
            if (h == null || nx < 2 || nz < 2) return 0;
            double fx = (x - x0) / step, fy = (z1 - z) / step;
            fx = Geom.Clamp(fx, 0, nx - 1.001); fy = Geom.Clamp(fy, 0, nz - 1.001);
            int c = (int)fx, r = (int)fy; double tx = fx - c, ty = fy - r;
            double a = h[r * nx + c], b = h[r * nx + c + 1], d = h[(r + 1) * nx + c], e = h[(r + 1) * nx + c + 1];
            return (a * (1 - tx) + b * tx) * (1 - ty) + (d * (1 - tx) + e * tx) * ty;
        }
    }

    public static class ObjWriter
    {
        public static void Write(string path, List<MeshData> pieces)
        {
            var sb = new StringBuilder(); int baseV = 1;
            var ci = CultureInfo.InvariantCulture;
            foreach (var m in pieces)
            {
                sb.Append("o ").Append(m.name).Append('\n');
                foreach (var p in m.v) sb.Append("v ").Append(p.x.ToString("0.###", ci)).Append(' ').Append(p.y.ToString("0.###", ci)).Append(' ').Append(p.z.ToString("0.###", ci)).Append('\n');
                foreach (var kv in m.tris)
                {
                    sb.Append("usemtl ").Append(kv.Key).Append('\n');
                    var t = kv.Value;
                    for (int i = 0; i + 2 < t.Count; i += 3) sb.Append("f ").Append(t[i] + baseV).Append(' ').Append(t[i + 1] + baseV).Append(' ').Append(t[i + 2] + baseV).Append('\n');
                }
                baseV += m.v.Count;
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
