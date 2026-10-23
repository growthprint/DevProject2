#nullable enable annotations
using System.Collections.Generic;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 HealthHUD, EnergyHUD, AimMarker 같은 생존 HUD 갱신을 프롤로그와 전투 월드가 함께 쓴다. 씬 컨트롤러가 레벨 초기화 때
    // 만들어 소유한다. 구독이나 빌린 객체가 없어 따로 정리하지 않는다. 생존 수치 진행은 PlayerSurvival이 맡는다.
    internal sealed class PlayerStatusHud
    {
        readonly GameRuntime _runtime;
        readonly OriginalPlayerMotor _motor;
        readonly SharedHudView _hud;
        // 장비 효과가 최대치를 바꾸면 같은 추적 대상으로 다시 연결하도록 BindHud가 받은 카메라와 적 목록을 둔다.
        // 적 목록이 null이면 아직 연결하지 않은 것이다.
        // 체력바가 스폰 후보 전체 대신 나온 적만 돌도록 적 월드의 나온 적 목록을 그대로 받는다.
        Camera? _hudCamera;
        IReadOnlyList<OriginalEnemyController>? _hudEnemies;
        // 현금 아이템 수를 마지막으로 센 소유 그래프와 그 수다. 그래프는 확정마다 새 인스턴스라 참조가 같으면 수도 같다.
        ItemOwnershipState? _cashGraph;
        long _cash;

        internal PlayerStatusHud(GameRuntime runtime, OriginalPlayerMotor motor, SharedHudView hud)
        {
            _runtime = runtime!;
            _motor = motor!;
            _hud = hud;
        }

        // 공용 HUD 상태 표시에 캐릭터 최대치 넷과 추적 대상을 연결한다. 최대치는 아바타 연결이 원본 플레이어 정의로 채운
        // 플레이어 GAS 어트리뷰트의 현재값이다. 원작에서 Stamina 스탯이 최대 스태미나다. 씬 컨트롤러가 공용 표시를 연결할 때 부른다.
        internal void BindHud(Camera camera, IReadOnlyList<OriginalEnemyController> enemies)
        {
            // RefreshHudMaxima가 같은 대상으로 다시 연결할 수 있게 기억한다.
            _hudCamera = camera;
            _hudEnemies = enemies;
            AbilitySystem abilities = _runtime.Vitals.Abilities;
            _hud.Status.BindCharacter(abilities.GetCurrent(CharacterAttributes.MaxHealth), abilities.GetCurrent(CharacterAttributes.Stamina),
                abilities.GetCurrent(CharacterAttributes.MaxEnergy), abilities.GetCurrent(CharacterAttributes.MaxWater), _motor.transform, camera,
                enemies);
        }

        // 원작 장비 스탯처럼 장비 GAS 효과가 붙거나 빠져 최대 체력, 스태미나, 에너지, 수분이 바뀌면 HUD 최대치를 다시 읽는다.
        // BindHud 전이면 연결할 대상이 없어 아무것도 하지 않는다. 다시 연결하면 스태미나 게이지가 캐릭터 옆에 바로 놓인다.
        internal void RefreshHudMaxima()
        {
            if (_hudEnemies != null) BindHud(_hudCamera!, _hudEnemies);
        }

        // 원본 HUD 컴포넌트처럼 Update에서 체력, 스태미나, 탄약, 상태 게이지와 시계, 조준 표시를 공용 HUD 상태 표시에 보낸다.
        // 체력과 생존 게이지는 매 프레임 현재 값을 그대로 보이므로 변경 알림을 구독하지 않는다. 결과 화면과 일시정지 중에도 갱신한다.
        // 호출자는 세션이 있을 때만 부른다. player가 null이면 수치 표시는 건너뛰고 조준점만 갱신한다.
        // 탄약은 원본 BulletCountHUD처럼 탄창 수와 소지 수를 따로 받는다.
        internal void RefreshHud(PlayerState? player, string loadedText, string reserveText, float scatter, int weaponIndex)
        {
            HudStatusDisplay status = _hud.Status;
            if (player != null)
            {
                status.ShowVitals(player.Health, _motor.Stamina, loadedText, reserveText);
                // 시각을 아직 기록하지 않았으면 null을 넘기고, 기본 시각 표시는 HUD가 맡는다. 시계 값은 GameManager의 GameClock이 가진다.
                status.ShowSurvival(player.Energy, player.Hydration, GameManager.Clock.Current.SecondsOfDay);
            }
            status.ShowAim(_motor.AimScreenPosition, scatter, weaponIndex);
            RefreshMoney();
        }

        // 원본 MoneyDisplay와 CashDisplay처럼 계좌 잔액과, 원작 FindAllBelongsToPlayer 출처(가방, 펫 가방, 기지면 창고)의 현금 아이템 수를 보인다.
        // 현금은 소유 그래프가 바뀐 프레임에만 다시 세고, 글자는 금액 표시가 값이 바뀐 때만 쓴다. 그래프 전(프롤로그 첫 루팅 전)은 0이다.
        void RefreshMoney()
        {
            GameSessionSnapshot snapshot = _runtime.Snapshot();
            ItemOwnershipState? items = snapshot.Campaign.Gameplay.Inventory.Items;
            if (!ReferenceEquals(items, _cashGraph))
            {
                _cashGraph = items;
                _cash = items != null
                    ? GameplayInventory.Count(items, GameplayDataSettings.ItemAssets.CashItemTypeID, ItemSourceRules.Owned(snapshot.SceneId))
                    : 0;
            }
            _hud.Money.Show(GameManager.Economy.Money, _cash);
        }
    }
}
