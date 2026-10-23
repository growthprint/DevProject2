#nullable enable annotations

namespace Game.App
{
    // 원작 View.ActiveView처럼 퀘스트 창구, 상점, 보관함, 퀘스트 일지, 열쇠 등록, 재능 선택, 건설, 열쇠 도감, 특성 트리, 수면, 수리, 분해, 개조 화면을 한 번에 하나만 연다. 새 화면을 열면 원작 View.OnOpen처럼
    // 열린 다른 화면을 먼저 닫는다. 전투 월드 컨트롤러가 준비 때 만들고, 상호작용 호스트(WorldInteractions), ESC 경로, 일지 키가 이 객체로 연다.
    // 입력 차단, 구독, 자동 저장 알림 같은 화면별 수명 규칙은 각 화면이 맡고, 이 객체는 화면 사이의 순서만 정한다.
    // 제조 창과 설계도 등록 창도 같은 규칙으로 하나만 연다.
    // 탭 단추 줄(원작 Tabs/ViewButtons)의 퀘스트와 열쇠 단추도 전투 월드 컨트롤러를 거쳐 이 객체로 연다. 인벤토리와 쪽지 창은 이 객체 밖이라
    // 단추를 받은 컨트롤러가 먼저 닫는다.
    internal sealed class GameplayScreens
    {
        readonly GameplayWorldContext _world;
        // 과제 알림은 화면이 닫혀 있어도 떠야 해서 화면 하나가 아니라 이 묶음이 만들고, 퀘스트 창구 화면에 넘겨 함께 쓴다.
        readonly QuestNotices _notices;
        // 열린 화면 판정, ESC 닫기, 다른 화면 닫기가 도는 순서다. 아래 화면 필드 순서와 같다.
        readonly GameplayScreen[] _all;
        readonly QuestGiverScreen _questGiver;
        readonly StockShopScreen _shop;
        readonly StorageDockScreen _storageDock;
        readonly QuestJournalScreen _journal;
        // 원작 MasterKeysRegisterView와 EndowmentSelectionPanel도 View.ActiveView 하나로 열리므로 같은 순서 규칙에 넣는다.
        readonly MasterKeyRegisterScreen _masterKeyRegister;
        readonly EndowmentSelectionScreen _endowmentSelection;
        // 원작 BuilderView와 열쇠 도감(MasterKeysIndexView)도 View.ActiveView 하나로 열리므로 같은 순서 규칙에 넣는다.
        readonly BuilderScreen _builder;
        readonly MasterKeysIndexScreen _masterKeysIndex;
        // 원작 PerkTreeView와 수면 창(SleepView)도 View.ActiveView 하나로 열리므로 같은 순서 규칙에 넣는다.
        readonly PerkTreeScreen _perkTree;
        readonly SleepScreen _sleep;
        // 원작 ItemRepairView, ItemDecomposeView, ItemCustomizeSelectionView도 View.ActiveView 하나로 열리므로 같은 순서 규칙에 넣는다.
        // 개조 화면은 선택 창과 개조 창을 함께 맡는 화면 하나다.
        readonly RepairScreen _repair;
        readonly DecomposeScreen _decompose;
        readonly CustomizeScreen _customize;
        // 원작 CraftView와 FormulasRegisterView도 View.ActiveView 하나로 열리므로 같은 순서 규칙에 넣는다.
        readonly CraftScreen _craft;
        readonly FormulaRegisterScreen _formulaRegister;
        // 원작 FormulasIndexView(레시피 도감)도 View.ActiveView 하나로 열리므로 같은 순서 규칙에 넣는다. 설계도 연구 창의 도감 단추로만 연다.
        readonly FormulasIndexScreen _formulasIndex;
        // 원작 MapSelectionView(순간이동기 지도 선택)도 View.ActiveView 하나로 열리므로 같은 순서 규칙에 넣는다.
        readonly MapSelectionScreen _mapSelection;

