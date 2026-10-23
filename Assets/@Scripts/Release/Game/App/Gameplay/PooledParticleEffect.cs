#nullable enable annotations
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // Spawner 풀에서 재사용하는 입자 효과 템플릿에 붙는다. 총구 섬광, 벽 탄착, 생체 피격, 사망 효과가 대상이다.
    // 대여와 반환 때 남은 입자를 지워 앞 재생이 이어 보이지 않게 한다. 재생은 전투 코드가 활성화 뒤 Play로 시작한다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class PooledParticleEffect : MonoBehaviour, IPoolable
    {
        // 대여 직후 꺼진 상태에서 불리며, 아직 Awake 전일 수 있다.
        void IPoolable.OnSpawn() => ResetParticles();

        void IPoolable.OnDespawn() => ResetParticles();

        // 자식 입자까지 함께 방출을 멈추고 남은 입자를 지운다.
        void ResetParticles() => GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
