// 수치지형도(국토지리정보원) 비교용 오버레이 불러오기 — 내 컴퓨터에서 만든 Assets/PNU_Map3/NGII/ngii_*.json 을 읽어
// 현재 지형·도로 위에 반투명으로 얹음(실제 도로 가장자리·건물 자리와 지금 모델 비교용). 데이터는 이 컴퓨터 밖으로 나가지 않음.
// 메뉴: PNU Map > NGII 오버레이 불러오기 / NGII 오버레이 켜기·끄기
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PNUNgiiOverlay
{
    const string DIR = "Assets/PNU_Map3/NGII/";
    const string ROOTNAME = "NGII_Overlay";

    [Serializable] class Chunk { public string cat; public float[] v; public int[] t; }
    [Serializable] class FileData { public string cat; public float[] color; public Chunk[] chunks; }

    static readonly string[] GROUND = { "Terrain", "SideRoads", "Asphalt", "Pavers", "SidePavers", "Curb", "SideCurb", "Median", "SideBridgeDeck" };
    static bool IsGround(string n)
    {
        foreach (var g in GROUND) if (n.Contains(g)) return true;
        return false;
    }

    static Material MakeMat(string cat, float[] c)
    {
        var col = new Color(c.Length > 0 ? c[0] : 0.7f, c.Length > 1 ? c[1] : 0.7f, c.Length > 2 ? c[2] : 0.7f);
        col.a = c.Length > 3 ? c[3] : 0.5f;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        Material m;
        if (sh != null)
        {
            m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            m.color = col;
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else
        {
            m = new Material(Shader.Find("Sprites/Default")); m.color = col;
        }
        m.renderQueue = 3100;
        m.name = "NGII_" + cat;
        return m;
    }

    [MenuItem("PNU Map/NGII 오버레이 불러오기 (수치지형도 비교)")]
    public static void Load()
    {
        string full = Path.Combine(Application.dataPath, "PNU_Map3/NGII");
        if (!Directory.Exists(full)) { EditorUtility.DisplayDialog("NGII", "Assets/PNU_Map3/NGII 폴더가 없어요. 먼저 Tools/ngii/run_ngii_overlay.bat 을 실행하세요.", "확인"); return; }
        var files = Directory.GetFiles(full, "ngii_*.json");
        if (files.Length == 0) { EditorUtility.DisplayDialog("NGII", "ngii_*.json 이 없어요. 먼저 Tools/ngii/run_ngii_overlay.bat 을 실행하세요.", "확인"); return; }
        var old = GameObject.Find(ROOTNAME); if (old != null) UnityEngine.Object.DestroyImmediate(old);
        var root = new GameObject(ROOTNAME).transform;

        // 높이: 지금 장면의 지형·도로 메시에 임시 충돌체를 붙여 위에서 아래로 쏨(가장 높은 바닥)
        var added = new List<MeshCollider>(); var addedSet = new HashSet<Collider>();
        foreach (var mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            if (mf.sharedMesh == null || !IsGround(mf.gameObject.name)) continue;
            if (mf.gameObject.GetComponent<MeshCollider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; added.Add(mc); addedSet.Add(mc);
        }
        Physics.SyncTransforms();
        int nObj = 0, nMiss = 0;
        try
        {
            for (int fi = 0; fi < files.Length; fi++)
            {
                var fd = JsonUtility.FromJson<FileData>(File.ReadAllText(files[fi]));
                if (fd == null || fd.chunks == null) continue;
                var catRoot = new GameObject(fd.cat).transform; catRoot.SetParent(root, false);
                var mat = MakeMat(fd.cat, fd.color ?? new float[0]);
                float lift = fd.cat == "road_area" ? 0.05f : (fd.cat == "building" ? 0.12f : 0.08f);
                for (int ci = 0; ci < fd.chunks.Length; ci++)
                {
                    EditorUtility.DisplayProgressBar("NGII 오버레이", fd.cat + " " + (ci + 1) + "/" + fd.chunks.Length, (fi + (float)ci / fd.chunks.Length) / files.Length);
                    var ch = fd.chunks[ci];
                    int nv = ch.v.Length / 2;
                    var V = new Vector3[nv];
                    for (int k = 0; k < nv; k++)
                    {
                        float x = ch.v[2 * k], z = ch.v[2 * k + 1];
                        float y = 0f; bool hit = false;
                        var hits = Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f);
                        foreach (var h in hits) { if (addedSet.Contains(h.collider) || IsGround(h.collider.gameObject.name)) { if (!hit || h.point.y > y) { y = h.point.y; hit = true; } } }
                        if (!hit) nMiss++;
                        V[k] = new Vector3(x, y + lift, z);
                    }
                    // 위에서 볼 때 앞면(유니티: 시계 방향)
                    var T = (int[])ch.t.Clone();
                    for (int k = 0; k + 2 < T.Length; k += 3)
                    {
                        var a = V[T[k]]; var b = V[T[k + 1]]; var c = V[T[k + 2]];
                        float cr = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
                        if (cr > 0) { int s = T[k + 1]; T[k + 1] = T[k + 2]; T[k + 2] = s; }
                    }
                    var mesh = new Mesh();
                    mesh.indexFormat = nv > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.vertices = V; mesh.triangles = T; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    mesh.name = "NGII_" + fd.cat + "_" + ci;
                    var go = new GameObject(mesh.name); go.transform.SetParent(catRoot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
                    nObj++;
                }
            }
        }
        finally
        {
            foreach (var mc in added) if (mc != null) UnityEngine.Object.DestroyImmediate(mc);
            EditorUtility.ClearProgressBar();
        }
        Debug.Log("NGII 오버레이: 메시 " + nObj + "개, 바닥을 못 찾은 점 " + nMiss + "개 (장면에 PNU_Map3 지도가 있어야 높이가 맞아요)");
    }

    [MenuItem("PNU Map/NGII 오버레이 켜기·끄기")]
    public static void Toggle()
    {
        var go = GameObject.Find(ROOTNAME);
        if (go == null) { foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) if (t.name == ROOTNAME && t.parent == null) { go = t.gameObject; break; } }
        if (go != null) go.SetActive(!go.activeSelf);
    }
}
