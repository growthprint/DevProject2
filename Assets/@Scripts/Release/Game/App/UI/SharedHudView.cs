#nullable enable annotations
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 LevelManager의 공용 HUD인 GameplayUICanvas와 HUDCanvas를 한 프리팹으로 재구성한 View다.
    // 앱 구성이 한 번 로드해 앱이 끝날 때까지 쓰고, 레벨 초기화를 마친 씬 컨트롤러 하나에 붙여 구동한다. 표시만 맡고 명령, 저장, 문구 선택은 소유자가 맡는다.
    // 붙이고 떼는 소유 규칙은 OwnedAppView가 맡고, 명령 이벤트 구독은 구독자가 스스로 해제한다.
    // 이 클래스는 프리팹 참조, HUD 표시 여부, 작은 하위 View만 다루고, 상태 표시, 결과 화면, 인벤토리 화면은 섹션 클래스가
    // 이 클래스의 참조를 직접 읽어 맡는다. 직렬화 필드 이름은 제작 도구와 프리팹이 쓰므로 바꾸지 않는다.
    [DisallowMultipleComponent]
    public sealed class SharedHudView : OwnedAppView
    {
        // 원본 HUDCanvas의 CanvasGroup과 CanvasGroupFade.fadeDuration이다. 원본 LevelManager의 값은 0.05초다.
        // 최소 시간은 제작 도구와 같은 UiFade 상수를 쓴다.
        [SerializeField] CanvasGroup _hudGroup = null!;
        [SerializeField, Min(UiFade._minimumDuration)] float _hudFadeDuration = .05f;
        // 원본 HUDCanvas/MouseKeyboardOnly/Interact_MouseKeyboard와 GameplayUICanvas/NotificationText.
        [SerializeField] InteractPromptView _interaction = null!;
        [SerializeField] NotificationTextView _notification = null!;
        // 원본 HUDCanvas/ActionProgress_Slider(모든 행동)와 AimMarker의 ActionProgress(장전 전용), EvacuationCountDown.
        [SerializeField] ActionProgressView _actionSlider = null!;
        [SerializeField] ActionProgressView _reloadProgress = null!;
        [SerializeField] EvacuationCountdownView _evacuation = null!;

        // 아래는 상태 표시(HudStatusDisplay)와 그 위젯이 직접 읽는 참조다. _statusObjects는 캐릭터가 연결된 씬에서만 켜는
        // 원본 HP, Energy, Water, ItemShortCut, WeaponButtons, BulletTypeHUD, TimeOfDayDisplay, AimMarker다.
        // _hideDuringInventory는 그중 인벤토리가 열린 동안 숨길 요소로, 퀵슬롯만 남겨 인벤토리 화면의 버튼을 가리지 않게 한다.
        [SerializeField] internal GameObject[] _statusObjects = Array.Empty<GameObject>();
        [SerializeField] internal GameObject[] _hideDuringInventory = Array.Empty<GameObject>();
        [SerializeField] internal TMP_Text _health = null!;
        [SerializeField] internal Image _healthFill = null!;
        // 원본 BulletCountHUD의 bulletCountText(BulletCount/Image/BulletCount)와 capacityText(BulletCount/Image/Capacity)다.
        [SerializeField] internal TMP_Text _ammo = null!, _ammoCapacity = null!;
        [SerializeField] internal TMP_Text _ammoType = null!, _clock = null!;
        // 원본 TimeOfDayDisplay의 phaseText, weatherText, stormRoot, stormTitleText, stormText, stormDescObject, stormIndicatorAnimator,
        // stormFillImage다. 폭풍 표시(HudStormDisplay)가 직접 읽고, 제작 도구(Author Shared HUD Storm Display)가 SerializedObject로 연결한다.
        // 지시기 Animator도 제작 도구가 원작 StormIndicator 컨트롤러 사본과 함께 늘 연결하고 SharedHudTests가 확인한다.
        [SerializeField] internal TMP_Text _timePhase = null!, _weatherName = null!, _stormTitle = null!, _stormTime = null!;
        [SerializeField] internal GameObject _stormRoot = null!, _stormDescription = null!;
        [SerializeField] internal Animator _stormIndicator = null!;
        [SerializeField] internal Image _stormFill = null!;
        [SerializeField] internal CanvasGroup _bulletGroup = null!;
        // 원본 BulletTypeHUD의 background(CurrentBulletType), typeList(SelectList), indicator(CurrentBulletType의 T 안내),
        // originSelectButton(BulletSelectButton) 참조다. 템플릿은 목록의 첫 항목으로 쓰고 나머지는 그 복제다.
        [SerializeField] internal Graphic _bulletTypeBackground = null!;
        [SerializeField] internal GameObject _bulletTypeList = null!, _bulletTypeIndicator = null!;
        [SerializeField] internal OriginalBulletTypeButton _bulletTypeButton = null!;
        [SerializeField] internal Image _energyFill = null!, _waterFill = null!;
        [SerializeField] internal RectTransform _quickRoot = null!;
        // 원본 ItemShortCut의 CanvasGroup이다. 원본처럼 평소에는 레이캐스트를 막지 않고, 인벤토리가 열린 동안만 끌어 놓기를 받는다.
        [SerializeField] internal CanvasGroup _quickGroup = null!;
        [SerializeField] internal TMP_Text[] _quickLabels = Array.Empty<TMP_Text>();
        [SerializeField] internal TMP_Text[] _quickNames = Array.Empty<TMP_Text>();
        [SerializeField] internal Image[] _quickIcons = Array.Empty<Image>(), _weaponIcons = Array.Empty<Image>();
        // 원본 ItemShortcutButton.usingIndicator(UseIndicator)다. 그 칸의 아이템을 손에 든 동안 켠다.
        [SerializeField] internal GameObject[] _quickUsing = Array.Empty<GameObject>();
        [SerializeField] internal GameObject[] _weaponFrames = Array.Empty<GameObject>();
        // 원본 WeaponButtons의 WeaponButton 칸이다. 원본 BulletTypeHUD.OnWeaponButtonSelected처럼 고른 칸 바로 위로 탄약 HUD를 옮길 때 쓴다.
        [SerializeField] internal RectTransform[] _weaponButtons = Array.Empty<RectTransform>();
        // 여러 창의 원본 MoneyDisplay/Money/Text와 MoneyDisplay/CashDisplay/Text다. 금액 표시(HudMoneyDisplay)가 직접 쓴다.
        [SerializeField] internal TMP_Text[] _moneyTexts = Array.Empty<TMP_Text>(), _cashTexts = Array.Empty<TMP_Text>();
        [SerializeField] internal RectTransform _aimRoot = null!;
        [SerializeField] internal RectTransform[] _crosshairArms = Array.Empty<RectTransform>();
        [SerializeField] internal RectTransform _followRoot = null!;
        [SerializeField] internal Vector2 _followOffset = new(-.5f, -.5f);
        [SerializeField, Min(0f)] internal float _followSmoothTime = .075f;
        [SerializeField, Min(0f)] internal float _followMaxDistance = 2f;
        [SerializeField] internal RectTransform _staminaRoot = null!;
        [SerializeField] internal Image _staminaFill = null!;
        [SerializeField] internal RectTransform _healthBars = null!;
        [SerializeField] internal OriginalEnemyHealthView _healthBarTemplate = null!;
        // 원본 HUDCanvas/CostTakerHUD의 항목 템플릿이다. 건설 지점 표시(HudCostTakers)가 지점마다 사본을 만든다.
        [SerializeField] internal OriginalCostTakerEntryView _costTakerEntry = null!;

        // 아래는 결과 화면(HudResultScreen)이 직접 읽는 참조다. 원본 경험치 곡선(_resultCurve)의 가중 접선은 제작 도구가 직렬화한다.
        [SerializeField] internal GameObject _resultPanel = null!;
        [SerializeField] internal CanvasGroup _resultFade = null!;
        [SerializeField] internal TMP_Text _resultTitle = null!;
        [SerializeField] internal TMP_Text _resultExperience = null!;
        [SerializeField] internal TMP_Text _resultLevel = null!;
        [SerializeField] internal Image _resultOldFill = null!, _resultCurrentFill = null!;
        [SerializeField] internal AnimationCurve _resultCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] internal Button _continue = null!;
        [SerializeField] internal TMP_Text _continueLabel = null!;

        // 아래는 인벤토리 화면(HudInventoryPanel)이 직접 읽는 참조다. 닫기 버튼은 런타임이 읽지 않아 CS0414 경고가 나므로
        // 참조 필드를 두지 않는다. 저작 도구가 경로로 찾아 영속 OnClick을 연결한다.
        [SerializeField] internal GameObject _inventory = null!;
        [SerializeField] internal TMP_Text _title = null!;
        [SerializeField] internal RectTransform _carried = null!;
        // 원본 ItemSlotsDisplay의 캐릭터 장비 칸 열 개다. 순서는 OriginalCharacterSlots.All과 같다.
        [SerializeField] internal OriginalSlotCell[] _slots = Array.Empty<OriginalSlotCell>();
        [SerializeField] internal TMP_Text _inventoryCapacity = null!, _inventoryWeight = null!;
        [SerializeField] internal Image _inventoryWeightFill = null!;
        // 원본 LootView의 lootTargetFadeGroup이 가리키는 LootTarget 영역이다. 상자도 창고도 열지 않으면 원본처럼 숨긴다.
        [SerializeField] internal GameObject _lootTarget = null!;
        [SerializeField] internal RectTransform _other = null!;
        // 원본 LootView의 petInventoryDisplay(InventoryDisplay_Pet)와 그 칸 격자(Container/Layout)다.
        // 원본처럼 펫 가방이 있을 때만 보이며, 제목줄과 용량 글자는 원본에서도 꺼져 있어 참조하지 않는다.
        [SerializeField] internal GameObject _petInventory = null!;
        [SerializeField] internal RectTransform _pet = null!;
        [SerializeField] internal OriginalInventoryCell _cell = null!;
        // 원본 LootView의 storeAllButton이다. 창고를 열었을 때만 보인다.
        [SerializeField] internal Button _storeAll = null!;
        // 원본 소지품과 LootTarget InventoryDisplay의 sortButton이다. 소지품 쪽은 늘, LootTarget 쪽은 창고를 열었을 때만 보인다.
        [SerializeField] internal Button _sortCarried = null!, _sortStorage = null!;
        [SerializeField] internal Button _use = null!;
        [SerializeField] internal TMP_Text _selected = null!;
        // 원본 ItemDraggingPointerDisplay다. 아이템을 끄는 동안 포인터를 따라간다.
        [SerializeField] internal ItemDraggingPointerView _dragPointer = null!;
        // 원본 LootView/ItemDetails의 ItemDetailsDisplay다. 셀을 고르면 그 아이템의 공통 정보, 아이템 칸, 속성 목록을 보인다.
        [SerializeField] internal OriginalItemDetailsView _details = null!;
        // 원본 GameplayUICanvas/Tabs/ViewArea/NoteIndexView다. 쪽지를 읽을 때 쪽지 화면(NoteScreen)이 연다.
        [SerializeField] internal OriginalNoteIndexView _notes = null!;
        // 원작 GameplayUICanvas의 QuestGiverView, StockShopView, StorageDock 창이다. 화면 컨트롤러가 상호작용으로 열고,
        // 소유자가 바뀌면 이 HUD가 닫아 앞 월드의 창을 남기지 않는다. 제작 도구가 SerializedObject로 연결한다.
        [SerializeField] OriginalQuestGiverView _questGiver = null!;
        [SerializeField] OriginalStockShopView _shop = null!;
        [SerializeField] OriginalStorageDockView _storageDock = null!;
        // 원작 GameplayUICanvas/Tabs/ViewArea/QuestVieｗ(마지막 글자 전각 w) 퀘스트 일지다. 일지 화면(QuestJournalScreen)이 입력으로 열고,
        // 소유자가 바뀌면 이 HUD가 닫는다. 제작 도구(Author Shared HUD Quest View)가 SerializedObject로 연결한다.
        [SerializeField] OriginalQuestView _questView = null!;
        // 원작 GameplayUICanvas의 MasterKeysRegisterView와 EndowmentSelection 창이다. 열쇠 등록 화면과 재능 선택 화면이 상호작용으로 열고,
        // 소유자가 바뀌면 이 HUD가 닫는다. 제작 도구(Author Shared HUD Master Keys Register, Endowment Selection)가 SerializedObject로 연결한다.
        [SerializeField] OriginalMasterKeysRegisterView _masterKeysRegister = null!;
        [SerializeField] OriginalEndowmentSelectionView _endowmentSelection = null!;
        // 원작 GameplayUICanvas/Tabs/ViewArea/MasterKeysIndex 열쇠 도감 탭이다. 도감 화면(MasterKeysIndexScreen)이 열고,
        // 소유자가 바뀌면 이 HUD가 닫는다. 제작 도구(Author Shared HUD Master Keys Index)가 SerializedObject로 연결한다.
        [SerializeField] OriginalMasterKeysIndexView _masterKeysIndex = null!;
        // 원작 GameplayUICanvas의 BuilderView 건설 창이다. 건설 화면(BuilderScreen)이 열고, 제작 도구가 연결한다.
        [SerializeField] OriginalBuilderView _builder = null!;
        // 원작 GameplayUICanvas의 PerkTreeView 특성 트리 창과 수면 창이다. 특성 트리 화면(PerkTreeScreen)과 수면 화면(SleepScreen)이 상호작용으로 열고,
        // 소유자가 바뀌면 이 HUD가 닫는다. 제작 도구(Author Shared HUD Perk Tree와 수면 도구)가 SerializedObject로 연결한다.
        [SerializeField] OriginalPerkTreeView _perkTree = null!;
        [SerializeField] OriginalSleepView _sleep = null!;
        // 원작 GameplayUICanvas의 ItemRepairView, ItemDecomposeView, ItemCustomizeSelectionView, ItemCustomizeView 창이다.
        // 수리, 분해, 개조 화면(RepairScreen, DecomposeScreen, CustomizeScreen)이 상호작용으로 열고, 소유자가 바뀌면 이 HUD가 닫는다.
        // 제작 도구(SharedHudAuthoring의 Repair, Decompose, Customize partial)가 SerializedObject로 연결한다.
        [SerializeField] OriginalItemRepairView _repair = null!;
        [SerializeField] OriginalItemDecomposeView _decompose = null!;
        [SerializeField] OriginalItemCustomizeSelectionView _customizeSelection = null!;
        [SerializeField] OriginalItemCustomizeView _customize = null!;
        // 원작 GameplayUICanvas의 CraftView와 FormulaRegisterView 창이다. 제조, 설계도 등록 화면(CraftScreen, FormulaRegisterScreen)이
        // 상호작용으로 열고, 소유자가 바뀌면 이 HUD가 닫는다. 제작 도구(SharedHudAuthoring의 Craft, FormulasRegister partial)가 SerializedObject로 연결한다.
        [SerializeField] OriginalCraftView _craft = null!;
        [SerializeField] OriginalFormulasRegisterView _formulasRegister = null!;
        // 원작 GameplayUICanvas/FormulasIndexView 레시피 도감 창이다. 설계도 연구 창의 도감 단추로 도감 화면(FormulasIndexScreen)이 열고,
        // 소유자가 바뀌면 이 HUD가 닫는다. 제작 도구(Author Shared HUD Formulas Index)가 SerializedObject로 연결한다.
        [SerializeField] OriginalFormulasIndexView _formulasIndex = null!;
        // 원작 GameplayUICanvas/DestinationSelectionView(MapSelectionView) 지도 선택 창이다. 기지 순간이동기로 지도 선택 화면(MapSelectionScreen)이 열고,
        // 소유자가 바뀌면 이 HUD가 닫는다. 제작 도구(Author Shared HUD Map Selection)가 SerializedObject로 연결한다.
        [SerializeField] MapSelectionView _mapSelection = null!;
        // 원작 GameplayUICanvas/Tabs/ViewButtons 탭 단추 줄이다. 전투 월드 컨트롤러가 단추 의도를 받아 탭 화면을 바꾸고 지금 탭을 강조한다.
        // 제작 도구(Author Shared HUD View Buttons)가 SerializedObject로 연결한다.
        [SerializeField] OriginalViewButtonsView _viewButtons = null!;

        readonly List<UnityEngine.Object> _hideTokens = new();
        bool _dialogueActive;
        // 상점, 보관함 같은 게임플레이 화면이 하나라도 열려 있는지다. 소유자가 SetScreenOpen으로 넘긴다.
        bool _screenOpen;
        // 섹션은 처음 쓸 때 한 번 만든다. EditMode 테스트가 Awake 없이 섹션 메서드를 부르기 때문이다.
        HudStatusDisplay? _statusDisplay;
        HudResultScreen? _resultScreen;
        HudInventoryPanel? _inventoryPanel;
        // 폭풍 표시와 금액 표시도 다른 섹션처럼 처음 쓸 때 만든다.
        HudStormDisplay? _stormDisplay;
        HudMoneyDisplay? _moneyDisplay;
        HudCostTakers? _costTakers;
        // 프리팹 참조는 제작 도구가 연결하고 SharedHudTests가 확인하므로 null이 아니라고 선언한다. 그래서 Awake와 공개 메서드는
        // 참조를 검사하지 않고 대체 UI도 만들지 않는다. 섹션 밖 호출자는 참조나 하위 View를 직접 바꾸지 않고 이 클래스와 섹션의 메서드만 쓴다.

        // 아래 세 섹션은 참조를 생성자로 다시 받지 않고 이 클래스의 internal 직렬화 필드를 직접 읽으므로 HUD만 넘긴다.
        // 원본 HUDCanvas의 체력, 생존 수치, 퀵슬롯, 무기, 탄약, 시계, 조준점, 스태미나, 적 체력바 표시다.
        internal HudStatusDisplay Status => _statusDisplay ??= new HudStatusDisplay(this);

        // 원본 ClosureView의 사망과 철수 결과 화면이다.
        internal HudResultScreen Result => _resultScreen ??= new HudResultScreen(this);

        // 원본 LootView의 루팅, 소지품, 장비 화면이다.
        internal HudInventoryPanel Inventory => _inventoryPanel ??= new HudInventoryPanel(this);

        // 원본 TimeOfDayDisplay의 단계, 날씨, 폭풍 표시다. 값은 월드의 시간대 볼륨(OriginalTimeOfDay)이 넘긴다.
        internal HudStormDisplay Storm => _stormDisplay ??= new HudStormDisplay(this);

        // 원본 MoneyDisplay와 CashDisplay의 계좌 잔액과 현금 표시다. 값은 생존 HUD 갱신(PlayerStatusHud)이 넘긴다.
        internal HudMoneyDisplay Money => _moneyDisplay ??= new HudMoneyDisplay(this);

        // 원본 CostTakerHUD의 건설 지점 이름과 필요 아이템 표시다. 위치와 가진 수는 월드의 표시기(CostTakerHud)가 넘긴다.
        internal HudCostTakers CostTakers => _costTakers ??= new HudCostTakers(this);

        // 원본 NoteIndexView 쪽지 창이다.
        internal OriginalNoteIndexView Notes => _notes;

        // 원작 퀘스트 창구, 상점, 창고 임시 보관함 창이다. 열기, 닫기, 명령은 각 화면 컨트롤러가 맡는다.
        internal OriginalQuestGiverView QuestGiver => _questGiver;
        internal OriginalStockShopView Shop => _shop;
        internal OriginalStorageDockView StorageDock => _storageDock;

        // 원작 QuestView 퀘스트 일지 창이다. 열기, 닫기, 명령은 일지 화면 컨트롤러가 맡는다.
        internal OriginalQuestView QuestView => _questView;

        // 원작 열쇠 등록 창과 재능 선택 창이다. 열기, 닫기, 명령은 각 화면 컨트롤러(MasterKeyRegisterScreen, EndowmentSelectionScreen)가 맡는다.
        internal OriginalMasterKeysRegisterView MasterKeysRegister => _masterKeysRegister;
        internal OriginalEndowmentSelectionView EndowmentSelection => _endowmentSelection;

        // 원작 MasterKeysView 열쇠 도감 탭이다. 열기, 닫기, 선택은 도감 화면 컨트롤러(MasterKeysIndexScreen)가 맡는다.
        internal OriginalMasterKeysIndexView MasterKeysIndex => _masterKeysIndex;
        // 건설 화면이 이 창에 표시를 맡긴다.
        internal OriginalBuilderView Builder => _builder;
        // 원작 특성 트리 창과 수면 창이다. 열기, 닫기, 명령은 각 화면 컨트롤러(PerkTreeScreen, SleepScreen)가 맡는다.
        internal OriginalPerkTreeView PerkTree => _perkTree;
        internal OriginalSleepView Sleep => _sleep;
        // 원작 수리, 분해, 개조 선택, 개조 창이다. 열기, 닫기, 명령은 각 화면 컨트롤러(RepairScreen, DecomposeScreen, CustomizeScreen)가 맡는다.
        internal OriginalItemRepairView Repair => _repair;
        internal OriginalItemDecomposeView Decompose => _decompose;
        internal OriginalItemCustomizeSelectionView CustomizeSelection => _customizeSelection;
        internal OriginalItemCustomizeView Customize => _customize;
        // 원작 제조 창과 설계도 등록 창이다. 열기, 닫기, 명령은 각 화면 컨트롤러(CraftScreen, FormulaRegisterScreen)가 맡는다.
        internal OriginalCraftView Craft => _craft;
        internal OriginalFormulasRegisterView FormulasRegister => _formulasRegister;
        // 원작 레시피 도감 창이다. 열기, 닫기, 선택은 도감 화면 컨트롤러(FormulasIndexScreen)가 맡는다.
        internal OriginalFormulasIndexView FormulasIndex => _formulasIndex;
        // 원작 지도 선택 창이다. 열기, 닫기, 항목과 확인 처리는 지도 선택 화면 컨트롤러(MapSelectionScreen)가 맡는다.
        internal MapSelectionView MapSelection => _mapSelection;
        // 원작 탭 단추 줄이다. 단추 의도 처리와 강조할 탭은 전투 월드 컨트롤러가 정한다.
        internal OriginalViewButtonsView ViewButtons => _viewButtons;

        // 원본 HUDManager.ShouldDisplay처럼 숨김 토큰, 대사, 결과 화면이 모두 없을 때만 HUDCanvas를 표시한다.
        // 토큰은 소유자가 닫을 때 직접 해제하므로 파괴된 토큰을 거르지 않고 개수만 본다.
        // 원본 View.ActiveView 조건도 넣어, 상점이나 보관함 같은 상호작용 창이 열린 동안 HUD가 창 밑에 겹쳐 보이지 않게 한다.
        public bool HudVisible => _hideTokens.Count == 0 && !_dialogueActive && !_screenOpen && !Result.IsOpen;

        void Awake()
        {
            // 상태 표시가 인벤토리 열림 상태를 읽으므로 인벤토리와 결과 화면을 먼저 초기화한다.
            Inventory.Initialize();
            Result.Initialize();
            Status.Initialize();
            _interaction.Hide();
            ApplyHudVisibility(true);
        }

        // HUD를 갓 만든 상태로 되돌린다. 결과 화면 취소는 앞 소유자의 연속을 그 자리에서 실행할 수 있으므로 맨 먼저 취소하고,
        // 뒤따르는 초기화가 그 연속이 건드린 표시를 다시 덮게 한다. 인벤토리를 닫은 뒤 상태 표시를 되돌린다.
        private protected override void ResetState()
        {
            Result.Reset();
            _hideTokens.Clear();
            _dialogueActive = false;
            _screenOpen = false;
            Inventory.Reset();
            // 앞 월드가 연 쪽지 창도 다음 소유자에게 남기지 않는다.
            _notes.Hide();
            // 앞 월드의 폭풍 문구도 남기지 않는다. 다음 월드의 시간대 볼륨이 Begin에서 다시 켠다.
            Storm.Reset();
            // 다음 월드의 첫 갱신이 금액을 반드시 다시 쓰게 한다.
            Money.Reset();
            // 앞 월드의 건설 지점 표시 사본은 그 월드의 지점을 가리키므로 지운다. 다음 월드가 자기 지점으로 다시 만든다.
            CostTakers.Clear();
            // 앞 월드가 연 상호작용 창과 탭 창, 강조한 탭 단추를 다음 소유자에게 남기지 않는다. 입력 차단은 화면 컨트롤러가 닫을 때 푼다.
            // 창 참조는 제작 도구가 모두 연결하고 SharedHudTests가 확인하므로 검사 없이 닫는다.
            _questGiver.Hide();
            _shop.Hide();
            _storageDock.Hide();
            _questView.Hide();
            _masterKeysRegister.Hide();
            _endowmentSelection.Hide();
            _masterKeysIndex.Hide();
            _builder.Hide();
            _perkTree.Hide();
            _sleep.Hide();
            _repair.Hide();
            _decompose.Hide();
            _customizeSelection.Hide();
            _customize.Hide();
            _craft.Hide();
            _formulasRegister.Hide();
            _formulasIndex.Hide();
            _mapSelection.Hide();
            _viewButtons.SetSelected(ViewTab.None);
            Status.Reset();
            _interaction.Hide();
            _actionSlider.ResetImmediate();
            _reloadProgress.ResetImmediate();
            _evacuation.ResetImmediate();
            _notification.Clear();
            ApplyHudVisibility(true);
        }

        // 원본 HUDManager.RegisterHideToken처럼, 커스터마이징같이 HUD를 가리는 화면이 열린 동안 HUD를 즉시 숨긴다.
        public void RegisterHideToken(UnityEngine.Object token)
        {
            // 내부 화면 소유자만 자신을 토큰으로 넘긴다. 화면 활성화마다 한 번만 등록하므로 중복 확인 없이 더한다.
            _hideTokens.Add(token);
            HideInteraction();
            ApplyHudVisibility(true);
        }

        // 표시 복귀는 LateUpdate의 페이드가 맡는다.
        public void UnregisterHideToken(UnityEngine.Object token) => _hideTokens.Remove(token);

        // 원본 DialogueUI.Active 동안 HUD를 숨기고 상호작용 안내를 남기지 않는다.
        public void SetDialogueActive(bool active)
        {
            _dialogueActive = active;
            if (active) HideInteraction();
        }

        // 원본 HUDManager의 View.ActiveView 조건이다. 게임플레이 화면 묶음(GameplayScreens)이 열린 동안 HUD를 페이드로 숨긴다.
        // 화면 묶음은 열림 알림이 없어 소유자가 매 프레임 넘기고, 표시는 LateUpdate가 상태가 바뀐 때만 쓴다.
        // 인벤토리는 퀵슬롯 끌어 놓기를 받으려고 이 조건에 넣지 않는다(의도한 차이).
        internal void SetScreenOpen(bool open)
        {
            if (open == _screenOpen) return;
            _screenOpen = open;
            if (open) HideInteraction();
        }

        // 원본 InteractHUD처럼 묶음 대표와 켜진 구성원의 이름을 차례로 보이고 selected 항목을 고른 상태로 표시한다.
        // labels는 대표가 첫 항목인 목록이고, 항목이 하나면 단일 안내와 모습이 같다.
        public void ShowInteraction(IReadOnlyList<string> labels, int selected, string key, Vector3 worldPoint, Camera camera)
        {
            if (!HudVisible || Inventory.IsOpen) { HideInteraction(); return; }
            _interaction.Show(labels, selected, key, worldPoint, camera);
        }

        public void HideInteraction() => _interaction.Hide();

        // 원본 NotificationText.Push처럼 현재 로캘 문구를 알림 대기열에 넣는다. HUD가 숨어 있으면 보이지 않게 지나가거나
        // 다음 Attach의 ResetState가 그 문구를 버린다.
        public void Notify(string text) => _notification.Push(text);

        // 원본 ActionProgressHUD 두 개: Whatever 슬라이더는 모든 진행 행동을, 조준점 원형은 장전만 표시한다.
        public void SetActionProgress(bool inProgress, float progress, bool reload)
        {
            _actionSlider.Present(inProgress, progress);
            _reloadProgress.Present(inProgress && reload, progress);
        }

        // 원본 EvacuationCountdownUI.Request/Release에 해당하는 철수 표시를 갱신한다.
        public void SetEvacuation(bool active, float remaining, float progress)
        {
            if (active) _evacuation.Show(remaining, progress);
            else _evacuation.Hide();
        }

        // 아래 고정 버튼 핸들러는 프리팹 영속 OnClick(Runtime Only)만 부르므로 코드로 구독하지 않는다.
        // 이름은 프리팹이 쓰므로 바꾸지 않고, 동작은 결과 화면과 인벤토리 화면 섹션에 넘긴다. 규칙은 Docs/rules/ui.md에 있다.
        public void OnContinueClicked() => Result.Continue();
        public void OnCloseClicked() => Inventory.RequestClose();
        public void OnUseClicked() => Inventory.RequestUse();

        // 모두 넣기 버튼은 열린 인벤토리 안에만 있어 인벤토리가 열린 동안에만 눌린다.
        public void OnStoreAllClicked()
        {
            Inventory.RequestStoreAll();
        }

        // 정리 버튼도 열린 인벤토리 안에만 있어 인벤토리가 열린 동안에만 눌린다.
        public void OnSortCarriedClicked()
        {
            Inventory.RequestSort(false);
        }

        public void OnSortStorageClicked()
        {
            Inventory.RequestSort(true);
        }

        // 마지막으로 적용한 HUD 표시 상태와 페이드 진행 여부다. 원본 HUDManager처럼 상태가 바뀔 때만 CanvasGroup을 쓰려고 둔다.
        bool _hudShown, _hudFading;

        // 원본 FadeGroup 페이드를 시간 배율과 무관하게 UiFade 규칙으로 진행하고 숨김 중에는 HUD 입력을 받지 않는다.
        // 결과 화면 열림은 섹션이 직접 바꿔 알림이 없으므로 매 프레임 상태만 비교한다. 입력 속성은 상태가 바뀔 때만, 알파는 페이드 동안만 쓴다.
        void ApplyHudVisibility(bool immediate)
        {
            bool visible = HudVisible;
            if (!immediate && visible == _hudShown && !_hudFading) return;
            if (immediate || visible != _hudShown)
            {
                _hudShown = visible;
                _hudGroup.blocksRaycasts = visible;
                _hudGroup.interactable = visible;
            }
            float target = visible ? 1f : 0f;
            float alpha = immediate ? target : UiFade.Step(_hudGroup.alpha, target, Time.unscaledDeltaTime, _hudFadeDuration);
            _hudGroup.alpha = alpha;
            _hudFading = alpha != target;
        }

        // 씬마다 루트를 끄고 켜면 GameObject 약 4,900개의 OnEnable과 CanvasGroup 알림 약 4만 회가 돌아 전환 프레임마다
        // 약 20 ms가 든다(2026-10-23). 그래서 루트는 켜 둔 채 두 루트 Canvas(GameplayUICanvas, HUDCanvas)의 그리기와 입력만 켜고 끈다.
        // 꺼진 루트는 보일 때 한 번 켠다. 규칙: Docs/product-ui-authoring.md#shared-hud
        private protected override void SetShown(bool shown)
        {
            if (shown && !gameObject.activeSelf) gameObject.SetActive(true);
            for (int index = 0; index < transform.childCount; index++)
                UiVisibility.SetCanvasShown(transform.GetChild(index).gameObject, shown);
        }

        void LateUpdate()
        {
            // 숨긴 HUD는 루트가 켜져 있어도 페이드와 상태 표시를 갱신하지 않는다. 루트를 끄던 때처럼 다음 Attach가 상태를 다시 맞추고,
            // 숨긴 동안 HUDCanvas의 CanvasGroup 입력 값을 다시 켜지 않는다.
            if (!Attached) return;
            ApplyHudVisibility(false);
            Status.LateUpdate();
        }
    }
}