        // 레이드 상점(비밀 상인) 구매가 가방에 다 들어가지 않으면 원작처럼 발밑에 떨구도록 월드의 떨굼 생성기를 상점 화면에 넘긴다.
        // 화면은 모두 월드 문맥으로 공용 HUD 연결, 세션, 카탈로그를 읽으므로 연결 소유자를 따로 받지 않는다.
        internal GameplayScreens(GameplayWorldContext world, DroppedItemSpawner drops)
        {
            _world = world;
            _notices = new QuestNotices(world);
            _questGiver = new QuestGiverScreen(world, _notices);
            _shop = new StockShopScreen(world, drops);
            _storageDock = new StorageDockScreen(world);
            _journal = new QuestJournalScreen(world);
            // 재능 선택 화면은 생성자로 View를 받는다. 공용 HUD는 월드 문맥이 만들어질 때 정해지므로 여기서 꺼내 넘긴다.
            _masterKeyRegister = new MasterKeyRegisterScreen(world);
            _endowmentSelection = new EndowmentSelectionScreen(world, world.Hud.EndowmentSelection);
            // 건설 화면은 만들 때 건설 입력 동작을 찾는다. 이 생성자는 레벨 준비 안에서만 불린다.
            _builder = new BuilderScreen(world);
            _masterKeysIndex = new MasterKeysIndexScreen(world);
            // 특성 트리와 수면 화면도 월드 문맥만 받는다. 둘 다 열 때 공용 HUD 연결을 확인한다.
            _perkTree = new PerkTreeScreen(world);
            _sleep = new SleepScreen(world);
            // 수리, 분해, 개조 화면도 월드 문맥만 받는다. 열 때 공용 HUD 연결을 확인한다.
            _repair = new RepairScreen(world);
            _decompose = new DecomposeScreen(world);
            _customize = new CustomizeScreen(world);
            // 제조와 설계도 등록 화면도 월드 문맥만 받는다. 열 때 공용 HUD 연결을 확인한다.
            _craft = new CraftScreen(world);
            _formulaRegister = new FormulaRegisterScreen(world);
            // 연구 창의 도감 단추 의도를 받아 도감을 연다. 두 화면 모두 이 객체와 수명이 같아 구독을 풀지 않는다.
            _formulasIndex = new FormulasIndexScreen(world);
            _formulaRegister.IndexRequested += ShowFormulasIndex;
            // 지도 선택 화면도 월드 문맥만 받는다. 출격 전환은 열 때 호스트가 넘긴다.
            _mapSelection = new MapSelectionScreen(world);
            // 화면 사이의 순서 규칙이 도는 목록이다. 새 화면은 필드와 같은 자리에 넣는다.
            _all = new GameplayScreen[]
            {
                _questGiver, _shop, _storageDock, _journal, _masterKeyRegister, _endowmentSelection, _builder, _masterKeysIndex, _perkTree, _sleep,
                _repair, _decompose, _customize, _craft, _formulaRegister, _formulasIndex, _mapSelection,
            };
        }

        // 이 묶음의 화면 중 하나라도 입력을 막고 열려 있는지다.
        internal bool IsOpen
        {
            get
            {
                foreach (GameplayScreen screen in _all)
                    if (screen.IsOpen) return true;
                return false;
            }
        }

        // 원작 QuestManager.OnTaskFinished 알림은 화면이 닫혀 있어도 뜬다. 컨트롤러가 공용 HUD를 붙일 때 켜고 뗄 때 끈다.
        internal void Attach() => _notices.Start();

        // 공용 HUD를 떼기 전에 알림 구독을 풀고 열린 화면을 닫아 입력 차단을 돌려준다. 여러 번 불러도 된다.
        internal void Detach()
        {
            _notices.Stop();
            Close();
        }

        internal void ShowQuestGiver(QuestGiver giver)
        {
            CloseOthers(_questGiver);
            _questGiver.Show(giver);
        }

        internal void ShowShop(StockShopInteract shop)
        {
            CloseOthers(_shop);
            _shop.Show(shop.Shop);
        }

        internal void ShowStorageDock()
        {
            CloseOthers(_storageDock);
            _storageDock.Show();
        }

        // 호출자: WorldInteractions.OpenMasterKeyRegister(Building_MasterKey 안의 MasterKeyRegisterViewInvoker).
        internal void ShowMasterKeyRegister()
        {
            CloseOthers(_masterKeyRegister);
            _masterKeyRegister.Show();
        }

        // 호출자: WorldInteractions.OpenEndowmentSelection(스킬 강화 기계의 EndowmentSelectionInteract).
        internal void ShowEndowmentSelection()
        {
            CloseOthers(_endowmentSelection);
            _endowmentSelection.Show();
        }

        // 호출자: WorldInteractions.OpenBuilder(기지 건설 입구의 BuilderViewInvoker).
        internal void ShowBuilder(BuildingArea area)
        {
            CloseOthers(_builder);
            _builder.Show(area);
        }

        // 호출자: OriginalGameplayController.OpenViewTab(탭 단추 줄의 KeyButton, 원작 ViewsProxy.ShowKeyView → MasterKeysView.Show).
        internal void ShowMasterKeysIndex()
        {
            CloseOthers(_masterKeysIndex);
            _masterKeysIndex.Show();
        }

        // 호출자: WorldInteractions.OpenPerkTree(스킬 강화 기계와 특성 건물의 PerkTreeUIInvoker). 트리는 진입점의 원작 perkTreeID로 고른다.
        internal void ShowPerkTree(PerkTreeUIInvoker invoker)
        {
            CloseOthers(_perkTree);
            _perkTree.Show(invoker.PerkTreeId);
        }

        // 호출자: WorldInteractions.OpenSleep(기지 침대의 SleepInteractable).
        internal void ShowSleep(SleepInteractable sleep)
        {
            CloseOthers(_sleep);
            _sleep.Show(sleep);
        }

        // 호출자: WorldInteractions.OpenRepair(기지 작업대 수리 항목의 ShowRepairView 동작).
        internal void ShowRepair()
        {
            CloseOthers(_repair);
            _repair.Show();
        }

        // 호출자: WorldInteractions.OpenDecompose(기지 작업대 분해 항목과 제로존 분해 스테이션의 ShowDecomposeView 동작).
        internal void ShowDecompose()
        {
            CloseOthers(_decompose);
            _decompose.Show();
        }

