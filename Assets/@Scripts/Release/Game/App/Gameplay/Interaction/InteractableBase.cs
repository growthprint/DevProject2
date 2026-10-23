#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 WhenToUseRequireItemTypes다. None은 열쇠처럼 지니기만 하면 되고, 나머지는 그 시점에 하나를 쓴다.
    // 정수로 직렬화되므로 값을 바꾸지 않는다.
    public enum RequireItemTiming { None = 0, OnFinished = 1, OnTimeOut = 2, OnStartInteract = 3 }

    // 원작 InteractableBase처럼 F로 고르는 상호작용 대상이다. 영역, 이름, 안내 위치, 상호작용 시간, 필요 아이템을 갖고,
    // 원작 UnityEvent 동작을 _onStart와 _onFinished로 옮겨 실행한다. 끝났을 때 할 일은 하위 클래스가 더한다.
    // 탐색, 안내 표시, 진행 바, 취소는 드라이버(WorldInteractions, 프롤로그 컨트롤러)가 맡는다.
    public class InteractableBase : MonoBehaviour
    {
        // 반복되는 오브젝트 이름 대신 원본 fileID로 만든 구분자다. 전리품 칸 키와 유실물 출격 토큰이 이 값을 쓴다.
        [SerializeField] string _sourceId = "";
        // 원작 interactCollider다. 탐색 구가 이 Collider에 닿으면 이 대상을 고른다.
        [SerializeField] Collider? _interactCollider;
        // 원작 _overrideInteractNameKey(상자는 displayNameKey)다. 안내 표시에 번역해 띄운다.
        [SerializeField] string _interactNameKey = "";
        // 원작 interactMarkerOffset이다. 안내 표시는 이 Transform 기준의 이 지점을 따라간다.
        [SerializeField] Vector3 _interactMarkerOffset;
        // 원작 interactTime(초)이다. 0이면 F를 누른 프레임에 끝나고, 0보다 크면 드라이버가 진행 바를 채운 뒤 끝낸다.
        [SerializeField] float _interactTime;
        // 원작 requireItem 묶음이다. 아이템이 있어야 시작하고, 처음 한 번은 unlockTime을 더 기다린다. requireOnce면 한 번 쓴 뒤 필요 없어진다.
        [SerializeField] bool _requireItem;
        [SerializeField] int _requireItemId;
        [SerializeField] bool _requireOnce = true;
        [SerializeField] float _unlockTime;
        [SerializeField] RequireItemTiming _whenToUseRequireItem;
        // 원작 disableOnFinish다. 끝나면 이 대상과 영역을 꺼 다시 고르지 않는다.
        [SerializeField] bool _disableOnFinish;
        // 원작 OnInteractStartEvent와 OnInteractFinishedEvent의 호출 가운데 옮긴 것이다.
        [SerializeField] InteractableEvent[] _onStart = Array.Empty<InteractableEvent>();
        [SerializeField] InteractableEvent[] _onFinished = Array.Empty<InteractableEvent>();
        // 원작 interactableGroup과 otherInterablesInGroup이다. 대표는 켜진 구성원과 함께 안내 목록을 만들고, 플레이어가 휠로 항목을 고른다.
        // 구성원은 대표 자리로 모이고 탐색은 대표만 고르므로, 구성원에는 따로 고를 수 있는 영역을 두지 않는다.
        [SerializeField] bool _interactableGroup;
        [SerializeField] InteractableBase[] _otherInteractablesInGroup = Array.Empty<InteractableBase>();
        // 원작 _interactbleList처럼 읽을 때마다 비우고 다시 채운다. 매 프레임 읽으므로 목록을 새로 만들지 않는다.
        readonly List<InteractableBase> _interactableList = new();
        // 원작 inLevelData의 필요 아이템 사용 기록처럼 이 레벨에서만 유지한다. 씬을 다시 불러오면 처음 상태로 돌아간다.
        bool _itemNoLongerRequired, _requireItemUsed, _finishedAndDisabled;

        public string SourceId => _sourceId;
        public string InteractNameKey => _interactNameKey;
        internal Collider? InteractCollider => _interactCollider;
        // 원작 InteractTime처럼 필요 아이템을 아직 쓰지 않았으면 해제 시간을 더한다.
        internal float InteractTime => RequiresItem && !_requireItemUsed ? _interactTime + _unlockTime : _interactTime;
        internal Vector3 MarkerPosition => transform.TransformPoint(_interactMarkerOffset);
        // 지금 필요 아이템이 있어야 시작할 수 있는지와 그 원본 아이템 ID다.
        internal bool RequiresItem => _requireItem && !_itemNoLongerRequired;
        internal int RequireItemId => _requireItemId;

        // 원작 CA_Interact.SwitchInteractable이 휠 전환을 받는 조건이다. 구성원이 없으면 목록은 대표 하나뿐이다.
        internal bool IsGroup => _interactableGroup && _otherInteractablesInGroup.Length > 0;

        // 원작 GetInteractableList다. 첫 항목은 늘 대표이고, 그 뒤에 켜진 구성원만 직렬화 순서대로 붙는다.
        // 돌려준 목록은 다음 읽기에서 다시 채워지므로 호출자는 같은 프레임 안에서만 쓴다.
        internal IReadOnlyList<InteractableBase> InteractableList
        {
            get
            {
                _interactableList.Clear();
                _interactableList.Add(this);
                if (!IsGroup) return _interactableList;
                foreach (InteractableBase member in _otherInteractablesInGroup)
                    if (member != null && member.gameObject.activeInHierarchy) _interactableList.Add(member);
                return _interactableList;
            }
        }

        // 원작 GetInteractableInGroup이다. 0은 대표이고, 목록 밖 번호는 null이라 F를 눌러도 시작하지 않는다.
        internal InteractableBase? InteractableInGroup(int index)
        {
            if (index == 0) return this;
            IReadOnlyList<InteractableBase> list = InteractableList;
            return index > 0 && index < list.Count ? list[index] : null;
        }

        // 원작 InteractableBase.Awake처럼 영역을 Interactable 레이어에 둔다. 탐색 구가 이 레이어만 찾는다.
        // 풀에서 빌린 대상의 영역은 템플릿에서부터 이 레이어다.
        protected virtual void Awake()
        {
            if (_interactCollider != null) _interactCollider.gameObject.layer = GameLayers.Interactable;
            // 원작처럼 구성원을 대표 자리와 방향으로 모으고 안내 위치를 대표 값으로 맞춘다. 고른 항목이 바뀌어도 안내가 한자리에 있다.
            foreach (InteractableBase member in _otherInteractablesInGroup)
            {
                if (member == null) continue;
                member.transform.SetPositionAndRotation(transform.position, transform.rotation);
                member._interactMarkerOffset = _interactMarkerOffset;
            }
        }

        // 원작 IsInteractable이다. 꺼진 대상과 끝나서 꺼진 대상은 고르지 않는다. 하위 클래스가 조건을 더할 때 이 결과와 함께 본다.
        internal virtual bool IsInteractable => gameObject.activeInHierarchy && !_finishedAndDisabled;

        // 원작 StartInteract의 필요 아이템 확인과 시작 동작이다. F를 누른 프레임에 드라이버가 부르고, false면 시작하지 않는다.
        internal bool TryStart(IInteractionHost host)
        {
            if (RequiresItem && !host.HasItem(_requireItemId)) return false;
            // 필요 아이템이 아직 필요하고 쓰는 시점이 지금일 때만 쓴다.
            if (UsesRequiredItemAt(RequireItemTiming.OnStartInteract) && !UseRequiredItem(host)) return false;
            Run(_onStart, host);
            return true;
        }

        // 원작 UpdateInteract의 시간 종료와 FinishInteract다. 시간이 0인 대상은 시작 프레임에, 아니면 진행 바가 찬 프레임에
        // 드라이버가 한 번 부른다. 그 시점에 써야 할 아이템을 쓰지 못하면 원작처럼 끝내지 않는다.
        // 아이템을 쓰면 RequiresItem이 바뀔 수 있어 시점마다 다시 읽는다.
        internal void Complete(IInteractionHost host)
        {
            if (UsesRequiredItemAt(RequireItemTiming.OnTimeOut) && !UseRequiredItem(host)) return;
            if (UsesRequiredItemAt(RequireItemTiming.None)) _requireItemUsed = true;
            if (UsesRequiredItemAt(RequireItemTiming.OnFinished) && !UseRequiredItem(host)) return;
            OnInteractFinished(host);
            Run(_onFinished, host);
            if (!_disableOnFinish) return;
            _finishedAndDisabled = true;
            if (_interactCollider != null) _interactCollider.enabled = false;
        }

        // 원작 OnInteractFinished다. 원작 InteractableBase 자체는 할 일이 없고 동작 목록만 실행한다. 하위 클래스가 할 일을 정한다.
        internal virtual void OnInteractFinished(IInteractionHost host) { }

        // 원작 whenToUseRequireItem 확인이다. 아이템을 아직 요구하고 쓰는 시점이 timing과 같을 때 true다.
        bool UsesRequiredItemAt(RequireItemTiming timing) => RequiresItem && _whenToUseRequireItem == timing;

        // 원작 UseRequiredItem이다. 호스트가 플레이어의 아이템 하나를 쓰고, requireOnce면 이 레벨에서는 더 요구하지 않는다.
        bool UseRequiredItem(IInteractionHost host)
        {
            if (!host.TryUseItem(_requireItemId)) return false;
            if (_requireOnce) { _itemNoLongerRequired = true; _requireItemUsed = true; }
            return true;
        }

        // 원작 UnityEvent 호출 순서대로 실행한다. 오브젝트 켜고 끄기와 문은 여기서, 말풍선과 알림은 HUD를 가진 호스트가 한다.
        void Run(InteractableEvent[] events, IInteractionHost host)
        {
            foreach (InteractableEvent action in events)
            {
                switch (action.Kind)
                {
                    case InteractableEventKind.SetActive: action.Target!.SetActive(action.Active); break;
                    case InteractableEventKind.DoorSwitch: action.Door!.Toggle(); break;
                    case InteractableEventKind.Bubble: host.Bubble(action.TextKey, action.Target!.transform, action.YOffset, action.Duration); break;
                    case InteractableEventKind.Notify: host.Notify(action.TextKey); break;
                    // 원작 Door.Switch를 부르던 스위치는 문이 아니라 OriginalSwitch로 옮겼다.
                    case InteractableEventKind.Switch: action.Switch!.Toggle(); break;
                    // 원작 TaskEventEmitter.EmitEvent 호출이다. 과제를 끝내는 키는 발신기가 가진다.
                    case InteractableEventKind.TaskEvent: action.Emitter!.EmitEvent(); break;
                    // 원작 ViewsProxy.ShowRepairView와 ShowDecomposeView 호출이다. 화면은 시설을 가진 호스트가 열고,
                    // 시설이 없는 장면에서는 아무 일도 하지 않는다.
                    case InteractableEventKind.ShowRepairView: host.Facilities?.OpenRepair(); break;
                    case InteractableEventKind.ShowDecomposeView: host.Facilities?.OpenDecompose(); break;
                }
            }
        }

        // 지연 상호작용이 범위를 벗어났는지 볼 때 쓰는 제곱 거리다. 꺼진 영역은 무한대다.
        // 비활성 하위 씬의 ClosestPoint는 입력 위치를 그대로 돌려줘 거리 0으로 잘못 볼 수 있기 때문이다.
        internal float SqrDistance(Vector3 position) => _interactCollider != null
            ? _interactCollider.enabled && _interactCollider.gameObject.activeInHierarchy
                ? (_interactCollider.ClosestPoint(position) - position).sqrMagnitude : float.PositiveInfinity
            : (transform.position - position).sqrMagnitude;

        // 제작 도구와 런타임 생성이 함께 쓰는 공통 구성이다. 풀에서 빌린 대상은 대여마다 이 값을 다시 쓰고, 반환 때 비운다.
        internal void ConfigureInteraction(string sourceId, Collider? interactCollider, string nameKey, Vector3 markerOffset, float interactTime)
        {
            _sourceId = sourceId; _interactCollider = interactCollider; _interactNameKey = nameKey;
            _interactMarkerOffset = markerOffset; _interactTime = interactTime;
        }

        // 제작 도구가 원본 requireItem 묶음과 disableOnFinish, UnityEvent 동작을 옮겨 적는다.
        internal void ConfigureRules(bool requireItem, int requireItemId, bool requireOnce, float unlockTime, RequireItemTiming whenToUse,
            bool disableOnFinish, InteractableEvent[] onStart, InteractableEvent[] onFinished)
        {
            _requireItem = requireItem; _requireItemId = requireItemId; _requireOnce = requireOnce; _unlockTime = unlockTime;
            _whenToUseRequireItem = whenToUse; _disableOnFinish = disableOnFinish; _onStart = onStart; _onFinished = onFinished;
        }
    }

    // 상호작용 대상이 맡기는 일이다. 전투 월드(WorldInteractions)와 프롤로그가 구현한다.
    // 대상이 그 장면에 없어 부르지 않는 일은 비워 둔다.
    internal interface IInteractionHost
    {
        // 기지 시설 화면과 낚시를 여는 쪽이다. 시설이 없는 장면은 null이고, 시설 진입점은 그때 아무 일도 하지 않는다.
        IFacilityHost? Facilities { get; }
        void OpenLoot(InteractableLootbox lootbox);
        void LoadScene(SceneLoaderInteractable loader);
        void Teleport(MultiSceneTeleporter teleporter);
        void RecoverLostBody(LostBodyInteractable body);
        void PickUp(InteractablePickup pickup);
        void OpenCustomization(Interact_CustomFace mirror);
        // 원작 NoteInteract의 해금과 쪽지 창 열기다.
        void ReadNote(NoteInteract note);
        // 원작 TryGetRequiredItem과 UseRequiredItem이다. 등록 열쇠, 장비 칸, 가방(+아이템 칸), 펫 가방(+아이템 칸) 순서로 찾고 창고는 보지 않는다.
        // 등록된 종류는 실물 없이 있다고 보지만 쓸 수는 없다(원작 그대로). 규칙은 GameplayCommands.HasRequiredItem과 TryUseRequiredItem에 있다.
        bool HasItem(int originalItemId);
        bool TryUseItem(int originalItemId);
        // 원작 DialogueBubbleProxy.Pop과 NotificationProxy.Notify다. 키는 번역 키다.
        void Bubble(string textKey, Transform at, float yOffset, float duration);
        void Notify(string textKey);
        // 원작 Cost.Pay다. 건설 비용을 모두 낼 수 있을 때만 내고 true를 돌려준다.
        bool TryPay(CostTaker taker);
    }

    // 기지 시설 화면과 낚시 시작이다. 전투 월드만 구현한다. 진입점이 ?.로 부르므로 UnityEngine.Object가 구현하지 않는다.
    internal interface IFacilityHost
    {
        // 원작 QuestGiver.OnInteractStart, StockShop.ShowUI, ViewsProxy.ShowStorageDock 자리다. 기지 화면이라 전투 월드만 연다.
        void OpenQuestGiver(QuestGiver giver);
        void OpenShop(StockShopInteract shop);
        void OpenStorageDock(StorageDockInteract dock);
        // 원작 MasterKeyRegisterViewInvoker.OnInteractFinished의 MasterKeysRegisterView.Show 자리다. 기지 Building_MasterKey 건물 안에만 있어 전투 월드만 연다.
        void OpenMasterKeyRegister(MasterKeyRegisterViewInvoker invoker);
        // 원작 Endowment 물체의 ViewsProxy.ShowEndowmentView 자리다. 기지 스킬 강화 기계에만 있어 전투 월드만 연다.
        void OpenEndowmentSelection(EndowmentSelectionInteract endowment);
        // 원작 BuilderViewInvoker.OnInteractFinished의 BuilderView.Show 자리다. 기지 건설 입구에만 있어 전투 월드만 연다.
        void OpenBuilder(BuilderViewInvoker invoker);
        // 원작 PerkTreeUIInvoker.OnInteractStart의 PerkTreeView.Show 자리다. 기지 스킬 강화 기계와 특성 건물에만 있어 전투 월드만 연다.
        void OpenPerkTree(PerkTreeUIInvoker invoker);
        // 원작 기지 침대의 수면 창 열기 자리다. 기지에만 있어 전투 월드만 연다. 진입점과 화면은 수면 묶음(3-S)이 만든다.
        void OpenSleep(SleepInteractable sleep);
        // 원작 ViewsProxy.ShowRepairView 자리다. 기지 작업대의 수리 항목에만 있어 전투 월드만 연다.
        void OpenRepair();
        // 원작 ViewsProxy.ShowDecomposeView 자리다. 기지 작업대의 분해 항목과 제로존 분해 스테이션에 있어 전투 월드만 연다.
        void OpenDecompose();
        // 원작 Workbench.OnInteractFinished의 ItemCustomizeSelectionView.Show 자리다. 기지 작업대 개조 항목에만 있어 전투 월드만 연다.
        void OpenCustomize(Workbench workbench);
        // 원작 InteractCrafter.OnInteractFinished의 CraftView.SetupAndOpenView 자리다. 기지 제조 장소에만 있어 전투 월드만 연다.
        void OpenCraft(InteractCrafter crafter);
        // 원작 FormulasRegisterViewInvoker.OnInteractFinished의 FormulasRegisterView.Show 자리다. 기지 작업대와 의료소에만 있어 전투 월드만 연다.
        void OpenFormulaRegister(FormulasRegisterViewInvoker invoker);
        // 원작 낚시터 Interact의 OnInteractTimeoutEvent(Action_FishingV2.OnInteractTimeOut → StartAction) 자리다. 낚시터는 기지와 제로존에만 있어
        // 전투 월드만 시작한다. 낚싯대와 미끼 확인, 진행은 낚시 행동과 그 호스트(IFishingHost)가 맡는다.
        void StartFishing(Action_FishingV2 fishing);
        // 원작 순간이동기의 ViewsProxy.ShowMapSelectionView 자리다. 기지 순간이동기에만 있어 전투 월드만 연다.
        // 출격 방식과 출발점은 지도 선택 화면(MapSelectionScreen)과 SceneFlow.BeginRaidAsync(RaidEntry)가 맡는다.
        void OpenMapSelection(MapSelectionViewInvoker invoker);
    }
}
