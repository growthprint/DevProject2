#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Game.App.World;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 전투 월드 컨트롤러가 PrepareWorld에서 한 번 만들어 전투 시뮬레이션, 인벤토리 화면, 월드 상호작용에 나눠 주는 씬 참조 묶음이다.
    // 컨트롤러도 씬 참조를 이 묶음으로만 읽어 두 곳이 어긋나지 않게 한다. 협력 객체는 컨트롤러를 거꾸로 참조하지 않는다.
    // 직접 가진 상태는 결함 기록, 프롤로그 상자 기억, 생성 상호작용 대상 목록처럼 저장하지 않는 것뿐이다. 저장할 변경은 세션 명령이,
    // 저장과 전환 순서는 Runtime이 맡는다.
    internal sealed class GameplayWorldContext
    {
        // 소유 그래프 결함을 키마다 한 번만 기록한다. 키는 "container:<ID>", "shortcut:<칸>"다.
        readonly Log.Once _reportedDefects = new Log.Once();
        readonly Func<bool> _simulationReady;
        // 하위 씬 판정 재료다. 루트와 이름은 같은 순서로 짝지어 저작되고, 지금 켠 번호는 컨트롤러가 바꿀 때마다 그대로 읽는다.
        readonly GameObject[] _subscenes;
        readonly string[] _subsceneIds;
        readonly Func<int> _activeSubscene;

        // 오디오 관리자는 오디오가 꺼져 있어도 늘 넘어온다. 꺼져 있으면 관리자가 명령을 조용히 무시한다.
        internal GameplayWorldContext(GameRuntime runtime, OriginalGameplayCatalog catalog, SharedHudView hud, OriginalGameplayView view,
            OriginalPlayerMotor motor, GameCharacterBinding character, Camera camera, OriginalGameCamera gameCamera,
            GameAudioManager audio, WorldObjectService objects, OriginalGameplayPoint[] points, InteractableBase[] interactables, Door[] doors,
            OriginalEnemyController[] enemies, Func<bool> simulationReady,
            UnityEngine.Object logContext,
            // 전투 월드 컨트롤러가 하위 씬 루트, 같은 순서의 원본 하위 씬 이름, 지금 켠 번호를 넘긴다.
            // 하위 씬이 없는 프롤로그는 넘기지 않아 모든 물체를 메인 씬 것으로 본다.
            GameObject[]? subscenes = null, string[]? subsceneIds = null, Func<int>? activeSubscene = null)
        {
            _subscenes = subscenes ?? Array.Empty<GameObject>();
            _subsceneIds = subsceneIds ?? Array.Empty<string>();
            _activeSubscene = activeSubscene ?? (() => -1);
            Runtime = runtime;
            Catalog = catalog;
            Hud = hud;
            View = view;
            Motor = motor;
            Character = character;
            Camera = camera;
            GameCamera = gameCamera;
            Audio = audio;
            Objects = objects;
            Points = points;
            Interactables = interactables;
            Doors = doors;
            Enemies = enemies;
            _simulationReady = simulationReady;
            LogContext = logContext;
        }

        internal GameRuntime Runtime { get; }
        // 씬 진행 명령을 받을 현재 세션을 GameManager에서 찾는다. 씬 정리 직전 프레임처럼 Runtime이 세션을 바꾸거나 해제했거나,
        // 앱 루트가 먼저 파괴된 뒤에는 기록 없이 false다. 이 컨텍스트가 보장하지 않는 상태이므로 호출자는 그 작업만 건너뛴다.
        // 그래서 true를 받은 호출자는 같은 프레임에 다른 GameManager 정적 속성도 바로 읽는다.
        internal bool TrySession([NotNullWhen(true)] out GameSessionManager? session)
        {
            session = GameManager.HasInstance ? GameManager.SessionManager : null;
            return session != null;
        }
        internal OriginalGameplayCatalog Catalog { get; }
        // 호스트 씬에 있는 앱 수명 공용 HUD다. 전투 월드 컨트롤러가 씬 확정 때 연결한 동안에만 바꾸고, 후보 준비 중에는 바꾸지 않는다.
        // Play Mode 종료처럼 씬 정리 없이 파괴될 때는 먼저 사라질 수 있어, 정리 경로는 PresentationAttached를 거친다.
        internal SharedHudView Hud { get; }
        // 대사 상자, 말풍선, 피해 숫자를 그리는 앱 수명 GameplayUI다. 공용 HUD와 같은 소유 규칙을 따른다.
        internal OriginalGameplayView View { get; }
        // 공용 HUD가 지금 이 월드의 컨트롤러에 연결되어 있는지다. 정리와 비활성화 경로는 다른 소유자의 HUD나 파괴된 HUD를
        // 바꾸지 않도록 이 판정을 먼저 거친다.
        internal bool PresentationAttached => Hud != null && Hud.IsAttachedTo(LogContext);
        // 화면이 공용 HUD를 바꿔도 되는지다. 앱 루트가 먼저 파괴됐거나 HUD가 이 월드에 연결되지 않았으면 false다.
        internal bool CanPresent => GameManager.HasInstance && PresentationAttached;
        // 공용 HUD가 이 월드에 연결된 동안에만 화면이 명령을 낼 세션을 찾는다. 연결이 없으면 세션을 찾지 않고 기록 없이 false다.
        internal bool TryPresentSession([NotNullWhen(true)] out GameSessionManager? session)
        {
            if (!PresentationAttached)
            {
                session = null;
                return false;
            }
            return TrySession(out session);
        }
        internal OriginalPlayerMotor Motor { get; }
        internal GameCharacterBinding Character { get; }
        internal Camera Camera { get; }
        // 전투 월드 컨트롤러의 플레이어 준비가 소유한 원본 게임 카메라다. 든 총의 ADS 수치, 반동과 근접 명중 충격, 대사 구도, 순간 이동 동기화에 쓴다.
        internal OriginalGameCamera GameCamera { get; }
        // 앱 수명 오디오 관리자다. 오디오가 꺼져 있으면 명령을 조용히 무시하므로 호출자는 null을 검사하지 않는다.
        internal GameAudioManager Audio { get; }
        // SceneContext가 소유한 이 씬의 World다. 전투 중 생기는 동적 객체와 런타임 지점을 빌리고 돌려준다.
        internal WorldObjectService Objects { get; }
        // 제작 도구가 저작한 영역 지점(대사, 안내, 철수), F 상호작용 대상, 문, 적 목록이다. 실행 중 바꾸지 않으며,
        // 매 프레임 순회해도 할당이 없도록 배열로 둔다.
        internal OriginalGameplayPoint[] Points { get; }
        internal InteractableBase[] Interactables { get; }
        internal Door[] Doors { get; }
        internal OriginalEnemyController[] Enemies { get; }
        // 과제 프리팹과 상인 부착물처럼 실행 중에 생겨 저작 표에 없는 F 상호작용 대상이다. 만든 쪽이 생성 때 넣고 사망, 해제, 파괴 때 뺀다.
        // 월드 상호작용이 저작 대상, 시체, 바닥 아이템 다음으로 영역 Collider를 찾는다. 이 목록은 저장하지 않는다.
        internal IReadOnlyList<InteractableBase> SpawnedInteractables => _spawnedInteractables;
        internal void AddSpawnedInteractable(InteractableBase interactable) => _spawnedInteractables.Add(interactable);
        internal void RemoveSpawnedInteractable(InteractableBase interactable) => _spawnedInteractables.Remove(interactable);
        readonly List<InteractableBase> _spawnedInteractables = new List<InteractableBase>();
        // 원본 장면 ID가 이 월드의 하위 씬 이름이면 그 번호다. 메인 씬 이름처럼 하위 씬이 아니면 -1이라 늘 켜진 쪽으로 본다.
        internal int SubsceneIndex(string sceneId) => Array.IndexOf(_subsceneIds, sceneId);
        // 이 Transform이 들어 있는 하위 씬 루트의 번호다. 어느 루트 아래에도 없으면 메인 씬 것이라 -1이다.
        internal int SubsceneOf(Transform target)
        {
            for (int i = 0; i < _subscenes.Length; i++)
                if (target.IsChildOf(_subscenes[i].transform)) return i;
            return -1;
        }
        // 원작 MultiSceneCore처럼 메인 씬(-1)과 지금 켠 하위 씬만 살아 있는 공간으로 본다. 첫 하위 씬을 켜기 전에는 하위 씬이 모두 꺼져 있다.
        internal bool IsSubsceneActive(int index) => index < 0 || index == _activeSubscene();
        // 떨군 물체를 그 하위 씬과 함께 켜고 끄도록 붙일 루트다. 번호는 지금 켠 하위 씬처럼 0 이상이다.
        internal Transform SubsceneRoot(int index) => _subscenes[index].transform;
        // 지금 켠 하위 씬 번호다. 하위 씬이 없거나 첫 하위 씬을 켜기 전이면 -1이다.
        internal int ActiveSubscene => _activeSubscene();
        // 원작 ItemExtensions.Drop의 MoveToActiveWithScene처럼, 바닥에 떨군 물체를 지금 켠 하위 씬과 함께 숨고 다시 보이게 둘 부모다.
        // 그 하위 씬 루트 아래 묶음을 처음 쓸 때 만든다. 켠 하위 씬이 없으면 null이라 호출자는 늘 켜진 World 루트에 둔다.
        internal Transform? DropParent()
        {
            int index = ActiveSubscene;
            if (index < 0) return null;
            Transform root = SubsceneRoot(index);
            Transform? drops = root.Find(_runtimeDropsName);
            if (drops != null) return drops;
            drops = new GameObject(_runtimeDropsName).transform;
            drops.SetParent(root, false);
            return drops;
        }
        // 하위 씬 루트 아래 떨군 물체 묶음의 이름이다. 저작 묶음(OriginalInteractables 등)과 겹치지 않게 런타임 전용 이름을 쓴다.
        internal const string _runtimeDropsName = "RuntimeDrops";
        // 진단 로그를 붙일 전투 월드 컨트롤러다. 이 컨트롤러가 공용 HUD와 GameplayUI의 연결 소유자라 PresentationAttached도 이 객체로 본다.
        internal UnityEngine.Object LogContext { get; }

        // 현재 세션의 소지품에 확정된 아이템 소유 그래프를 찾는다. 세션이 없거나 소유 그래프가 아직 초기화되지 않았으면 기록 없이 false다.
        internal bool TryItems([NotNullWhen(true)] out ItemOwnershipState? items) => TryItems(out _, out items);
        // 그래프를 읽고 같은 프레임에 세션 명령도 내는 호출자용이다. 세션을 두 번 찾지 않도록 찾은 세션을 함께 돌려준다.
        internal bool TryItems([NotNullWhen(true)] out GameSessionManager? session, [NotNullWhen(true)] out ItemOwnershipState? items)
        {
            items = TrySession(out session) ? session!.Snapshot().Campaign.Gameplay.Inventory.Items : null;
            return items != null;
        }
        internal Vector3 PlayerPosition => Motor != null ? Motor.transform.position : Vector3.zero;
        // 시뮬레이션을 돌려도 되는지다. 초기화, 내비게이션, 전환, 대사 같은 컨트롤러 조건과 Runtime 활성 판정을 함께 본다.
        internal bool SimulationReady => _simulationReady();
        // 메모리에 확정된 사건 키가 완료됐는지다. 사건을 기록하는 튜토리얼이나 출격 중에만 인정하고, 그 판정과 키 규약,
        // 완료 판정은 CampaignEventLog가 맡는다. 좌표가 붙은 사망 이벤트도 원본 문의 사망 조건을 만족한다. 세션이 없으면 기록 없이 false다.
        internal bool TutorialEventComplete(string id)
        {
            if (!TrySession(out GameSessionManager? session)) return false;
            CampaignState campaign = session!.Snapshot().Campaign;
            return CampaignEventLog.IsRecording(campaign) && CampaignEventLog.IsComplete(campaign, id);
        }

        // 상자 칸을 이미 집었는지다. 튜토리얼과 출격은 세션 사건으로, 사건 집합이 없는 프롤로그는 이 월드가 기억한 키로 본다.
        // 프롤로그는 다시 들어올 수 없는 첫 장면이라 원작 상자 Inventory처럼 레벨 동안만 기억하면 된다.
        internal bool LootTaken(string key) => _takenLoot.Contains(key) || TutorialEventComplete(key);
        internal void RememberLoot(string key) => _takenLoot.Add(key);
        readonly HashSet<string> _takenLoot = new HashSet<string>(StringComparer.Ordinal);
        // 원본 NotificationText.Push처럼 현재 로캘 문구를 공용 HUD 알림 대기열에 넣는다.
        internal void Notify(string key) => Hud.Notify(GameLocalization.Text(key));
        // 원본 NotificationText처럼 가방이 가득 찬 거절만 알린다. 다른 거절을 보일지는 호출자가 정한다.
        internal void NotifyIfInventoryFull(InventoryRejection rejection)
        {
            if (rejection == InventoryRejection.InventoryFull) Notify("gameplay.inventoryFull");
        }
        // 원본 CharacterMainControl.PopText처럼 플레이어 위에 현재 로캘 문구를 띄운다. 2m 높이는 GameplayUI가 더한다.
        internal void PopText(string key) => View.PopText(GameLocalization.Text(key), PlayerPosition, Camera);

        // 결함 키를 이 월드에서 처음 볼 때만 오류를 기록한다. 커밋 알림, 발사, HUD 갱신처럼 반복되는 경로가 같은 결함을 만나도
        // 로그가 늘지 않는다.
        internal void ReportDefectOnce(string key, string message)
        {
            _reportedDefects.Error(key, message, LogContext);
        }
    }
}
