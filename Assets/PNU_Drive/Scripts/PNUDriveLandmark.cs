// 부산대·부경대 정문 자리(후배 모델 넣는 곳): 이 오브젝트 밑에 모델을 넣으면 됨(위치 = 정문 가운데, 앞 = 길 쪽)
// 자리(다각형) 안에는 맵 생성 때 건물·필지·나무를 두지 않았음. 편집 화면에서만 노란 테두리로 보임(게임에는 안 보임)
using UnityEngine;

namespace PNU.Drive
{
    public class PNUDriveLandmark : MonoBehaviour
    {
        public string key;
        public Vector3[] reserve;     // 자리 테두리(월드 좌표, 바닥 높이)

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
            if (reserve != null && reserve.Length > 1)
                for (int i = 0; i < reserve.Length; i++) Gizmos.DrawLine(reserve[i], reserve[(i + 1) % reserve.Length]);
            Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.9f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 12f);   // 정면(길 쪽)
            Gizmos.DrawWireCube(transform.position + Vector3.up * 4f, new Vector3(20f, 8f, 4f));
        }
    }
}
