#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // Collider가 속한 적과 플레이어 모터를 씬 동안 기억하는 캐시다. 탄 충돌 필터, 화면 조준, 근접과 폭발 판정이 함께 쓴다.
    // 적은 씬에 저작되고 모터는 월드 준비 때 붙으며, 부모 계층은 씬 동안 바뀌지 않는다고 보고 첫 조회 때만 GetComponentInParent를 부른다.
    // 캐시한 컴포넌트가 파괴되었으면 다시 찾는다. 씬 수명이라 전투 시뮬레이션이 하나를 가지고 씬 정리 때 비운다. 메인 스레드 전용이다.
    // 단순 체력 대상의 DamageReceiver도 같은 캐시에 둔다. 원작처럼 Collider 객체 자신의 컴포넌트만 본다.
    internal sealed class ColliderOwnerCache
    {
        readonly Dictionary<Collider, (OriginalEnemyController? enemy, OriginalPlayerMotor? motor, DamageReceiver? receiver)> _owners = new();

        // Collider가 속한 적을 찾고, 적이 아닐 때만 플레이어 모터를 찾는다. 적 Collider는 플레이어 계층 아래에 없기 때문이다.
        internal void FindOwner(Collider collider, out OriginalEnemyController? enemy, out OriginalPlayerMotor? motor) =>
            FindOwner(collider, out enemy, out motor, out _);

        // 캐릭터가 아니면 단순 체력 대상의 DamageReceiver를 찾는다. 셋 다 없으면 소속이 없는 피해 Collider다.
        internal void FindOwner(Collider collider, out OriginalEnemyController? enemy, out OriginalPlayerMotor? motor, out DamageReceiver? receiver)
        {
            if (_owners.TryGetValue(collider, out var owner) && CachedOwnersAlive(owner))
            {
                enemy = owner.enemy;
                motor = owner.motor;
                receiver = owner.receiver;
                return;
            }
            enemy = Found(collider.GetComponentInParent<OriginalEnemyController>());
            motor = enemy == null ? Found(collider.GetComponentInParent<OriginalPlayerMotor>()) : null;
            receiver = enemy == null && motor == null ? Found(collider.GetComponent<DamageReceiver>()) : null;
            _owners[collider] = (enemy, motor, receiver);
        }

        // 씬 정리 때 소속 캐시를 비운다. 여러 번 불러도 된다.
        internal void Clear() => _owners.Clear();

        // 캐시한 세 소속이 모두 '못 찾음'이거나 아직 파괴되지 않았을 때만 캐시 값을 그대로 쓴다.
        static bool CachedOwnersAlive((OriginalEnemyController? enemy, OriginalPlayerMotor? motor, DamageReceiver? receiver) owner) =>
            Alive(owner.enemy) && Alive(owner.motor) && Alive(owner.receiver);

        // 찾지 못한 값(null 참조)이거나 아직 파괴되지 않은 컴포넌트면 캐시 값을 그대로 쓴다.
        static bool Alive(Object? value) => ReferenceEquals(value, null) || value != null;

        // 에디터의 가짜 null 같은 Unity null을 실제 null로 바꾼다. 그래야 Alive가 '못 찾음'으로 보고 매번 다시 찾지 않는다.
        static T? Found<T>(T? value) where T : Object => value != null ? value : null;
    }
}
