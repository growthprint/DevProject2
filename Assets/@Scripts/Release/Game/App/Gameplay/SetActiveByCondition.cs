#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 SetActiveByCondition이다. 레벨 초기화가 끝난 뒤 조건이 모두 참이면 대상을 켜고 아니면 끈다. _inverse면 반대다.
    // _update면 원작 CheckAndLoop처럼 1초마다 다시 보고, 아니면 한 번 본 뒤 이 컴포넌트를 끈다. 원작 autoCheck와 requireLevelInited는 범위 안 값이 모두 1이라 두지 않는다.
    // 원작은 다시 보기를 UniTask로 돌려 대상이 자기 오브젝트여도 계속 본다. 제품은 Update로 돌리므로 제작 도구가 이 컴포넌트를 대상 밖(대상이 자기면 그 부모)에 붙인다.
    // 건물 프리팹 제작 도구가 원작 값을 옮겨 적고, 실행 중에 지은 건물은 Building.Setup이 생길 때 바로 맞춘다. 메인 스레드에서만 쓴다.
    public sealed class SetActiveByCondition : MonoBehaviour
    {
        // 원작 checkTimeSpace다. 원작 WaitForSeconds처럼 게임 시간으로 세어 일시정지 중에는 다시 보지 않는다.
        const float _checkSeconds = 1f;

        [SerializeField] GameObject _target;
        [SerializeField] bool _inverse;
        // 원작 conditions다. 모두 참이어야 참이고, 비어 있으면 원작 ConditionExtensions.Satisfied처럼 참이다.
        [SerializeField] ActiveCondition[] _conditions = Array.Empty<ActiveCondition>();
        [SerializeField] bool _update;

        // 레벨 준비 뒤 처음 맞췄는지와, _update일 때 마지막으로 본 뒤 흐른 게임 시간이다.
        bool _begun;
        float _elapsed;

        // 원작 Update처럼 레벨 초기화가 끝난 첫 프레임에 맞추고, 그 뒤에는 _update일 때만 1초마다 다시 본다.
        // 앱이 먼저 내려간 뒤에는 읽을 소유자가 없어 다시 보지 않는다.
        void Update()
        {
            if (!_begun)
            {
                TryBegin();
                return;
            }
            _elapsed += Time.deltaTime;
            if (_elapsed < _checkSeconds || !GameManager.HasInstance) return;
            _elapsed = 0f;
            Apply();
        }

        // 레벨이 준비되고 앱이 있으면 처음 한 번 맞춘다. 실행 중에 지은 건물이 첫 메뉴 전에 맞도록 Building.Setup도 부른다.
        // 레벨 준비 전에 부르면 아무것도 하지 않고 Update가 준비 뒤 맞춘다. 다시 보지 않는 조건은 원작처럼 맞춘 뒤 이 컴포넌트를 끈다.
        internal void TryBegin()
        {
            if (_begun || !SceneContext.LevelInited || !GameManager.HasInstance) return;
            _begun = true;
            Apply();
            if (!_update) enabled = false;
        }

        // 원작 Set이다. 특성, 제조식, 퀘스트 소유자의 조회만 읽는다.
        void Apply()
        {
            PerkTreeManager perks = GameManager.Perks;
            CraftingManager crafting = GameManager.Crafting;
            QuestManager quests = GameManager.Quests;
            bool satisfied = true;
            for (int index = 0; index < _conditions.Length && satisfied; index++) satisfied = _conditions[index].Evaluate(perks, crafting, quests);
            _target.SetActive(satisfied != _inverse);
        }
    }
}
