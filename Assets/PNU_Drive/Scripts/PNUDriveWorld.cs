// PNU_Drive 새 주행 맵: 타일 묶음(Data/*.bytes, gzip) → 메쉬(편집·플레이 모두). 씬 파일에는 저장하지 않음(가볍게)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

namespace PNU.Drive
{
    [ExecuteAlways]
    public class PNUDriveWorld : MonoBehaviour
    {
        public TextAsset[] packs;
        public bool[] packColliders;              // 묶음마다 충돌체 만들기(도로 묶음만)
        public Material[] materials;              // 재질 번호 = 배열 번호
        public bool buildOnEnable = true;
        [NonSerialized] public int tileCount, vertexCount;
        Transform genRoot;

        void OnEnable() { if (buildOnEnable) Build(); }
        void OnDisable() { Clear(); }

        public void Clear()
        {
            var old = transform.Find("_gen");
            if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
            genRoot = null;
        }

        public void Build()
        {
            Clear();
            var go = new GameObject("_gen"); go.hideFlags = HideFlags.DontSave; go.transform.SetParent(transform, false);
            genRoot = go.transform; tileCount = 0; vertexCount = 0;
            if (packs == null) return;
            for (int i = 0; i < packs.Length; i++)
            {
                if (packs[i] == null) continue;
                bool col = packColliders != null && i < packColliders.Length && packColliders[i];
                try { LoadPack(packs[i], col); }
                catch (Exception e) { Debug.LogError("[PNU Drive] 묶음 읽기 실패 " + packs[i].name + ": " + e.Message); }
            }
            Debug.Log("[PNU Drive] 타일 " + tileCount + "개, 꼭짓점 " + vertexCount);
        }

        void LoadPack(TextAsset ta, bool collider)
        {
            byte[] raw;
            using (var gz = new GZipStream(new MemoryStream(ta.bytes), CompressionMode.Decompress))
            using (var ms = new MemoryStream()) { gz.CopyTo(ms); raw = ms.ToArray(); }
            var br = new BinaryReader(new MemoryStream(raw));
            if (new string(br.ReadChars(4)) != "PNUP") throw new Exception("머리글 다름");
            br.ReadUInt32(); uint nt = br.ReadUInt32();
            var packRoot = new GameObject(ta.name); packRoot.hideFlags = HideFlags.DontSave; packRoot.transform.SetParent(genRoot, false);
            var verts = new List<Vector3>(); var uvs = new List<Vector2>();
            for (int t = 0; t < nt; t++)
            {
                int nl = br.ReadUInt16(); string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nl));
                var org = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                uint ns = br.ReadUInt32();
                verts.Clear(); uvs.Clear();
                var subs = new List<int[]>(); var mats = new List<Material>();
                for (int k = 0; k < ns; k++)
                {
                    uint mat = br.ReadUInt32(), nv = br.ReadUInt32(), ni = br.ReadUInt32(); byte fmt = br.ReadByte(); br.ReadByte();
                    int baseV = verts.Count;
                    for (int v = 0; v < nv; v++)
                    {
                        short x = br.ReadInt16(), y = br.ReadInt16(), z = br.ReadInt16();
                        verts.Add(new Vector3(org.x + x * 0.01f, org.y + y * 0.01f, org.z + z * 0.01f));
                    }
                    for (int v = 0; v < nv; v++) { float u = br.ReadSingle(), w = br.ReadSingle(); uvs.Add(new Vector2(u, w)); }
                    var idx = new int[ni];
                    for (int q = 0; q < ni; q++) idx[q] = baseV + (fmt == 0 ? (int)br.ReadUInt16() : (int)br.ReadUInt32());
                    subs.Add(idx);
                    mats.Add(materials != null && mat < materials.Length ? materials[mat] : null);
                }
                var mesh = new Mesh(); mesh.name = name; mesh.hideFlags = HideFlags.DontSave;
                mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.subMeshCount = subs.Count;
                for (int k = 0; k < subs.Count; k++) mesh.SetTriangles(subs[k], k, false);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var tile = new GameObject(name); tile.hideFlags = HideFlags.DontSave; tile.transform.SetParent(packRoot.transform, false);
                tile.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = tile.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats.ToArray();
                if (collider) tile.AddComponent<MeshCollider>().sharedMesh = mesh;
                tileCount++; vertexCount += verts.Count;
            }
        }
    }
}
