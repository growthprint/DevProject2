#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 전투 월드의 낚시 드라이버와 낚시 호스트(IFishingHost)다. 낚시터 상호작용이 끝나면 StartFishing이 행동을 시작하고,
    // 컨트롤러가 시뮬레이션 단계에서 TickFishing을 불러 행동을 진행한다. 낚시 중에는 그 프레임의 나머지 입력(무기 전환, 퀵슬롯, F 탐색, 사격)을 건너뛴다.
    internal sealed partial class WorldInteractions : IFishingHost
    {
        // 원작 FishingRod.Bait가 찾는 낚싯대 아이템 칸 키와, 원작 SpawnFish가 물고기에 넣는 FromInfoKey다.
        const string _baitSlot = "Bait";
        const string _fishingSource = "Fishing";
        // 원작 CatchFish의 UniTask.WaitForSeconds(0.65f)다. 게임 시간이라 일시정지 중에는 미뤄진다.
        const float _caughtNoticeDelay = .65f;
        // 원작 TimeOfDayController 자리인 이 씬의 시간대 볼륨이다. 없는 씬은 낮과 맑음(원작 기본 날씨)으로 본다.
        readonly OriginalTimeOfDay? _timeOfDay;
        // 저작 대상 가운데 낚시터다. 생성자가 한 번 모으고, 매 프레임 손에 든 무기를 넘겨 상호작용 표시를 맞춘다.
        readonly List<Action_FishingV2> _fishingPoints = new();
        // 낚시터에 마지막으로 넘긴 손 아이템의 원본 ID다. 어떤 아이템 ID와도 다른 처음 값이라 첫 틱은 반드시 넘긴다.
        int _syncedHeldItem = int.MinValue;
        // 진행 중인 낚시와, 낚은 뒤 띄울 알림 문구와 시각이다.
        Action_FishingV2? _fishing;
        string? _caughtNotice;
        float _caughtNoticeAt;

        // 낚시 중인지다. 인벤토리 화면이 원작 CanEditInventory false처럼 편집과 사용을 막는 데 읽는다(FishingInventoryActions).
        internal bool IsFishing => _fishing != null && _fishing.IsRunning;

        // 낚시가 진행 중이면 행동을 한 프레임 진행하고 true를 돌려준다. 아니면 낚시터의 상호작용 표시를 손에 든 무기에 맞추고 false다.
        // catchPressed는 원작 대시 입력의 TryCatch다. 컨트롤러가 입력 차단이 없고 창에 포커스가 있을 때의 Player/Jump 누름을 넘긴다.
        // 낚은 뒤 알림은 이 틱에서 시간이 되면 띄운다.
        internal bool TickFishing(bool catchPressed)
        {
            if (_caughtNotice != null && Time.time >= _caughtNoticeAt)
            {
                _world.Hud.Notify(_caughtNotice);
                _caughtNotice = null;
            }
            Action_FishingV2? fishing = _fishing;
            if (fishing == null || !fishing.IsRunning)
            {
                _fishing = null;
                SyncFishingPoints();
                return false;
            }
            _world.Hud.HideInteraction();
            fishing.Tick(Time.deltaTime, catchPressed);
            if (!fishing.IsRunning) _fishing = null;
            return true;
        }

        // 원작 OnMainCharacterChangeHoldItemAgentEvent의 SyncInteractable이다. 손 아이템 변경 알림이 없어 매 프레임 비교하고, 원작처럼 바뀐 때만 넘긴다.
        // 그때는 꺼진 하위 씬 낚시터도 함께 맞춰, 하위 씬을 켠 첫 프레임에도 값이 맞는다. 목록은 씬과 함께만 파괴되는 저작 대상이라 빈 칸이 없다.
        void SyncFishingPoints()
        {
            if (_fishingPoints.Count == 0) return;
            int held = HeldItem(out _, out int type, out _) ? type : -1;
            if (held == _syncedHeldItem) return;
            _syncedHeldItem = held;
            for (int index = 0; index < _fishingPoints.Count; index++) _fishingPoints[index].SyncInteractable(held);
        }

        // 씬 해제 정리가 EndTimedInteraction으로 부른다. 진행 중인 낚시를 멈춰 억제와 행동 태그를 반납하고, 남은 알림은 버린다.
        void EndFishing()
        {
            Action_FishingV2? fishing = _fishing;
            _fishing = null;
            _caughtNotice = null;
            if (fishing != null) fishing.Stop();
        }

        // 원작 낚시터 Interact의 시간 종료다. 낚는 동안에는 F 탐색이 새 상호작용을 시작하지 않아 이때 진행 중인 낚시가 없다.
        // 낚싯대와 미끼 확인은 행동이 한다.
        // 원작 OnStart의 ForceTurnTo처럼 시작에 성공하면 몸을 대상 지점 쪽으로 즉시 돌린다. 플레이어 위치는 옮기지 않는다.
        void IFacilityHost.StartFishing(Action_FishingV2 fishing)
        {
            if (!fishing.TryStart(this)) return;
            _fishing = fishing;
            Transform? target = fishing.TargetPoint;
            if (target != null) _world.Motor.FaceDirection(target.position - _world.PlayerPosition);
        }

        // 손에 든 무기 칸(CombatSimulation.WeaponIndex)의 아이템과 그 Bait 칸 미끼를 확정 그래프에서 읽는다. 스킬 아이템을 들었거나 손이 비면 false다.
        bool HeldItem(out string itemId, out int originalItemId, out int baitItemId)
        {
            itemId = "";
            originalItemId = -1;
            baitItemId = -1;
            int index = _combat.WeaponIndex;
            if (index < 0 || !_world.TryItems(out ItemOwnershipState? items)
                || !OriginalInventoryQueries.TryEquippedIn(items!, GameplayWeaponSlots.All[index], out string? id) || id == null) return false;
            ItemInstanceState? item = items!.Find(id);
            if (item == null) return false;
            itemId = id;
            originalItemId = item.OriginalItemId;
            IReadOnlyList<ItemSlotState> slots = item.Slots;
            for (int slot = 0; slot < slots.Count; slot++)
            {
                if (slots[slot].Key != _baitSlot) continue;
                ItemInstanceState? bait = items.Find(slots[slot].InstanceId);
                if (bait != null) baitItemId = bait.OriginalItemId;
                break;
            }
            return true;
        }

        bool IFishingHost.TryHeldItem(out string itemId, out int originalItemId, out int baitItemId) =>
            HeldItem(out itemId, out originalItemId, out baitItemId);

        // 원작 캐릭터 스탯 FishingTime과 FishingQualityFactor다. 캐릭터 어트리뷰트 목록이 등록한 같은 키를 읽는다.
        float IFishingHost.FishingTime => _abilities.GetCurrent(CharacterAttributes.FishingTime);
        float IFishingHost.FishingQualityFactor => _abilities.GetCurrent(CharacterAttributes.FishingQualityFactor);
        // 원작 AtNight는 시간대 단계가 밤인지다. 단계 경계는 시간대 볼륨이 가진다.
        bool IFishingHost.AtNight => _timeOfDay != null && _timeOfDay.AtNight;
        int IFishingHost.Weather => _timeOfDay != null ? (int)_timeOfDay.CurrentWeather : (int)OriginalWeather.Sunny;
        float IFishingHost.Random01() => UnityEngine.Random.value;

        IReadOnlyList<string>? IFishingHost.FishTags(int fishId) =>
            _world.Catalog.TryGet(fishId, out OriginalItemDefinition? fish) ? fish!.Tags : null;

        IReadOnlyList<int> IFishingHost.SearchFish(string requireTag, HashSet<string> excludeTags, int quality) =>
            _world.Catalog.Search(requireTag, excludeTags, quality);

        float IFishingHost.FishingDifficulty(int fishId) =>
            _world.Catalog.TryGet(fishId, out OriginalItemDefinition? fish) ? fish!.Stat("FishingDifficulty") : 0f;

        // 씬 정리 중이라 세션이 없으면 쓰지 않는다. 낚시 중에는 인벤토리 편집이 막혀 미끼가 칸을 떠나지 않는다.
        bool IFishingHost.TryConsumeBait(string rodId) =>
            _world.TrySession(out GameSessionManager? session) && session!.Inventory.TryConsumeSlotContent(_world.Catalog, rodId, _baitSlot);

        // 원작 CatchFish의 PickupItem과 SetHasFished다. 원작 PickupItem은 빈 캐릭터 칸, 소지품(합치기) 순으로 넣고 못 넣으면 발밑에
        // 떨군다(Drop(위치, 강체, forward, 360)). 물고기는 캐릭터 칸에 맞지 않아 소지품만 본다. 낚시 경험은 줍기와 상관없이 기록한다.
        void IFishingHost.CatchFish(int fishId, string gotFishTextKey)
        {
            if (!_world.TrySession(out GameSessionManager? session)) return;
            OriginalGameplayCatalog catalog = _world.Catalog;
            if (session!.Inventory.TryDeliver(catalog, new[] { (fishId, 1) }, ItemDelivery.PlayerThenDrop, _fishingSource,
                    out IReadOnlyList<ItemOwnershipState> dropped) && dropped.Count > 0)
                _drops.SpawnAroundPlayer(dropped);
            GameManager.Flags.TrySetHasFished();
            string name = catalog.TryGet(fishId, out OriginalItemDefinition? fish) ? GameLocalization.Text(fish!.NameKey) : "";
            _caughtNotice = GameLocalization.Text(gotFishTextKey) + " " + name + "!";
            _caughtNoticeAt = Time.time + _caughtNoticeDelay;
        }

        void IFishingHost.PopText(string textKey) => _world.PopText(textKey);
        void IFishingHost.Notify(string textKey) => _world.Notify(textKey);

        // 원작 낚시 행동의 CanMove, CanRun, CanControlAim, CanUseHand가 false인 동안이다. 모터 억제로 이동, 조준, 대시를 막고,
        // 행동 태그로 다른 행동과 F 상호작용을, 차단 태그로 무기 전환, 달리기, 조준, 공격을 막는다.
        IDisposable IFishingHost.HoldFishingAction() => new FishingHold(_abilities, _world.Motor.SuppressInput());

        // 낚시 동안 쥐는 억제 토큰과 행동·차단 태그다. Dispose는 한 번만 반납하고 다시 불러도 안전하다.
        sealed class FishingHold : IDisposable
        {
            // 원작 ActionPriorities.Fishing 행동 자리다. State.Action 아래라 ActionInProgress가 참이 된다.
            static readonly GameplayTag[] _tags =
            {
                CharacterTags.ActionFishing, CharacterTags.BlockWeaponSwitch, CharacterTags.BlockSprint, CharacterTags.BlockAim,
                CharacterTags.BlockAttack
            };
            readonly AbilitySystem _abilities;
            IDisposable? _suppression;

            internal FishingHold(AbilitySystem abilities, IDisposable suppression)
            {
                _abilities = abilities;
                _suppression = suppression;
                foreach (GameplayTag tag in _tags) abilities.AddLooseTag(tag);
            }

            public void Dispose()
            {
                IDisposable? suppression = _suppression;
                if (suppression == null) return;
                _suppression = null;
                suppression.Dispose();
                foreach (GameplayTag tag in _tags) _abilities.RemoveLooseTag(tag);
            }
        }
    }

    // 인벤토리 화면이 묻는 행동 상태에 낚시를 더한다. 원작 Action_FishingV2.CanEditInventory가 false라 낚는 동안 편집, 사용, 장착을 막는다.
    // 전투 월드 컨트롤러가 인벤토리 화면을 만들 때 전투 시뮬레이션을 감싸 넘긴다. 낚시 여부는 나중에 만들어지는 상호작용 객체에서 읽는다.
    internal sealed class FishingInventoryActions : IInventoryActions
    {
        readonly IInventoryActions _actions;
        readonly Func<bool> _fishing;

        internal FishingInventoryActions(IInventoryActions actions, Func<bool> fishing)
        {
            _actions = actions;
            _fishing = fishing;
        }

        public bool ActionInProgress => _actions.ActionInProgress;
        public bool UsingItem => _actions.UsingItem || _fishing();
        public bool TryBeginUse(string id) => !_fishing() && _actions.TryBeginUse(id);
    }
}
