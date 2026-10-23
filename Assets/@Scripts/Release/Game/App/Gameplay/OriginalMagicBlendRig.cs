#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 MagicBlending이 쓰는 캐릭터 템플릿 리그 컴포넌트 목록처럼 손 자세 혼합이 다루는 뼈 목록이다. 경로는 Animator 기준이고
    // 부모가 자식보다 앞에 온다. 혼합은 이 순서로 뼈마다 월드 회전을 다시 쓰므로, 가중치가 0인 뼈는 혼합 전 월드 회전을 지킨다.
    // 목록에 없는 날개 같은 뼈는 부모를 따라간다.
    public sealed class OriginalMagicBlendRig : ScriptableObject
    {
        [SerializeField] string[] _paths;

        internal string[] Paths => _paths;
    }
}
