// v17 비 막는 구역(터널·지하차도): 빌더가 경로의 터널·덮인 구간마다 상자(BoxCollider, Ignore Raycast 층)를 이어 놓음.
//   빗방울 입자는 이 상자에 닿으면 사라지고(PNUDriveWeather 의 입자 충돌), 카메라가 상자 안이면 유리 물방울·빗소리를 끔.
//   씬에서 상자를 옮기거나 늘려도 됨(안/밖 판정은 상자 그대로 씀). 레이캐스트(Ignore Raycast)에는 안 걸림
using UnityEngine;

namespace PNU.Drive
{
    public class PNUDriveRainBlock : MonoBehaviour
    {
        public const int Layer = 2;                    // Ignore Raycast: 일반 레이캐스트·바닥 찾기에 안 걸림
        public BoxCollider[] boxes;
        public int[] zoneOf;                           // 상자마다 구역 번호
        public Vector3[] portalA, portalB;             // 구역마다 들어가는 곳·나오는 곳(입구·출구)
        public string[] zoneName;

        /// <summary>p 가 어느 상자 안이면 true, portalDist = 그 구역 입구·출구 중 가까운 쪽까지 거리</summary>
        public bool Inside(Vector3 p, out float portalDist)
        {
            portalDist = 1e9f;
            if (boxes == null) return false;
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                if (b == null || !b.enabled || !b.gameObject.activeInHierarchy) continue;
                Vector3 l = b.transform.InverseTransformPoint(p) - b.center, h = b.size * 0.5f;
                if (Mathf.Abs(l.x) > h.x || Mathf.Abs(l.y) > h.y || Mathf.Abs(l.z) > h.z) continue;
                int z = zoneOf != null && i < zoneOf.Length ? zoneOf[i] : -1;
                if (z >= 0 && portalA != null && z < portalA.Length && portalB != null && z < portalB.Length)
                    portalDist = Mathf.Min(Vector3.Distance(p, portalA[z]), Vector3.Distance(p, portalB[z]));
                else portalDist = 0f;
                return true;
            }
            return false;
        }

        /// <summary>p 에서 가장 가까운 입구·출구까지 거리(구역 밖에서도)</summary>
        public float PortalDist(Vector3 p)
        {
            float d = 1e9f;
            if (portalA != null) foreach (var q in portalA) d = Mathf.Min(d, Vector3.Distance(p, q));
            if (portalB != null) foreach (var q in portalB) d = Mathf.Min(d, Vector3.Distance(p, q));
            return d;
        }

        void OnDrawGizmosSelected()
        {
            if (boxes == null) return;
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.35f);
            foreach (var b in boxes)
            {
                if (b == null) continue;
                Gizmos.matrix = b.transform.localToWorldMatrix; Gizmos.DrawCube(b.center, b.size);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
