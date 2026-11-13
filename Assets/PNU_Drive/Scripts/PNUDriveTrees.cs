// PNU_Drive 나무: 모델(trees_models.bytes) + 자리(trees_inst.bytes) → GPU 인스턴싱(게임오브젝트 없이)
// 가까이는 원래 모델, 멀리는 십자 판(임포스터). 카메라마다(편집 화면 포함) 시야·거리로 고름
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

namespace PNU.Drive
{
    [ExecuteAlways]
    public class PNUDriveTrees : MonoBehaviour
    {
        public TextAsset models, instances;
        public Material[] materials;                 // 서브메쉬 재질 번호 = 배열 번호
        public float drawDistance = 1400f;           // 이보다 멀면 안 그림
        public float lodBias = 1f;                   // 모델↔판 바꾸는 거리 배율
        public bool castShadows = true;
        public float forestLodScale = 0.45f;         // 숲 나무는 더 가까이에서만 모델
        [NonSerialized] public int speciesCount, instanceCount, drawnLast;

        class Lod { public Mesh mesh; public int[] mats; }
        class Species { public string name; public Lod[] lods; public float lodDist, radius, height; }
        class Cell { public Bounds b; public int[] sp; public Matrix4x4[] m; public Vector3[] p; public float[] lk; }

        Species[] sp;
        readonly List<Cell> cells = new List<Cell>();
        Matrix4x4[][][] buf; int[][] cnt;             // [종][LOD][1023]
        readonly Plane[] planes = new Plane[6];
        const int Batch = 1023;
        const float CellSize = 160f;

        void OnEnable()
        {
            Load();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Camera.onPreCull += OnPreCullBuiltin;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Camera.onPreCull -= OnPreCullBuiltin;
            Free();
        }

        void Free()
        {
            if (sp != null)
                foreach (var s in sp) foreach (var l in s.lods) if (l.mesh != null) { if (Application.isPlaying) Destroy(l.mesh); else DestroyImmediate(l.mesh); }
            sp = null; cells.Clear();
        }

        static byte[] Gunzip(byte[] b)
        {
            using (var gz = new GZipStream(new MemoryStream(b), CompressionMode.Decompress))
            using (var ms = new MemoryStream()) { gz.CopyTo(ms); return ms.ToArray(); }
        }

