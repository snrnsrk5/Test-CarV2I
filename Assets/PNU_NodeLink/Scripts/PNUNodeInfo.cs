// 표준노드링크 보기 — 노드 하나의 속성
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PNU.NodeLink
{
    /// <summary>노드 하나의 속성 — 들어오는/나가는 링크, 회전 제한</summary>
    public class PNUNodeInfo : MonoBehaviour
    {
        public string nodeId, nodeType, nodeName, turnP;
        public List<string> inLinks = new List<string>();
        public List<string> outLinks = new List<string>();
        public List<string> turnRules = new List<string>();
    }
}
