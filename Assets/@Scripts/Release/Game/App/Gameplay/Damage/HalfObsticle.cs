#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 HalfObsticle(모래주머니)이다. 부서지면 기본 표시를 부서진 표시로 바꾼다. 공기 벽은 원작처럼 부서진 뒤에도 남는다.
    // 원작은 캐릭터가 Trigger에 들어오면 근처 모래주머니로 기록하고, 주인공은 근처에 하나라도 있으면 모든 모래주머니를,
    // 적은 근처의 것만 탄이 지나가게 한다. 여기서는 Trigger 이벤트 대신 탄을 거를 때 쏜 캐릭터 위치가 Trigger 안인지 본다.
    // 알려진 차이: 원작 외곽선(EPOOutline)은 옮기지 않는다.
    [DisallowMultipleComponent]
    public sealed class HalfObsticle : MonoBehaviour
    {
        // 켜진 모래주머니 목록이다. 주인공이 아무 모래주머니 근처에 있는지 볼 때 쓴다. 메인 스레드에서만 쓴다.
        static readonly List<HalfObsticle> _enabled = new List<HalfObsticle>();
        // 원작 Trigger 판정은 캐릭터 Collider가 닿을 때다. 캐릭터 피해 캡슐 반지름 0.3만큼 넓혀 위치로 본다.
        const float _nearRadius = .3f;

        [SerializeField] GameObject _defaultVisuals = null!;
        [SerializeField] GameObject _deadVisuals = null!;
        [SerializeField] Collider? _airWall;
        // 원작 HalfObsticle 객체의 Trigger Collider다. 원작 기지 TestHalfObsticle처럼 없으면 OnTriggerEnter가 오지 않아 근처로 보지 않는다.
        [SerializeField] Collider? _trigger;
        bool _dead;

        void Awake()
        {
            _defaultVisuals.SetActive(true);
            _deadVisuals.SetActive(false);
            if (_airWall != null) _airWall.gameObject.SetActive(true);
        }

        void OnEnable() => _enabled.Add(this);
        void OnDisable() => _enabled.Remove(this);

        // 원작 Dead다. 한 번만 표시를 바꾼다.
        internal void OnDead()
        {
            if (_dead) return;
            _dead = true;
            _defaultVisuals.SetActive(false);
            _deadVisuals.SetActive(true);
        }

        // 쏜 캐릭터가 이 모래주머니의 Trigger 범위 안이면 원작의 근처 모래주머니다.
        internal bool Near(Vector3 position) =>
            _trigger != null && (_trigger.ClosestPoint(position) - position).sqrMagnitude <= _nearRadius * _nearRadius;

        // 원작 HasNearByHalfObsticle이다. 주인공 탄은 근처에 하나라도 있으면 모든 모래주머니를 지나간다.
        internal static bool AnyNear(Vector3 position)
        {
            foreach (HalfObsticle obsticle in _enabled)
                if (obsticle.Near(position)) return true;
            return false;
        }

        // 제작 도구와 테스트가 쓰는 구성이다.
        internal void Configure(GameObject defaultVisuals, GameObject deadVisuals, Collider? airWall, Collider? trigger)
        {
            _defaultVisuals = defaultVisuals; _deadVisuals = deadVisuals; _airWall = airWall; _trigger = trigger; _dead = false;
        }
    }
}
