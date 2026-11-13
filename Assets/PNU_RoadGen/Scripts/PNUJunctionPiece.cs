// PNU 도로 생성기 — 교차로 하나. 정보 + 사람이 고칠 값(횡단보도 종류·색, 대각선, 모서리 반경)
using System;
using System.Collections.Generic;
using UnityEngine;
using PNU.RoadGen;

public enum PNUCrosswalk
{
    [InspectorName("자동 (편도 3차로 이상은 2단, 나머지 일반)")] Auto = -1,
    [InspectorName("없음")] None = 0,
    [InspectorName("일반")] Standard = 1,
    [InspectorName("방향 분리 (2단, 화살표)")] Split = 2,
    [InspectorName("교통섬 이단 (준비 중: 지금은 일반)")] Island = 3,
}

public enum PNUCrosswalkColor
{
    [InspectorName("자동 (흰색)")] Auto = -1,
    [InspectorName("흰색")] White = 0,
    [InspectorName("노란색 (어린이보호구역)")] Yellow = 1,
}

public enum PNUSignalPlacement
{
    [InspectorName("자동 (업무편람: 제1주 + 편도 3차로 이상은 건너편 제2주)")] Auto = 0,
    [InspectorName("제1주만 (교차로 건너기 전, 정지선 10m 앞 오른쪽 모서리)")] First = 1,
    [InspectorName("제1주 + 제2주 (건너편)")] FirstAndSecond = 2,
    [InspectorName("정지선 부근 + 제2주 (제1주 자리가 안 될 때)")] StopLineAndSecond = 3,
    [InspectorName("건너편만 (예전 방식)")] FarOnly = 4,
}

[Serializable]
public class PNUArmSetting
{
    [HideInInspector] public string roadKey;
    [HideInInspector] public bool atStart;
    public string label;
    public PNUCrosswalk crosswalk = PNUCrosswalk.Auto;
    public PNUCrosswalkColor color = PNUCrosswalkColor.Auto;
}

public class PNUJunctionPiece : MonoBehaviour
{
    [HideInInspector] public string key;
    [HideInInspector] public int junctionId;
    public string junctionName;
    [TextArea(2, 6)] public string info;

    [Header("교차로 전체")]
    [Tooltip("횡단보도·정지선(자동 = 큰 교차로에만)")]
    public PNUToggle crosswalks = PNUToggle.Auto;
    [Tooltip("모서리 연석 곡선 반경(m), 0 = 자동(간선 10m, 이면 6m)")]
    public float cornerRadius = 0f;
    [Tooltip("대각선 횡단보도(마주 보는 모서리끼리 X 자)")]
    public bool diagonalCrosswalk = false;
    [Tooltip("차량 신호등 배치(교통신호기 설치·관리 업무편람 3장 그림 3-2·표 3-2)")]
    public PNUSignalPlacement signalPlacement = PNUSignalPlacement.Auto;

    [Header("진입로별 횡단보도")]
    public List<PNUArmSetting> arms = new List<PNUArmSetting>();

    public bool HasOverride
    {
        get
        {
            if (crosswalks != PNUToggle.Auto || cornerRadius > 0 || diagonalCrosswalk || signalPlacement != PNUSignalPlacement.Auto) return true;
            foreach (var a in arms) if (a.crosswalk != PNUCrosswalk.Auto || a.color != PNUCrosswalkColor.Auto) return true;
            return false;
        }
    }

    public JunctionOverride ToOverride()
    {
        var o = new JunctionOverride { key = key, crosswalks = (int)crosswalks, cornerRadius = cornerRadius, diagonal = diagonalCrosswalk ? 1 : 0, signalMode = (int)signalPlacement };
        foreach (var a in arms) o.arms.Add(new ArmOverride { roadKey = a.roadKey, atStart = a.atStart, crosswalk = (int)a.crosswalk, yellow = (int)a.color });
        return o;
    }

    public void From(JunctionOverride o)
    {
        if (o == null) return;
        crosswalks = (PNUToggle)o.crosswalks; cornerRadius = (float)o.cornerRadius; diagonalCrosswalk = o.diagonal == 1; signalPlacement = (PNUSignalPlacement)o.signalMode;
        foreach (var a in arms)
            foreach (var x in o.arms)
                if (x.roadKey == a.roadKey && x.atStart == a.atStart) { a.crosswalk = (PNUCrosswalk)x.crosswalk; a.color = (PNUCrosswalkColor)x.yellow; }
    }
}
