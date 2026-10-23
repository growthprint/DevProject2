#nullable enable annotations
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.GAS;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 전투 월드의 대사, F 상호작용 드라이버(원작 CA_Interact), 지연 상호작용을 맡는다. 상호작용 대상이 끝났을 때 맡기는 일
    // (IInteractionHost)과 기지 시설 화면(IFacilityHost)도 여기서 한다. 순간이동은 WorldTeleports, 철수는 ExtractionCountdown,
    // 상자 배치는 LootBoxSetup이 맡고, 컨트롤러는 이 객체의 같은 멤버로 부른다. 컨트롤러를 역참조하지 않도록 공유 참조와 입력,
    // 출격과 씬 전환 동작을 모두 생성자로 받는다. 사건은 제출하는 즉시 세션 메모리에 확정한다.
    // 낚시 드라이버와 낚시 호스트(IFishingHost)를 자기 partial 파일(WorldInteractions.Fishing.cs)에 두도록 partial로 연다.
    internal sealed partial class WorldInteractions : IInteractionHost, IFacilityHost
    {
        // 지연 상호작용은 지점에서 1.5m를 벗어나면 취소한다. 대사, 안내, 철수 영역은 OriginalInteractionSearch의 영역 판정 높이에서 본다.
        // 거리는 제곱근 없이 제곱 거리로 비교하므로 제곱값도 상수로 둔다. 2.25는 float로 정확해서 경계에서도 판정이 같다.
        const float _interactRange = 1.5f;
        const float _sqrInteractRange = _interactRange * _interactRange;
        readonly GameplayWorldContext _world;
        readonly InventoryScreen _inventory;
        // 쪽지를 읽으면 해금하고 여는 쪽지 창이다.
        readonly NoteScreen _notes;
        // 퀘스트 창구, 상점, 보관함 대상이 끝나면 여는 기지 화면 묶음이다. 하나만 열기는 이 묶음이 맡는다.
        readonly GameplayScreens _screens;
        // F 상호작용을 시작하기 전에 장전, 사용, 대시 같은 캐릭터 행동이 진행 중인지 묻는 데 쓴다.
        readonly CombatSimulation _combat;
        readonly DialogueSequenceRunner _dialogue;
        readonly InputAction _interact;
        // 출격 전환은 출격 방식(벙커 문, 지도 선택 비콘 번호)을 받는다. 벙커 문 출구와 지도 선택 화면이 같은 컨트롤러 전환 루트를 쓴다.
        readonly Func<RaidEntry, UniTask> _depart;
        // 원작 기지 난이도 재선택 출구의 동작이다. 준비 화면으로 가는 씬 전환은 전투 월드 컨트롤러가 맡는다.
        readonly Func<UniTask> _reselectDifficulty;
        readonly Func<RaidOutcome, UniTask> _finish;
        // 순간이동 대기와 레벨 시작 배치, 철수 카운트다운이다. 생성자가 같은 컨텍스트와 동작으로 만든다.
        readonly WorldTeleports _teleports;
        readonly ExtractionCountdown _extraction;
        // 진행 바를 채우는 중인 상호작용 대상이다. 상호작용 시간이 0보다 큰 대상만 여기 들어온다.
        InteractableBase? _timedInteraction;
        // 지연 상호작용 동안 쥐는 모터 입력 억제 토큰이다. 원본 CA_Interact는 CanMove, CanRun, CanControlAim이 false이고
        // 대시(우선순위 4)가 상호작용(7)을 끊지 못하므로 이동, 조준, 대시를 모두 막는다. 완료, 취소, 해제 때 반납한다.
        IDisposable? _interactionSuppression;
        // 원본 CA_Interact다. 지연 상호작용이 진행되는 동안(_timedInteraction) 실행 중이고, 다른 행동 중에는 시작하지 않는다.
        readonly AbilitySystem _abilities;
        readonly GameplayAbilityDefinition _interactAbility;
        float _interactionUntil;
        readonly HashSet<string> _visited = new(StringComparer.Ordinal);
        // 적 시체와 유실물 묘비 같은 런타임 대상 목록이다. 생성 객체가 대여에 성공하면 넣고, 반환과 씬 해제 때 뺀다.
        // 여기서는 읽기만 하므로 매 프레임 World 전체를 뒤지지 않는다.
        readonly IReadOnlyList<InteractableBase> _runtimeInteractables;
        // 가방이 줄어 바닥에 떨군 아이템과 그 줍기를 맡는 생성 객체다. F 대상 후보로 그 RuntimePoints도 함께 찾는다.
        readonly DroppedItemSpawner _drops;
        // 저작한 F 상호작용 대상을 영역 Collider로 찾는 표다. 저작 대상은 실행 중 바뀌지 않으므로 생성자가 한 번 만든다.
        readonly Dictionary<Collider, InteractableBase> _interactablesByArea = new();
        // 꺼진 하위 씬 상자에 못 넣은 현금이다. 상자 배치가 넣고, 그 하위 씬을 켤 때 OnSubsceneSwitched가 떨군다.
        readonly List<LootBoxSetup.DeferredCash> _deferredCash = new();
        // 원본 CA_Interact처럼 탐색 구 결과를 다섯 개까지 담는 재사용 버퍼다. 매 프레임 배열을 만들지 않는다.
        readonly Collider[] _hits = new Collider[5];
        // 안내에 띄운 묶음 항목 이름의 번역 캐시다. 프롤로그와 같은 InteractPromptLabels를 쓰고, 로캘이 바뀌면 컨트롤러가 ClearLabelCache로 비운다.
        readonly InteractPromptLabels _labels = new();
        // 원작 InteractHUD가 매 프레임 쓰는 전역 셰이더 값 이름이다. 머리 위 퀘스트 표시 셰이더(BillBoardOverlay_Markers 포트)가 알파에 곱한다.
        static readonly int _interactableShaderId = Shader.PropertyToID("Interactable");
        // 원작 CA_Interact의 masterInteractableAround와 interactIndexInGroup이다. 번호는 늘 0 이상, 대표 목록 길이 미만이다.
        InteractableBase? _master;
        int _groupIndex;
        internal WorldInteractions(GameplayWorldContext world, InventoryScreen inventory, NoteScreen notes, GameplayScreens screens,
            // 난이도 재선택 출구도 출격처럼 컨트롤러의 씬 전환 동작을 받아 역참조하지 않는다.
            CombatSimulation combat, DialogueSequenceRunner dialogue, InputAction interact, Func<RaidEntry, UniTask> depart, Func<UniTask> reselectDifficulty,
            Func<RaidOutcome, UniTask> finish, Action<int> switchSubscene, IReadOnlyList<InteractableBase> runtimeInteractables, DroppedItemSpawner drops,
            // 낚시 물고기 추첨이 원작 TimeOfDayController처럼 밤 여부와 날씨를 읽는 이 씬의 시간대 볼륨이다. 없는 씬은 null이다.
            OriginalTimeOfDay? timeOfDay)
        {
            _timeOfDay = timeOfDay;
            _world = world;
            _inventory = inventory;
            _notes = notes;
            _screens = screens;
            _combat = combat;
            _dialogue = dialogue;
            _interact = interact;
            _depart = depart;
            _reselectDifficulty = reselectDifficulty;
            _finish = finish;
            // 순간이동과 레벨 시작 배치는 컨트롤러의 하위 씬 전환을, 철수는 판 종료 동작을 받는다.
            _teleports = new WorldTeleports(world, switchSubscene);
            _extraction = new ExtractionCountdown(world, finish);
            _runtimeInteractables = runtimeInteractables;
            // 바닥 아이템은 시체 목록과 따로 대여되므로 그 생성 객체를 받아 대상 찾기와 줍기에 쓴다.
            _drops = drops;
            // 저작 대상은 저마다 다른 영역 Collider를 써서 표에 겹치지 않고, 배열에 빈 칸이 없다.
            // OriginalGameplaySceneDataTests가 세 씬에서 확인한다. 원작 묶음 구성원은 자기 영역 없이 대표 영역으로만 고르므로 영역이 없으면 넣지 않는다.
            foreach (InteractableBase interactable in world.Interactables)
            {
                if (interactable.InteractCollider != null) _interactablesByArea.TryAdd(interactable.InteractCollider, interactable);
                // 낚시터는 손에 든 무기에 따라 상호작용 표시를 매 프레임 맞추므로 따로 모은다.
                if (interactable is Action_FishingV2 fishing) _fishingPoints.Add(fishing);
            }
            PlayerVitals vitals = world.Runtime.Vitals;
            _abilities = vitals.Abilities;
            _interactAbility = vitals.AbilitySet.Interact!;
        }
        // 대사가 진행 중인지다. 시뮬레이션 준비, 게임 카메라 입력 판정, 테스트 헬퍼가 읽는다.
        internal bool IsDialogueOpen => _dialogue.IsOpen;
        // 대사 완료 사건은 세션에, 처치는 처치 수, 사건, 경험치 소유자에 같은 프레임 메모리로만 확정한다. 파일은 다음 저장 지점이 쓴다.
        // 거절은 기록하지 않는다. 키 형식과 값 범위는 OriginalGameplaySceneDataTests가 저작 데이터에서 막고,
        // 불변식 위반은 명령이 한 번 기록한다. 게이트가 닫혔거나 세션이 없는 경우는 전환이나 정리 중의 정상 거절이다.
        // 처치 경험치는 SubmitKill이 맡아 대사 사건에는 경험치가 없다.
        internal void SubmitEvent(string id)
        {
            // 경험치 소유자는 GameManager가 내준다. 세션을 찾았으면 앱이 살아 있어 바로 읽는다.
            if (_world.TrySession(out GameSessionManager? session))
                GameplayCommands.TryCompleteEvent(session!, GameManager.Experience, id, 0);
        }
        // 적 처치를 처치 명령 하나로 확정해, 사건이 거절되거나 이미 완료됐으면 처치 수도 오르지 않게 한다. 거절 처리는 SubmitEvent와 같다.
        internal void SubmitKill(string id, string enemyKey, bool fromPlayer, long experience)
        {
            // 처치 수와 경험치 소유자도 GameManager가 내준다. 세션을 찾았으면 앱이 살아 있어 바로 읽는다.
            if (_world.TrySession(out GameSessionManager? session))
                GameplayCommands.TryRecordKill(session!, GameManager.Experience, GameManager.KillCounts, id, enemyKey, fromPlayer, experience);
        }
        // 대사가 진행 중이면 true를 돌려준다. 확인 입력은 지도 같은 모달이 없고 창에 포커스가 있을 때만 넘긴다. 프롤로그와 같은 규칙이다.
        // 막힌 프레임에도 대사 상자와 글자 공개는 계속되고 확인만 무시된다. 마지막 대사를 닫은 Space나 클릭이
        // 회피나 사격으로 이어지지 않게 하는 지연은 모터의 억제 토큰이 맡는다.
        internal bool TickDialogue()
        {
            if (!_dialogue.IsOpen) return false;
            _dialogue.Tick(_world.Runtime.AcceptsGameplayInput && Application.isFocused);
            return true;
        }
        // 원본 벙커 출입과 하위 씬 이동 같은 지연 상호작용을 진행하고, 진행 중이면 true를 돌려준다. 그동안 억제 토큰으로 움직일 수 없고,
        // 순간이동 등으로 1.5m 범위를 벗어나면 취소한다. 원본 OnUIInventoryInput(:411-431)과 UpdateInteract(:354-388)처럼 인벤토리를 열어도
        // 계속되므로 컨트롤러가 인벤토리 게이트보다 먼저 부른다. 진행은 원본 CA_Interact처럼 하단 ActionProgress_Slider에만 표시한다.
        // 완료되면 억제를 반납한 뒤 원본 OnTimeOut처럼 대상의 OnInteractFinished를 부른다.
        internal bool TickTimedInteraction()
        {
            if (_timedInteraction == null) return false;
            SharedHudView hud = _world.Hud;
            // 원작은 상호작용 중 이동을 막을 뿐 거리로 끊지 않아 이 기준점에 짝이 없다. 원작 F 탐색 구 중심과 같은 높이를 둔다.
            if (_timedInteraction.SqrDistance(_world.PlayerPosition + Vector3.up * OriginalInteractionSearch._probeHeight) > _sqrInteractRange)
            {
                EndTimedInteraction();
                hud.SetActionProgress(false, 0, false);
                return false;
            }
            hud.HideInteraction();
            // 진행률 자르기는 ActionProgressView가 하고, 시간이 다 찬 프레임은 아래 완료 분기가 표시를 내린다.
            // 지연 상호작용은 상호작용 시간이 0보다 큰 대상만 시작하므로 나누는 값은 늘 양수다.
            float total = _timedInteraction.InteractTime;
            hud.SetActionProgress(true, 1 - (_interactionUntil - Time.time) / total, false);
            if (Time.time >= _interactionUntil)
            {
                InteractableBase completed = _timedInteraction;
                EndTimedInteraction();
                hud.SetActionProgress(false, 1, false);
                // 원작 UpdateInteract의 시간 종료와 FinishInteract다. 그 시점에 써야 할 필요 아이템을 쓰지 못하면 끝내지 않는다.
                completed.Complete(this);
            }
            return true;
        }

        // 지연 상호작용 대상을 비우고 억제 토큰을 한 번만 반납하며 상호작용 어빌리티를 끝낸다. 완료, 범위 이탈, 씬 해제가 함께 쓰고
        // 여러 번 불러도 안전하다. 진행 표시는 호출자가 내린다. 씬 해제 때는 앞서 공용 HUD를 떼면서 표시가 초기화되므로 HUD를 부르지 않는다.
        internal void EndTimedInteraction()
        {
            _timedInteraction = null;
            _interactionSuppression?.Dispose();
            _interactionSuppression = null;
            _abilities.EndAbility(_interactAbility);
            // 씬 해제 정리가 진행 중인 낚시의 억제와 행동 태그도 반납하게 한다. 지연 상호작용 완료와 범위 이탈 경로에서는
            // 낚시가 진행 중일 수 없어(낚는 동안 F 탐색을 하지 않음) 아무것도 바뀌지 않는다.
            EndFishing();
            // 씬 해제 정리도 이 메서드를 부르므로, 효과나 검은 화면을 보이며 기다리던 순간이동도 여기서 멈추고 억제와 덮기를 되돌린다.
            // 기다리는 동안에는 TickPoints가 새 상호작용을 시작하지 않아, 완료와 범위 이탈 경로에서는 기다리는 순간이동이 없다.
            _teleports.Stop();
        }
        // 컨트롤러가 탄환 진행 직후 부르는 철수 판정이다. 철수를 시작했으면 true를 돌려준다. 규칙은 ExtractionCountdown이 가진다.
        internal bool TickExtraction() => _extraction.Tick();

        // 대사와 안내는 저작 지점의 영역에 들어설 때 시작하고, 대사를 시작하면 이번 프레임을 끝낸다. 그 밖에는 원본 CA_Interact처럼
        // 조준 앞 탐색 구에 닿은 가장 가까운 대상의 안내와 F 입력을 처리한다. 키캡은 옵션을 적용할 때마다 컨트롤러가 다시 계산한 값이다.
        internal void TickPoints(string interactKey)
        {
            // 원작 대사·안내 Trigger도 캐릭터 루트 캡슐로 감지하므로 철수와 같은 높이에서 판정한다.
            Vector3 position = _world.PlayerPosition + Vector3.up * OriginalInteractionSearch._areaProbeHeight;
            foreach (OriginalGameplayPoint point in _world.Points)
                if (VisitArea(point, position)) return;
            // 효과나 검은 화면을 보이며 순간이동을 기다리는 동안에는 대상을 고르지 않고 안내를 숨긴다. 원작은 기다리는 동안 다른 대상도
            // 다룰 수 있지만, 끼어든 지연 상호작용의 끝이 EndTimedInteraction으로 기다리던 순간이동을 멈추지 않게 하는 원작 차이다.
            // 대표를 비워 이동 뒤 첫 탐색에서 묶음 번호가 0부터 시작한다.
            if (_teleports.Waiting)
            {
                _master = null;
                _groupIndex = 0;
                _world.Hud.HideInteraction();
                return;
            }
            // 원본 CA_Interact.SearchInteractableAround처럼 탐색은 묶음 대표만 고르고, 대표가 바뀌거나 없어지면 묶음 번호를 0으로 되돌린다.
            InteractableBase? master = FindInteractable();
            _groupIndex = TrackedGroupIndex(_master, master, _groupIndex);
            _master = master;
            SharedHudView hud = _world.Hud;
            // 원본 InteractHUD처럼 대표의 안내 위치를 따라가며 대표와 켜진 구성원의 번역한 이름을 차례로 보이고, 고른 항목에 키캡을 표시한다.
            if (master != null)
                // 이름 번역 캐시는 프롤로그와 같은 InteractPromptLabels가 맡는다.
                hud.ShowInteraction(_labels.For(master.InteractableList), _groupIndex, interactKey, master.MarkerPosition, _world.Camera);
            else hud.HideInteraction();
            // 원본 CharacterMainControl.Interact()(:2440)처럼 장전, 사용, 대시 같은 행동이 없을 때만 F 상호작용을 시작한다.
            // 즉시와 지연 상호작용 모두 같고, 안내는 행동 중에도 표시한다.
            if (master == null || !_interact.WasPressedThisFrame() || _combat.ActionInProgress) return;
            // 원작 CA_Interact.OnStart와 StartInteract처럼 고른 항목의 조건과 필요 아이템을 본다. 탐색 조건은 대표에만 적용했으므로
            // 고른 구성원이 지금 다룰 수 없거나 필요 아이템이 없으면 시작하지 않는다. 시작하면 그 항목의 시작 동작을 먼저 실행한다.
            InteractableBase? selected = master.InteractableInGroup(_groupIndex);
            if (selected == null || !CanInteract(selected) || !selected.TryStart(this)) return;
            if (selected.InteractTime <= 0f)
            {
                selected.Complete(this);
                return;
            }
            // 원본 CA_Interact처럼 상호작용 어빌리티로 행동 태그를 쥐고, 지연 상호작용 동안 이동과 조준을 막는 토큰을 쥔다.
            // 위에서 진행 중인 행동이 없음을 봤으므로 시작은 거절되지 않는다. 진행 중인 지연 상호작용이 없을 때만 와서 토큰도 비어 있다.
            if (_abilities.TryActivateAbility(_interactAbility) != AbilityActivationResult.Activated) return;
            _timedInteraction = selected; _interactionUntil = Time.time + selected.InteractTime;
            _interactionSuppression = _world.Motor.SuppressInput();
        }

        // 원작 CA_Interact.SwitchInteractable이다. 컨트롤러가 휠 위는 -1, 아래는 +1로 넘긴다.
        internal void SwitchInteractable(int direction) => _groupIndex = SwitchedGroupIndex(_master, _groupIndex, direction);

        // 대표가 묶음이 아니면 번호는 0이고, 묶음이면 끝에서 처음으로, 처음에서 끝으로 돈다.
        internal static int SwitchedGroupIndex(InteractableBase? master, int index, int direction)
        {
            if (master == null || !master.IsGroup) return 0;
            int count = master.InteractableList.Count;
            index += direction;
            return index >= count ? 0 : index < 0 ? count - 1 : index;
        }

        // 원작 SearchInteractableAround처럼 대표가 바뀌거나 없으면 번호를 0으로 되돌린다. 구성원이 꺼져 목록이 줄어
        // 번호가 목록 밖이 되어도 0으로 되돌려 번호가 늘 목록 안에 있게 한다.
        internal static int TrackedGroupIndex(InteractableBase? previous, InteractableBase? master, int index) =>
            master == null || master != previous || index >= master.InteractableList.Count ? 0 : index;

        // 대사와 안내 지점을 판정하고, 꺼진 지점은 건너뛴다. 저작 지점은 씬과 함께만 파괴되고 배열에 빈 칸이 없다.
        // 영역에 처음 들어설 때 한 번 시작하며, 대사를 시작해 이번 프레임 순회를 끝내야 하면 true를 돌려준다.
        bool VisitArea(OriginalGameplayPoint point, Vector3 position)
        {
            if (!point.gameObject.activeInHierarchy) return false;
            if (point.Kind == OriginalPointKind.Dialogue || point.Kind == OriginalPointKind.Hint)
            {
                // 이 월드에서 아직 들르지 않았고 완료 사건도 없는 지점의 영역에 들어섰을 때만 시작한다.
                if (IsNewArea(point, position))
                {
                    // 한 번만 재생하는 컷신(머드 첫 만남)은 캠페인 기록이 있으면 다시 시작하지 않는다.
                    if (point.TryGetComponent(out OriginalCutScene cut) && !cut.CanPlay(GameManager.Flags)) return false;
                    _visited.Add(point.SourceId);
                    if (point.Kind == OriginalPointKind.Dialogue) { BeginDialogue(point); return true; }
                    // 원본 트리거 안내는 대사 상자가 아니라 플레이어를 따라다니는 별도 말풍선으로 표시한다.
                    _world.View.Bubble(GameLocalization.Text(point.LabelKey), _world.Motor.transform, _world.Camera, point.Duration);
                }
            }
            return false;
        }

        // 들른 기록과 완료 사건을 영역 판정보다 먼저 본다. 완료한 대사는 다시 불러온 월드에서도 시작하지 않는다.
        bool IsNewArea(OriginalGameplayPoint point, Vector3 position) =>
            !_visited.Contains(point.SourceId) && !_world.TutorialEventComplete(point.SourceId) && point.Contains(position);

        // 원본 CA_Interact.SearchInteractableAround처럼 탐색 구에 닿은 Collider 중 플레이어 루트에서 Collider Transform까지 가장 가깝고
        // 지금 F로 다룰 수 있는 대상을 고른다. 원본처럼 더 가까운 Collider만 대상을 찾아 보고, 고르지 못한 후보는 최단 거리를 바꾸지 않는다.
        InteractableBase? FindInteractable()
        {
            OriginalPlayerMotor motor = _world.Motor;
            Vector3 root = motor.transform.position;
            int count = OriginalInteractionSearch.Overlap(root, motor.AimDirection, _hits);
            InteractableBase? closest = null;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider hit = _hits[i];
                float sqrDistance = (hit.transform.position - root).sqrMagnitude;
                if (sqrDistance >= nearest) continue;
                InteractableBase? interactable = InteractableOf(hit);
                if (interactable == null || !CanInteract(interactable)) continue;
                nearest = sqrDistance;
                closest = interactable;
            }
            return closest;
        }

        // 저작 대상은 생성자가 만든 표에서 찾는다. 시체, 유실물, 바닥 아이템, 과제 프리팹 같은 생성 대상은 대여나 생성마다 영역이 정해지므로
        // 세 런타임 목록에서 같은 Collider를 찾는다. Unity 객체라 ?? 대신 비교로 다음 목록으로 넘어간다.
        InteractableBase? InteractableOf(Collider area)
        {
            if (_interactablesByArea.TryGetValue(area, out InteractableBase interactable)) return interactable;
            InteractableBase? runtime = RuntimeInteractableOf(_runtimeInteractables, area);
            if (runtime != null) return runtime;
            InteractableBase? drop = RuntimeInteractableOf(_drops.RuntimePoints, area);
            return drop != null ? drop : RuntimeInteractableOf(_world.SpawnedInteractables, area);
        }

        // 인터페이스 열거자 박싱을 피하려고 인덱스로 순회하고, 파괴된 인스턴스의 대상은 Unity null이라 건너뛴다.
        static T? RuntimeInteractableOf<T>(IReadOnlyList<T> interactables, Collider area) where T : InteractableBase
        {
            for (int i = 0; i < interactables.Count; i++)
            {
                T runtime = interactables[i];
                if (runtime != null && ReferenceEquals(runtime.InteractCollider, area)) return runtime;
            }
            return null;
        }

        // 꺼진 대상과 순간이동 직후 1초 동안의 순간이동 대상은 고르지 않는다.
        // 건설 지점은 비용을 낼 수 있을 때만 고른다.
        bool CanInteract(InteractableBase interactable) => interactable.IsInteractable
            && !(interactable is MultiSceneTeleporter && _teleports.RecentlyTeleported)
            && !(interactable is CostTaker taker && !CanPay(taker));

        // 로캘이 바뀌면 컨트롤러가 불러 이름 캐시를 비운다. 다음 표시에서 새 로캘로 다시 번역한다.
        internal void ClearLabelCache() => _labels.Clear();

        // 원작 InteractHUD.Update(73-75)의 Shader.SetGlobalFloat("Interactable")다. 입력이 살아 있고(원작 InputManager.InputActived)
        // 진행 중인 행동이 없을 때(원작 CurrentAction이 없거나 Running이 아님) 1, 아니면 0이다. 원작 입력 비활성 조건인 일시정지, 대사, 씬 전환은
        // 시뮬레이션 준비가, 화면과 지도는 입력 차단이 맡는다. 지연 상호작용도 행동 태그를 쥐어 0이 된다. 컨트롤러가 초기화 뒤 매 프레임 부른다.
        internal void TickInteractableShader()
        {
            bool interactable = _world.SimulationReady && !_world.Runtime.GameplayInput.IsBlocked && !_combat.ActionInProgress;
            Shader.SetGlobalFloat(_interactableShaderId, interactable ? 1f : 0f);
        }

        // 상자는 상자 칸을, 기지 창고 상자는 플레이어 창고를 인벤토리 화면으로 연다.
        void IInteractionHost.OpenLoot(InteractableLootbox lootbox) =>
            _inventory.Open(lootbox.PlayerStorage ? null : lootbox, lootbox.PlayerStorage);

        // 기지 출구는 출격을, 제로존 벙커 입구는 탈출로 판을 끝내고, 기지 난이도 재선택 출구는 준비 화면으로 간다.
        // 프롤로그 출구는 전투 월드에 없다.
        void IInteractionHost.LoadScene(SceneLoaderInteractable loader)
        {
            // 기지 출구는 벙커 문 출격이라 씬에 저작된 캐릭터 자리에서 시작한다.
            if (loader.Target == SceneLoaderTarget.Sortie) _depart(RaidEntry.BunkerDoor).Forget();
            else if (loader.Target == SceneLoaderTarget.ReturnToBase) _finish(RaidOutcome.Extracted).Forget();
            else if (loader.Target == SceneLoaderTarget.ReselectDifficulty) _reselectDifficulty().Forget();
        }

        // 원작 SimpleTeleporter.Teleport다. 효과, 대기, 이동 순서는 순간이동 객체가 맡는다.
        void IInteractionHost.Teleport(MultiSceneTeleporter teleporter) => _teleports.Begin(teleporter);

        // 유실물 회수를 세션 메모리에 확정한다. 파일은 다음 저장 지점이 쓰고, 다 되찾은 표식은 커밋 알림의 유실물 갱신이 내린다.
        // 일부나 전부가 가방에 들어가지 못하면 원본 NotificationText로 알린다. 다른 거절은 세션이 이미 기록한 구조 결함이거나
        // 도달하지 않으므로 다시 기록하지 않는다.
        void IInteractionHost.RecoverLostBody(LostBodyInteractable body)
        {
            // 유실물 묘비 ID는 시체와 유실물 생성 객체가 출격 토큰을 불변 문화권 문자열로 넣은 값뿐이라 변환은 늘 성공한다.
            // 씬 정리 중이라 세션이 없으면 기록 없이 회수하지 않는다.
            uint.TryParse(body.SourceId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture,
                out uint token);
            if (!_world.TrySession(out GameSessionManager? session)) return;
            // 일부만 되찾은 성공도 남은 아이템이 있다는 InventoryFull 코드를 돌려주므로 성공 여부와 상관없이 코드로 알린다.
            session!.Inventory.TryRecoverLostBody(_world.Catalog, token, out InventoryRejection rejection);
            _world.NotifyIfInventoryFull(rejection);
        }

        // 바닥 아이템을 세션 메모리의 소지품으로 되돌린다. 가방이 차서 못 주우면 유실물 회수와 같은 알림을 띄우고 아이템은 바닥에 남는다.
        // 다른 거절은 세션이 이미 기록한 구조 결함이거나 씬 정리 중이라 다시 기록하지 않는다. 어떤 거절을 알릴지는 컨텍스트가 정한다.
        void IInteractionHost.PickUp(InteractablePickup pickup)
        {
            _drops.TryPickUp(pickup, out InventoryRejection rejection);
            _world.NotifyIfInventoryFull(rejection);
        }

        // 전투 월드에는 거울이 없다.
        void IInteractionHost.OpenCustomization(Interact_CustomFace mirror) { }

        void IInteractionHost.ReadNote(NoteInteract note) => _notes.Show(note.NoteKey);

        // 기지 시설 화면과 낚시는 전투 월드만 연다. 시설 진입점은 이 값으로 시설 쪽 일을 맡긴다.
        IFacilityHost? IInteractionHost.Facilities => this;

        // 기지 화면은 화면 묶음으로 넘겨 원작 View처럼 하나만 연다. 각 화면이 HUD 연결과 입력 차단을 확인한다.
        void IFacilityHost.OpenQuestGiver(QuestGiver giver) => _screens.ShowQuestGiver(giver);
        void IFacilityHost.OpenShop(StockShopInteract shop) => _screens.ShowShop(shop);
        void IFacilityHost.OpenStorageDock(StorageDockInteract dock) => _screens.ShowStorageDock();
        // 원작 MasterKeysRegisterView.Show다. 열쇠 등록 창도 같은 화면 묶음이 하나만 열기와 ESC 닫기를 맡는다.
        void IFacilityHost.OpenMasterKeyRegister(MasterKeyRegisterViewInvoker invoker) => _screens.ShowMasterKeyRegister();
        // 원작 ViewsProxy.ShowEndowmentView다. 재능 선택 창도 같은 화면 묶음이 하나만 열기와 ESC 닫기를 맡는다.
        void IFacilityHost.OpenEndowmentSelection(EndowmentSelectionInteract endowment) => _screens.ShowEndowmentSelection();
        // 원작 BuilderView.Show(buildingArea)다. 건설 창도 같은 화면 묶음이 하나만 열기와 ESC 닫기를 맡는다.
        void IFacilityHost.OpenBuilder(BuilderViewInvoker invoker) => _screens.ShowBuilder(invoker.BuildingArea);
        // 원작 PerkTreeView.Show와 수면 창 열기다. 두 창도 같은 화면 묶음이 하나만 열기와 ESC 닫기를 맡는다.
        void IFacilityHost.OpenPerkTree(PerkTreeUIInvoker invoker) => _screens.ShowPerkTree(invoker);
        void IFacilityHost.OpenSleep(SleepInteractable sleep) => _screens.ShowSleep(sleep);
        // 원작 ViewsProxy.ShowRepairView, ShowDecomposeView와 ItemCustomizeSelectionView.Show다. 세 창도 같은 화면 묶음이 하나만 열기와 ESC 닫기를 맡는다.
        // 개조 화면은 원작처럼 진입점 값을 쓰지 않는다.
        void IFacilityHost.OpenRepair() => _screens.ShowRepair();
        void IFacilityHost.OpenDecompose() => _screens.ShowDecompose();
        void IFacilityHost.OpenCustomize(Workbench workbench) => _screens.ShowCustomize();
        // 원작 CraftView.SetupAndOpenView와 FormulasRegisterView.Show다. 두 창도 같은 화면 묶음이 하나만 열기와 ESC 닫기를 맡고,
        // 제조 창은 진입점의 장소 태그로, 등록 창은 진입점의 추가 태그로 연다.
        void IFacilityHost.OpenCraft(InteractCrafter crafter) => _screens.ShowCraft(crafter);
        void IFacilityHost.OpenFormulaRegister(FormulasRegisterViewInvoker invoker) => _screens.ShowFormulaRegister(invoker);
        // 원작 MapSelectionView.Show다. 지도 선택 창도 같은 화면 묶음이 하나만 열기와 ESC 닫기를 맡고, 확인하면 이 월드의 출격 전환으로 떠난다.
        void IFacilityHost.OpenMapSelection(MapSelectionViewInvoker invoker) => _screens.ShowMapSelection(_depart);

        // 필요 아이템은 확정 세션에서 찾고, 쓰면 세션 메모리에 확정한다. 씬 정리 중이라 세션이 없으면 없는 것으로 본다.
        bool IInteractionHost.HasItem(int originalItemId) =>
            _world.TrySession(out GameSessionManager? session) && HasRequired(session!, originalItemId);

        bool IInteractionHost.TryUseItem(int originalItemId) =>
            _world.TrySession(out GameSessionManager? session) && TryUseRequired(session!, _world.Catalog, originalItemId);

        // 원작 TryGetRequiredItem의 찾기를 전투 월드와 프롤로그가 함께 쓴다. 등록 열쇠와 세션 소지품 두 소유자에 걸친 규칙이라
        // GameplayCommands에 두고 여기서는 넘기기만 한다(장부 1단계 검토 7번). 등록 열쇠 소유자는 GameManager가 내준다.
        // 세션을 찾았으면 앱이 살아 있어 바로 읽는다.
        internal static bool HasRequired(GameSessionManager session, int originalItemId) =>
            GameplayCommands.HasRequiredItem(session, GameManager.MasterKeys, originalItemId);

        internal static bool TryUseRequired(GameSessionManager session, OriginalGameplayCatalog catalog, int originalItemId) =>
            GameplayCommands.TryUseRequiredItem(session, GameManager.MasterKeys, catalog, originalItemId);

        // 원작 DialogueBubbleProxy.Pop이다. 말풍선은 GameplayUI가 그 물체 위 높이에 띄운다.
        void IInteractionHost.Bubble(string textKey, Transform at, float yOffset, float duration) =>
            _world.View.Bubble(GameLocalization.Text(textKey), at, _world.Camera, duration, yOffset);

        void IInteractionHost.Notify(string textKey) => _world.Notify(textKey);

        // 원작 CostTaker의 Cost.Pay다. 원작 건설 비용은 돈이 0이라 아이템만 내며, 씬 기본 출처에서 쓴다. 레이드 중에는 가방과 펫 가방만,
        // 기지에서는 창고부터 쓴다.
        bool IInteractionHost.TryPay(CostTaker taker) =>
            _world.TrySession(out GameSessionManager? session)
            && session!.Inventory.TryConsumeOwned(taker.Costs, ItemSourceRules.Owned(session));

        // 원작 CostTaker.IsInteractable(cost.Enough)이다. 비용이 모자라면 안내도 띄우지 않는다. 지불과 같은 출처로 센다.
        bool CanPay(CostTaker taker) =>
            _world.TrySession(out GameSessionManager? session)
            && session!.Inventory.HasOwned(taker.Costs, ItemSourceRules.Owned(session));

        // 원작 LootBoxLoader처럼 무작위 상자의 내용을 정한다. 컨트롤러가 첫 월드 초기화에서 한 번 부르고, 시드는 출격과 상자마다 같다.
        // 원작 AdvancedDebuffMode 규칙은 난이도 소유자의 현재 규칙에서 읽는다. 난이도는 일반 클래스라 ?.를 써도 된다.
        internal void SetupLootBoxes(Func<string, int> seed) =>
            LootBoxSetup.Run(_world, _drops, seed, GameManager.Difficulty.Current?.AdvancedDebuffMode == true, _deferredCash);

        // 컨트롤러가 하위 씬을 바꾼 직후 부른다. 원작 상자가 하위 씬을 불러올 때 현금을 떨구듯, 켠 하위 씬 상자의 미룬 현금을 떨군다.
        internal void OnSubsceneSwitched() => LootBoxSetup.DropDeferred(_world, _drops, _deferredCash);

        // 원작 LevelManager.InitLevel의 출발점 배치다. 컨트롤러가 첫 월드 초기화에서 하위 씬을 켠 뒤 한 번 부르고, 순간이동 객체가 옮긴다.
        internal void PlaceAtRaidStart(OriginalStartPoints? points, Func<string, int> seed) => _teleports.PlaceAtRaidStart(points, seed);

        // 줄, 화자, 줄별 카메라 전환은 지점에 저작한 원본 대사 트리 데이터가 정한다. 마지막 줄을 넘기면 같은 프레임에
        // 대사 완료 사건을 확정하고, 그 뒤 대사 데이터가 플레이 구도로 되돌린다.
        // 컷신 지점은 대사가 끝난 뒤 재생 1회를 캠페인 기록에 남긴다(원작 CutScene 1회 재생).
        void BeginDialogue(OriginalGameplayPoint point) =>
            point.Dialogue.Begin(_dialogue, _world.GameCamera, () =>
            {
                SubmitEvent(point.SourceId);
                if (point.TryGetComponent(out OriginalCutScene cut)) cut.MarkFinished(GameManager.Flags);
            });
    }

    // 상호작용 안내 이름의 번역 캐시다. 전투 월드와 프롤로그 드라이버가 같은 규칙을 쓰도록 한 곳에 둔다(검토 5번: 프롤로그 전용 캐시 중복).
    // 매 프레임 불리므로 항목 이름 키가 모두 같으면 저장한 문구 목록을 그대로 돌려주고, 로캘이 바뀌면 드라이버가 Clear로 비운다.
    internal sealed class InteractPromptLabels
    {
        readonly List<string> _keys = new();
        readonly List<string> _texts = new();

        // 대표가 첫 항목인 묶음 목록(InteractableBase.InteractableList)의 이름을 차례로 번역한다. 인터페이스 열거자 박싱을 피하려고 인덱스로 순회한다.
        internal IReadOnlyList<string> For(IReadOnlyList<InteractableBase> group)
        {
            bool same = group.Count == _keys.Count;
            for (int i = 0; same && i < group.Count; i++) same = group[i].InteractNameKey == _keys[i];
            if (same) return _texts;
            _keys.Clear();
            _texts.Clear();
            for (int i = 0; i < group.Count; i++)
            {
                string key = group[i].InteractNameKey;
                _keys.Add(key);
                _texts.Add(GameLocalization.Text(key));
            }
            return _texts;
        }

        internal void Clear()
        {
            _keys.Clear();
            _texts.Clear();
        }
    }
}
