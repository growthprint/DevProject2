#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 원작 BuilderView의 열기, 건물 목록, 배치, 건물 메뉴, 회수, 건설 카메라를 맡는 화면 컨트롤러다. MasterKeyRegisterScreen과 같은 수명 규칙이고,
    // 입력 차단 핸들은 화면 공통 바탕(GameplayScreen)이 쥔다. 배치는 원작처럼 범위, 데이터 겹침, 물리 겹침을 미리 거른 뒤 TryBuyAndPlaceBuilding으로
    // 보내고, 회수는 건물 소유자에 보낸다. 원작 철거 모드(Destroying)는 여는 버튼과 호출 코드가 없어 옮기지 않는다.
    // 매 프레임 일은 전투 월드 컨트롤러가 GameplayScreens.Tick으로 부른다.
    internal sealed class BuilderScreen : GameplayScreen
    {
        // 원작 BuyAndPlace 성공 소리 UI/building_up, 목록 줄의 tokenFormatKey, 건물 메뉴 회수 항목의 textKey다.
        const string _builtSound = "event:/UI/building_up";
        const string _tokenFormatKey = "BuilderView_TokenAmountFormat";
        const string _recycleKey = "UI_Recycle";

        readonly GameplayWorldContext _world;
        // 원작 input_Rotate(Q, E), input_RequestContextMenu(오른쪽 클릭), input_MoveCamera(WASD)다. 창이 열리면 Player 맵이
        // 잠기므로 UI 맵 동작을 쓰고, 열린 동안만 꺼져 있던 것을 임대로 켠다. 임대는 구독 묶음이 이벤트 구독과 함께 돌려준다.
        readonly InputAction[] _actions;
        readonly InputAction _rotate, _requestContextMenu, _moveCamera;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        BuildingArea? _area;
        // 원작 Placing 모드에서 놓을 건물 정의다. null이면 None 모드다. 화면 상태라 저장하지 않는다.
        BuildingInfo? _placing;
        // 원작 previewBuilding이다. 프리팹을 비동기로 불러오므로 배치 중에도 잠시 비어 있을 수 있고, 판정은 정의의 크기로 한다.
        Building? _preview;
        // 미리보기를 지우거나 창을 닫을 때마다 오른다. 그 전에 시작한 로드는 끝나도 미리보기를 만들지 않는다.
        int _previewGeneration;
        // 원작 previewRotation과 cameraCursor다. 회전은 원작처럼 배치를 끝내도 다음 배치에 남는다.
        BuildingRotation _rotation;
        Vector3 _cameraCursor;
        // 건물 메뉴가 가리키는 설치 번호다. 설치 번호는 0 이상이라 -1은 메뉴 대상 없음이다.
        int _menuTarget = -1;
        // 목록 다시 그리기가 줄마다 한 번 판정한 값(정의 번호, 토큰 수, 지불 가능)이다. 두 번 돌며 순서만 나누므로 판정을 다시 하지 않는다.
        readonly List<(int info, int tokens, bool canPay)> _rows = new List<(int info, int tokens, bool canPay)>();

        // 레벨 준비가 화면 묶음을 만들 때 한 번 찾는다. 입력 에셋에 동작이 없으면 레벨 준비가 멈춘다.
        internal BuilderScreen(GameplayWorldContext world)
        {
            _world = world;
            InputActionAsset actions = GameManager.InputActions;
            _rotate = actions.FindAction("UI/BuilderRotate", true);
            _requestContextMenu = actions.FindAction("UI/RightClick", true);
            _moveCamera = actions.FindAction("UI/BuilderMoveCamera", true);
            _actions = new[] { _rotate, _requestContextMenu, _moveCamera };
        }

        // 원작 BuilderView.Show와 OnOpen이다. 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 못 받으면(정리 중) 열지 않는다.
        // 호출자: GameplayScreens.ShowBuilder(기지 건설 입구의 BuilderViewInvoker).
        internal void Show(BuildingArea area)
        {
            // 연결과 세션 확인, 다시 열기 전 닫기, 입력 차단은 다른 화면과 같은 순서라 공통 바탕이 맡는다.
            if (!TryBeginShow(_world, out GameSessionManager? session)) return;
            _area = area;
            // 꺼져 있던 동작만 켜고 그 동작만 끄는 임대 규칙은 구독 묶음이 맡는다. 닫을 때 Clear가 차단 해제보다 먼저 돌려준다.
            foreach (InputAction action in _actions) _subscriptions.Enable(action);
            OriginalBuilderView view = _world.Hud.Builder;
            Action<string> select = Select, recycleAll = RecycleAll;
            Action confirm = Confirm, recycle = RecycleMenuTarget, close = Close, redraw = Redraw;
            view.EntrySelected += select;
            _subscriptions.Add(() => view.EntrySelected -= select);
            view.RecycleRequested += recycleAll;
            _subscriptions.Add(() => view.RecycleRequested -= recycleAll);
            view.ConfirmRequested += confirm;
            _subscriptions.Add(() => view.ConfirmRequested -= confirm);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
            OriginalBuildingContextMenuView menu = view.ContextMenu;
            menu.RecycleRequested += recycle;
            _subscriptions.Add(() => menu.RecycleRequested -= recycle);
            // 원작 목록은 건물 목록 신호로, 비용 표시는 돈과 아이템 변경으로 다시 그린다. 열린 동안만 듣고 줄이 적어 알림마다 다시 그린다.
            BuildingManager.Changed += redraw;
            _subscriptions.Add(() => BuildingManager.Changed -= redraw);
            EconomyManager.Changed += redraw;
            _subscriptions.Add(() => EconomyManager.Changed -= redraw);
            // 세션과 로캘 다시 그리기는 공용 구독 도우미가 붙이고 뗀다.
            _subscriptions.BindSessionChanged(session, redraw);
            _subscriptions.BindLocaleChanged(redraw);
            view.Show();
            EnterNone(view);
            // 원작 GridDisplay.Setup은 영역 왼쪽 아래 칸과 거기서 끝 칸까지의 칸 차이(반폭 × 2 - 1)를 넘긴다.
            view.Grid.Show(area.CenterCoord - (area.Size - Vector2Int.one), area.Size * 2 - Vector2Int.one);
            _cameraCursor = area.transform.position;
            view.PlaceCamera(_cameraCursor);
            // 원작 vcam은 우선순위가 높아 켜지면 화면이 그쪽으로 섞인다. 제품 게임 카메라의 가상 카메라 섞기로 같은 전환을 낸다.
            _world.GameCamera.SetDialogueCamera(view.VirtualCamera);
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 OnClose다. 구독과 입력 임대를 먼저 풀고 미리보기를 지운 뒤 차단을 돌려준다. 임대를 먼저 풀어야 차단 해제가 켠 맵을 건드리지 않는다.
        // 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            _placing = null;
            _menuTarget = -1;
            DestroyPreview();
            ReleaseInput();
            _area = null;
            // 정리 경로라 Play Mode 종료로 게임 카메라가 먼저 파괴됐으면 섞기를 되돌리지 않는다.
            if (_world.Camera != null) _world.GameCamera.SetDialogueCamera(null);
            if (_world.PresentationAttached)
            {
                OriginalBuilderView view = _world.Hud.Builder;
                view.Grid.Close();
                view.Hide();
            }
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 Update다. 모드별 갱신, 카메라, 마우스 올림 안내 순서로 한다. 게임플레이가 멈추거나 대사 중인 프레임에는 불리지 않는다.
        internal void Tick()
        {
            if (!IsOpen) return;
            BuildingArea area = _area!;
            OriginalBuilderView view = _world.Hud.Builder;
            if (_placing != null) TickPlacing(area, view, _placing);
            else TickNone(area, view);
            TickCamera(area, view);
            // 원작 UpdateContextMenuIndicator다. 메뉴가 열려 있지 않고 커서 칸에 표시된 건물이 있을 때만 안내를 보인다.
            bool hovering = !view.ContextMenu.IsShown && TryGetPointingCoord(area, Vector2Int.one, BuildingRotation.Zero, out Vector2Int coord)
                && area.GetBuildingInstanceAt(coord) != null;
            view.SetHovering(hovering);
        }

        // 원작 UpdatePlacing이다. 커서가 영역 평면 밖이면 미리보기만 숨긴다. 회전은 ±1을 더한 뒤 4로 나눈 나머지이고, 오른쪽 클릭은 배치를 취소한다.
        void TickPlacing(BuildingArea area, OriginalBuilderView view, BuildingInfo info)
        {
            Vector2Int dimensions = info.Dimensions;
            if (!TryGetPointingCoord(area, dimensions, _rotation, out Vector2Int coord))
            {
                if (_preview != null) _preview.gameObject.SetActive(false);
                return;
            }
            // 원작 IsValidPlacement다. 배치 명령과 같은 판정(범위, 데이터 겹침, 물리 겹침 순서)을 건설 규칙에서 함께 쓴다.
            bool valid = BuildingRules.IsValidPlacement(area, GameManager.Buildings, dimensions, _rotation, coord);
            view.Grid.SetPreview(coord, BuildingRules.RotatedDimensions(dimensions, _rotation), valid);
            ShowPreview(area, dimensions, coord);
            if (_rotate.WasPressedThisFrame()) _rotation = (BuildingRotation)(int)(((int)_rotation + _rotate.ReadValue<float>() + 4f) % 4f);
            if (_requestContextMenu.WasPressedThisFrame()) EnterNone(view);
        }

        // 원작 UpdateNone이다. 오른쪽 클릭한 칸에 표시된 건물이 있으면 그 건물 메뉴를 열고, 없으면 메뉴를 닫는다.
        void TickNone(BuildingArea area, OriginalBuilderView view)
        {
            if (!_requestContextMenu.WasPressedThisFrame() || !TryGetPointingCoord(area, Vector2Int.one, BuildingRotation.Zero, out Vector2Int coord))
                return;
            Building building = area.GetBuildingInstanceAt(coord);
            if (building == null)
            {
                HideMenu(view);
                return;
            }
            // 표시된 건물은 정의가 있는 설치 건물이라 두 조회가 늘 성공한다.
            GameManager.Buildings.TryGetInstallation(building.InstanceId, out BuildingInstallationState? installation);
            GameplayDataSettings.BuildingDataCollection.TryGetInfo(installation!.OriginalBuildingId, out BuildingInfo? info);
            _menuTarget = building.InstanceId;
            view.ContextMenu.Show(building.transform, _world.Camera, GameLocalization.Text(info!.DisplayNameKey), GameLocalization.Text(_recycleKey));
        }

        // 원작 UpdateCamera다. 이동 입력은 시간 배율과 상관없이 커서를 옮기고, 커서는 영역 위치 ± 반폭 안에 머문다.
        void TickCamera(BuildingArea area, OriginalBuilderView view)
        {
            if (_moveCamera.IsPressed())
            {
                Vector3 cursor = view.PanCamera(_cameraCursor, _moveCamera.ReadValue<Vector2>(), Time.unscaledDeltaTime);
                Vector3 center = area.transform.position;
                Vector2Int size = area.Size;
                cursor.x = Mathf.Clamp(cursor.x, center.x - size.x, center.x + size.x);
                cursor.z = Mathf.Clamp(cursor.z, center.z - size.y, center.z + size.y);
                _cameraCursor = cursor;
            }
            view.PlaceCamera(_cameraCursor);
        }

        // 원작 OnButtonSelected다. 원작처럼 토큰도 비용도 모자라면 안내 없이 무시하고, 최대 개수에 닿았으면 아무것도 하지 않는다.
        void Select(string buildingId)
        {
            if (!IsOpen || !_world.TrySession(out GameSessionManager? session)) return;
            // 목록 줄은 정의가 있는 건물만 보이므로 조회는 늘 성공한다.
            GameplayDataSettings.BuildingDataCollection.TryGetInfo(buildingId, out BuildingInfo? info);
            BuildingManager buildings = GameManager.Buildings;
            // 비용 출처는 지불 명령과 같은 세션 기준 소유 출처다.
            if (buildings.GetTokenAmount(buildingId) <= 0
                && !GameplayCommands.CanPay(session, GameManager.Economy, info!.Cost, ItemSourceRules.Owned(session), true)) return;
            if (BuildingRules.ReachedAmountLimit(info!, buildings)) return;
            BeginPlacing(_world.Hud.Builder, info!);
        }

        // 원작 BeginPlacing이다. 프리팹이 아직 없는 정의도 배치는 할 수 있고 미리보기만 없다. 표시 못 한 건물은 BuildingArea가 한 번 경고한다.
        void BeginPlacing(OriginalBuilderView view, BuildingInfo info)
        {
            EnterNone(view);
            _placing = info;
            view.SetPlacing(true);
            BuildingArea area = _area!;
            if (info.Prefab.RuntimeKeyIsValid()) LoadPreviewAsync(area, info, _previewGeneration).Forget();
            TickPlacing(area, view, info);
        }

        // 원작 SetMode(None)이다. 배치 중이었으면 미리보기와 격자의 놓을 칸을 지운다.
        void EnterNone(OriginalBuilderView view)
        {
            view.SetPlacing(false);
            HideMenu(view);
            if (_placing == null) return;
            _placing = null;
            DestroyPreview();
            view.Grid.HidePreview();
        }

        void HideMenu(OriginalBuilderView view)
        {
            _menuTarget = -1;
            view.ContextMenu.Hide();
        }

        // 원작 OnPointerClick의 배치 확정(ConfirmPlacement)이다. 원작처럼 결과와 상관없이 None으로 돌아가고, 성공했을 때만 소리를 낸다.
        // 거절이면 명령이 아무것도 바꾸지 않았다.
        void Confirm()
        {
            if (!IsOpen) return;
            OriginalBuilderView view = _world.Hud.Builder;
            HideMenu(view);
            BuildingInfo? info = _placing;
            if (info == null) return;
            BuildingArea area = _area!;
            Vector2Int dimensions = info.Dimensions;
            if (!TryGetPointingCoord(area, dimensions, _rotation, out Vector2Int coord))
            {
                if (_preview != null) _preview.gameObject.SetActive(false);
                return;
            }
            // 명령에 보내기 전에 배치 명령과 같은 건설 규칙 판정으로 거른다. 명령도 같은 판정으로 다시 본다.
            if (!BuildingRules.IsValidPlacement(area, GameManager.Buildings, dimensions, _rotation, coord)
                || !_world.TrySession(out GameSessionManager? session)) return;
            if (GameplayCommands.TryBuyAndPlaceBuilding(session, GameManager.Economy, GameManager.Buildings, GameManager.Quests, area, info.Id, coord,
                    _rotation, out _))
                _world.Audio.PlayOneShot(_builtSound);
            EnterNone(view);
        }

        // 원작 건물 메뉴의 ReturnBuilding이다. 메뉴는 대상 건물이 회수되어 파괴되면 스스로 숨는다.
        void RecycleMenuTarget()
        {
            if (!IsOpen || _menuTarget < 0) return;
            GameManager.Buildings.TryReturn(_menuTarget);
        }

        // 원작 목록 회수 버튼(길게 누르기)의 ReturnBuildingsOfType이다.
        void RecycleAll(string buildingId)
        {
            if (!IsOpen) return;
            GameManager.Buildings.ReturnAllOfType(buildingId);
        }

        // 원작 BuildingSelectionPanel.Refresh와 BuildingBtnEntry.Refresh, CostDisplay.Setup이다. 목록 규칙은 배치 명령과 같은 IsListed다.
        // 원작처럼 데이터 순서로 넣고 비용이 모자란 줄만 뒤로 보내므로, 두 번 돌아 각 무리 안의 데이터 순서를 지킨다.
        // 비용 출처는 지불 명령과 같아 기지에서는 창고도 센다. 씬 정리 중이라 세션이 없으면 그리지 않는다.
        void Redraw()
        {
            if (!IsOpen || !_world.TrySession(out GameSessionManager? session)) return;
            OriginalBuilderView view = _world.Hud.Builder;
            BuildingManager buildings = GameManager.Buildings;
            QuestManager quests = GameManager.Quests;
            EconomyManager economy = GameManager.Economy;
            // 지불 명령과 같은 세션 기준 소유 출처다.
            ItemSources sources = ItemSourceRules.Owned(session);
            // 줄마다 가방 전체를 다시 세지 않도록 이 다시 그리기의 소유 수를 한 번 센다(Docs/runtime-boundaries.md#main-thread-split).
            OwnedItemCounts owned = session.Inventory.CountAllOwned(sources);
            // 원작 RefreshMoneyBackground처럼 돈 줄은 계좌와 현금을 합쳐 본다. 줄이 여럿이라 한 번만 센다.
            long money = CostDisplayDrawing.OwnedMoney(economy, owned);
            string tokenFormat = GameLocalization.Text(_tokenFormatKey);
            IReadOnlyList<BuildingInfo> infos = GameplayDataSettings.BuildingDataCollection.Infos;
            // 목록 규칙, 토큰 수, 지불 가능은 줄마다 한 번만 판정한다. 아래 두 번의 반복은 넣는 순서만 나눈다.
            _rows.Clear();
            for (int index = 0; index < infos.Count; index++)
            {
                BuildingInfo info = infos[index];
                if (!BuildingRules.IsListed(info, buildings, quests)) continue;
                _rows.Add((index, buildings.GetTokenAmount(info.Id), GameplayCommands.CanPay(session, economy, info.Cost, owned, true)));
            }
            view.BeginEntries();
            for (int pass = 0; pass < 2; pass++)
            {
                foreach ((int infoIndex, int tokens, bool canPay) in _rows)
                {
                    bool costEnough = tokens > 0 || canPay;
                    if (costEnough != (pass == 0)) continue;
                    BuildingInfo info = infos[infoIndex];
                    int amount = buildings.GetBuildingAmount(info.Id);
                    bool reached = BuildingRules.ReachedAmountLimit(info, buildings);
                    string amountText = Number(amount) + "/" + (info.MaxAmount > 0 ? Number(info.MaxAmount) : "∞");
                    OriginalCostDisplayView cost = view.AddEntry(info.Id, info.Icon, GameLocalization.Text(info.DisplayNameKey),
                        GameLocalization.Text(info.DescriptionKey), tokenFormat.Replace("{tokenAmount}", Number(tokens)), tokens <= 0, amountText,
                        reached, !reached && costEnough, amount > 0);
                    // 원작 CostDisplay.Setup이다. 줄의 지불 가능 값과 이 다시 그리기의 수량 묶음을 넘겨 공용 도우미가 그린다.
                    CostDisplayDrawing.Draw(cost, _world.Catalog, info.Cost, owned, money, canPay);
                }
            }
            view.EndEntries();
        }

        // 원작 TryGetPointingCoord다. 게임 카메라에서 포인터로 쏜 광선이 영역 평면에 닿는 점을 건물 크기와 회전으로 칸에 맞춘다.
        // 포인터 장치가 없으면 가리키는 칸이 없다.
        bool TryGetPointingCoord(BuildingArea area, Vector2Int dimensions, BuildingRotation rotation, out Vector2Int coord)
        {
            coord = default;
            Pointer? pointer = Pointer.current;
            if (pointer == null) return false;
            Ray ray = _world.Camera.ScreenPointToRay(pointer.position.ReadValue());
            if (!area.Plane.Raycast(ray, out float enter)) return false;
            coord = BuildingRules.CursorToCoord(ray.GetPoint(enter), dimensions, rotation);
            return true;
        }

        // 원작 ShowPreview다. 건물 프리팹처럼 영역 회전 위에 회전 × 90도를 더한다.
        void ShowPreview(BuildingArea area, Vector2Int dimensions, Vector2Int coord)
        {
            if (_preview == null) return;
            _preview.transform.SetPositionAndRotation(BuildingRules.CoordToWorldPosition(coord, dimensions, _rotation),
                area.transform.rotation * Quaternion.Euler(0f, 90 * (int)_rotation, 0f));
            _preview.gameObject.SetActive(true);
        }

        // 정리 경로도 지나므로 이미 파괴된 미리보기는 Unity null 비교로 건너뛴다.
        void DestroyPreview()
        {
            _previewGeneration++;
            if (_preview != null) UnityEngine.Object.Destroy(_preview.gameObject);
            _preview = null;
        }

        // 아무도 기다리지 않는 미리보기 로드 루트다. 프리팹은 영역 씬의 서랍에 로드해 씬과 함께 반납한다. 서랍은 월드 문맥의 Runtime에서 얻는다.
        // 미리보기는 영역 아래에 두어 씬과 함께 사라지고, 원작 SetupPreview로 기능과 콜라이더를 꺼 물리 겹침 검사에 걸리지 않게 한다.
        async UniTaskVoid LoadPreviewAsync(BuildingArea area, BuildingInfo info, int generation)
        {
            try
            {
                GameObject prefab = await _world.Runtime.Assets.LoadAsync<GameObject>(info.Prefab, area.gameObject.scene);
                // 기다리는 동안 배치를 끝냈거나 다른 건물을 골랐거나 창을 닫았으면 만들지 않는다. 영역 씬이 내려갔으면 영역도 파괴됐다.
                if (generation != _previewGeneration || area == null) return;
                GameObject created = UnityEngine.Object.Instantiate(prefab, area.transform);
                Building preview = created.GetComponent<Building>();
                preview.SetupPreview();
                // 다음 Tick이 커서 칸에 놓고 켠다. 그 전 한 프레임 동안 영역 원점에 보이지 않게 끈다.
                created.SetActive(false);
                _preview = preview;
            }
            // 예외 경계: 아무도 기다리지 않는 미리보기 로드 루트라 프리팹 로드 실패를 잃지 않도록 여기서 한 번 기록한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, _world.LogContext);
            }
        }

        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
