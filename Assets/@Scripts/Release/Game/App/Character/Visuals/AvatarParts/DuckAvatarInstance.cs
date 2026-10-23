#nullable enable annotations

using System;
using System.Collections.Generic;
using DuckCharacter.AvatarParts;
using Game.Core;
using UnityEngine;


namespace DuckCharacter
{
    // 캐릭터 인스턴스(DuckCharacterInstance)가 생성하고 소유하는 외형 조립기다.
    internal sealed class DuckAvatarInstance
    {

        readonly DuckCharacterRig _rig;
        // 파츠 루트를 파괴하는 동작이다. 제품은 UnityObjects.DestroyOwned를, 테스트는 해제 실패를 일으키는 대역을 넣는다.
        readonly Action<GameObject> _release;
        // 파츠 컴포넌트가 파괴돼도 루트 GameObject로 소유권을 추적한다. 해제에 실패한 루트는 _pendingCleanup에 남겨 다시 시도한다.
        readonly HashSet<GameObject> _ownedRoots = new HashSet<GameObject>();
        readonly HashSet<GameObject> _pendingCleanup = new HashSet<GameObject>();
        // 적용이나 정리 중에 Clear가 다시 불리면 정리하지 않고 검사로 알린다. 다른 재진입은 소유자가 막는다.
        bool _mutating;
        // Clear의 재진입 검사 문구다.
        const string _reentryMessage = "외형 적용 또는 정리 중에는 다시 변경할 수 없습니다.";

        // 부위마다 소켓, 배치값, 붙인 파츠와 롤백 기록을 한 객체에 묶는다. 원작 CustomFacePartUtility와 같은 단위다.
        // DuckAvatarPartType 값이 인덱스다. 테스트 도우미 CharacterTestAccess가 이 필드와 슬롯의 _first, _second를 이름으로 읽는다.
        readonly PartSlot[] _slots;

        // 가림 상태를 비트로 보관하여 장비 교체마다 집합을 할당하지 않는다.
        uint _hiddenMask;

        // 확정된 외형 복사본만 보관한다.
        DuckAvatarPreset? _current;

        // 장비 계층이 쓰는 리그다. 구성에 결함이 있어도 참조는 남기고 적용 검사가 거부한다.
        public DuckCharacterRig Rig => _rig;

        // 리그는 캐릭터 수명 동안 고정하고, 파츠 라이브러리는 적용할 때마다 받는다.
        internal DuckAvatarInstance(DuckCharacterRig characterRig)
            : this(characterRig, UnityObjects.DestroyOwned) { }

        // 테스트는 해제 동작만 바꾸고 제품과 같은 소유권과 정리 경로를 탄다.
        // 캐릭터는 씬이나 편집 화면을 준비할 때만 만든다.
        internal DuckAvatarInstance(DuckCharacterRig characterRig, Action<GameObject> release)
        {
            _slots = new PartSlot[(int)DuckAvatarPartType.Count];
            for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
                _slots[(int)type] = new PartSlot(this, type);

            _release = release;
            _rig = characterRig;
        }

        // 모든 종료 경로에서 잠금을 풀어 Clear의 재진입 검사가 적용 중인지 알게 한다. 재진입은 소유자 DuckCharacterInstance가 먼저 막는다.
        public DuckAvatarApplyResult Apply(DuckAvatarPartLibrary avatarPartLibrary, DuckAvatarPreset appearance)
        {
            _mutating = true;
            try { return ApplyCore(avatarPartLibrary, appearance); }
            finally { _mutating = false; }
        }

