#nullable enable annotations
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 프롤로그의 F 상호작용 탐색과, 거울, 출구, 은신처 상자가 끝났을 때 맡기는 일(IInteractionHost)이다. 저장과 진행 확정은
    // Runtime 명령에 맡긴다. 프롤로그 컨트롤러가 레벨 초기화 때 씬 참조 묶음과 인벤토리 화면으로 만든다. 원작 프롤로그에는 기지 시설이 없다.
    internal sealed class PrologueInteractions : IInteractionHost
    {
        // 세션, 카탈로그, GameplayUI, 카메라, 알림, 기록 문맥(프롤로그 컨트롤러)을 이 묶음에서 읽어 컨트롤러와 어긋나지 않게 한다.
        readonly GameplayWorldContext _world;
        // 원본 Prologue_1의 F 상호작용 대상이다. 컨트롤러가 직렬화한 배열을 그대로 받는다.
        readonly InteractableBase[] _interactables;
        readonly InventoryScreen _inventory;
        // 원본 검색의 최대 다섯 충돌체를 재사용하여 프레임별 배열 할당을 피한다.
        readonly Collider[] _hits = new Collider[5];
        // 상호작용 대상 이름의 번역 캐시다. 전투 월드와 같은 InteractPromptLabels를 써서 캐시 규칙을 한 곳에 두고,
        // 로캘이 바뀌면 컨트롤러가 ClearLabels로 비운다.
        readonly InteractPromptLabels _labels = new();
        // 상호작용 명령이 도는 동안 켠다.
        bool _interacting;

        internal PrologueInteractions(GameplayWorldContext world, InteractableBase[] interactables, InventoryScreen inventory)
        {
            _world = world;
            _interactables = interactables;
            _inventory = inventory;
        }

        // 지금 가장 가까운 상호작용 대상이며 없으면 null이다. 바깥에서는 읽기만 하고, 진행 상태는 상호작용 명령이 바꾼다.
        internal InteractableBase? Target { get; private set; }

        // 상호작용 명령이 도는 동안 참이다. 컨트롤러의 입력 판정과 진행 게이트가 읽는다.
        internal bool IsInteracting => _interacting;

        // 대사 시작, 입력 차단, 비활성화 때 지금 대상을 비운다.
        internal void ClearTarget() => Target = null;

        // 로캘이 바뀌면 캐시한 문구를 비워 다음 표시 때 새 로캘로 번역한다.
        internal void ClearLabels() => _labels.Clear();

        // 대상 묶음의 번역된 안내 이름이다. 이름 키가 그대로면 캐시한 목록을 다시 돌려주어 매 프레임 번역하지 않는다.
        internal IReadOnlyList<string> PromptLabels(InteractableBase target) => _labels.For(target.InteractableList);

        // 이번 프레임의 가장 가까운 대상을 찾아 지금 대상으로 기억하고 돌려준다.
        internal InteractableBase? UpdateTarget(OriginalPlayerMotor motor)
        {
            InteractableBase? target = FindInteraction(motor);
            Target = target;
            return target;
        }

        // 원본 CA_Interact.SearchInteractableAround처럼 조준 앞 탐색 구에 닿은 Collider 중 플레이어 루트에서 가장 가까운 대상을
        // 돌려주고, 없으면 null이다. 탐색 구는 전투 월드와 같은 OriginalInteractionSearch가 정하며, 모터는 컨트롤러 Update가 이번 프레임에
        // 확인한 것을 받는다. 출구가 꺼져 있으면 그 영역도 꺼져 탐색 구에 잡히지 않는다.
        InteractableBase? FindInteraction(OriginalPlayerMotor motor)
        {
            Vector3 position = motor.transform.position;
            int count = OriginalInteractionSearch.Overlap(position, motor.AimDirection, _hits);
            float nearest = float.PositiveInfinity;
            InteractableBase? target = null;
            for (int index = 0; index < count; index++)
            {
                Collider hit = _hits[index];
                float distance = (hit.transform.position - position).sqrMagnitude;
                if (distance >= nearest) continue;
                InteractableBase? candidate = InteractableOf(hit);
                if (candidate == null || !candidate.IsInteractable) continue;
                nearest = distance; target = candidate;
            }
            return target;
        }

        // 대상이 거울, 출구, 은신처 상자 셋뿐이라 표 없이 배열을 훑는다.
        InteractableBase? InteractableOf(Collider area)
        {
            foreach (InteractableBase interactable in _interactables)
                if (ReferenceEquals(interactable.InteractCollider, area)) return interactable;
            return null;
        }

        // 원작 프롤로그에는 퀘스트 창구, 상점, 보관함, 작업대, 제조 장소, 낚시터, 순간이동기 같은 기지 시설이 없다.
        IFacilityHost? IInteractionHost.Facilities => null;

        // 거울은 커스터마이징을 연다.
        void IInteractionHost.OpenCustomization(Interact_CustomFace mirror) => InteractAsync(true).Forget();

        // 출구는 튜토리얼로 넘어간다. 프롤로그에 다른 장면 전환 대상은 없다.
        void IInteractionHost.LoadScene(SceneLoaderInteractable loader)
        {
            if (loader.Target == SceneLoaderTarget.Tutorial) InteractAsync(false).Forget();
        }

        // 원작 은신처 상자처럼 상자 칸을 인벤토리 화면으로 연다.
        void IInteractionHost.OpenLoot(InteractableLootbox lootbox) => _inventory.Open(lootbox, false);

        // 프롤로그에는 순간이동, 유실물, 바닥 아이템, 쪽지가 없다.
        void IInteractionHost.Teleport(MultiSceneTeleporter teleporter) { }
        void IInteractionHost.RecoverLostBody(LostBodyInteractable body) { }
        void IInteractionHost.PickUp(InteractablePickup pickup) { }
        void IInteractionHost.ReadNote(NoteInteract note) { }

        // 필요 아이템, 말풍선, 알림은 전투 월드와 같은 규칙이다. 필요 아이템은 전투 월드와 같은 찾기 함수를 불러 출처 규칙을 한 곳에 둔다.
        // 씬 정리 중이라 세션이 없으면 아이템은 없는 것으로 본다.
        bool IInteractionHost.HasItem(int originalItemId) =>
            _world.TrySession(out GameSessionManager? session) && WorldInteractions.HasRequired(session!, originalItemId);

        bool IInteractionHost.TryUseItem(int originalItemId) =>
            _world.TrySession(out GameSessionManager? session) && WorldInteractions.TryUseRequired(session!, _world.Catalog, originalItemId);

        void IInteractionHost.Bubble(string textKey, Transform at, float yOffset, float duration) =>
            _world.View.Bubble(GameLocalization.Text(textKey), at, _world.Camera, duration, yOffset);

        void IInteractionHost.Notify(string textKey) => _world.Notify(textKey);

        // 원작 프롤로그에는 건설 지점이 없다.
        bool IInteractionHost.TryPay(CostTaker taker) => false;

        // 앱 수명의 Runtime 명령을 부르고 실패 처리는 SceneCommand가 맡는다. 거울 편집의 복귀 검정 화면은 편집이 실제로
        // 열렸을 때만 오는 커스터마이징 닫힘 알림이 재생하므로, 여기서는 명령 결과를 보지 않는다. 기록 문맥은 프롤로그 컨트롤러다.
        async UniTask InteractAsync(bool openCustomization)
        {
            // Forget으로 시작하므로 상태 설정부터 모두 try 안에 둔다. 취소는 기록 없이 끝내고 그 밖의 실패는 기록한다.
            try
            {
                _interacting = true;
                // 거울 편집은 편집 흐름(GameManager.Customization)이, 튜토리얼 진행은 씬 흐름(GameManager.Scenes)이 맡는다.
                // 컨트롤러 Update가 Runtime 입력 허용을 확인한 프레임에서 시작하므로 앱이 살아 있다.
                if (openCustomization) await SceneCommand.TryRunAsync(() => GameManager.Customization.OpenAsync(), _world.LogContext);
                else await SceneCommand.TryRunAsync(() => GameManager.Scenes.AdvanceAsync(CampaignStage.Tutorial), _world.LogContext);
            }
            // 예외 경계: Forget으로 시작한 루트라, SceneCommand 밖에서 생긴 예상 못 한 실패를 잃지 않게 기록한다.
            catch (Exception error) { Log.Instance.Exception(error, _world.LogContext); }
            finally { _interacting = false; }
        }
    }
}
