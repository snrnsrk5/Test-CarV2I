// PNU_Drive 도로변 건물: lots.json(필지: 가운데·폭·깊이·높이·정면) → 건물 프리팹(일본 도시 에셋)을 크기 맞춰 골라 세움
// 편집·플레이 모두, 씬에는 저장 안 함(가볍게). 가까이 = 원래 모델, 멀리 = _low 모델(LODGroup), 더 멀리 = 숨김
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU.Drive
{
    [ExecuteAlways]
    public class PNUDriveCity : MonoBehaviour
    {
        [Serializable] public class Lot { public int id; public float[] p; public float w, d, h; public float[] f; public string k; public float gs; }
        [Serializable] class Lots { public Lot[] lots; }

        public TextAsset lots;
        public GameObject[] prefabs;          // 원래 모델
        public GameObject[] prefabsLow;       // _low (없으면 null)
        public Vector3[] size;                // 정면을 +Z 로 돌렸을 때 폭(x)·높이(y)·깊이(z)
        public Vector3[] center;              // 같은 기준에서 바닥 가운데(x,z) + 바닥 높이(y)
        public float[] frontYaw;              // 프리팹 정면 방향(도) — 이만큼 빼면 정면이 +Z
        public Material fallback;             // 맞는 프리팹이 없을 때 상자
        public Material plinthMat;            // 건물 밑 받침(바닥 아래 7m): 비탈에서 건물 뒤쪽이 떠 보이지 않게 — 한 메쉬로 합침
        public float plinthDepth = 7f;
        public float lod1Screen = 0.18f, cullScreen = 0.015f;
        public bool buildOnEnable = true;
        [NonSerialized] public int placed, boxes;

        void OnEnable() { if (buildOnEnable) Build(); }
        void OnDisable() { Clear(); }

        public void Clear()
        {
            var old = transform.Find("_gen");
            if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
        }

        static float Cost(Vector3 s, Lot L)
        {
            float cw = Mathf.Abs(Mathf.Log(Mathf.Max(0.1f, s.x) / Mathf.Max(0.5f, L.w)));
            float cd = Mathf.Abs(Mathf.Log(Mathf.Max(0.1f, s.z) / Mathf.Max(0.5f, L.d)));
            float ch = Mathf.Abs(Mathf.Log(Mathf.Max(0.1f, s.y) / Mathf.Max(3f, L.h)));
            return cw * 2f + cd + ch * 0.8f;
        }

        public void Build()
        {
            Clear(); placed = 0; boxes = 0;
            if (lots == null || prefabs == null || size == null || prefabs.Length == 0) return;
            Lots data;
            try { data = JsonUtility.FromJson<Lots>(lots.text); } catch (Exception e) { Debug.LogError("[PNU Drive] 필지 읽기 실패: " + e.Message); return; }
            if (data == null || data.lots == null) return;
            var root = new GameObject("_gen"); root.hideFlags = HideFlags.DontSave; root.transform.SetParent(transform, false);
            var cand = new List<KeyValuePair<float, int>>();
            var pv = new List<Vector3>(); var pi = new List<int>();
            foreach (var L in data.lots)
            {
                if (L.p == null || L.p.Length < 3 || L.f == null || L.f.Length < 2) continue;
                var rng = new System.Random(L.id * 7919 + 17);
                cand.Clear();
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (prefabs[i] == null) continue;
                    var s = size[i];
                    if (s.x > L.w * 1.2f + 1.0f || s.z > L.d * 1.3f + 2.0f) continue;     // 필지를 너무 넘으면 안 씀
                    if (s.x < L.w * 0.45f || s.z < L.d * 0.35f) continue;                 // 너무 작아도 안 씀
                    float tall = s.y > Mathf.Max(L.h * 1.7f, L.h + 14f) ? 6f : 0f;      // 원래 높이보다 훨씬 높은 탑은 다른 게 없을 때만
                    cand.Add(new KeyValuePair<float, int>(Cost(s, L) + tall + (float)rng.NextDouble() * 0.15f, i));
                }
                float yaw = Mathf.Atan2(L.f[0], L.f[1]) * Mathf.Rad2Deg;
                var pos = new Vector3(L.p[0], L.p[1], L.p[2]);
                if (cand.Count == 0) { Box(root.transform, L, pos, yaw); continue; }
                cand.Sort((x1, x2) => x1.Key.CompareTo(x2.Key));
                int top = 1; while (top < Mathf.Min(3, cand.Count) && cand[top].Key < cand[0].Key + 0.6f) top++;   // 비슷하게 맞는 것끼리만 섞기
                int pick = cand[rng.Next(top)].Value;
                var sz = size[pick];
                float sx = Mathf.Clamp(L.w / sz.x, 0.82f, 1.12f), sz_ = Mathf.Clamp(L.d / sz.z, 0.82f, 1.12f);
                if (sz.x * sx > L.w + 1.0f) sx = (L.w + 1.0f) / sz.x;
                float sy = Mathf.Clamp(L.h / sz.y, 0.92f, 1.08f);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var holder = new GameObject(prefabs[pick].name + "_" + L.id); holder.hideFlags = HideFlags.DontSave;
                holder.transform.SetParent(root.transform, false);
                holder.transform.position = pos; holder.transform.rotation = rot;
                // 프리팹 정면을 +Z 로 돌린 자식 → 크기 맞춤
                var inner = new GameObject("fit"); inner.hideFlags = HideFlags.DontSave; inner.transform.SetParent(holder.transform, false);
                inner.transform.localScale = new Vector3(sx, sy, sz_);
                var c = center[pick];
                var r0 = Quaternion.Euler(0f, -frontYaw[pick], 0f);
                var a = Instantiate(prefabs[pick]); a.hideFlags = HideFlags.DontSave;
                a.transform.SetParent(inner.transform, false); a.transform.localRotation = r0;
                a.transform.localPosition = new Vector3(-c.x, -c.y + Mathf.Min(0f, -0.15f), -c.z);
                foreach (var col in holder.GetComponentsInChildren<Collider>(true)) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
                Plinth(pv, pi, pos, rot, sz.x * sx * 0.97f, sz.z * sz_ * 0.97f);
                var own = a.GetComponentsInChildren<LODGroup>(true);
                if (own.Length > 0)
                {
                    // 프리팹에 LOD 가 이미 있음(lod0·lod1): 그대로 쓰고 마지막 단계만 멀리서 숨기게
                    foreach (var g in own)
                    {
                        var lods = g.GetLODs(); if (lods.Length == 0) continue;
                        var last = lods[lods.Length - 1];
                        if (last.screenRelativeTransitionHeight < cullScreen) { last.screenRelativeTransitionHeight = cullScreen; lods[lods.Length - 1] = last; g.SetLODs(lods); }
                    }
                }
                else
                {
                    GameObject b = null;
                    if (prefabsLow != null && pick < prefabsLow.Length && prefabsLow[pick] != null)
                    {
                        b = Instantiate(prefabsLow[pick]); b.hideFlags = HideFlags.DontSave;
                        b.transform.SetParent(inner.transform, false); b.transform.localRotation = r0; b.transform.localPosition = a.transform.localPosition;
                        foreach (var col in b.GetComponentsInChildren<Collider>(true)) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
                        foreach (var g in b.GetComponentsInChildren<LODGroup>(true)) { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }
                    }
                    var lg = holder.AddComponent<LODGroup>();
                    var r0s = a.GetComponentsInChildren<Renderer>(true);
                    if (b != null) lg.SetLODs(new[] { new LOD(lod1Screen, r0s), new LOD(cullScreen, b.GetComponentsInChildren<Renderer>(true)) });
                    else lg.SetLODs(new[] { new LOD(cullScreen, r0s) });
                    lg.RecalculateBounds();
                }
                placed++;
            }
            if (plinthMat != null && pv.Count > 0)
            {
                var m = new Mesh(); m.name = "plinths"; m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(pv); m.SetTriangles(pi, 0); m.RecalculateNormals(); m.RecalculateBounds();
                var pg = new GameObject("plinths"); pg.hideFlags = HideFlags.DontSave; pg.transform.SetParent(root.transform, false);
                pg.AddComponent<MeshFilter>().sharedMesh = m; pg.AddComponent<MeshRenderer>().sharedMaterial = plinthMat;
            }
            Debug.Log("[PNU Drive] 도로변 건물 " + placed + "채(에셋), 상자 " + boxes + ", 받침 " + pv.Count / 16);
        }

        // 건물 바닥 받침: 바닥(pos.y+0.02)에서 plinthDepth 아래까지 옆면 4개(위·아래 면 없음) — 면마다 꼭짓점 따로(모서리 각지게)
        void Plinth(List<Vector3> V, List<int> I, Vector3 pos, Quaternion rot, float w, float d)
        {
            float y0 = 0.02f, y1 = -plinthDepth;
            var c = new[] { new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0, -d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(-w / 2, 0, d / 2) };
            for (int k = 0; k < 4; k++)
            {
                var p0 = c[k]; var p1 = c[(k + 1) % 4]; int b = V.Count;
                V.Add(pos + rot * new Vector3(p0.x, y0, p0.z)); V.Add(pos + rot * new Vector3(p1.x, y0, p1.z));
                V.Add(pos + rot * new Vector3(p1.x, y1, p1.z)); V.Add(pos + rot * new Vector3(p0.x, y1, p0.z));
                I.Add(b); I.Add(b + 1); I.Add(b + 2); I.Add(b); I.Add(b + 2); I.Add(b + 3);      // 바깥에서 보면 시계 방향(유니티 앞면)
            }
        }

        void Box(Transform root, Lot L, Vector3 pos, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.hideFlags = HideFlags.DontSave; go.name = "box_" + L.id;
            var col = go.GetComponent<Collider>(); if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
            go.transform.SetParent(root, false);
            float h = Mathf.Max(3f, L.h);
            go.transform.position = pos + Vector3.up * (h / 2f - 0.3f); go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(Mathf.Max(2f, L.w), h, Mathf.Max(2f, L.d));
            if (fallback != null) go.GetComponent<Renderer>().sharedMaterial = fallback;
            boxes++;
        }
    }
}
