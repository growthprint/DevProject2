#nullable enable annotations
using System;
// 물고기 후보가 없는 콘텐츠 결함을 Log로 남긴다.
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 Action_FishingV2.FishingStates다. 원작 철자 cancleBack만 CancelBack으로 고쳤다. 저장하지 않는 판 안 상태다.
    internal enum FishingState { Non = 0, Throwing = 1, Waiting = 2, Ring = 3, CancelBack = 4, SuccessBack = 5, FailBack = 6 }

    // 원작 낚시터 루트의 Action_FishingV2와 그 Interact 자식(InteractableBase, 0.2초)을 한 컴포넌트로 옮긴다. 상호작용이 끝나면 호스트가
    // 낚시를 시작하고, 월드 드라이버(WorldInteractions.TickFishing)가 일시정지와 준비 관문 안에서만 Tick을 부른다.
    // 상호작용은 손에 든 무기 칸 아이템이 낚싯대일 때만 고를 수 있다(원작 SyncInteractable의 Interact 끄기). 낚시 상태는 저장하지 않는다.
    // 원작 차이: 찌·줄·물결·입자 연출, 소리, 줌 카메라, 던지기 공격 동작(ForcePlayAttackAnimation)은 옮기지 않았다.
    // 몸 돌리기(ForceTurnTo)는 호스트가 시작 뒤 TargetPoint 쪽으로 한다. 안내 캔버스(fishingHudCanvas)와 고리 Animator는 이 컴포넌트가 원작 시점에 켜고 끈다.
    public sealed class Action_FishingV2 : InteractableBase
    {
        // 원작 targetPoint, fishingHudCanvas, scaleRing이다. 낚시 판정은 이 셋을 쓰지 않고, 고리 HUD와 연출이 읽는다.
        [SerializeField] Transform? _targetPoint;
        [SerializeField] GameObject? _fishingHud;
        [SerializeField] Transform? _scaleRing;
        // 원작 ringAnimator(TargetPoint/RingParent)다. 정수 State가 0 기다림(투명), 1 줄어드는 고리, 2 성공, 3 실패 클립을 고른다.
        [SerializeField] Animator? _ringAnimator;
        // 원작 ringAnimator.SetInteger("State", ...)의 파라미터다.
        static readonly int _ringStateId = Animator.StringToHash("State");
        // 원작 같은 루트의 FishSpawner 값과 낚싯대 목록이다.
        [SerializeField] FishSpawnTable? _table;
        // 원작 말풍선과 알림 번역 키다.
        [SerializeField] string _noRodText = "Pop_NoRod";
        [SerializeField] string _noBaitText = "Pop_NoBait";
        [SerializeField] string _gotFishText = "Notify_GotFish";
        [SerializeField] string _failText = "Notify_FishRunAway";
        // 원작 제로존 낚시터의 저작 값이다. 고리는 scaleRange.x(3)에서 y(0.5)로 줄고, 크기가 성공 범위 안일 때 누르면 낚는다.
        [SerializeField] Vector2 _waitTimeRange = new Vector2(3f, 9f);
        [SerializeField] Vector2 _scaleRange = new Vector2(3f, .5f);
        [SerializeField] Vector2 _successRange = new Vector2(.75f, 1.1f);
        // 원작 throwStartTime(0.1초)은 찌를 띄우는 연출에만 쓰여 옮기지 않았다. 던지기는 outTime이 지나면 기다림으로 넘어간다.
        [SerializeField] float _outTime = .6f;
        [SerializeField] float _backTime = .3f;
        // 원작 private 상수다. 고리 시간 = FishingTime × 1.25 ÷ 물고기 난이도, 성공 뒤 0.2초 기다렸다가 줄을 거둔다.
        const float _scaleTimeFactor = 1.25f;
        const float _maxScaleTime = 7f;
        const float _successPause = .2f;
        // 원작 TransToRing의 하한이다. 고리 시간은 0.01초 아래로 내려가지 않고, 0.02 미만 난이도는 1로 본다.
        const float _minRingTime = .01f;
        const float _minDifficulty = .02f;
        // 원작 ringAnimator State 정수값이다. 고리 Animator 저작이 이 값으로 클립을 고른다.
        const int _ringWaiting = 0;
        const int _ringShrink = 1;
        const int _ringSuccess = 2;
        const int _ringFail = 3;

        // 실행 중에만 있는 호스트와 억제·행동 태그 묶음이다. 호스트가 있으면 실행 중이다.
        IFishingHost? _host;
        IDisposable? _hold;
        string? _rodId;
        int _baitId = -1;
        // 원작 currentFish다. 아직 소지품에 넣지 않은 물고기의 원본 ID이고, 없으면 -1이다. 못 잡으면 버려 소지품에 남지 않는다.
        int _fish = -1;
        FishingState _state;
        // 고리 크기는 scaleRing에만 쓰고 따로 보관하지 않는다. 읽는 Release 코드가 없어 RingScale을 두지 않는다(5-F2).
        float _stateTimer, _waitTime, _scaleTime;
        bool _catchInput, _rodHeld;

        internal bool IsRunning => _host != null;
        // 지금 낚시 상태다. 고리 Animator의 State 값(SyncRing)이 이 값에서 정해진다.
        internal FishingState State => _state;
        // 원작 targetPoint다. 낚시를 시작한 호스트가 몸을 이쪽으로 돌린다(원작 ForceTurnTo).
        internal Transform? TargetPoint => _targetPoint;

        // 원작 SyncInteractable처럼 낚싯대를 들었을 때만 고를 수 있고, 낚는 동안에는 다시 고르지 않는다.
        internal override bool IsInteractable => base.IsInteractable && _rodHeld && !IsRunning;

        protected override void Awake()
        {
            base.Awake();
            SetHud(false);
        }

        // 원작 OnDisable처럼 꺼지면 낚시를 멈춰 억제와 행동 태그를 반납한다.
        void OnDisable()
        {
            if (IsRunning) Stop();
        }

        // 원작 OnMainCharacterChangeItemAgent다. 월드 드라이버가 손에 든 무기 칸 아이템의 원본 ID(없으면 -1)를 매 프레임 넘긴다.
        internal void SyncInteractable(int heldItemId) => _rodHeld = _table != null && _table.IsRod(heldItemId);

        // 원작 OnInteractTimeOut의 StartAction이다. 시설을 가진 호스트만 시작하고, 시작 조건은 그 호스트가 TryStart로 본다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.StartFishing(this);

        // 원작 OnStart다. 낚싯대가 없거나 미끼가 없으면 말풍선만 띄우고 시작하지 않는다. 플레이어는 옮기지 않는다.
        // 낚는 중인 낚시터는 IsInteractable이 거짓이라 호스트는 멈춘 낚시터만 시작한다.
        internal bool TryStart(IFishingHost host)
        {
            if (_table == null || !host.TryHeldItem(out string rodId, out int itemId, out int baitId) || !_table.IsRod(itemId))
            {
                host.PopText(_noRodText);
                return false;
            }
            if (baitId < 0)
            {
                host.PopText(_noBaitText);
                return false;
            }
            _host = host;
            _rodId = rodId;
            _baitId = baitId;
            _fish = -1;
            _catchInput = false;
            _waitTime = _waitTimeRange.x + (_waitTimeRange.y - _waitTimeRange.x) * host.Random01();
            _hold = host.HoldFishingAction();
            SetHud(true);
            _stateTimer = 0f;
            _state = FishingState.Throwing;
            // 원작 TransToThrowing처럼 고리를 켜고 기다림(투명) 클립으로 둔다.
            SetRingActive(true);
            SyncRing();
            return true;
        }

        // 원작 OnUpdateAction이다. catchPressed는 원작 TryCatch(대시 입력)이고 기다림과 고리 상태에서만 받는다.
        // 손의 낚싯대가 바뀌거나 사라지면 멈춘다. 복귀 상태가 끝나면 멈추고, 실패 알림은 복귀가 끝난 뒤 띄운다.
        internal void Tick(float deltaTime, bool catchPressed)
        {
            IFishingHost? host = _host;
            if (host == null) return;
            if (catchPressed && (_state == FishingState.Waiting || _state == FishingState.Ring)) _catchInput = true;
            if (!host.TryHeldItem(out string heldId, out _, out _) || heldId != _rodId)
            {
                Stop();
                return;
            }
            bool stop = false;
            switch (_state)
            {
                case FishingState.Throwing:
                    if (_catchInput) ToBack(FishingState.CancelBack);
                    else if (_stateTimer >= _outTime) ToWaiting(host);
                    break;
                case FishingState.Waiting:
                    if (_catchInput)
                    {
                        ToBack(FishingState.CancelBack);
                        break;
                    }
                    if (_stateTimer >= _waitTime)
                    {
                        // 원작은 물고기를 만들지 못하면 "Error:Spawn fish failed"를 띄운다. 제품은 추첨 때 Log로 남겼으므로 취소로만 돌아간다.
                        if (_fish >= 0) ToRing(host);
                        else ToBack(FishingState.CancelBack);
                    }
                    break;
                case FishingState.Ring:
                {
                    float scale = Mathf.Lerp(_scaleRange.y, _scaleRange.x, 1f - _stateTimer / _scaleTime);
                    if (_scaleRing != null) _scaleRing.localScale = Vector3.one * scale;
                    if (_catchInput)
                    {
                        if (scale < _successRange.x || scale > _successRange.y)
                        {
                            ToBack(FishingState.FailBack);
                            break;
                        }
                        ToSuccess(host);
                    }
                    if (_stateTimer > _scaleTime) ToBack(FishingState.FailBack);
                    break;
                }
                case FishingState.CancelBack:
                    stop = _stateTimer > _backTime;
                    break;
                case FishingState.SuccessBack:
                    stop = _stateTimer >= _successPause && _stateTimer - _successPause > _backTime;
                    break;
                case FishingState.FailBack:
                    if (_stateTimer > _backTime)
                    {
                        host.Notify(_failText);
                        stop = true;
                    }
                    break;
            }
            _catchInput = false;
            _stateTimer += deltaTime;
            if (stop) Stop();
        }

        // 원작 OnStop이다. 못 잡은 물고기는 소지품에 넣은 적이 없어 버리기만 한다. 억제와 행동 태그를 한 번만 반납하며 여러 번 불러도 안전하다.
        internal void Stop()
        {
            IDisposable? hold = _hold;
            _hold = null;
            _host = null;
            _rodId = null;
            _baitId = -1;
            _fish = -1;
            _catchInput = false;
            _state = FishingState.Non;
            SetHud(false);
            // 원작 OnStop처럼 고리를 기다림 클립으로 돌리고 끈다. 꺼진 Animator에는 값을 쓰지 않으므로 먼저 쓴다.
            SyncRing();
            SetRingActive(false);
            hold?.Dispose();
        }

        // 원작 TransToRing의 고리 시간이다. FishingTime × 1.25(0.01 이상)를 물고기 난이도로 나누고, 난이도가 0.02 미만이면 1로 보며, 7초를 넘지 않는다.
        internal static float RingTime(float fishingTime, float difficulty)
        {
            // 하한 값은 원작 상수를 이름으로 읽는다.
            float time = Mathf.Max(_minRingTime, fishingTime * _scaleTimeFactor);
            if (difficulty < _minDifficulty) difficulty = 1f;
            time /= difficulty;
            return time > _maxScaleTime ? _maxScaleTime : time;
        }

        // 원작 TransToWaiting과 SpawnFish다. 물고기를 정한 직후 미끼를 한 번만 쓴다. 정하지 못하거나 미끼를 쓰지 못하면 물고기 없이 기다리고,
        // 기다림이 끝나면 취소로 돌아간다. 그 뒤 취소해도 쓴 미끼는 돌아오지 않는다.
        void ToWaiting(IFishingHost host)
        {
            _stateTimer = 0f;
            _state = FishingState.Waiting;
            // 원작 TransToWaiting의 ringAnimator State 0이다.
            SyncRing();
            FishSpawnTable table = _table!;
            if (!FishSpawnRule.TryPick(table.Settings, _baitId, host.FishingQualityFactor, host.AtNight, host.Weather, host.Random01, host.FishTags,
                    host.SearchFish, out int fish))
            {
                Log.Instance.Warning("낚시 물고기 후보가 없습니다: " + name + ", 미끼 " + _baitId, this);
                return;
            }
            if (host.TryConsumeBait(_rodId!)) _fish = fish;
        }

        // 원작 TransToRing이다.
        void ToRing(IFishingHost host)
        {
            _scaleTime = RingTime(host.FishingTime, host.FishingDifficulty(_fish));
            _stateTimer = 0f;
            _catchInput = false;
            _state = FishingState.Ring;
            // 원작 TransToRing의 State 1이다. 고리가 보이고, 크기는 Tick이 scaleRing에 쓴다.
            SyncRing();
        }

        // 원작 TransToSuccessback이다. 물고기를 줍고 낚시 경험을 기록하는 일은 호스트가 한다.
        void ToSuccess(IFishingHost host)
        {
            _stateTimer = 0f;
            _state = FishingState.SuccessBack;
            // 원작 TransToSuccessback의 State 2(성공 클립)다.
            SyncRing();
            SetHud(false);
            int fish = _fish;
            _fish = -1;
            host.CatchFish(fish, _gotFishText);
        }

        // 원작 TransToCancleBack과 TransToFailBack이다. 고리 HUD를 끄고 줄을 거둔다.
        void ToBack(FishingState state)
        {
            _stateTimer = 0f;
            _state = state;
            // 원작 취소는 State 0(투명), 실패는 State 3(실패 클립)이다.
            SyncRing();
            SetHud(false);
        }

        // 원작 fishingHudCanvas.gameObject.SetActive다. 고리 HUD를 저작하지 않은 낚시터는 건너뛴다.
        void SetHud(bool visible)
        {
            if (_fishingHud != null) _fishingHud.SetActive(visible);
        }

        // 원작 ringAnimator.gameObject.SetActive다. 고리를 저작하지 않은 낚시터는 건너뛴다.
        void SetRingActive(bool visible)
        {
            if (_ringAnimator != null) _ringAnimator.gameObject.SetActive(visible);
        }

        // 원작 ringAnimator.SetInteger("State", ...)를 지금 낚시 상태(State)에서 정한다. 고리 1, 성공 복귀 2, 실패 복귀 3, 나머지는 0이다.
        // 꺼졌거나 컨트롤러가 붙지 않은 Animator는 값을 받지 못하고 경고만 남기므로 건너뛴다. 다시 켜지면 기본 상태(기다림, 0)에서 시작해 건너뛴 0을 잃지 않는다.
        void SyncRing()
        {
            // 낚시 상태와 고리 클립 정수값의 짝은 원작 Animator 저작을 따른다.
            int ring = State switch { FishingState.Ring => _ringShrink, FishingState.SuccessBack => _ringSuccess, FishingState.FailBack => _ringFail, _ => _ringWaiting };
            if (_ringAnimator != null && _ringAnimator.isActiveAndEnabled && _ringAnimator.isInitialized) _ringAnimator.SetInteger(_ringStateId, ring);
        }
    }
}