        // 리그 구성 결함은 TryCheckRuntimeReady가 Log.Instance.Fail로 한 번 보고하고, 아무것도 바꾸기 전에 거부한다.
        // 에디터와 DEBUG 빌드는 Fail에서 멈춘다.
        DuckAvatarApplyResult ApplyCore(DuckAvatarPartLibrary avatarPartLibrary, DuckAvatarPreset appearance)
        {
            if (!_rig.TryCheckRuntimeReady())
                return DuckAvatarApplyResult.Reject();

            // 외형은 판독이나 편집 세션에서 검증됐거나 제작 데이터라 재검사 없이 복제하고, 호출자와 분리된 사본을 후보로 쓴다.
            DuckAvatarPreset candidate = appearance.Clone();

            avatarPartLibrary.Sanitize(candidate);
            // 아무것도 바꾸기 전에 모든 부위의 프리팹과 소켓을 먼저 찾는다.
            foreach (PartSlot slot in _slots) slot.Request(avatarPartLibrary, candidate);

            // 롤백이 적용 코드를 다시 부르지 않고 복원하도록, 지금 외형과 리그, 부위별 참조와 실제 표시값을 기록해 둔다.
            DuckAvatarPreset? previousCurrent = _current;
            var previousState = new DuckAppearanceState();
            _rig.CaptureAppearance(previousState);
            foreach (PartSlot slot in _slots) slot.Capture(previousState);
            GameObject? staging = null;
            // 후보 컴포넌트가 콜백에서 파괴되어도 해당 게임 오브젝트를 정리할 수 있게 보관한다.
            var stagedRoots = new List<GameObject>();
            // 준비나 확정 중 예외가 나면 finally가 롤백한 뒤 같은 예외가 전파되므로 예외를 잡지 않는다.
            bool committed = false;
            try
            {
                // 준비와 확정이 같은 롤백 경계 안에 있어, 어떤 예외에도 후보가 남지 않는다.
                StageSlots(stagedRoots, ref staging);
                CommitSlots(candidate);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    var recoveryFailures = new List<Exception>();
                    // 부위별 되돌릴 참조는 슬롯이 들고 있으므로 외형 전체에 걸친 기록만 넘긴다.
                    Rollback(previousCurrent, previousState, stagedRoots, staging, recoveryFailures);
                    // 예외가 전파되는 중이라 결과로 넘길 수 없으므로 복구와 정리 실패를 여기서 한 번씩 기록한다.
                    foreach (Exception recovery in recoveryFailures) Log.Instance.Exception(recovery, _rig);
                }
            }

            // 확정 뒤의 정리 실패가 롤백으로 이어지지 않도록 현재 외형을 먼저 게시하고 나서 정리한다.
            _current = candidate;
            var cleanupFailures = new List<Exception>();
            // 앞선 적용에서 해제에 실패해 남은 루트도 이번 확정 뒤 다시 정리한다.
            RetryPendingCleanup(cleanupFailures);
            // 교체된 부위의 루트만 정리하고, 정리에 실패한 루트는 재시도 목록에 유지한다.
            foreach (PartSlot slot in _slots) slot.ReleaseReplaced(cleanupFailures);
            // 정리 오류가 있어도 적용 확정 상태와 예외 목록을 함께 반환한다.
            TryReleasePart(staging, cleanupFailures);
            return DuckAvatarApplyResult.Commit(cleanupFailures);
        }

        // 교체할 부위마다 비활성 준비 루트 아래 후보를 만든다. 준비 루트는 처음 교체하는 부위에서 만든다.
        // 예외로 끝나도 호출자가 준비 루트를 정리할 수 있게 ref로 바로 돌려준다.
        void StageSlots(List<GameObject> stagedRoots, ref GameObject? staging)
        {
            foreach (PartSlot slot in _slots)
            {
                if (!slot.DecideReplacement())
                    continue;

                if (staging == null)
                {
                    staging = new GameObject("외형 교체 준비");
                    // 준비용 루트도 해제에 성공할 때까지 소유 목록에 둔다.
                    _ownedRoots.Add(staging);
                    staging.SetActive(false);

                    staging.transform.SetParent(_rig.transform, false);
                }

                // 모든 후보 루트를 준비 시점부터 추적한다.
                slot.Prepare(staging.transform, stagedRoots);
            }
        }

