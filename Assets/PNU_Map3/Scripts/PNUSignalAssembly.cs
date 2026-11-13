// 신호등 조립: 도로명판·방면 표지·규제 표지(지주)를 신호등 모델에 붙임
// - 인스펙터에서 켜고 끄거나 위치를 바꾼 뒤, 컴포넌트 메뉴(⋮) "다시 조립"을 누르면 다시 만들어짐
// - 판 모양은 단순한 사각판(앞면: 아틀라스 그림, 뒷면: 회색). 나중에 판 모델로 바꿔 끼워도 됨
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU
{
    [Serializable]
    public class SignCell
    {
        public string key; public float u0, v0, u1, v1, w, h;
    }
    [Serializable]
    public class SignPlacement
    {
        public bool enabled = true;
        public SignCell cell;
        public Vector3 localPos;      // 신호등 모델 기준(앞면 -Z, 팔 -X 방향, 높이 Y)
    }

    public class PNUSignalAssembly : MonoBehaviour
    {
        public Material atlasMaterial, backMaterial, bracketMaterial;
        public SignPlacement nameSign = new SignPlacement();
        public SignPlacement guideSign = new SignPlacement();
        public List<SignPlacement> poleSigns = new List<SignPlacement>();
        public float armHeight = 6.0f;

        const string GROUP = "Signs";

        [ContextMenu("다시 조립")]
        public void Rebuild()
        {
            var old = transform.Find(GROUP);
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }
            var g = new GameObject(GROUP).transform; g.SetParent(transform, false);
            if (nameSign != null && nameSign.enabled && nameSign.cell != null && nameSign.cell.w > 0) MakePlate(g, "NameSign", nameSign, true);
            if (guideSign != null && guideSign.enabled && guideSign.cell != null && guideSign.cell.w > 0) MakePlate(g, "GuideSign", guideSign, true);
            if (poleSigns != null)
                for (int i = 0; i < poleSigns.Count; i++)
                    if (poleSigns[i] != null && poleSigns[i].enabled && poleSigns[i].cell != null && poleSigns[i].cell.w > 0) MakePlate(g, "PoleSign_" + i, poleSigns[i], false);
        }

        void MakePlate(Transform parent, string name, SignPlacement p, bool onArm)
        {
            var c = p.cell; float w = c.w, h = c.h;
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = p.localPos;
            // 앞면(-Z를 봄)
            var front = new GameObject("Front"); front.transform.SetParent(go.transform, false);
            front.AddComponent<MeshFilter>().sharedMesh = Quad(w, h, c.u0, c.v0, c.u1, c.v1, false, -0.012f);
            front.AddComponent<MeshRenderer>().sharedMaterial = atlasMaterial;
            // 뒷면
            var back = new GameObject("Back"); back.transform.SetParent(go.transform, false);
            back.AddComponent<MeshFilter>().sharedMesh = Quad(w, h, 0, 0, 1, 1, true, 0.012f);
            back.AddComponent<MeshRenderer>().sharedMaterial = backMaterial;
            // 고정대(팔/지주와 판 사이)
            var br = GameObject.CreatePrimitive(PrimitiveType.Cube); br.name = "Bracket"; br.transform.SetParent(go.transform, false);
            var col = br.GetComponent<Collider>(); if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            if (onArm)
            {
                bool above = p.localPos.y > armHeight;
                float top = above ? -h * 0.5f : h * 0.5f;
                float len = Mathf.Abs(p.localPos.y - armHeight) - h * 0.5f;
                br.transform.localScale = new Vector3(0.06f, Mathf.Max(0.05f, len), 0.06f);
                br.transform.localPosition = new Vector3(0, top + (above ? -1 : 1) * Mathf.Max(0.05f, len) * 0.5f, 0.05f);
            }
            else
            {
                br.transform.localScale = new Vector3(0.08f, 0.08f, Mathf.Abs(p.localPos.z));
                br.transform.localPosition = new Vector3(0, 0, -p.localPos.z * 0.5f);
            }
            br.GetComponent<MeshRenderer>().sharedMaterial = bracketMaterial;
        }

        static Mesh Quad(float w, float h, float u0, float v0, float u1, float v1, bool back, float z)
        {
            var m = new Mesh(); m.name = back ? "SignBack" : "SignFront";
            float x0 = -w * 0.5f, x1 = w * 0.5f, y0 = -h * 0.5f, y1 = h * 0.5f;
            m.vertices = new[] { new Vector3(x0, y0, z), new Vector3(x0, y1, z), new Vector3(x1, y1, z), new Vector3(x1, y0, z) };
            if (!back)
            {
                // 앞(-Z)에서 볼 때 오른쪽이 +X → 그림 왼쪽(u0)이 x0
                m.uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
                m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            }
            else
            {
                m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            }
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
