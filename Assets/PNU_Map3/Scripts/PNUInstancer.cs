// 부산대 → 광안대교 → 부경대 주행 재생 (런타임)
// - Data/pnu_drive.json, Data/npc_tracks.json 을 읽어 자차·카메라·신호·NPC·가로수/가로등을 재생
// - 메뉴 "PNU Map/Build Drive Scene (v3)" 가 씬을 만들고 이 컴포넌트를 붙여 줌
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU
{
    /// <summary>가로수·가로등 GPU 인스턴싱(카메라 주변만)</summary>
    public class PNUInstancer : MonoBehaviour
    {
        public Mesh trunk, crown, pole, head; public Material trunkMat, crownMat, poleMat, headMat;
        public float radius = 450f;
        Vector3[] treePos; float[] treeV; Vector3[] lampPos; float[] lampYaw;
        readonly List<Matrix4x4> mT = new List<Matrix4x4>(), mC = new List<Matrix4x4>(), mP = new List<Matrix4x4>(), mH = new List<Matrix4x4>();
        readonly Matrix4x4[] buf = new Matrix4x4[1023];
        float nextCull; public Camera cam;
        public TextAsset driveJson;
        void Start()
        {
            if (driveJson != null)
            {
                var d = JsonUtility.FromJson<DriveData>(driveJson.text);
                Init(d.trees, d.lamps);
            }
            if (cam == null) cam = Camera.main;
        }
        public void Init(float[] trees, float[] lamps)
        {
            int nt = trees == null ? 0 : trees.Length / 4; treePos = new Vector3[nt]; treeV = new float[nt];
            for (int i = 0; i < nt; i++) { treePos[i] = new Vector3(trees[i * 4], trees[i * 4 + 1], trees[i * 4 + 2]); treeV[i] = trees[i * 4 + 3]; }
            int nl = lamps == null ? 0 : lamps.Length / 4; lampPos = new Vector3[nl]; lampYaw = new float[nl];
            for (int i = 0; i < nl; i++) { lampPos[i] = new Vector3(lamps[i * 4], lamps[i * 4 + 1], lamps[i * 4 + 2]); lampYaw[i] = lamps[i * 4 + 3]; }
            if (trunkMat) trunkMat.enableInstancing = true; if (crownMat) crownMat.enableInstancing = true;
            if (poleMat) poleMat.enableInstancing = true; if (headMat) headMat.enableInstancing = true;
        }
        void Cull()
        {
            mT.Clear(); mC.Clear(); mP.Clear(); mH.Clear();
            if (cam == null || treePos == null) return;
            Vector3 c = cam.transform.position; float r2 = radius * radius;
            for (int i = 0; i < treePos.Length; i++)
            {
                if ((treePos[i] - c).sqrMagnitude > r2) continue;
                float sc = 0.8f + treeV[i] * 0.1f;
                var rot = Quaternion.Euler(0, treeV[i] * 57.3f, 0);
                mT.Add(Matrix4x4.TRS(treePos[i] + Vector3.up * 1.3f * sc, rot, new Vector3(0.35f, 1.3f, 0.35f) * sc));
                mC.Add(Matrix4x4.TRS(treePos[i] + Vector3.up * 4.1f * sc, rot, new Vector3(2.9f, 3.4f, 2.9f) * sc));
            }
            for (int i = 0; i < lampPos.Length; i++)
            {
                if ((lampPos[i] - c).sqrMagnitude > r2) continue;
                var rot = Quaternion.Euler(0, lampYaw[i], 0);
                mP.Add(Matrix4x4.TRS(lampPos[i] + Vector3.up * 4.25f, rot, new Vector3(0.2f, 4.25f, 0.2f)));
                mH.Add(Matrix4x4.TRS(lampPos[i] + rot * new Vector3(0f, 8.35f, -1.2f), rot, new Vector3(0.3f, 0.15f, 2.4f)));
            }
        }
        void Draw(Mesh m, Material mat, List<Matrix4x4> list)
        {
            if (m == null || mat == null) return;
            for (int i = 0; i < list.Count; i += 1023)
            {
                int n = Mathf.Min(1023, list.Count - i);
                list.CopyTo(i, buf, 0, n);
                Graphics.DrawMeshInstanced(m, 0, mat, buf, n);
            }
        }
        void Update()
        {
            if (Time.unscaledTime > nextCull) { nextCull = Time.unscaledTime + 0.4f; Cull(); }
            Draw(trunk, trunkMat, mT); Draw(crown, crownMat, mC); Draw(pole, poleMat, mP); Draw(head, headMat, mH);
        }
    }
}