        // 교체될 파츠를 끈 뒤 부위마다 새 파츠를 붙인다. 그다음 머리와 얼굴, 피부색, 가림 상태를 적용한다.
        void CommitSlots(DuckAvatarPreset candidate)
        {
            // 새 후보를 하나라도 활성화하기 전에 교체될 파츠를 모두 비활성화한다.
            foreach (PartSlot slot in _slots) slot.DeactivateReplaced();
            foreach (PartSlot slot in _slots) slot.Commit();

            // 파츠 활성화가 모두 끝난 뒤 머리 변형과 얼굴 위치를 적용한다. 머리와 이마 본은 적용 전에 TryCheckRuntimeReady가 확인한다.
            _rig.ApplyHead(candidate._headSetting);
            _rig.ApplyFaceOffsets(candidate.GetInfo(DuckAvatarPartType.Eye));
            // 피부색은 헤어색과 독립된 저장값을 표시 객체에 전달한다.
            _rig.Rendering.ApplyMainColor(candidate._headSetting._mainColor);

            // 새로 붙인 파츠에도 지금 장비의 가림 상태를 다시 적용한다.
            ApplyVisibility();
        }

        // 부위 하나의 소켓, 배치값, 붙인 파츠와 롤백 기록을 묶는다. 원작 CustomFacePartUtility처럼 부위 하나만 맡고,
        // 적용은 Request, Capture, Prepare, Commit 순서로 부른다. 실패하면 Restore가 Capture한 참조로 되돌린다.
        sealed class PartSlot
        {
            // 리그, 루트 소유 목록, 해제 동작은 모든 부위가 소유자 하나를 함께 쓴다.
            readonly DuckAvatarInstance _owner;
            readonly DuckAvatarPartType _type;

            // 확정된 파츠다. 좌우 한 쌍인 부위는 _second에 오른쪽 파츠를 둔다. 테스트 도우미가 두 필드를 이름으로 읽는다.
            DuckAvatarPart? _first;
            DuckAvatarPart? _second;
            // 적용한 프리팹이다. 라이브러리는 id가 같은 파츠만 돌려주므로 프리팹이 같으면 id도 같아, 이때 지금 인스턴스를 다시 쓴다.
            DuckAvatarPart? _prefab;

            // 이번 적용의 요청과 후보다. Request가 모든 부위에서 끝난 뒤에만 준비와 확정이 이 값을 쓴다.
            DuckAvatarPart? _requested;
            Transform? _socket;
            Transform? _secondSocket;
            DuckAvatarPartInfo? _info;
            bool _replace;
            DuckAvatarPart? _stagedFirst;
            DuckAvatarPart? _stagedSecond;

            // 확정에 실패하면 같은 Unity 인스턴스와 프리팹으로 되돌릴 수 있게 적용 전 상태를 둔다.
            // 교체될 파츠 컴포넌트가 비활성화 콜백에서 파괴돼도 해제할 루트를 잃지 않게 루트도 미리 받아 둔다.
            DuckAvatarPart? _previousFirst;
            DuckAvatarPart? _previousSecond;
            DuckAvatarPart? _previousPrefab;
            GameObject? _previousFirstRoot;
            GameObject? _previousSecondRoot;

            internal PartSlot(DuckAvatarInstance owner, DuckAvatarPartType type)
            {
                _owner = owner;
                _type = type;
            }

