// 표준노드링크 보기 — 링크 하나(한 방향)의 속성
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PNU.NodeLink
{
    /// <summary>링크 하나(한 방향)의 속성 — 씬에서 선을 누르면 보임</summary>
    public class PNULinkInfo : MonoBehaviour
    {
        public string linkId, fromNode, toNode, roadName, rank, roadType, connect, roadNo, multiLink, roadUse, restVeh;
        public int lanes, speed;
        public float length;
        public Vector3 fromPos, toPos;
#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(fromPos, 3f);
            Gizmos.color = Color.red; Gizmos.DrawWireSphere(toPos, 3f);
            Handles.Label(fromPos + Vector3.up * 3f, "시작 " + fromNode);
            Handles.Label(toPos + Vector3.up * 3f, "끝 " + toNode);
        }
#endif
    }
}
