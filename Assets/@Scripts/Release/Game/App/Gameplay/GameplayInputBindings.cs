#nullable enable annotations
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 원작 InputManager처럼 전투 월드의 게임플레이 입력 액션, 마우스 휠 상태, UI 액션 임대, 상호작용 키캡을 맡는다.
    // 전투 월드 컨트롤러가 준비 때 앱의 입력 실행 사본으로 만든다. 입력을 전투와 상호작용에 나누는 일과 프레임 순서는 컨트롤러가 정한다.
    internal sealed class GameplayInputBindings
    {
        // 같은 단축키를 인벤토리에서는 칸 등록에, 플레이 중에는 사용에 쓴다. 칸 수는 저장 퀵슬롯과 퀵슬롯 표시가 함께 쓰는 값이다.
        internal const int _quickSlotCount = ItemShortcutState._slotCount;
        // 원본 UpdateScollerInput의 AmmoAndInteract 기본값이다. 휠 값이 이 크기를 넘고, 직전에 휠이 이 프레임 수 이상 멈춰 있었을 때만 넘긴다.
        const float _scrollThreshold = .5f;
        const int _scrollIdleFramesRequired = 2;
        readonly InputAction[] _quickActions = new InputAction[_quickSlotCount];
        // 원본 ScrollWheel(마우스 휠)이다. 원본의 위아래 화살표 입력은 두지 않는다.
        readonly InputAction _scrollWheel;
        // 이 씬이 직접 켠 UI 액션의 임대다. 해제 때 이 임대만 끄고 다른 입력 소유자의 상태를 보존한다.
        readonly UiEventSubscriptions _uiActions = new UiEventSubscriptions();
        // 원본 scollYZeroFrames다. 휠이 멈춘 프레임 수를 2에서 멈춰 세어, 한 번 굴릴 때 한 칸만 넘기게 한다.
        int _scrollIdleFrames;

        // 입력은 저장한 키 설정을 따르는 앱의 입력 실행 사본에서 찾는다. 하나라도 없으면 FindAction이 던져 레벨 초기화를 멈추므로
        // 만든 객체에는 null 액션이 없다. 키캡은 원본 InputIndicator처럼 키보드 바인딩 문자열을 여기서 한 번 계산한다.
        internal GameplayInputBindings(InputActionAsset actions)
        {
            Interact = actions.FindAction(GameInputPaths._playerInteract, true);
            Attack = actions.FindAction(GameInputPaths._playerAttack, true);
            DialogueConfirm = actions.FindAction(GameInputPaths._uiDialogueConfirm, true);
            Inventory = actions.FindAction(GameInputPaths._uiToggleInventory, true);
            Reload = actions.FindAction("Player/Reload", true);
            PrimaryWeapon = actions.FindAction("Player/PrimaryWeapon", true);
            SecondaryWeapon = actions.FindAction("Player/SecondaryWeapon", true);
            MeleeWeapon = actions.FindAction("Player/MeleeWeapon", true);
            SwitchBulletType = actions.FindAction("Player/SwitchBulletType", true);
            _scrollWheel = actions.FindAction("Player/ScrollWheel", true);
            CatchFish = actions.FindAction(GameInputPaths._playerJump, true);
            Aim = actions.FindAction(GameInputPaths._playerAim, true);
            InteractKey = GameLocalization.KeyboardBindingLabel(Interact);
            for (int i = 0; i < _quickSlotCount; i++) _quickActions[i] = actions.FindAction("UI/QuickSlot" + (i + 1), true);
            QuestView = actions.FindAction("UI/ToggleQuest", true);
        }

        internal InputAction Interact { get; }
        // 사격과 대사 상자 클릭에 쓴다. 컨트롤러는 직접 읽지 않고 전투 시뮬레이션과 대사 진행자에 넘긴다.
        internal InputAction Attack { get; }
        // 원작 CancelSkill은 ADS와 같은 우클릭이라 전투 시뮬레이션이 스킬 조준 취소에도 쓴다.
        internal InputAction Aim { get; }
        // 원본 DialogueUI처럼 Space로 대사를 확인한다. 클릭은 Player/Attack이 맡는다.
        internal InputAction DialogueConfirm { get; }
        internal InputAction Inventory { get; }
        // 원작 UI_Quest(CharacterInputControl.OnUIQuestViewInput) 입력이다. Player 맵이 잠겨도 일지를 닫을 수 있게 UI 액션 임대로 켠다.
        internal InputAction QuestView { get; }
        // 원작 InputManager.Dash가 낚시 행동에 넘기는 낚아채기 입력이다. 모터가 대시에 읽는 Player/Jump를 켜고 끄지 않고 함께 읽는다.
        internal InputAction CatchFish { get; }
        internal InputAction Reload { get; }
        internal InputAction PrimaryWeapon { get; }
        internal InputAction SecondaryWeapon { get; }
        internal InputAction MeleeWeapon { get; }
        // 원본 SwitchBulletType(T)이다.
        internal InputAction SwitchBulletType { get; }
        // 상호작용 안내에 보일 키캡이다. 옵션 적용 때 RefreshInteractKey가 다시 계산한다.
        internal string InteractKey { get; private set; }

        // 이번 프레임에 그 칸의 단축키를 눌렀는지다. 칸 번호는 0부터 _quickSlotCount 앞까지다.
        internal bool QuickSlotPressed(int index) => _quickActions[index].WasPressedThisFrame();

        // 원본 UpdateScollerInput처럼 휠 한 번에 한 칸만 넘긴다. 위로 굴리면 +1, 아래로 굴리면 -1, 넘기지 않으면 0이다.
        // 휠이 멈춘 프레임 수를 세므로 플레이 입력을 처리하는 프레임마다 한 번만 부른다.
        internal int ReadScrollStep()
        {
            float scroll = _scrollWheel.ReadValue<Vector2>().y;
            bool scrolled = Mathf.Abs(scroll) > _scrollThreshold;
            int direction = scrolled && _scrollIdleFrames >= _scrollIdleFramesRequired ? (scroll > 0f ? 1 : -1) : 0;
            if (scrolled) _scrollIdleFrames = 0;
            else if (_scrollIdleFrames < _scrollIdleFramesRequired) _scrollIdleFrames++;
            return direction;
        }

        // Player 맵이 잠겨도 퀵슬롯 등록, 인벤토리 토글, 대사 확인(Space)을 쓸 수 있게 꺼져 있던 액션만 켠다.
        // 실제로 켠 임대만 보관한다. 일지가 열려 Player 맵이 잠겨도 같은 키로 닫을 수 있게 일지 키도 켠다.
        internal void AcquireUiActions()
        {
            foreach (InputAction quickAction in _quickActions) _uiActions.Enable(quickAction);
            _uiActions.Enable(Inventory);
            _uiActions.Enable(DialogueConfirm);
            _uiActions.Enable(QuestView);
        }

        // 이번 씬 임대가 켠 키만 원래 비활성 상태로 복원하고 다른 소유자가 켠 상태는 보존한다. 여러 번 불러도 된다.
        internal void ReleaseUiActions() => _uiActions.Clear();

        // 옵션 적용에는 키 재바인딩이 들어 있을 수 있어 상호작용 키캡을 다시 계산한다. 컨트롤러가 옵션 적용 알림에 건다.
        internal void RefreshInteractKey(GameOptions options) => InteractKey = GameLocalization.KeyboardBindingLabel(Interact);
    }
}