            // 프리팹과 소켓을 찾고 배치값 사본을 만든다. 확정 상태는 바꾸지 않는다. 부위마다 맞는 프리팹이 등록돼 있고
            // 리그에 부위 소켓과 손 파츠 소켓이 있는지는 라이브러리와 리그 프리팹을 검사하는 EditMode 테스트가 확인하므로,
            // 소켓 누락은 저작 결함이라 아래 두 소켓 조회의 결과를 보지 않는다.
            internal void Request(DuckAvatarPartLibrary library, DuckAvatarPreset candidate)
            {
                DuckCharacterRig rig = _owner._rig;
                DuckAvatarPart? prefab = library.Get(_type, candidate.GetID(_type));
                _ = rig.TryGetAvatarSockets(_type, out Transform? first, out Transform? second);

                // 장갑 날개는 원작 UpdateHands처럼 매 프레임 손 위치를 복사하지 않고, 적용할 때 좌우 손 파츠 소켓에 한 번 붙인다.
                if (prefab.FollowsHands)
                    _ = rig.TryGetHandPartSockets(out first, out second);

                _requested = prefab;
                _socket = first;
                _secondSocket = second;
                // 배치값은 사본을 쓴다. 눈썹은 눈 높이를 heightOffset으로 받아 눈을 따라 움직인다.
                _info = candidate.GetInfo(_type).Clone();
                if (_type == DuckAvatarPartType.Eyebrow)
                    _info._heightOffset = candidate.GetInfo(DuckAvatarPartType.Eye)._height;
                // 앞선 적용의 판정과 후보가 이번 확정에 섞이지 않게 비운다.
                _replace = false;
                _stagedFirst = null;
                _stagedSecond = null;
            }

            // 지금 참조와 루트를 기록하고, 파츠의 실제 표시값을 롤백 기록에 더한다.
            internal void Capture(DuckAppearanceState state)
            {
                _previousFirst = _first;
                _previousSecond = _second;
                _previousPrefab = _prefab;
                _previousFirstRoot = RootOf(_first);
                _previousSecondRoot = RootOf(_second);
                if (_first != null) _first!.CaptureAppearance(state);
                if (_second != null) _second!.CaptureAppearance(state);
            }

            // 프리팹이 다르거나 필요한 파츠가 파괴됐으면 교체한다. 프리팹이 같으면 id도 같다. 판정은 비활성화, 확정, 정리가 함께 쓴다.
            internal bool DecideReplacement()
            {
                _replace = _prefab != _requested || _first == null || (_secondSocket != null && _second == null);
                return _replace;
            }

            // 교체될 좌우 파츠를 끈다. 실패하면 롤백 기록이 원래 활성 상태를 복원한다.
            internal void DeactivateReplaced()
            {
                if (!_replace) return;
                Deactivate(_previousFirst);
                Deactivate(_previousSecond);
            }

            // 교체 부위는 준비된 후보를, 재사용 부위는 새 수치를 반영한 지금 파츠를 소켓에 붙이고 활성화한다.
            internal void Commit()
            {
                if (_replace)
                {
                    _first = _stagedFirst;
                    _second = _stagedSecond;
                }
                else
                {
                    // 재사용 인스턴스만 새 수치를 반영하고 준비된 교체 파츠는 중복 구성하지 않는다.
                    _first!.SetMirrored(false);
                    _first!.Apply(_info!);
                    if (_second != null)
                    {
                        _second!.SetMirrored(true);
                        _second!.Apply(_info!);
                    }
                }

                // 파츠 활성화 콜백은 다른 파츠와 소켓을 파괴하거나 옮기지 않으므로 부착 결과를 다시 확인하지 않는다.
                Attach(_first!, _socket!);

                // 오른쪽 부착 여부는 준비와 교체 판정과 같은 Unity null 비교로 정해, 세 단계가 늘 같은 판단을 내린다.
                if (_secondSocket != null)
                    Attach(_second!, _secondSocket!);

                _prefab = _requested;
            }

            // 확정 뒤 교체된 부위의 적용 전 루트를 정리한다. 정리에 실패한 루트는 소유자의 재시도 목록에 남는다.
            internal void ReleaseReplaced(List<Exception> failures)
            {
                if (!_replace) return;
                _owner.TryReleasePart(_previousFirstRoot, failures);
                _owner.TryReleasePart(_previousSecondRoot, failures);
            }

            // Capture한 참조로 되돌린다. Unity 객체는 건드리지 않아, 후보 정리와 표시값 복원은 소유자가 이어서 한다.
            internal void Restore()
            {
                _first = _previousFirst;
                _second = _previousSecond;
                _prefab = _previousPrefab;
            }

            // 콜백이 파괴한 파츠는 건너뛰어, 남은 쪽 파츠의 가림 갱신이 끊기지 않게 한다.
            internal void SetVisible(bool visible)
            {
                if (_first != null) _first!.SetVisible(visible);
                if (_second != null) _second!.SetVisible(visible);
            }