        // 호출자: WorldInteractions.OpenCustomize(기지 작업대 개조 항목의 Workbench).
        internal void ShowCustomize()
        {
            CloseOthers(_customize);
            _customize.Show();
        }

        // 호출자: WorldInteractions.OpenCraft(기지 제조 장소의 InteractCrafter). 목록은 진입점의 원작 requireTag로 거른다.
        internal void ShowCraft(InteractCrafter crafter)
        {
            CloseOthers(_craft);
            _craft.Show(crafter);
        }

        // 호출자: WorldInteractions.OpenFormulaRegister(기지 작업대와 의료소의 FormulasRegisterViewInvoker). 칸 판정은 진입점의 원작 additionalTags를 쓴다.
        internal void ShowFormulaRegister(FormulasRegisterViewInvoker invoker)
        {
            CloseOthers(_formulaRegister);
            _formulaRegister.Show(invoker);
        }

        // 호출자: 설계도 연구 창의 도감 단추(FormulaRegisterScreen.IndexRequested). 원작 View.OnOpen처럼 연구 창을 닫고 도감을 연다.
        // 원작은 도감을 닫아도 연구 창으로 돌아가지 않는다.
        void ShowFormulasIndex()
        {
            CloseOthers(_formulasIndex);
            _formulasIndex.Show();
        }

        // 호출자: WorldInteractions.OpenMapSelection(기지 순간이동기의 MapSelectionViewInvoker). 확인하면 넘겨받은 출격 전환으로 떠난다.
        internal void ShowMapSelection(System.Func<RaidEntry, Cysharp.Threading.Tasks.UniTask> depart)
        {
            CloseOthers(_mapSelection);
            _mapSelection.Show(depart);
        }

        // 원작 BuilderView.Update, PerkEntry.FixedUpdate, PerkDetails.Update 자리다. 건설 창은 매 프레임 커서, 회전, 카메라 입력을 보고,
        // 특성 트리 창은 연구 남은 시간을 그린다. 전투 월드 컨트롤러가 게임플레이를 진행할 수 있는 프레임에만 부른다.
        internal void Tick()
        {
            _builder.Tick();
            _perkTree.Tick();
        }

        // 원작 CharacterInputControl.OnUIQuestViewInput이다. 일시정지, 대사, 씬 전환 중에는 무시한다(SimulationReady).
        // 일지가 열려 있으면 원작 TryQuit처럼 닫는다. 닫혀 있으면 다른 화면(인벤토리, 쪽지, 지도, 이 묶음의 화면)이 입력을 막지 않을 때만
        // 연다. 원작 View.ActiveView == null 조건이다.
        internal void ToggleJournal()
        {
            if (!_world.SimulationReady) return;
            if (_journal.IsOpen)
            {
                _journal.TryClose();
                return;
            }
            if (_world.Runtime.GameplayInput.IsBlocked) return;
            _journal.Toggle();
        }

        // 호출자: OriginalGameplayController.OpenViewTab(탭 단추 줄의 QuestsButton, 원작 ViewsProxy.ShowQuestView → QuestView.Show).
        // 원작 View.Open처럼 열린 다른 화면을 닫고 일지를 연다. 일지 키와 달리 이미 열려 있으면 닫지 않는다.
        internal void ShowJournal()
        {
            CloseOthers(_journal);
            if (!_journal.IsOpen) _journal.Toggle();
        }

        // 탭 단추 줄의 강조(원작 ViewTabDisplayEntry)가 묻는다. 이 묶음의 화면 중 원작 Tabs/ViewArea에 있는 창은 일지와 열쇠 도감뿐이다.
        internal ViewTab OpenTab => _journal.IsOpen ? ViewTab.Quest : _masterKeysIndex.IsOpen ? ViewTab.Key : ViewTab.None;

        // ESC 처리가 묻는다. 한 번에 하나만 열리므로 처음으로 true를 돌려준 화면에서 멈춘다. 퀘스트 창구는 완료 패널만 닫고
        // 남을 수 있고, 지도 선택 창은 원작 TryQuit처럼 목적지 확인 중이면 닫지 않고 ESC만 받는다.
        internal bool TryClose()
        {
            foreach (GameplayScreen screen in _all)
                if (screen.TryClose()) return true;
            return false;
        }

        // 씬 전환과 정리가 무조건 불러 모든 화면을 닫고 입력 차단을 돌려준다. 화면마다 여러 번 불러도 된다.
        // 열쇠 등록 창은 칸의 열쇠를 가방으로 돌려주고, 건설 창은 카메라를 되돌리며, 지도 선택 창은 기다리던 출발을 멈춘다.
        internal void Close() => CloseOthers(null);

        // 원작 View.OnOpen처럼 새 화면을 열 때 나머지 화면을 목록 순서로 닫는다. 화면은 일반 클래스라 참조로 비교하고,
        // keep이 null이면 모든 화면을 닫는다.
        void CloseOthers(GameplayScreen? keep)
        {
            foreach (GameplayScreen screen in _all)
                if (!ReferenceEquals(keep, screen)) screen.Close();
        }
    }
}
