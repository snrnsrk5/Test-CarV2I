using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModularTrafficLights.Editor
{
    /// <summary>
    /// Fix the traffic light models in Unity's importer, without rewriting the FBX files.
    /// v1 flipped only the arm tip (3H_TL_Arm_ExtensionB_Geometry).
    /// v2 (PNU Drive) flipped whole meshes that faced inward, but the pole and the arm base have mixed winding
    ///    (the base flange and the arm clamp stayed inside-out), and one flat number segment was flipped by mistake.
    /// v3 (PNU Drive): for the three signal FBX files, per mesh
    ///    1) faces that share an edge get the same winding (like Blender's Recalculate Outside),
    ///    2) each connected solid piece faces outward (flat or open pieces keep the authored side),
    ///    3) normals are recalculated like Blender's "Smooth by Angle 60°" + "Weighted Normal (Face Area)".
    /// v3c: v3 called Mesh.Clear() first; inside the importer that makes a non-readable mesh refuse every later write
    ///    ("Not allowed to access vertices ... isReadable is false") and the meshes came out empty. Now the mesh is never cleared:
    ///    the original vertices keep their slots, split copies are appended, and indices are replaced with SetIndices (as v2 did).
    ///    If the first write is refused, the mesh is left exactly as imported. The fix is also skipped in Play mode.
    /// </summary>
    public sealed class TrafficLightArmNormalsPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 3;
        const float SmoothAngle = 60f;
        static readonly string[] Targets =
        {
            "/TrafficLightSystem/Models/TrafficLight_Assembly_3Head.fbx",
            "/TrafficLightSystem/Models/TrafficLight_Pedestrian_Bar.fbx",
            "/TrafficLightSystem/PedestrianSignal/PedestrianSignal.fbx",
        };

        const string Key = "ModularTrafficLights.MeshFix.v3c";

        [InitializeOnLoadMethod]
        private static void ScheduleReimport()
        {
            EditorApplication.delayCall += ReimportOnce;
            EditorApplication.playModeStateChanged -= OnPlayMode; EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange s) { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += ReimportOnce; }

        private static void ReimportOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;          // 재생 중: 편집 모드로 돌아오면 OnPlayMode 가 다시 부름
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += ReimportOnce; return; }
            if (SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, true);
            foreach (var t in Targets)
            {
                string path = "Assets" + t;
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        private static bool IsTarget(string path)
        {
            foreach (var t in Targets) if (path.EndsWith(t, StringComparison.Ordinal)) return true;
            return false;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsTarget(assetPath)) return;
            string file = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // 재생 중에는 메쉬를 못 고치므로(고치다 비게 됨) 원본 그대로 두고, 편집 모드로 돌아오면 다시 가져옴
                SessionState.SetBool(Key, false);
                Debug.LogWarning("[PNU Drive] 신호등 FBX 면·노멀(v3c): 재생 중이라 이번엔 원본 그대로 — 편집 모드로 돌아오면 다시 가져와 고침 (" + file + ")");
                return;
            }
            int meshes = 0, skipped = 0, flippedFaces = 0, faces = 0, pieces = 0, solid = 0, outwardPieces = 0;
            var turned = new List<string>();
            var seen = new HashSet<Mesh>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (!mesh || !seen.Add(mesh)) continue;
                var r = FixMesh(mesh, SmoothAngle);
                if (r == null) { skipped++; continue; }
                meshes++; faces += r.faces; flippedFaces += r.flipped; pieces += r.pieces; solid += r.solidPieces; outwardPieces += r.turnedPieces;
                if (r.flipped > 0) turned.Add(filter.name + " " + r.flipped + "/" + r.faces);
            }
            Debug.Log("[PNU Drive] 신호등 FBX 면·노멀(v3c) " + file + ": 메쉬 " + meshes + (skipped > 0 ? " (못 고친 메쉬 " + skipped + "개는 원본 그대로)" : "") + ", 조각 " + pieces + "(입체 " + solid + "), 뒤집은 면 " + flippedFaces + "/" + faces +
                      ", 노멀 " + SmoothAngle.ToString("0") + "° 각도·면적 가중으로 다시" + (turned.Count > 0 ? " — " + string.Join(", ", turned) : ""));
        }

        /// <summary>Apply PNUMeshFixCore to a mesh: flip faces, split vertices where the new normals differ, recalc tangents.</summary>
        public static PNUMeshFixCore.Result FixMesh(Mesh mesh, float smoothAngle)
        {
            int subN = mesh.subMeshCount;
            for (int s = 0; s < subN; s++) if (mesh.GetTopology(s) != MeshTopology.Triangles) return null;
            var V = mesh.vertices; int nv = V.Length;
            if (nv == 0) return null;
            var pos = new float[nv * 3];
            for (int i = 0; i < nv; i++) { pos[3 * i] = V[i].x; pos[3 * i + 1] = V[i].y; pos[3 * i + 2] = V[i].z; }
            var subs = new int[subN][]; int total = 0;
            for (int s = 0; s < subN; s++) { subs[s] = mesh.GetTriangles(s); total += subs[s].Length; }
            var all = new int[total]; int o = 0;
            for (int s = 0; s < subN; s++) { Array.Copy(subs[s], 0, all, o, subs[s].Length); o += subs[s].Length; }
            var r = PNUMeshFixCore.Fix(pos, all, smoothAngle);

            // 새 꼭짓점: (원래 꼭짓점, 새 노멀)마다 하나
            var uvs = new List<Vector4>[4];
            for (int c = 0; c < 4; c++) { uvs[c] = new List<Vector4>(); mesh.GetUVs(c, uvs[c]); if (uvs[c].Count != nv) uvs[c] = null; }
            var cols = mesh.colors32; if (cols != null && cols.Length != nv) cols = null;
            var nrm0 = mesh.normals; if (nrm0 != null && nrm0.Length != nv) nrm0 = null;
            var vb = new VertexBuilder(V, nrm0, uvs, cols);
            var newSubs = new int[subN][]; int tri = 0;
            for (int s = 0; s < subN; s++)
            {
                var src = subs[s]; var dst = new int[src.Length];
                for (int i = 0; i + 2 < src.Length; i += 3, tri++)
                {
                    int g = tri * 3;
                    Vector3 n0 = N(r.cornerNormals, g), n1 = N(r.cornerNormals, g + 1), n2 = N(r.cornerNormals, g + 2);
                    int a = vb.Take(src[i], n0), b = vb.Take(src[i + 1], n1), c = vb.Take(src[i + 2], n2);
                    if (r.flip[tri]) { dst[i] = a; dst[i + 1] = c; dst[i + 2] = b; }
                    else { dst[i] = a; dst[i + 1] = b; dst[i + 2] = c; }
                }
                newSubs[s] = dst;
            }
            // Clear() 없이: 원래 꼭짓점은 제자리, 나눈 꼭짓점은 뒤에 붙음 → 꼭짓점을 먼저 늘려도 옛 인덱스가 그대로 유효
            if (vb.pos.Count < nv || (vb.pos.Count > 65535 && mesh.indexFormat == IndexFormat.UInt16)) return null;
            mesh.SetVertices(vb.pos);
            if (mesh.vertexCount != vb.pos.Count) return null;                 // 쓰기를 거절당함 → 원본 그대로 둠
            mesh.SetNormals(vb.nrm);
            for (int c = 0; c < 4; c++) if (vb.uv[c] != null) mesh.SetUVs(c, vb.uv[c]);
            if (vb.col != null) mesh.SetColors(vb.col);
            for (int s = 0; s < subN; s++) mesh.SetIndices(newSubs[s], MeshTopology.Triangles, s, false, 0);
            mesh.RecalculateBounds();
            if (vb.uv[0] != null) mesh.RecalculateTangents();
            return r;
        }

        /// <summary>(원래 꼭짓점, 새 노멀)마다 꼭짓점 하나 — 노멀이 다른 면끼리는 나눔.
        /// 원래 꼭짓점 i 는 처음 쓰일 때 자리 i 그대로(안 쓰이는 꼭짓점도 자리 유지), 다른 노멀이 또 필요하면 뒤에 복사본</summary>
        sealed class VertexBuilder
        {
            public readonly List<Vector3> pos, nrm;
            public readonly List<Vector4>[] uv = new List<Vector4>[4];
            public readonly List<Color32> col;
            readonly List<int>[] made;
            public VertexBuilder(Vector3[] v, Vector3[] n0, List<Vector4>[] uvs, Color32[] cols)
            {
                int nv = v.Length; made = new List<int>[nv];
                pos = new List<Vector3>(v); nrm = new List<Vector3>(nv);
                for (int i = 0; i < nv; i++) nrm.Add(n0 != null ? n0[i] : Vector3.up);
                for (int c = 0; c < 4; c++) uv[c] = uvs[c] != null ? new List<Vector4>(uvs[c]) : null;
                col = cols != null ? new List<Color32>(cols) : null;
            }
            public int Take(int v, Vector3 n)
            {
                var l = made[v];
                if (l == null) { made[v] = new List<int>(2) { v }; nrm[v] = n; return v; }
                foreach (var k in l) if (Vector3.Dot(nrm[k], n) > 0.9999f) return k;
                int idx = pos.Count; pos.Add(pos[v]); nrm.Add(n);
                for (int c = 0; c < 4; c++) if (uv[c] != null) uv[c].Add(uv[c][v]);
                if (col != null) col.Add(col[v]);
                l.Add(idx); return idx;
            }
        }

        static Vector3 N(float[] a, int k) => new Vector3(a[3 * k], a[3 * k + 1], a[3 * k + 2]);
    }

    /// <summary>Pure C# mesh orientation + normal calculation (no Unity types, so it can be tested outside Unity).</summary>
    public static class PNUMeshFixCore
    {
        public sealed class Result
        {
            public bool[] flip;                  // per triangle: swap two corners
            public float[] cornerNormals;        // per triangle corner (original corner order), xyz
            public int faces, flipped, pieces, solidPieces, turnedPieces;
        }

        struct Q : IEquatable<Q>
        {
            public int x, y, z;
            public bool Equals(Q o) => x == o.x && y == o.y && z == o.z;
            public override bool Equals(object o) { return o is Q && Equals((Q)o); }
            public override int GetHashCode() => (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
        }

        public static Result Fix(float[] pos, int[] tris, float smoothDeg)
        {
            int nv = pos.Length / 3, nt = tris.Length / 3;
            var r = new Result { flip = new bool[nt], cornerNormals = new float[nt * 9], faces = nt };
            // 1) 같은 자리 꼭짓점 묶기(0.1mm)
            var weld = new int[nv]; var map = new Dictionary<Q, int>(); int nw = 0;
            for (int i = 0; i < nv; i++)
            {
                var q = new Q { x = (int)Math.Round(pos[3 * i] * 10000.0), y = (int)Math.Round(pos[3 * i + 1] * 10000.0), z = (int)Math.Round(pos[3 * i + 2] * 10000.0) };
                int w; if (!map.TryGetValue(q, out w)) { w = nw++; map[q] = w; }
                weld[i] = w;
            }
            var T = new int[nt * 3]; for (int i = 0; i < nt * 3; i++) T[i] = weld[tris[i]];
            // 면 법선(감김)·넓이
            var fn = new double[nt * 3]; var area = new double[nt]; var ctr = new double[nt * 3];
            bool[] degenerate = new bool[nt];
            for (int t = 0; t < nt; t++)
            {
                int a = tris[3 * t], b = tris[3 * t + 1], c = tris[3 * t + 2];
                double ax = pos[3 * a], ay = pos[3 * a + 1], az = pos[3 * a + 2];
                double ux = pos[3 * b] - ax, uy = pos[3 * b + 1] - ay, uz = pos[3 * b + 2] - az;
                double vx = pos[3 * c] - ax, vy = pos[3 * c + 1] - ay, vz = pos[3 * c + 2] - az;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                area[t] = l * 0.5;
                if (l < 1e-12 || T[3 * t] == T[3 * t + 1] || T[3 * t + 1] == T[3 * t + 2] || T[3 * t] == T[3 * t + 2]) { degenerate[t] = true; l = 1; }
                fn[3 * t] = nx / l; fn[3 * t + 1] = ny / l; fn[3 * t + 2] = nz / l;
                ctr[3 * t] = ax + (ux + vx) / 3; ctr[3 * t + 1] = ay + (uy + vy) / 3; ctr[3 * t + 2] = az + (uz + vz) / 3;
            }
            // 2) 변 → 면(방향 포함)
            var edges = new Dictionary<long, List<int>>();   // 값: t*3 + k (k = 변 a→b 의 시작 모서리)
            for (int t = 0; t < nt; t++)
            {
                if (degenerate[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int a = T[3 * t + k], b = T[3 * t + (k + 1) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    List<int> l; if (!edges.TryGetValue(key, out l)) { l = new List<int>(2); edges[key] = l; }
                    l.Add(t * 3 + k);
                }
            }
            // 3) 붙은 면끼리 감김 맞추기(두 면만 닿는 변으로만 퍼짐) — 조각마다
            var comp = new int[nt]; for (int t = 0; t < nt; t++) comp[t] = -1;
            var rel = new bool[nt];               // 조각 첫 면 기준으로 뒤집혀 있는지
            var stack = new Stack<int>(); var compFaces = new List<List<int>>();
            for (int s = 0; s < nt; s++)
            {
                if (comp[s] >= 0 || degenerate[s]) continue;
                int id = compFaces.Count; var list = new List<int>(); compFaces.Add(list);
                comp[s] = id; rel[s] = false; stack.Push(s);
                while (stack.Count > 0)
                {
                    int t = stack.Pop(); list.Add(t);
                    for (int k = 0; k < 3; k++)
                    {
                        int a = T[3 * t + k], b = T[3 * t + (k + 1) % 3];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        var l = edges[key]; if (l.Count != 2) continue;
                        int other = l[0] / 3 == t ? l[1] : l[0]; int u = other / 3, ku = other % 3;
                        if (u == t) continue;
                        int ua = T[3 * u + ku];
                        bool sameDir = ua == a;           // 두 면이 같은 방향으로 변을 지나면 감김이 서로 반대
                        bool want = sameDir ? !rel[t] : rel[t];
                        if (comp[u] < 0) { comp[u] = id; rel[u] = want; stack.Push(u); }
                    }
                }
            }
            r.pieces = compFaces.Count;
            // 4) 조각마다 방향: 입체면 바깥, 아니면 원래 감김이 더 넓은 쪽
            for (int id = 0; id < compFaces.Count; id++)
            {
                var list = compFaces[id];
                double keepA = 0, flipA = 0; double minx = 1e30, miny = 1e30, minz = 1e30, maxx = -1e30, maxy = -1e30, maxz = -1e30;
                foreach (int t in list)
                {
                    if (rel[t]) flipA += area[t]; else keepA += area[t];
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[3 * t + k]; double x = pos[3 * v], y = pos[3 * v + 1], z = pos[3 * v + 2];
                        if (x < minx) minx = x; if (y < miny) miny = y; if (z < minz) minz = z; if (x > maxx) maxx = x; if (y > maxy) maxy = y; if (z > maxz) maxz = z;
                    }
                }
                bool flipAll = flipA > keepA;     // 원래 감김을 가장 많이 지키는 쪽
                // 입체인지: 열린 변이 적고, 납작하지 않고, 부피가 뜻이 있음
                int open = 0, all = 0;
                foreach (int t in list)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = T[3 * t + k], b = T[3 * t + (k + 1) % 3];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        all++; if (edges[key].Count == 1) open++;
                    }
                double ex = maxx - minx, ey = maxy - miny, ez = maxz - minz;
                double e0 = Math.Min(ex, Math.Min(ey, ez)), e2 = Math.Max(ex, Math.Max(ey, ez)), e1 = ex + ey + ez - e0 - e2;
                double cx = (minx + maxx) / 2, cy = (miny + maxy) / 2, cz = (minz + maxz) / 2;
                double vol = 0;
                foreach (int t in list)
                {
                    int a = tris[3 * t], b = tris[3 * t + 1], c = tris[3 * t + 2];
                    double ax = pos[3 * a] - cx, ay = pos[3 * a + 1] - cy, az = pos[3 * a + 2] - cz;
                    double bx = pos[3 * b] - cx, by = pos[3 * b + 1] - cy, bz = pos[3 * b + 2] - cz;
                    double qx = pos[3 * c] - cx, qy = pos[3 * c + 1] - cy, qz = pos[3 * c + 2] - cz;
                    double d = ax * (by * qz - bz * qy) - ay * (bx * qz - bz * qx) + az * (bx * qy - by * qx);
                    vol += (rel[t] ? -d : d) / 6.0;      // 조각 첫 면 기준으로 맞춘 감김의 부피
                }
                // 입체(부피 부호를 믿을 수 있음): 닫혔거나(열린 변 5% 이하), 납작하지 않은(가장 얇은 폭 >= 가운데 폭의 10%) 관·상자.
                // 납작한 열린 판(지느러미·밑판·숫자 조각)은 원래 감김(넓은 쪽)을 그대로
                double box = ex * ey * ez;
                bool closed = open <= all * 0.05, flat = e0 < e1 * 0.1;
                bool solid = list.Count >= 4 && box > 1e-12 && Math.Abs(vol) >= box * 0.02 && (closed || !flat);
                if (solid)
                {
                    r.solidPieces++;
                    bool outward = vol > 0;           // rel=false 쪽 감김이 바깥이면 양수
                    bool f = !outward;                 // 그 기준에서 바깥으로
                    if (f != flipAll) r.turnedPieces++;
                    flipAll = f;
                }
                foreach (int t in list) { bool fl = rel[t] ^ flipAll; r.flip[t] = fl; if (fl) r.flipped++; }
            }
            // 5) 노멀: 꼭짓점 둘레 면 중 각도 안(60°)인 면들의 넓이 가중 평균
            var nf = new double[nt * 3];
            for (int t = 0; t < nt; t++) { double s = r.flip[t] ? -1 : 1; nf[3 * t] = fn[3 * t] * s; nf[3 * t + 1] = fn[3 * t + 1] * s; nf[3 * t + 2] = fn[3 * t + 2] * s; }
            var around = new List<int>[nw];
            for (int t = 0; t < nt; t++)
            {
                if (degenerate[t]) continue;
                for (int k = 0; k < 3; k++) { int w = T[3 * t + k]; if (around[w] == null) around[w] = new List<int>(6); if (!around[w].Contains(t)) around[w].Add(t); }
            }
            double cosA = Math.Cos(smoothDeg * Math.PI / 180.0);
            for (int t = 0; t < nt; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    double sx = 0, sy = 0, sz = 0;
                    var l = around[T[3 * t + k]];
                    if (l != null && !degenerate[t])
                        foreach (int u in l)
                        {
                            double dot = nf[3 * t] * nf[3 * u] + nf[3 * t + 1] * nf[3 * u + 1] + nf[3 * t + 2] * nf[3 * u + 2];
                            if (u != t && dot < cosA) continue;
                            double wgt = Math.Max(area[u], 1e-12);
                            sx += nf[3 * u] * wgt; sy += nf[3 * u + 1] * wgt; sz += nf[3 * u + 2] * wgt;
                        }
                    double len = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                    if (len < 1e-12) { sx = nf[3 * t]; sy = nf[3 * t + 1]; sz = nf[3 * t + 2]; len = Math.Max(1e-12, Math.Sqrt(sx * sx + sy * sy + sz * sz)); }
                    int o = (t * 3 + k) * 3;
                    r.cornerNormals[o] = (float)(sx / len); r.cornerNormals[o + 1] = (float)(sy / len); r.cornerNormals[o + 2] = (float)(sz / len);
                }
            }
            return r;
        }
    }
}