            // 확정 상태와 적용마다 다시 채우는 참조를 비운다. 루트 소유권은 해제에 성공할 때까지 소유자가 따로 유지한다.
            internal void Clear()
            {
                _first = null;
                _second = null;
                _prefab = null;
                _requested = null;
                _socket = null;
                _secondSocket = null;
                _info = null;
                _stagedFirst = null;
                _stagedSecond = null;
                _previousFirst = null;
                _previousSecond = null;
                _previousPrefab = null;
                _previousFirstRoot = null;
                _previousSecondRoot = null;
            }

            // 교체할 부위의 후보를 만든다. 오른쪽 후보는 오른쪽 소켓이 있을 때만 만든다.
            internal void Prepare(Transform staging, List<GameObject> stagedRoots)
            {
                _stagedFirst = PrepareSide(staging, _info!, false, stagedRoots);
                if (_secondSocket != null)
                    _stagedSecond = PrepareSide(staging, _info!, true, stagedRoots);
            }

            // 비활성 부모 아래에서 새 파츠를 완성해, 확정 전에 보이거나 활성화 콜백이 돌지 않게 한다.
            // 만든 루트는 구성 전에 소유 목록에 넣어, 구성 중 예외가 나도 롤백이 정리한다.
            DuckAvatarPart PrepareSide(Transform staging, DuckAvatarPartInfo info, bool mirrored, List<GameObject> stagedRoots)
            {
                DuckAvatarPart? prefab = _requested;
                DuckAvatarPart part = UnityEngine.Object.Instantiate(prefab, staging, false);
                GameObject root = part.gameObject;
                _owner._ownedRoots.Add(root);
                stagedRoots.Add(root);
                part.gameObject.SetActive(false);
                part.SetMirrored(mirrored);
                part.Apply(info);
                return part;
            }

            // 준비에서 구성을 마친 파츠를 소켓에 붙이고 활성화만 한다.
            void Attach(DuckAvatarPart part, Transform socket)
            {
                part.transform.SetParent(socket, false);
                part.transform.localPosition = Vector3.zero;
                part.transform.localRotation = Quaternion.identity;
                part.gameObject.SetActive(true);
            }
        }

        // 관리 참조부터 되돌린다. 정리나 복구에 실패해도 목록에 모으고 나머지 복구를 계속한다.
        void Rollback(DuckAvatarPreset? previousCurrent, DuckAppearanceState previousState, List<GameObject> stagedRoots,
            GameObject? staging, List<Exception> failures)
        {
            foreach (PartSlot slot in _slots) slot.Restore();
            _current = previousCurrent;

            // 후보를 모두 정리한 다음에 원래 파츠의 활성 상태를 복원한다.
            foreach (GameObject root in stagedRoots) TryReleasePart(root, failures);
            // 준비 루트에 남은 후보도 복원 전에 정리한다. 그래야 파괴 콜백이 바꾼 값까지 아래 복원이 되돌린다.
            TryReleasePart(staging, failures);
            previousState.Restore(failures);
        }

        // 저장이나 편집 코드가 내부 외형을 고치지 못하도록 깊은 사본을 돌려준다.
        public DuckAvatarPreset? Snapshot() => _current?.Clone();

        // 장비 표시가 가릴 부위를 비트 마스크로 넘긴다. 달라진 부위만 갱신한다.
        internal void SetHiddenMask(uint mask)
        {
            uint changed = _hiddenMask ^ mask;
            if (changed == 0)
                return;
            _hiddenMask = mask;
            ApplyVisibility(changed);
        }

        // 외형 교체는 전체를, 장비 교체는 실제 가림이 달라진 부위만 적용한다.
        void ApplyVisibility(uint changedMask = uint.MaxValue)
        {
            for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
            {
                // 값이 그대로인 렌더러에는 enabled를 다시 기록하지 않는다.
                uint bit = 1u << (int)type;
                if ((changedMask & bit) == 0) continue;
                // 좌우 파츠가 모두 같은 가림 상태를 따르도록 슬롯이 함께 적용한다.
                _slots[(int)type].SetVisible((_hiddenMask & bit) == 0);
            }
        }

