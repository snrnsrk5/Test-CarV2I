// PNU 도로 생성기 — 신호등 자리(빈 오브젝트). 신호등 모양은 따로 만드는 신호등 생성기가 맡음
//   위치 = 기둥 밑(인도 윗면), 앞(+Z) = 등면이 보는 방향(다가오는 차·건너편 보행자 쪽)
//   가로형(내민식) 팔은 로컬 +X 쪽(운전자 기준 왼쪽, 차로 위)으로 armLength 만큼, 등면은 headOffsets 자리에 매닮
//   sharedPole 이 있으면 그 자리의 기둥에 같이 붙는 등(보조신호등·보행 신호등) → 기둥은 만들지 않음
//   근거: 교통신호기 설치·관리 업무편람(2026) 제1편 3장(그림 3-2, 표 3-2)
using UnityEngine;

public enum PNUSignalKind
{
    [InspectorName("차량 제1주 (교차로 건너기 전)")] VehicleMain = 0,
    [InspectorName("차량 보조 (정지한 차용, 200mm)")] VehicleAux = 1,
    [InspectorName("보행")] Pedestrian = 2,
    [InspectorName("차량 제2주 (교차로 건너편)")] VehicleSecond = 3,
}

public enum PNUSignalMount
{
    [InspectorName("세로형 (기둥에 바로)")] Post = 0,
    [InspectorName("가로형 (내민식 팔)")] MastArm = 1,
}

/// <summary>신호등 생성기 쪽에서 구현하면, 자리마다 프리팹을 깔 때 이 자리 정보를 넘겨줌</summary>
public interface IPNUSignalTarget
{
    void ApplyAnchor(PNUSignalAnchor anchor);
}

public class PNUSignalAnchor : MonoBehaviour
{
    public PNUSignalKind kind;
    public string junctionName;
    [Tooltip("이 신호를 보는 차가 들어오는 도로(보행은 횡단보도가 있는 도로)")] public string approachRoad;
    [Tooltip("들어오는 차로 수")] public int lanesIn;

    [Header("모양 정보 (신호등 생성기가 씀)")]
    public PNUSignalMount mount;
    [Tooltip("등면 수(표 3-2: 편도 1·2차로 1~2, 3차로 2, 4·5차로 3 — 제2주 포함)")] public int faces = 1;
    [Tooltip("가로형 팔 길이(m, 로컬 +X 쪽)")] public float armLength;
    [Tooltip("기둥에서 팔을 따라 각 등면 가운데까지(m). 간격 2.4m 이상")] public float[] headOffsets = new float[0];
    [Tooltip("등면 아래 끝 높이(m, 이 자리 기준 = 인도 윗면). 가로형은 도로 면에서 4.5m 이상, 세로형 2.5~3.5m, 보행 2~3m")] public float headHeight;
    [Tooltip("렌즈 지름(mm): 주신호 300, 보조 200")] public int lensMM = 300;
    [Tooltip("이 등이 붙는 기둥(비어 있으면 제 기둥)")] public PNUSignalAnchor sharedPole;

    [Header("자리 정보")]
    [Tooltip("배치 방식")] public string placement;
    [Tooltip("정지선 가운데에서 기둥까지(m)")] public float distanceFromStopLine;
    [Tooltip("노드링크 회전정보에서 온 부착물 힌트(비보호, U턴, 좌회전금지 등)")] public string[] hints = new string[0];
    [TextArea(1, 3)] public string note;
    [HideInInspector] public string junctionKey;
    [HideInInspector] public string approachLink;

    void OnDrawGizmos()
    {
        var p = transform.position;
        Color c = kind == PNUSignalKind.Pedestrian ? new Color(0.2f, 1f, 0.4f) : kind == PNUSignalKind.VehicleAux ? new Color(1f, 0.6f, 0.6f)
            : kind == PNUSignalKind.VehicleSecond ? new Color(1f, 0.6f, 0.1f) : new Color(1f, 0.15f, 0.15f);
        Gizmos.color = c;
        float hh = headHeight > 0 ? headHeight : (kind == PNUSignalKind.Pedestrian ? 2.2f : 3f);
        if (sharedPole == null)
        {
            float top = hh + (mount == PNUSignalMount.MastArm ? 1.5f : 1.3f);
            Gizmos.DrawLine(p, p + Vector3.up * top);
        }
        Vector3 head = p + Vector3.up * (hh + 0.3f);
        if (mount == PNUSignalMount.MastArm && armLength > 0)
        {
            Vector3 a = p + Vector3.up * (hh + 0.95f);
            Gizmos.DrawLine(a, a + transform.right * armLength);
            foreach (var o in headOffsets)
            {
                Vector3 h = p + transform.right * o + Vector3.up * (hh + 0.22f);
                Gizmos.DrawWireCube(h, new Vector3(0.5f, 0.45f, 0.5f));
                Gizmos.DrawLine(h, h + transform.forward * 1.2f);
            }
        }
        else
        {
            Gizmos.DrawWireSphere(head, kind == PNUSignalKind.VehicleAux ? 0.15f : 0.25f);
            Gizmos.DrawLine(head, head + transform.forward * 1.2f);                  // 앞면이 보는 방향
        }
    }
}
