#nullable enable
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 CA_UseItem처럼 아이템 사용 하나의 진행과 완료를 맡는다. 전투 시뮬레이션이 하나를 가진다.
    // 장전, 대시, 상호작용과의 우선순위는 아이템 사용 어빌리티의 태그 규칙이 정한다. 소비와 효과는 완료 때 세션 명령으로만 확정한다.
    internal sealed class ItemUseAction
    {
        readonly GameplayWorldContext _world;
        // 아바타 연결이 플레이어 GAS 시스템에 준 아이템 사용 어빌리티다. 사용하는 동안 실행 중이고, 쥐는 Block 태그가 달리기, ADS,
        // 무기 교체, 공격을 막는다.
        readonly AbilitySystem _abilities;
        readonly GameplayAbilityDefinition _ability;
        // 사용 중인 아이템 인스턴스와 시작 때 찾은 정의다. 사용하면 아이템이 소비되므로 정의는 시작 때 잡아 둔다.
        string? _item;
        OriginalItemDefinition? _definition;
        // 완료 시각이다. 원본 ActionProgressHUD 진행률의 분모인 사용 시간은 실행 중 바뀌지 않는 정의에서 읽는다.
        float _until;

        // 원본 우선순위에서 대시가 사용보다 높아, 대시 어빌리티가 사용을 끊으면 소비 없이 취소한다. Release가 처리기를 푼다.
        internal ItemUseAction(GameplayWorldContext world)
        {
            _world = world;
            PlayerVitals vitals = world.Runtime.Vitals;
            _abilities = vitals.Abilities;
            _ability = vitals.AbilitySet.UseItem!;
            CharacterActionAbility.SetCanceledHandler(_abilities, _ability, Cancel);
        }

        internal bool Active => _item != null;

        // 사용, 대시, 지연 상호작용 중이면 시작할 수 없다. 아이템을 찾기 전에 상태를 바꾸지 않고 묻는다.
        internal bool CanBegin => _abilities.CanActivateAbility(_ability) == AbilityActivationResult.Activated;

        // 어빌리티를 시작하면 규칙대로 진행 중인 장전이 취소된다. 사용 중에는 걷기와 조준 방향만 허용된다.
        internal bool TryBegin(string instanceId, OriginalItemDefinition definition)
        {
            if (_abilities.TryActivateAbility(_ability) != AbilityActivationResult.Activated) return false;
            _item = instanceId; _definition = definition;
            // 사용 시간은 정의가 가지므로 완료 시각만 잡는다.
            _until = Time.time + definition.UseTime;
            return true;
        }

        // 어빌리티가 취소되면 불린다. 아이템을 소비하지 않고 사용을 버린다. 앱 종료로 GAS 시스템이 먼저 해제되며 부를 수도 있어,
        // 공용 HUD가 이 월드에 연결된 동안만 진행 표시를 내린다.
        void Cancel()
        {
            Clear();
            if (_world.PresentationAttached) _world.Hud.SetActionProgress(false, 0, false);
        }

        // 씬 정리 때 부르며 여러 번 불러도 된다. 처리기를 먼저 풀어, 끝내는 어빌리티가 내려가는 HUD를 건드리지 않게 한다.
        internal void Release()
        {
            CharacterActionAbility.SetCanceledHandler(_abilities, _ability, null);
            _abilities.CancelAbility(_ability);
            Clear();
        }

        // 원본 ActionProgressHUD처럼 하단 슬라이더에만 진행률을 보이고, 완료 시각이 되면 세션 명령으로 확정한다. 사용 중일 때만 부른다.
        // 피해를 준 사용이 확정되면 true라 호출자가 피격 표시를 낸다.
        internal bool Tick()
        {
            float remaining = Mathf.Max(0, _until - Time.time);
            // 진행률의 분모는 사용 중인 아이템 정의의 사용 시간이다.
            float total = _definition!.UseTime;
            _world.Hud.SetActionProgress(true, total > 0 ? 1 - remaining / total : 1, false);
            if (Time.time < _until) return false;
            // 세션 명령 전에 상태와 어빌리티를 먼저 끝낸다. 그래야 커밋 알림으로 다시 그리는 인벤토리 화면이 끝난 상태를 보고,
            // 명령이 던져도 다음 프레임에 같은 완료를 반복하지 않는다. 사용 중일 때만 불리므로 아이템과 정의가 있다.
            string item = _item!;
            OriginalItemDefinition definition = _definition!;
            Clear();
            _abilities.EndAbility(_ability);
            _world.Hud.SetActionProgress(false, 1, false);
            // 씬 정리 중이라 세션이 없으면 기록 없이 완료를 확정하지 않는다. 세션 명령은 규칙 거절을 기록 없이 false로 돌려준다.
            if (!_world.TrySession(out GameSessionManager? session)) return false;
            // 소비는 세션 그래프에, 효과는 생존 수치 소유자에 적용하므로 두 쪽을 먼저 함께 확인하는 명령을 쓴다.
            // 원본 Drug.Heal에서 RedEye, BlueEye 같은 음수 회복은 Health.Hurt라 적 피해와 같은 피격 표시를 낸다.
            // 아이템 사용 효과에는 GAS 피격 큐가 없으므로, 음수 회복이면 true를 돌려 전투 쪽이 표시를 직접 내게 한다.
            // 피해는 난이도 소유자의 배율과 세션의 현재 장소로 계산한다. 세션을 찾았으면 앱이 살아 있어 GameManager를 바로 읽는다.
            // 원작 사용 과제가 사용 횟수를 세도록 퀘스트 소유자도 같은 명령에 넘긴다.
            return GameplayCommands.TryUseItem(session, _world.Runtime.Vitals, GameManager.Quests, _world.Catalog, item,
                GameManager.Difficulty.Current, session.Snapshot().SceneId) && definition.Heal < 0;
        }

        void Clear()
        {
            _item = null;
            _definition = null;
        }
    }
}