        // 소유한 루트를 모두 정리하고, 해제 실패는 호출자 목록에 모은다. 실패한 루트는 남겨 다음 종료 때 다시 시도한다.
        // 모은 실패는 소유자 DuckCharacterInstance.Dispose가 기록한다.
        internal void Clear(List<Exception> failures)
        {
            if (_mutating)
            {
                // 재진입은 소유자가 먼저 막으므로 검사만 하고 정리하지 않는다. 진행 중인 작업을 깨지 않도록
                // 정리 경로에서도 검사를 먼저 하는 예외다.
                Log.Instance.Assert(false, _reentryMessage);
                return;
            }
            _mutating = true;
            try
            {
                _current = null;
                _hiddenMask = 0;
                // 슬롯은 참조만 비우고, 실제 해제는 아래 소유 루트 정리가 맡는다.
                foreach (PartSlot slot in _slots) slot.Clear();
                foreach (GameObject root in new List<GameObject>(_ownedRoots)) TryReleasePart(root, failures);
            }
            finally { _mutating = false; }
        }

        // 관리 슬롯이 비워져도 해제 성공 전까지 루트 소유권과 재시도 참조를 유지한다.
        void TryReleasePart(GameObject? target, List<Exception> failures)
        {
            // 실제 null만 건너뛴다. 이미 파괴된 객체도 참조는 남아 있어 소유 목록에서 지울 수 있다.
            if (target is null) return;
            GameObject ownedRoot = target;
            try
            {
                if (target != null)
                {
                    try { target.SetActive(false); }
                    finally { if (target != null) _release(target); }
                }
                // 파괴 여부와 상관없이 처음 받은 참조로 소유 목록에서 지운다.
                _ownedRoots.Remove(ownedRoot);
                _pendingCleanup.Remove(ownedRoot);
            }
            // 예외 경계: 한 루트의 해제 실패를 보관하고 다른 모든 루트의 정리를 계속한다.
            catch (Exception exception)
            {
                failures.Add(exception);
                if (target != null) _pendingCleanup.Add(target);
                else
                {
                    // 해제 중에 이미 파괴됐으면 다시 시도할 대상이 없으므로 받아 둔 참조로 소유 목록에서 지운다.
                    _ownedRoots.Remove(ownedRoot);
                    _pendingCleanup.Remove(ownedRoot);
                }
            }
        }

        // 재시도 중 집합이 갱신되므로 이번 시도 대상을 먼저 복사한다.
        void RetryPendingCleanup(List<Exception> failures)
        {
            foreach (GameObject root in new List<GameObject>(_pendingCleanup)) TryReleasePart(root, failures);
        }

        // 파괴된 컴포넌트를 역참조하지 않고 살아 있는 루트만 반환한다.
        static GameObject? RootOf(DuckAvatarPart? part) => part != null ? part.gameObject : null;

