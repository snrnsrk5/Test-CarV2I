// 4/6 사람 한 명(프리팹 뿌리, 빌더가 사람 FBX(Humanoid)로 만듦): 앞 = +Z, 원점 = 발 사이 바닥
//   애니메이터: Speed(걷는 속도, m/s) 로 서기 ↔ 걷기, 걷기 재생 속도 = 걷는 속도 / walkRef
//   나타나기·사라지기: 렌더러마다 투명 재질 + _BaseColor 알파(MaterialPropertyBlock)
using UnityEngine;

namespace PNU.Drive
{
    public class PNUDrivePerson : MonoBehaviour
    {
        public Animator anim;
        public Renderer[] rends;
        public Material[] opaqueMats, fadeMats;              // rends 순서대로(렌더러마다 재질 하나)
        [Tooltip("걷기 동작 1배속일 때 걷는 속도(m/s)")] public float walkRef = 1.35f;
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock mpb; bool fading = true; float shown = -1f;

        /// <summary>v = 걷는 속도(m/s), rate = 재생 배속(일시정지면 0 → 멈춤)</summary>
        public void SetMotion(float v, float rate)
        {
            if (anim == null) return;
            anim.SetFloat(SpeedId, v);
            anim.speed = (v > 0.2f ? Mathf.Clamp(v / Mathf.Max(0.3f, walkRef), 0.6f, 1.6f) : 1f) * Mathf.Max(0f, rate);
        }

        public void SetAlpha(float a)
        {
            if (rends == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            bool f = a < 0.995f && fadeMats != null && fadeMats.Length == rends.Length;
            if (f)
            {
                if (!fading) { for (int i = 0; i < rends.Length; i++) if (rends[i] != null) rends[i].sharedMaterial = fadeMats[i]; fading = true; }
                if (Mathf.Abs(a - shown) > 0.01f)
                {
                    shown = a; mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                    foreach (var r in rends) if (r != null) r.SetPropertyBlock(mpb);
                }
            }
            else if (fading)
            {
                for (int i = 0; i < rends.Length; i++) if (rends[i] != null) { if (opaqueMats != null && i < opaqueMats.Length && opaqueMats[i] != null) rends[i].sharedMaterial = opaqueMats[i]; rends[i].SetPropertyBlock(null); }
                fading = false; shown = 1f;
            }
        }
    }
}
