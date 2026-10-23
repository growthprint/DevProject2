#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 비밀 상인(원작 EnemyPreset_Merchant_Myst0)의 피해 전 비적대와 원작 AICharacterController.hideIfFoundEnemy(상점 부착물 숨김)다.
    // 피해 전 비적대는 원작 코드에서 근거를 찾지 못한 제품 규칙이라 프로젝트 값 _passiveUntilHurt로 적는다(원작 차이). 도발 전에는 시야, 소리,
    // 무리 목표 나누기로 알아채지 않는다. 받아들인 피해 한 번으로 도발되면 그 뒤는 일반 적과 같다. 도발은 판 안 상태라 저장하지 않는다.
    // 팀 교전을 옮기면서 도발은 플레이어 피해만 한다(post5 P1-D). AI에게 맞은 상인이 플레이어까지 공격하지 않도록, AI 피해는 도발 없이
    // 그 공격자만 표적으로 삼게 하고(NoticeHurt), 도발 전에는 배치 탐색도 넣지 않는다. 그 공격자를 놓치면 다시 비적대로 돌아간다.
    public sealed partial class OriginalEnemyController
    {
        // 원작에 없는 프로젝트 값이다. 제작 도구가 EnemyPreset_Merchant_Myst0 후보만 켠다.
        [SerializeField] bool _passiveUntilHurt;
        // 원작 specialAttachmentBases로 만든 상점 부착물이다. 제작 도구가 후보 루트 아래에 붙인다. 부착물이 없는 적은 비어 있다.
        [SerializeField] AISpecialAttachment_Shop? _shopAttachment;
        // 원작 hideIfFoundEnemy다. 부착물 Init이 넣고, 교전 여부에 따라 이 적이 켜고 끈다.
        GameObject? _hideIfFoundEnemy;
        // 플레이어 피해를 한 번이라도 받아들였는지다. 레벨 동안만 유지한다.
        bool _provoked;

        // 피해 전 비적대 적이 아직 도발되지 않았는지다.
        bool Passive => _passiveUntilHurt && !_provoked;

        // 원작 AISpecialAttachment_Shop.OnInited의 aiCharacterController.hideIfFoundEnemy = shop이다.
        internal void SetHideIfFoundEnemy(GameObject target) => _hideIfFoundEnemy = target;

        // 스폰 그룹이 적 파사드, 월드 문맥, 스폰 루트의 하위 씬 이름과 함께 부르는 준비다. 원작 spawnTimeSpace처럼 고른 순서의
        // 개체 간격만 기다린 뒤 생성하고, 원작 CharacterRandomPreset처럼 캐릭터를 준비한 뒤 부착물을 Init한다.
        internal void Prepare(OriginalEnemyWorld world, GameplayWorldContext shared, DifficultyState? difficulty, float spawnDelay, string subscene)
        {
            _spawnDelay = spawnDelay;
            Prepare(world, shared.Catalog, difficulty, subscene);
            PrepareAttachments(shared);
        }

        // 상점은 캐릭터가 생성되기 전이라 숨겨 두고, Update가 생성 뒤 교전 여부로 맞춘다. 저장에서 되살린 시체도 숨긴 채 남는다.
        // 부착물 Init이 저작된 상점 물체를 바로 넣으므로 그 뒤에는 숨길 대상이 있다.
        void PrepareAttachments(GameplayWorldContext shared)
        {
            if (_shopAttachment == null) return;
            _shopAttachment.Init(this, shared);
            _hideIfFoundEnemy!.SetActive(false);
        }

        // 원작 AICharacterController.cs:388-391이다. 목표가 있으면 끄고 없으면 켜며, 상태가 다를 때만 부른다.
        void SyncHideIfFoundEnemy()
        {
            if (_hideIfFoundEnemy != null && !_aware != _hideIfFoundEnemy.activeSelf) _hideIfFoundEnemy.SetActive(!_aware);
        }

        // 원작은 죽은 캐릭터를 부착물과 함께 지운다. 제품은 시체 자리에 루트가 남으므로 상점만 꺼서 고를 수 없게 한다.
        void HideAttachmentsOnDeath()
        {
            if (_hideIfFoundEnemy != null) _hideIfFoundEnemy.SetActive(false);
        }
    }
}