        // 교체 대상만 비활성화하여 재사용 파츠의 콜백과 코루틴 수명을 유지한다.
        static void Deactivate(DuckAvatarPart? part)
        {
            if (part != null) part.gameObject.SetActive(false);
        }

    }

    // 외형 적용이 바꾸는 실제 Unity 상태만 기록해, 적용 코드를 다시 호출하지 않고 복구한다.
    internal sealed class DuckAppearanceState
    {
        // 복구 항목은 대상이 파괴됐으면 던지지 않고 실패 목록에 직접 남긴다.
        readonly List<Action<List<Exception>>> _restore = new List<Action<List<Exception>>>();
        // 배치와 렌더러를 모두 복구한 뒤에 원래 파츠를 활성화해, 복구 도중의 값이 보이지 않게 한다.
        readonly List<Action<List<Exception>>> _restoreActivation = new List<Action<List<Exception>>>();
        readonly HashSet<Transform> _transforms = new HashSet<Transform>();
        readonly HashSet<Renderer> _renderers = new HashSet<Renderer>();

        // 본, 소켓, 배치점은 로컬 변형만 기록하고, 부착 루트는 부모와 활성 상태까지 기록한다.
        internal void CaptureTransform(Transform? target, bool attachment = false)
        {
            if (target == null || !_transforms.Add(target))
                return;
            Transform? parent = target.parent;
            Vector3 position = target.localPosition;
            Quaternion rotation = target.localRotation;
            Vector3 scale = target.localScale;
            bool active = target.gameObject.activeSelf;
            // 부착 루트의 형제 순서도 적용 전 계층과 같게 복원한다.
            int siblingIndex = target.GetSiblingIndex();
            // 대상이나 부모가 파괴됐으면 던지지 않고 복구 실패로 남긴 뒤 그 항목만 건너뛴다.
            _restore.Add(failures =>
            {
                if (target == null)
                {
                    failures.Add(new MissingReferenceException("복구할 외형 Transform이 파괴되었습니다."));
                    return;
                }
                if (attachment)
                {
                    if (!ReferenceEquals(parent, null) && parent == null)
                    {
                        failures.Add(new MissingReferenceException("복구할 외형 부모가 파괴되었습니다."));
                        return;
                    }
                    if (target.parent != parent) target.SetParent(parent, false);
                    if (target.GetSiblingIndex() != siblingIndex) target.SetSiblingIndex(siblingIndex);
                }
                target.localPosition = position;
                target.localRotation = rotation;
                target.localScale = scale;
            });
            // 활성화 콜백은 로컬 상태 복원이 모두 끝난 뒤 실행하고, 파괴된 대상은 복구 실패로 보고한다.
            if (attachment) _restoreActivation.Add(failures =>
            {
                if (target == null)
                {
                    failures.Add(new MissingReferenceException("복구할 외형 활성 대상이 파괴되었습니다."));
                    return;
                }
                if (target.gameObject.activeSelf != active) target.gameObject.SetActive(active);
            });
        }

        // 공유 재질은 건드리지 않고, 표시 여부와 틴트가 쓰는 렌더러 전체 프로퍼티 블록을 복원한다.
        internal void CaptureRenderers(Renderer[]? targets)
        {
            foreach (Renderer target in targets)
            {
                if (target == null || !_renderers.Add(target)) continue;
                bool enabled = target.enabled;
                var block = new MaterialPropertyBlock();
                target.GetPropertyBlock(block);
                // 파괴된 렌더러는 던지지 않고 복구 실패로 보고한다.
                _restore.Add(failures =>
                {
                    if (target == null)
                    {
                        failures.Add(new MissingReferenceException("복구할 외형 Renderer가 파괴되었습니다."));
                        return;
                    }
                    target.enabled = enabled;
                    // 같은 프레임 안의 복원이라 블록에 함께 담긴 피격 밝기도 적용 직전 값 그대로다.
                    target.SetPropertyBlock(block.isEmpty ? null : block);
                });
            }
        }

        // 한 대상을 복구하지 못해도 나머지는 끝까지 복구한다.
        internal void Restore(List<Exception> failures)
        {
            foreach (Action<List<Exception>> restore in _restore) Attempt(restore, failures);
            // 상태 복구 실패도 수집하면서 살아 있는 나머지 파츠의 활성 상태는 복구한다.
            foreach (Action<List<Exception>> restore in _restoreActivation) Attempt(restore, failures);
        }

        // 롤백 복구 항목 하나를 실행한다. 항목이 직접 남긴 실패와 Unity API 예외를 같은 목록에 모으고,
        // 외형 적용이 그 목록을 한 번씩 기록한다.
        internal static void Attempt(Action<List<Exception>> action, List<Exception> failures)
        {
            try { action(failures); }
            // 예외 경계: 한 항목이 실패해도 나머지 복구를 계속하고, 실패는 모아 외형 적용이 기록한다.
            catch (Exception exception) { failures.Add(exception); }
        }
    }
}
