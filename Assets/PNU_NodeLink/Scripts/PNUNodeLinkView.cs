// 표준노드링크 보기 (편집기에서 데이터 확인용) — 메뉴 "PNU Map/노드링크 보기 씬 만들기" 가 씬을 만들고 이 컴포넌트를 붙임
// - 링크 = 방향별 선. 폭 = 차로 수, 화살표 = 진행 방향, 색 = 아래 '색 기준'(도로 등급·차로 수·도로 종류·제한속도·연결로)
// - 노드 = 작은 원(색 = 노드 종류). 선/원을 누르면 인스펙터에 속성
// - 씬 뷰에서 가까이 가면 도로명·차로·속도 글씨가 뜸(라벨 거리)
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PNU.NodeLink
{
    /// <summary>노드링크 보기 루트 — 설정 바꾸고 인스펙터의 [다시 그리기]</summary>
    public class PNUNodeLinkView : MonoBehaviour
    {
        public enum ColorBy
        {
            [InspectorName("도로 등급")] RoadRank,
            [InspectorName("차로 수")] Lanes,
            [InspectorName("도로 종류(고가·지하·교량·터널)")] RoadType,
            [InspectorName("제한속도")] Speed,
            [InspectorName("연결로(램프)")] Connect
        }
        [Tooltip("nodelink_view.json")] public TextAsset data;
        [Tooltip("선 색 기준")] public ColorBy colorBy = ColorBy.RoadRank;
        [Tooltip("켜면 지형 높이(기존 맵과 겹쳐 보기), 끄면 평평하게(y=0)")] public bool useTerrainHeight = true;
        [Tooltip("지형 위로 띄우는 높이(m)")] public float heightOffset = 1.5f;
        [Tooltip("선 폭 = 기본 + 차로당")] public float baseWidth = 1.2f;
        public float widthPerLane = 0.9f;
        [Tooltip("켜면 지형·건물에 가려도 항상 위에 그림")] public bool drawOnTop = true;
        public bool showNodes = true;
        public bool showRoute = true;
        [Tooltip("씬 뷰 카메라에서 이 거리 안의 링크·교차로에 글씨")] public bool showLabels = true;
        public float labelDistance = 300f;
        [Header("요약(읽기용)")]
        public int linkCount;
        public int nodeCount;
        public int turnCount;
        public string source;
        [HideInInspector] public Vector3 routeStart;
        [HideInInspector] public Vector3[] linkLabelPos;
        [HideInInspector] public string[] linkLabelText;
        [HideInInspector] public Vector3[] nodeLabelPos;
        [HideInInspector] public string[] nodeLabelText;
#if UNITY_EDITOR
        static GUIStyle sLink, sNode;
        void OnDrawGizmos()
        {
            if (!showLabels || linkLabelPos == null) return;
            var sv = SceneView.currentDrawingSceneView;
            if (sv == null || sv.camera == null) return;
            if (sLink == null)
            {
                sLink = new GUIStyle(EditorStyles.helpBox); sLink.fontSize = 10; sLink.alignment = TextAnchor.MiddleCenter; sLink.padding = new RectOffset(3, 3, 1, 1);
                sNode = new GUIStyle(EditorStyles.helpBox); sNode.fontSize = 11; sNode.fontStyle = FontStyle.Bold; sNode.alignment = TextAnchor.MiddleCenter; sNode.normal.textColor = new Color(0.75f, 0.1f, 0.1f);
            }
            Vector3 cp = sv.camera.transform.position;
            float d2 = labelDistance * labelDistance;
            int shown = 0;
            if (nodeLabelPos != null)
                for (int i = 0; i < nodeLabelPos.Length && shown < 120; i++)
                    if ((nodeLabelPos[i] - cp).sqrMagnitude < d2 * 1.5f) { Handles.Label(nodeLabelPos[i] + Vector3.up * 4f, nodeLabelText[i], sNode); shown++; }
            shown = 0;
            for (int i = 0; i < linkLabelPos.Length && shown < 300; i++)
                if ((linkLabelPos[i] - cp).sqrMagnitude < d2) { Handles.Label(linkLabelPos[i] + Vector3.up * 2f, linkLabelText[i], sLink); shown++; }
        }
#endif
    }
}