        public void Load()
        {
            Free();
            if (models == null || instances == null) return;
            try
            {
                var br = new BinaryReader(new MemoryStream(Gunzip(models.bytes)));
                if (new string(br.ReadChars(4)) != "PNUT") throw new Exception("모델 머리글 다름");
                br.ReadUInt32(); int ns = (int)br.ReadUInt32();
                sp = new Species[ns];
                for (int s = 0; s < ns; s++)
                {
                    var S = new Species(); int nl = br.ReadUInt16(); S.name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nl));
                    S.lodDist = br.ReadSingle(); S.radius = br.ReadSingle(); S.height = br.ReadSingle();
                    int nlod = (int)br.ReadUInt32(); S.lods = new Lod[nlod];
                    for (int l = 0; l < nlod; l++)
                    {
                        int nsub = (int)br.ReadUInt32();
                        var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>(); var subs = new List<int[]>(); var mats = new int[nsub];
                        for (int k = 0; k < nsub; k++)
                        {
                            mats[k] = (int)br.ReadUInt32(); int nv = (int)br.ReadUInt32(), ni = (int)br.ReadUInt32(); byte fmt = br.ReadByte();
                            int b0 = V.Count;
                            for (int v = 0; v < nv; v++) V.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                            for (int v = 0; v < nv; v++) N.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                            for (int v = 0; v < nv; v++) UV.Add(new Vector2(br.ReadSingle(), br.ReadSingle()));
                            var I = new int[ni];
                            for (int q = 0; q < ni; q++) I[q] = b0 + (fmt == 0 ? (int)br.ReadUInt16() : (int)br.ReadUInt32());
                            subs.Add(I);
                        }
                        var m = new Mesh { name = S.name + "_LOD" + l, hideFlags = HideFlags.DontSave };
                        m.indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                        m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.subMeshCount = subs.Count;
                        for (int k = 0; k < subs.Count; k++) m.SetTriangles(subs[k], k, false);
                        m.RecalculateBounds(); m.UploadMeshData(false);
                        S.lods[l] = new Lod { mesh = m, mats = mats };
                    }
                    sp[s] = S;
                }
                // 자리
                br = new BinaryReader(new MemoryStream(Gunzip(instances.bytes)));
                if (new string(br.ReadChars(4)) != "PNUI") throw new Exception("자리 머리글 다름");
                br.ReadUInt32(); int n = (int)br.ReadUInt32();
                var dict = new Dictionary<long, List<int>>();
                var P = new Vector3[n]; var M = new Matrix4x4[n]; var SPI = new int[n]; var LK = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float x = br.ReadSingle(), y = br.ReadSingle(), z = br.ReadSingle();
                    float yaw = br.ReadUInt16() * (360f / 65536f); float sc = br.ReadUInt16() / 10000f; int s = br.ReadByte(); int fl = br.ReadByte();
                    if (s >= ns) s = 0;
                    P[i] = new Vector3(x, y, z); SPI[i] = s; LK[i] = fl == 1 ? forestLodScale : 1f;
                    M[i] = Matrix4x4.TRS(P[i], Quaternion.Euler(0f, yaw, 0f), new Vector3(sc, sc, sc));
                    long key = ((long)Mathf.FloorToInt(x / CellSize) << 32) ^ (uint)Mathf.FloorToInt(z / CellSize);
                    List<int> L; if (!dict.TryGetValue(key, out L)) { L = new List<int>(); dict[key] = L; }
                    L.Add(i);
                }
                foreach (var kv in dict)
                {
                    var L = kv.Value; var c = new Cell { sp = new int[L.Count], m = new Matrix4x4[L.Count], p = new Vector3[L.Count], lk = new float[L.Count] };
                    var b = new Bounds(P[L[0]], Vector3.zero);
                    for (int k = 0; k < L.Count; k++)
                    {
                        int i = L[k]; c.sp[k] = SPI[i]; c.m[k] = M[i]; c.p[k] = P[i]; c.lk[k] = LK[i];
                        var S = sp[SPI[i]]; float r = S.radius * M[i].m11; float h = S.height * M[i].m11;
                        b.Encapsulate(new Bounds(P[i] + Vector3.up * h * 0.5f, new Vector3(2 * r, h, 2 * r)));
                    }
                    c.b = b; cells.Add(c);
                }
                buf = new Matrix4x4[ns][][]; cnt = new int[ns][];
                for (int s = 0; s < ns; s++)
                {
                    buf[s] = new Matrix4x4[sp[s].lods.Length][]; cnt[s] = new int[sp[s].lods.Length];
                    for (int l = 0; l < sp[s].lods.Length; l++) buf[s][l] = new Matrix4x4[Batch];
                }
                speciesCount = ns; instanceCount = n;
                Debug.Log("[PNU Drive] 나무 " + ns + "종, " + n + "그루, 칸 " + cells.Count);
            }
            catch (Exception e) { Debug.LogError("[PNU Drive] 나무 읽기 실패: " + e.Message); Free(); }
        }

        void OnPreCullBuiltin(Camera cam) { if (GraphicsSettings.currentRenderPipeline == null) Draw(cam); }
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam) { Draw(cam); }

        void Draw(Camera cam)
        {
            if (sp == null || materials == null || cam == null) return;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            float dd = Mathf.Min(drawDistance, cam.farClipPlane); float dd2 = dd * dd;
            // 화각에 따른 거리 보정(망원일수록 멀리까지 모델)
            float fovK = Mathf.Clamp(60f / Mathf.Max(5f, cam.fieldOfView), 0.5f, 6f) * lodBias;
            drawnLast = 0;
            for (int s = 0; s < sp.Length; s++) for (int l = 0; l < cnt[s].Length; l++) cnt[s][l] = 0;
            foreach (var c in cells)
            {
                if (c.b.SqrDistance(cp) > dd2) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, c.b)) continue;
                for (int k = 0; k < c.p.Length; k++)
                {
                    float d2 = (c.p[k] - cp).sqrMagnitude; if (d2 > dd2) continue;
                    int s = c.sp[k]; var S = sp[s];
                    float ld = S.lodDist * fovK * c.lk[k];
                    int l = (S.lods.Length > 1 && d2 > ld * ld) ? 1 : 0;
                    buf[s][l][cnt[s][l]++] = c.m[k]; drawnLast++;
                    if (cnt[s][l] == Batch) { Flush(cam, s, l); cnt[s][l] = 0; }
                }
            }
            for (int s = 0; s < sp.Length; s++) for (int l = 0; l < cnt[s].Length; l++) if (cnt[s][l] > 0) Flush(cam, s, l);
        }

        void Flush(Camera cam, int s, int l)
        {
            var lod = sp[s].lods[l]; int n = cnt[s][l];
            for (int k = 0; k < lod.mesh.subMeshCount; k++)
            {
                int mi = lod.mats[k]; if (mi < 0 || mi >= materials.Length || materials[mi] == null) continue;
                var rp = new RenderParams(materials[mi])
                {
                    camera = cam,
                    shadowCastingMode = (castShadows && l == 0) ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer
                };
                Graphics.RenderMeshInstanced(rp, lod.mesh, k, buf[s][l], n);
            }
        }
    }
}
