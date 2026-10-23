#nullable enable
using System;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원작 CharacterActionBase(장전, 아이템 사용, 대시, 스킬 조준, 상호작용)의 우선순위 규칙을 GAS 어빌리티로 둔다. 어느 행동을 막고 끊는지,
    // 비용과 쿨다운은 GA 에셋의 태그와 효과가 정한다. 진행 시간과 연출은 행동을 가진 전투, 모터, 상호작용 객체가 맡는다.
    [CreateAssetMenu(menuName = "GAS/Character Action Ability", fileName = "GA_New")]
    internal sealed class CharacterActionAbility : GameplayAbilityDefinition
    {
        protected override GameplayAbility CreateInstance(AbilitySystem system) => new Instance(this, system);

        // 다른 행동이 이 행동을 끊으면 행동을 가진 쪽이 자기 상태(소리, 진행 표시, 사용 중인 아이템)를 정리하도록 처리기를 건다.
        // 어빌리티를 받은 시스템에만 걸리고, null이면 푼다.
        internal static void SetCanceledHandler(AbilitySystem system, GameplayAbilityDefinition definition, Action? handler)
        {
            if (system.FindAbility(definition) is Instance instance) instance.Canceled = handler;
        }

        sealed class Instance : GameplayAbility
        {
            internal Instance(GameplayAbilityDefinition definition, AbilitySystem system) : base(definition, system)
            {
            }

            internal Action? Canceled { get; set; }

            // 원작 대시처럼 시작할 때 비용을 낸다. 비용 효과가 없는 행동은 아무것도 하지 않는다.
            protected override void OnActivate(in GameplayEventData trigger) => CommitCost();

            // 정상 종료면 쿨다운을 건다(원작 대시는 끝난 시각부터 다시 쓸 때까지 기다린다).
            // 취소는 다른 행동이 끊었거나 아바타 연결이 풀린 경우라 쿨다운 없이 처리기만 부른다.
            protected override void OnEnd(bool canceled)
            {
                if (!canceled) CommitCooldown();
                else Canceled?.Invoke();
            }
        }
    }
}
