#nullable enable annotations
using System;
using DuckCharacter;
using Game.GAS;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 플레이어 캐릭터 루트를 CharacterController로 움직이고 조준, 달리기, 대시, 스태미나를 처리한다. 이동과 스태미나 수치는
    // 원본 플레이어 정의로 채운 GAS 어트리뷰트에서 읽으며, 캐릭터 뼈는 직접 움직이지 않고 Animator 파라미터만 갱신한다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class OriginalPlayerMotor : MonoBehaviour
    {
        // 조준 중 걷기 속도에 곱하는 배율이다. 원본 Movement.walkSpeed처럼 든 총의 AdsWalkSpeedMultiplier를 쓰되 1을 넘지 않는다.
        // 총이 없으면 원본 CharacterMainControl 기본값 0.5다. 전투 시뮬레이션이 무기를 바꿀 때 SetAdsWalkSpeedMultiplier로 갱신한다.
        internal const float _defaultAdsWalkSpeedMultiplier = .5f;
        float _adsWalkSpeedMultiplier = _defaultAdsWalkSpeedMultiplier;
        internal void SetAdsWalkSpeedMultiplier(float multiplier) => _adsWalkSpeedMultiplier = Mathf.Min(1f, multiplier);
        // 원본 CharacterWalkSpeed와 CharacterRunSpeed가 곱하는 든 무기의 이동 배율이다. 맨손이면 1이다.
        // 전투 시뮬레이션이 무기를 바꿀 때 OriginalCharacter.WeaponMoveSpeedMultiplier로 구해 넣는다.
        float _weaponMoveSpeedMultiplier = 1f;
        internal void SetWeaponMoveSpeedMultiplier(float multiplier) => _weaponMoveSpeedMultiplier = multiplier;
        // 원본 중력 가속도다. CharacterController는 중력을 적용하지 않으므로 직접 적분한다.
        const float _gravity = 15f;
        // 원본 충돌 레이어 마스크다. 전역 물리 설정이 아니라 이 컨트롤러에만 적용한다.
        const int _collisionMask = 532673;
        // 이보다 작은 입력이나 방향은 0으로 본다. 이동 입력, 조준 방향, 애니메이션 정지 판정에 쓴다.
        const float _inputThreshold = 0.02f;
        // 원본 마우스 감도 기준값이다. 감도가 이 값이면 Look 델타를 그대로 쓴다.
        const float _baseMouseSensitivity = 10f;
        // 포인터를 투영할 조준 평면이 캐릭터 루트보다 높은 거리다.
        const float _aimPlaneHeight = .5f;
        // 첫 조준 프레임 전의 조준점을 몸 앞쪽 이 거리에 둔다.
        const float _initialAimDistance = 10f;
        // 원본 이벤트가 없는 이동 컨트롤러의 고정 주기 발소리 횟수(초당)다. 달리기 7번, 걷기 4번이다.
        const float _runFootstepRate = 7f, _walkFootstepRate = 4f;
        // 원본 PostFootStepSound처럼 이벤트 발소리 사이에 두는 최소 간격(초)이다.
        const float _animationFootstepInterval = .08f;
        // 달리기를 새로 시작하는 데 필요한 최소 스태미나 비율이다. 원본 CharacterMainControl.CanRun과 같다.
        const float _minimumRunStaminaRatio = .2f;
        // 원본 TerrainGrass 셰이더가 풀을 밀어내는 기준인 캐릭터 위치 전역이다.
        static readonly int _characterPosId = Shader.PropertyToID("CharacterPos");
        // 원본 OcclusionFadeManager가 넣는 캐릭터 위치 전역이다. 머리 위 퀘스트 표시 셰이더가 거리로 크기와 투명도를 정한다.
        // 제품에는 가림 페이드가 없어, 원본에서 페이드가 켜진 레벨처럼 늘 플레이어 위치를 넣는다.
        static readonly int _occlusionCharacterPosId = Shader.PropertyToID("OC_CharacterPos");

        // 게임플레이 활성 여부와 입력 차단을 읽는 실행 소유자다.
        GameRuntime _runtime = null!;
        // 적용 알림을 구독한 앱의 옵션 관리자다. Release는 앱이 먼저 사라졌을 수 있어 정적 속성 대신 이 참조로 푼다.
        GameOptionsManager _options = null!;
        // 캐릭터 루트의 충돌 이동을 맡는다.
        CharacterController _controller = null!;
        // 카메라는 조준과 이동 기준 조회에만 사용한다.
        Camera _camera = null!;
        // 뼈 위치는 직접 바꾸지 않고 캐릭터에 있는 Animator의 파라미터만 갱신한다.
        Animator _animator = null!;
        // 같은 Animator의 손 상태와 공격 레이어다. 무기 꺼내기와 공격은 전투 시뮬레이션이 정하고, 보내기는 이 모터의 애니메이션 갱신이 한다.
        OriginalHandAnimation _hands = null!;
        internal OriginalHandAnimation Hands => _hands;
        // 발소리와 대시 소리를 낸다. 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다.
        GameAudioManager _audio = null!;
        // 공유 입력 액션이라 켜고 끄지 않고 읽기만 한다. Player/Jump(Space)는 대시에 쓴다.
        // 낚시 중에는 낚시 억제 토큰이 대시를 막고, 같은 누름은 전투 월드 컨트롤러가 낚아채기로 넘긴다(원작 InputManager.Dash).
        // UI/Point는 조준 위치를 실제 포인터에 맞출 때, Player/Look은 가상 조준 포인터에 더할 델타를 읽을 때 쓴다.
        InputAction _moveInput = null!;
        InputAction _sprintInput = null!;
        InputAction _dashInput = null!;
        InputAction _pointInput = null!;
        InputAction _lookInput = null!;
        // 원본처럼 OS 포인터와 따로 관리하는 가상 조준 화면 위치와 반동 상태다. 구성 전 Update도 동기화를 끊으므로 처음부터 만든다.
        readonly OriginalAimCursor _aim = new OriginalAimCursor();
        // 옵션의 마우스 감도다. 기본값 10은 Look 델타를 그대로 쓰는 배율 1이다.
        float _mouseSensitivity = _baseMouseSensitivity;
        // 전투 시뮬레이션이 실제 발사 결과의 반동을 걸고 무기를 바꿀 때 지운다. 반동 방향은 캐릭터 화면 위치 기준이다.
        internal void ClearRecoil() => _aim.ClearRecoil();
        internal void AddRecoil(OriginalItemDefinition weapon, float control, float difficulty) =>
            _aim.AddRecoil(_camera.WorldToScreenPoint(transform.position), weapon, control, difficulty);
        // Player/Aim(우클릭) 액션에서 조준 유지 상태를 읽는다.
        InputAction _adsInput = null!;
        // Attach가 의존성과 옵션 구독을 마치기 전이나 Release 뒤에는 이동하지 않는다.
        bool _configured;
        // 매 프레임 옵션 사본을 만들지 않도록 달리기 방식과 이동 기준을 캐시한다.
        bool _holdToRun = true;
        bool _moveRelativeToAim;
        // 누르고 있는 방식이 꺼졌을 때 쓰는 달리기 토글 상태다.
        bool _runToggled;
        // 달리는 중인지다. 계속 달릴 때와 새로 달리기 시작할 때의 스태미나 문턱을 가르는 데도 쓴다.
        bool _running;
        // 이번 프레임의 조준 상태다. 이동, 회전, 애니메이션이 함께 읽는다.
        bool _aiming;
        // 대시 중이면 끝날 때까지 시작 방향과 대시 곡선 속도를 쓴다.
        bool _dashing;
        // 수평 이동 속도다. 중력은 _verticalSpeed로 따로 적분한다.
        Vector3 _velocity;
        // 수평 조준 방향이다. 포인터가 조준 평면에 닿지 않는 프레임에는 마지막 값을 유지한다.
        Vector3 _aimDirection = Vector3.forward;
        // 대시 도중 이동 입력이 시작 방향을 바꾸지 못하게 보관한다.
        Vector3 _dashDirection;
        // 중력으로 쌓이는 수직 속도다.
        float _verticalSpeed;
        // 생존 수치 소유자가 가진 플레이어 GAS 시스템이다. 이동, 회전, 대시 속도와 스태미나 수치는 버프가 바꾸므로 쓸 때마다 읽는다.
        // 현재 스태미나도 이 시스템의 CurrentStamina가 갖는다. 아바타를 연결할 때 최대치로 시작하며 저장하지 않는다.
        AbilitySystem? _abilities;
        // 원본 CA_Dash다. 쿨다운, 비용, 장전과 아이템 사용 취소 규칙은 어빌리티 에셋이 갖고, 방향과 곡선은 이 모터가 맡는다.
        // 게임 중에는 대시를 취소하는 어빌리티가 없어 대시가 진행되는 동안(_dashing) 어빌리티도 실행 중이다. 정리 때 아바타 연결 해제가
        // 실행 중인 대시를 먼저 취소해도 모터는 곧 Release되어 다시 돌지 않으므로 _dashing은 맞추지 않는다.
        GameplayAbilityDefinition? _dashAbility;
        // 마지막 소비 뒤 회복이 시작되기까지 남은 시간이다.
        float _recoveryRemaining;
        // 대시 시작 뒤 지난 시간이다. 대시 길이로 나눠 곡선 위치를 구한다.
        float _dashElapsed;
        // 컨트롤러에 원본 발소리 이벤트가 없을 때 쓰는 고정 주기 발소리 타이머다.
        float _footstepElapsed;
        // 컨트롤러 클립에 원본 발소리 이벤트가 하나라도 있으면 고정 주기 발소리를 끄고 이벤트로만 낸다.
        // 마지막 이벤트 발소리 시각은 최소 간격을 지키는 데 쓴다.
        bool _animationFootsteps;
        float _lastAnimationFootstep = -999f;

        // HUD와 이 모터가 읽는 현재 스태미나다. HUD는 리그 준비가 끝난 뒤에만 만들어지므로 늘 묶인 GAS 시스템에서 읽는다.
        public float Stamina => Current(CharacterAttributes.CurrentStamina);
        // 원작에서 Stamina 스탯은 최대 스태미나다. 회복 상한과 달리기 시작 문턱이 읽는다.
        float MaximumStamina => Current(CharacterAttributes.Stamina);

        // 버프가 언제든 수치를 바꿀 수 있으므로 프레임을 넘겨 캐시하지 않고 쓰는 순간의 GAS 현재값을 읽는다.
        float Current(GameplayAttribute attribute) => _abilities!.GetCurrent(attribute);

        // 원본 Movement.walkSpeed, runSpeed다. 기아 버프는 걷기와 달리기 현재값에, 갈증 버프는 Moveability 현재값에 들어 있고,
        // 둘 다 든 무기의 이동 배율을, 조준 중 걷기는 든 총의 ADS 배율도 곱한다. 이동, 대시 종료 속도, 이동 애니메이션이 같은 값을 써야 원본과 맞는다.
        float WalkSpeed => Current(CharacterAttributes.WalkSpeed) * (_aiming ? _adsWalkSpeedMultiplier : 1f) * Current(CharacterAttributes.Moveability)
            * HeldWeaponMoveSpeedMultiplier;
        float RunSpeed => Current(CharacterAttributes.RunSpeed) * Current(CharacterAttributes.Moveability) * HeldWeaponMoveSpeedMultiplier;
        // 원작 CharacterWalkSpeed와 CharacterRunSpeed다. 원작처럼 조준 중 ADS 배율은 곱하지 않는다.
        // 원작 PetAI.SyncSpeed가 펫 속도로 이 값을 읽는다.
        internal float CharacterWalkSpeed => Current(CharacterAttributes.WalkSpeed) * Current(CharacterAttributes.Moveability)
            * HeldWeaponMoveSpeedMultiplier;
        internal float CharacterRunSpeed => RunSpeed;
        // 원본 CA_UseItem은 사용하는 아이템을 손에 들어 무기가 손에 없으므로, 아이템 사용 어빌리티가 실행 중이면 무기 배율을 곱하지 않는다.
        float HeldWeaponMoveSpeedMultiplier => _abilities!.Tags.HasTag(CharacterTags.ActionUseItem) ? 1f : _weaponMoveSpeedMultiplier;

        // 현재 스태미나는 수정자가 붙지 않는 어트리뷰트라 Base에 쓴다. 값이 그대로면 쓰지 않아,
        // 가득 찬 동안 매 프레임 GAS 정리 작업을 하지 않는다.
        void SetStamina(float value)
        {
            if (value != Stamina) _abilities!.SetBaseValue(CharacterAttributes.CurrentStamina, value);
        }

        // 중력과 분리된 현재 평면 이동 속도다.
        public Vector3 Velocity => _velocity;
        // 마지막 유효한 평면 조준 방향이다.
        public Vector3 AimDirection => _aimDirection;
        // 조준 평면 위 월드 조준점과 조준 여부다. 조준점은 사격이, 조준 여부는 카메라 ADS가 읽는다. 둘 다 모터 Update가 갱신하므로
        // 그보다 먼저 도는 Update에서는 지난 프레임 값이다. 포인터가 조준 평면에 닿지 않은 프레임에는 조준점이 마지막 값으로 남는다.
        public Vector3 AimPoint {get;private set;}
        public bool IsAiming => _aiming;
        // 가상 조준 화면 위치다. 카메라, HUD 커서, 사격이 모두 이 한 좌표를 읽는다. 모터 Update가 갱신하므로
        // 실행 순서가 정해지지 않은 컨트롤러 Update의 사격은 지난 프레임 값을 읽을 수 있다.
        public Vector2 AimScreenPosition => _aim.Position;
        // 대시 중인지다. 전투 쪽은 읽기만 하고, 대시 상태는 이 모터만 바꾼다.
        public bool IsDashing => _dashing;
        // 원본 IsInAdsInput에 해당한다. 아이템을 쓰는 중에는 _aiming이 거짓이고 조준 중에는 달리지 않으므로,
        // 원본처럼 동작 중으로 보는 대시만 따로 뺀다.
        bool InAdsInput => _aiming && !_dashing;

        // 대사, 지연 상호작용, 전환이 각자 들고 있는 입력 억제 토큰과 해제 뒤 대기다. 원본 InputManager.blockInputSources 집합에 해당한다.
        // 구성 전 모터에도 토큰을 발급할 수 있도록 처음부터 만든다.
        readonly OriginalInputSuppression _suppression = new OriginalInputSuppression();

        // 씬 대사 중에는 일시정지 화면을 열지 않고 이동 입력만 막는다. 토큰이 남아 있거나, 모두 풀린 뒤 2프레임 대기 중이면 억제 상태다.
        internal bool InputSuppressed => _suppression.IsSuppressed;

        // 실행 중인 행동이 막은 입력인지 본다. 원본 CA_UseItem처럼 아이템 사용 어빌리티가 Block.Sprint와 Block.ADS를 쥐는 동안
        // 걷기와 조준 방향은 두고 달리기와 ADS 입력만 무시한다.
        bool Blocked(GameplayTag block) => _abilities!.Tags.HasTag(block);

        // 소유자별 억제 토큰을 발급한다. 원본 DisableInput처럼 즉시 2프레임 대기를 채우고, 모든 토큰이 해제되어야 대기가 줄기 시작한다.
        internal IDisposable SuppressInput() => _suppression.Acquire();

        // 원본 UpdateInputActived처럼 토큰이 남아 있으면 대기를 2로 유지하고, 모두 풀렸으면 한 프레임씩 줄인다.
        internal void AdvanceSuppressionFrame() => _suppression.AdvanceFrame();

        // 원작 OnSetPositionEvent다. 순간 이동을 마친 위치를 알려 펫이 주인 곁으로 따라오게 한다.
        internal event Action<Vector3>? Teleported;

        // 원작 CharacterSoundMaker.PostFootStepSound가 AI에게 내는 발소리다. 발소리 위치와 달리기 여부를 넘기고,
        // 반경은 GAS 값과 소지 무게를 아는 전투 쪽이 정한다. 조준 입력 중에는 소리와 함께 내지 않는다.
        internal event Action<Vector3, bool>? FootstepPosted;

        // 순간 이동은 이동 의도와 대시, 수직 속도를 먼저 비우고, 충돌 컨트롤러를 끈 채 위치만 옮긴 뒤 바로 다시 켠다.
        // 끄고 켜는 사이에 다른 호출을 두지 않아 컨트롤러가 꺼진 채 남지 않으므로 Update는 컨트롤러 상태를 보지 않는다.
        // 유일한 호출자인 지연 상호작용 완료는 구성된 모터에서만 온다. 캠페인 해제도 전투 월드를 모터보다 먼저 내린다.
        internal void Teleport(Vector3 position)
        {
            HaltMovementIntent();
            _verticalSpeed = 0f;
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = true;
            // 원작 SetPosition처럼 옮긴 뒤에 알린다.
            Teleported?.Invoke(position);
        }

        // 원작 Movement.ForceTurnTo다. 낚시처럼 조준을 막는 행동이 시작될 때 몸을 대상 쪽으로 즉시 돌린다. 억제 중에는 Turn이 돌지 않아
        // 행동이 끝날 때까지 이 방향을 지키고, 억제가 풀리면 원작처럼 다시 마우스 쪽으로 돈다. 수평 성분이 없으면 돌리지 않는다.
        internal void FaceDirection(Vector3 direction)
        {
            Vector3 flat = Horizontal(direction);
            if (flat.sqrMagnitude <= 0f) return;
            _aimDirection = flat.normalized;
            transform.rotation = Quaternion.LookRotation(_aimDirection, Vector3.up);
        }

        // OriginalPlayerRig가 씬 준비 중 메인 스레드에서 한 번 부르는 플레이어 이동 준비다. Armature를 직계 자식으로 둔
        // Animator에 이동 컨트롤러를 넣고, 모터를 붙여 의존성으로 구성한다. 수치는 리그가 바로 이어서 Bind로 넘기는 GAS 시스템에서 읽는다.
        // 오디오 관리자는 오디오가 꺼져 있어도 늘 넘어온다. 옵션 관리자는 씬 컨트롤러가 앱에서 한 번 읽어 리그를 거쳐 넘긴다.
        internal static OriginalPlayerMotor Attach(GameCharacterBinding character, RuntimeAnimatorController locomotion,
            GameRuntime runtime, InputActionAsset actions, Camera camera, GameAudioManager audio, GameOptionsManager options)
        {
            Animator animator = character.Rig.GetComponentInChildren<Animator>(true);
            animator.runtimeAnimatorController = locomotion;
            OriginalPlayerMotor motor = character.gameObject.AddComponent<OriginalPlayerMotor>();
            InputAction move = actions.FindAction("Player/Move", true);
            InputAction sprint = actions.FindAction("Player/Sprint", true);
            // 여러 곳이 찾는 입력 경로는 공용 상수로 쓴다.
            InputAction dash = actions.FindAction(GameInputPaths._playerJump, true);
            InputAction point = actions.FindAction(GameInputPaths._uiPoint, true);
            // 원본 ADS처럼 입력 에셋의 Player/Aim(우클릭)을 반드시 쓴다. UI 오른쪽 클릭으로 대신하지 않는다.
            InputAction ads = actions.FindAction(GameInputPaths._playerAim, true);
            InputAction look = actions.FindAction("Player/Look", true);
            // RequireComponent가 모터를 붙일 때 CharacterController도 붙이므로 null을 검사하지 않는다.
            // 이동 캡슐 크기는 영역 판정 높이와 같은 원작 값을 공용 상수에서 읽는다.
            CharacterController controller = motor.GetComponent<CharacterController>();
            controller.radius = OriginalCharacterColliders._movementRadius;
            controller.height = OriginalCharacterColliders._movementHeight;
            controller.center = new Vector3(0f, OriginalCharacterColliders._movementCenterHeight, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.25f;
            controller.includeLayers = _collisionMask;
            controller.excludeLayers = ~_collisionMask;
            // 원본 Character.prefab처럼 루트는 Character 레이어에 두고, 탄환과 근접 판정이 찾는 DamageReceiver 자식을 붙인다.
            // 컨트롤러 마스크가 DamageReceiver 레이어를 빼서 제 피해 캡슐에 막히지 않는다. 원본은 플레이어 머리 조준 Collider를 끄므로 두지 않는다.
            motor.gameObject.layer = GameLayers.Character;
            CapsuleCollider receiver = OriginalCharacterColliders.AddDamageReceiver(motor.transform);
            // 원본 CharacterMainControl처럼 피해 캡슐 높이를 Helmet 소켓까지 늘린다. 외형 적용이 이 준비보다 먼저 소켓 높이를 정한다.
            Transform? helmet = character.Rig.GetEquipmentSocket(EquipPartType.Helmet);
            if (helmet != null) OriginalCharacterColliders.FitToHead(receiver, motor.transform, helmet);
            animator.applyRootMotion = false;

            // 정적 준비라 방금 붙인 모터 인스턴스에 의존성과 시작 상태를 직접 넣는다. 반환 전에 구성이 끝난다.
            motor._runtime = runtime;
            motor._controller = controller;
            motor._camera = camera;
            motor._animator = animator;
            motor._hands = new OriginalHandAnimation(animator);
            motor._audio = audio;
            motor._moveInput = move;
            motor._sprintInput = sprint;
            motor._dashInput = dash;
            motor._pointInput = point;
            motor._lookInput = look;
            motor._adsInput = ads;
            Vector3 facing = Horizontal(motor.transform.forward);
            motor._aimDirection = facing.sqrMagnitude > 0f ? facing.normalized : Vector3.forward;
            // 첫 조준 프레임 전에도 사격과 카메라가 몸 앞쪽 조준점을 읽게 한다.
            motor.AimPoint = motor.transform.position + Vector3.up * _aimPlaneHeight + motor._aimDirection * _initialAimDistance;
            // AnimationEvent는 Animator GameObject에서 발생하므로 수신기를 거기에 두고 발소리 콜백을 연결한다.
            GameplayComponents.GetOrAdd<OriginalFootstepReceiver>(animator.gameObject).Configure(motor.PostAnimationFootstep);
            // 원본처럼 손 레이어 상태가 고른 자세로 팔을 Animator 루트 기준으로 덮는다. 걷거나 구를 때 몸이 움직여도 총은 조준 방향을 지킨다.
            GameplayComponents.GetOrAdd<OriginalMagicBlend>(animator.gameObject);
            motor._animationFootsteps = HasFootstepEvent(animator.runtimeAnimatorController);
            // 앱 옵션 관리자의 확정값을 지금 한 번 적용하고, 이후 변경은 적용 알림으로 받는다. 저장소가 없으면 확정값은 기본값이다.
            // 구독한 관리자를 필드에 잡아 Release가 같은 관리자에서 푼다.
            // 구독과 구성 표시를 맨 끝에 두어, 앞 단계가 던지면 구성 전 모터만 남고 Update와 Release가 아무것도 하지 않는다.
            motor._options = options;
            motor.ApplyOptions(options.Current);
            options.Applied += motor.ApplyOptions;
            motor._configured = true;
            return motor;

            // 수신기 메서드 이름과 같은 원본 발소리 이벤트 이름을 찾는다.
            static bool HasFootstepEvent(RuntimeAnimatorController controller)
            {
                foreach (AnimationClip clip in controller.animationClips)
                    foreach (AnimationEvent animationEvent in clip.events)
                        if (animationEvent.functionName == OriginalCharacterMotion._footstepEventName) return true;
                return false;
            }
        }

        // OriginalPlayerRig가 Attach와 같은 동기 호출 안에서 생존 수치 소유자의 플레이어 GAS 시스템과 그 시스템에 준 대시 어빌리티를 넘긴다.
        // 리그만 부르는 내부 계약이라 두 값은 늘 있다. 묶기 전에 리그 준비가 실패하면 리그가 이 모터를 Release한다.
        internal void Bind(AbilitySystem abilities, GameplayAbilityDefinition dash)
        {
            _abilities = abilities;
            _dashAbility = dash;
        }

        // 구성되고 카메라와 Animator가 살아 있으며 게임플레이가 활성인 프레임에만 입력과 이동을 진행한다.
        bool Simulating => _configured && _camera && _animator && _runtime.IsGameplayActive;
        // 대사 억제 토큰, 모달 입력 차단, 창 포커스 상실 가운데 하나라도 있으면 입력만 끊는다.
        bool InputCutOff => InputSuppressed || _runtime.GameplayInput.IsBlocked || !Application.isFocused;

        // 준비나 전환, 일시정지 중이거나 씬이 비활성이면 입력과 이동 타이머를 모두 멈춘다.
        // 창 포커스를 잃었을 때는 입력만 끊고 중력, 스태미나, 대시 대기는 계속 진행한다.
        void Update()
        {
            // 원본 CharacterPosInShader처럼 플레이어가 있는 동안 정지, 입력 차단과 상관없이 매 프레임 위치를 넣는다.
            // 플레이어가 없는 씬에서는 아무도 쓰지 않아 마지막 값이 남는 것도 원본과 같다.
            Shader.SetGlobalVector(_characterPosId, transform.position);
            // 머리 위 표시 셰이더가 읽는 원본 이름으로도 같은 위치를 넣는다.
            Shader.SetGlobalVector(_occlusionCharacterPosId, transform.position);
            // 원본 InputManager.Update처럼 매 프레임 먼저 억제 대기를 진행한다.
            AdvanceSuppressionFrame();
            // 아래 이동과 스태미나 계산은 모두 GAS 시스템을 읽는다. 리그가 Attach와 같은 호출 안에서 Bind하므로 프레임 사이에
            // 구성된 모터는 늘 시스템을 가진다. 원본 CharacterMainControl.Update처럼 여기서는 포커스를 보지 않는다. 적과 생존 수치는
            // 백그라운드에서도 돌므로 플레이어만 공중이나 대시 도중에 멈추면 안 된다.
            if (!Simulating)
            {
                // 설정 창이나 전환 중에 움직인 커서를 조준에 누적하지 않는다.
                _aim.Desynchronize();
                // 거울 편집 진입이나 씬 전환처럼 세션 명령이 진행 중이면 마지막 이동 애니메이션이 남지 않게 멈춘다.
                if (_configured && _animator && _runtime.IsBusy)
                    HaltMovementIntent();
                return;
            }
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            // 대사는 억제 토큰으로, 지도 같은 모달은 Runtime의 입력 차단으로 막는다. 두 상태는 따로 세므로 서로 덮어쓰지 않는다.
            // 창 포커스를 잃어도 같은 길로 입력만 끊고, 다른 앱에서 움직인 커서는 조준에 누적하지 않는다.
            if (InputCutOff)
            {
                // 원본 DisableInput처럼 달리기, 조준, 대시 의도를 끊어 제자리에서 이동 애니메이션이 돌지 않게 하고 조준 동기화를 끊는다.
                HaltMovementIntent();
                _aim.Desynchronize();
                // 원본 CharacterMainControl.Update는 입력 차단과 상관없이 중력과 스태미나 회복을 매 프레임 진행하고, 대시 쿨다운도
                // 시간 기준이다(CA_Dash: Time.time - lastEndTime). 그래서 차단 중에도 셋을 진행한다. 달리기 토글은 모달 차단 때만 끈다.
                // 대시 쿨다운은 이동 시계 효과라 여기서 그 시계를 진행한다.
                if (_runtime.GameplayInput.IsBlocked) _runToggled = false;
                _abilities.TickClock(CharacterTags.ClockMovement, deltaTime);
                UpdateStamina(deltaTime);
                // 차단 중에는 평면 이동 없이 평소 이동과 같은 중력 규칙으로만 움직인다.
                MoveWithGravity(Vector3.zero, deltaTime);
                return;
            }

            // 조준 반동도 이동과 같은 이번 프레임 경과 시간으로 진행한다.
            UpdateAim(deltaTime);
            // 아이템을 쓰는 중에는 ADS와 달리기 입력을 무시한다. 걷기, 조준 방향, 대시는 허용하며, 대시 어빌리티가 시작하면서 사용을 취소한다.
            _aiming = !Blocked(CharacterTags.BlockAim) && _adsInput.IsPressed();
            Vector3 moveDirection = ReadMoveDirection();
            if (!_holdToRun && _sprintInput.WasPressedThisFrame()) _runToggled = !_runToggled;
            bool wantsRun = !Blocked(CharacterTags.BlockSprint) && (_holdToRun ? _sprintInput.IsPressed() : _runToggled);
            // 대시 쿨다운 효과를 대시 시작 판정 전에 진행해, 쿨다운이 끝나는 프레임에 바로 대시할 수 있게 한다.
            _abilities.TickClock(CharacterTags.ClockMovement, deltaTime);
            if (!_dashing && _dashInput.WasPressedThisFrame())
                TryStartDash(moveDirection);

            if (_dashing)
            {
                _running = false;
                // 원본 대시 곡선에 버프가 바꿀 수 있는 DashSpeed 현재값을 곱한다. 시간과 곡선은 원작 적 대시와 같은 공용 값이다.
                _dashElapsed = Mathf.Min(OriginalCharacterMotion._dashDuration, _dashElapsed + deltaTime);
                _velocity = _dashDirection * (Current(CharacterAttributes.DashSpeed)
                    * OriginalCharacterMotion._dashCurve.Evaluate(_dashElapsed / OriginalCharacterMotion._dashDuration));
            }
            else
            {
                bool hasMove = moveDirection.sqrMagnitude > 0f;
                // 달리는 중이면 스태미나가 남은 동안, 새로 달릴 때는 최대치의 20% 이상일 때만 달린다.
                _running = wantsRun && hasMove && !_aiming
                    && (_running ? Stamina > 0f : Stamina >= MaximumStamina * _minimumRunStaminaRatio);
                float speed = _running ? RunSpeed : WalkSpeed;
                float acceleration = _running ? Current(CharacterAttributes.RunAcc) : Current(CharacterAttributes.WalkAcc);
                _velocity = Vector3.MoveTowards(_velocity, moveDirection * speed, acceleration * deltaTime);
            }

            UpdateStamina(deltaTime);
            Turn(deltaTime);
            // 애니메이션과 발소리는 충돌을 거친 실제 평면 이동을 쓰므로 중력 이동 전후 위치로 잰다.
            Vector3 before = transform.position;
            MoveWithGravity(_velocity, deltaTime);
            Vector3 actualVelocity = Horizontal(transform.position - before) / deltaTime;
            // 대시 길이는 원작 적 대시와 같은 공용 값이다.
            if (_dashing && _dashElapsed >= OriginalCharacterMotion._dashDuration) FinishDash();
            UpdateAnimation(actualVelocity);
            // 땅 판정은 방금 이동한 컨트롤러 상태로 넘긴다. 주기 규칙은 컨트롤러 없이 시험할 수 있다.
            UpdateFootsteps(_controller.isGrounded, actualVelocity.magnitude, deltaTime);
        }

        // 가상 조준 위치를 캐릭터 루트보다 0.5 높은 평면에 투영해 원본처럼 평면 조준을 계산한다.
        void UpdateAim(float deltaTime)
        {
            // 원본 InputManager처럼 Look 델타에 감도/10을 곱해 조준 위치에 누적하고, 같은 값을 반동 보상에도 쓴다.
            // 실제 포인터는 동기화가 끊긴 프레임에만 조준 위치로 쓰인다.
            Vector2 look = _lookInput.ReadValue<Vector2>() * (_mouseSensitivity / _baseMouseSensitivity);
            _aim.Advance(_pointInput.ReadValue<Vector2>(), look, deltaTime);
            Vector2 aim = _aim.Position;
            // Editor와 Player 모두 표시 커서를 가상 조준 위치에 맞추어 감도와 클릭 지점이 어긋나지 않게 한다.
            if (Mouse.current != null) Mouse.current.WarpCursorPosition(aim);
            Ray ray = _camera.ScreenPointToRay(aim);
            Vector3 position = transform.position;
            var plane = new Plane(Vector3.up, position + Vector3.up * _aimPlaneHeight);
            if (!plane.Raycast(ray, out float distance)) return;
            // 원본 카메라 초점 추적과 사격이 같은 월드 조준점을 읽는다.
            AimPoint=ray.GetPoint(distance);
            Vector3 direction = Horizontal(AimPoint - position);
            if (direction.sqrMagnitude > _inputThreshold * _inputThreshold)
                _aimDirection = direction.normalized;
        }

        // 작은 입력은 버리고, 옵션에 따라 조준 방향이나 카메라를 기준으로 한 평면 이동 방향으로 바꾼다.
        // 카메라가 거의 수직으로 내려다보면 수평 앞쪽이 사라지므로 카메라 위쪽 방향을 앞으로 쓴다.
        Vector3 ReadMoveDirection()
        {
            Vector2 input = _moveInput.ReadValue<Vector2>();
            if (input.sqrMagnitude <= _inputThreshold * _inputThreshold) return Vector3.zero;
            input.Normalize();
            Vector3 forward = _moveRelativeToAim ? _aimDirection : Horizontal(_camera.transform.forward);
            if (forward.sqrMagnitude <= _inputThreshold * _inputThreshold)
                forward = Horizontal(_camera.transform.up);
            forward = forward.sqrMagnitude > 0f ? forward.normalized : _aimDirection;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return (right * input.x + forward * input.y).normalized;
        }

        // 대시 어빌리티가 쿨다운과 스태미나 비용을 보고 시작하며, 시작하면서 비용을 내고 장전과 아이템 사용을 끊는다.
        // 회복 지연은 그 순간의 StaminaRecoverTime 현재값으로 다시 건다. 방향은 시작 때 정하며, 공중에서도 이후 입력으로 바뀌지 않는다.
        void TryStartDash(Vector3 moveDirection)
        {
            if (_abilities!.TryActivateAbility(_dashAbility!) != AbilityActivationResult.Activated) return;
            // 원본 CA_Dash처럼 이동 입력이 없으면 마우스가 아니라 지금 몸이 보는 쪽으로 구른다.
            // 구르기 클립은 제자리에서 몸 앞쪽으로만 구르므로, 원본 ForceTurnTo처럼 몸을 대시 방향으로 즉시 돌려야 비스듬히 구르지 않는다.
            _dashDirection = moveDirection.sqrMagnitude > 0f ? moveDirection : Horizontal(transform.forward).normalized;
            transform.rotation = Quaternion.LookRotation(_dashDirection, Vector3.up);
            // 원본 CA_Dash.sfx다. 원작 적 대시와 같은 공용 경로를 쓴다.
            _audio.PlayOneShot(OriginalCharacterMotion._dashSound, transform.position);
            _dashElapsed = 0f;
            _dashing = true;
            _running = false;
            _recoveryRemaining = Current(CharacterAttributes.StaminaRecoverTime);
        }

        // 대시가 끝나면 원본 달리기 속도를 남긴다. 어빌리티를 정상 종료해 종료 시점부터 쿨다운 효과가 흐른다.
        void FinishDash()
        {
            _dashing = false;
            _abilities!.EndAbility(_dashAbility!);
            // 원본 CharacterRunSpeed처럼 남기는 달리기 속도도 갈증과 기아 버프가 반영된 RunSpeed와 Moveability 현재값으로 정한다.
            _velocity = _dashDirection * RunSpeed;
        }

        // 달리는 동안 소비하고, 마지막 소비 뒤 지연이 지나면 회복한다. 게임플레이가 활성인 시간만 센다.
        // 소비량, 회복 지연, 회복량은 버프가 바꿀 수 있는 GAS 어트리뷰트라 쓰는 순간에 읽는다.
        void UpdateStamina(float deltaTime)
        {
            if (_running)
            {
                SetStamina(Mathf.Max(0f, Stamina - Current(CharacterAttributes.StaminaDrainRate) * deltaTime));
                _recoveryRemaining = Current(CharacterAttributes.StaminaRecoverTime);
                return;
            }
            float recoveryTime = Mathf.Max(0f, deltaTime - _recoveryRemaining);
            _recoveryRemaining = Mathf.Max(0f, _recoveryRemaining - deltaTime);
            // 회복량 현재값에는 원본 갈증, 기아 버프의 StaminaRecoverRate 보정이 이미 들어 있다.
            SetStamina(Mathf.Min(MaximumStamina, Stamina + Current(CharacterAttributes.StaminaRecoverRate) * recoveryTime));
        }

        // 걷거나 서 있을 때는 조준 방향, 조준 없이 달릴 때는 이동 방향, 대시 중에는 시작 방향을 본다.
        void Turn(float deltaTime)
        {
            Vector3 direction = _dashing ? _dashDirection
                : _running && !_aiming && _velocity.sqrMagnitude > 0f ? _velocity.normalized : _aimDirection;
            if (direction.sqrMagnitude <= 0f) return;
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            // 원본 IsAiming은 우클릭과 상관없이 달리기와 대시가 아니면 참이라, 그동안은 AimTurnSpeed로 마우스를 본다.
            // 그래서 대시가 끝나면 원본처럼 빠르게 마우스 쪽으로 돌아온다. 원본 회전 속도처럼 갈증 버프가 낮춘 Moveability를 곱한다.
            bool facesAim = !_running && !_dashing;
            float turnSpeed = facesAim ? Current(CharacterAttributes.AimTurnSpeed) : Current(CharacterAttributes.TurnSpeed);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation,
                turnSpeed * Current(CharacterAttributes.Moveability) * deltaTime);
        }

        // 입력 억제나 차단, 창 포커스 상실, 세션 명령 대기, 순간 이동 때 이동 의도를 끊고 정지 애니메이션을 보낸다.
        // 달리기 토글은 그대로 두고, 대시를 끝내더라도 대시 종료 속도는 남기지 않는다.
        void HaltMovementIntent()
        {
            _running = false; _aiming = false;
            if (_dashing) FinishDash();
            _velocity = Vector3.zero;
            UpdateAnimation(Vector3.zero);
        }

        // 충돌을 거친 실제 평면 이동을 Animator 기준 방향으로 바꿔 파라미터만 보낸다. 뼈와 Animator 위치는 건드리지 않는다.
        void UpdateAnimation(Vector3 actualVelocity)
        {
            float speed = actualVelocity.magnitude;
            // 원본 GetMoveAnimationValue처럼 실제로 움직일 때만 달리기 값을 쓰고, 정지 시 방향은 원본 대기값 (0,1)을 보낸다.
            bool moving = speed > _inputThreshold;
            // 이미 구한 속력으로 나눠 정규화한다. moving이면 속력이 _inputThreshold보다 커서 normalized와 결과가 같고 0으로 나누지 않는다.
            Vector3 localDirection = moving ? _animator.transform.InverseTransformDirection(actualVelocity / speed) : Vector3.forward;
            // 원본처럼 이동에 쓴 지금 걷기, 달리기 속도로 나누고 기본 걷기(어트리뷰트 Base) 대비 비율을 곱한다.
            // 파라미터 해시는 이동 컨트롤러를 쓰는 모든 캐릭터가 같은 표를 읽는다.
            _animator.SetFloat(OriginalAnimatorParameters._moveSpeed, OriginalCharacter.MoveAnimationValue(speed, _running && moving, WalkSpeed,
                RunSpeed, _abilities!.GetBase(CharacterAttributes.WalkSpeed)));
            _animator.SetFloat(OriginalAnimatorParameters._moveDirX, localDirection.x);
            _animator.SetFloat(OriginalAnimatorParameters._moveDirY, localDirection.z);
            _animator.SetBool(OriginalAnimatorParameters._dashing, _dashing);
            _animator.SetBool(OriginalAnimatorParameters._gunReady, _aiming && !_dashing);
            // 입력 억제나 세션 명령 대기로 멈춘 프레임에도 공격 레이어 가중치가 끝까지 줄어들도록 여기서 함께 보낸다.
            _hands.Tick(Time.deltaTime);
        }

        // 땅에서 실제로 움직일 때만 원본 발소리를 걷기 초당 4번, 달리기 초당 7번으로 낸다. grounded는 컨트롤러의 땅 판정이다.
        void UpdateFootsteps(bool grounded, float actualSpeed, float deltaTime)
        {
            // 원본 이벤트를 가진 이동 컨트롤러는 이벤트 프레임에서만 발소리를 낸다.
            if (_animationFootsteps) return;
            // 오디오가 꺼져 있어도 관리자가 재생을 무시하므로, 발소리 주기는 이동 조건만 본다. 최소 속력은 적 발소리와 같다.
            if (!grounded || actualSpeed <= OriginalCharacterMotion._footstepMinSpeed)
            {
                _footstepElapsed = 0f;
                return;
            }
            // 원본 고정 주기 횟수에서 발소리 간격을 구한다.
            float interval = 1f / (_running ? _runFootstepRate : _walkFootstepRate);
            _footstepElapsed += deltaTime;
            if (_footstepElapsed < interval) return;
            _footstepElapsed %= interval;
            // 원본 CharacterSoundMaker처럼 조준 입력 중에도 주기는 세고 소리만 내지 않는다.
            if (InAdsInput) return;
            // 이벤트 발소리와 같은 소리와 AI 발소리 알림을 낸다.
            PostFootstep();
        }

        // 게임플레이 중 땅에서 최소 속력 이상으로 움직이고, 직전 이벤트 발소리에서 최소 간격이 지났을 때만 참이다.
        // 원본 PostFootStepSound처럼 입력 억제와 조준 입력 중에는 거짓이다.
        bool CanPostAnimationFootstep => _configured && isActiveAndEnabled && !InputSuppressed && _controller.isGrounded
            && _runtime.IsGameplayActive && !InAdsInput
            && _velocity.sqrMagnitude >= OriginalCharacterMotion._footstepMinSpeed * OriginalCharacterMotion._footstepMinSpeed
            && Time.time - _lastAnimationFootstep >= _animationFootstepInterval;

        // 애니메이션 발소리 이벤트를 받는다. 원본처럼 최소 0.08초 간격으로 낸다.
        // 오디오가 꺼져 있어도 관리자가 재생을 무시하므로 오디오 상태는 따로 보지 않는다.
        internal void PostAnimationFootstep()
        {
            if (!CanPostAnimationFootstep) return;
            _lastAnimationFootstep = Time.time;
            // 고정 주기 발소리와 같은 소리와 AI 발소리 알림을 낸다.
            PostFootstep();
        }

        // 원작 PostFootStepSound처럼 발소리 오디오 바로 뒤에 AI 발소리를 알린다. 두 발소리 경로가 이 한곳을 거친다.
        // 소리는 적 발소리와 같은 원본 경로를 공용 상수로 낸다.
        void PostFootstep()
        {
            Vector3 position = transform.position;
            _audio.PlayOneShot(OriginalCharacterMotion._footstepSound, position);
            FootstepPosted?.Invoke(position, _running);
        }

        // 확정 옵션에서 이동 관련 값만 캐시한다. 입력 맵을 켜고 끄는 일은 일시정지 소유자가 맡는다.
        void ApplyOptions(GameOptions options)
        {
            if (_holdToRun != options.HoldToRun) _runToggled = false;
            _holdToRun = options.HoldToRun;
            _moveRelativeToAim = options.MoveRelativeToAim;
            // 옵션 변경은 다음 조준 입력부터 실제 누적 배율에 반영한다.
            _mouseSensitivity = options.MouseSensitivity;
        }

        // 조준, 이동, 애니메이션 계산에 쓰도록 수직 성분을 버린다.
        static Vector3 Horizontal(Vector3 value) => new Vector3(value.x, 0f, value.z);

        // 수직 속도를 적분해 수평 속도와 함께 옮긴다. 입력 차단 중과 평소 이동이 같은 중력 규칙을 쓴다.
        // 닿은 쪽으로 움직이던 수직 속도만 비운다. 수직 속도는 0 아래로만 쌓이므로 실제로는 바닥에 닿을 때만 비운다.
        void MoveWithGravity(Vector3 horizontalVelocity, float deltaTime)
        {
            if (_controller.isGrounded && _verticalSpeed < 0f) _verticalSpeed = 0f;
            _verticalSpeed -= _gravity * deltaTime;
            CollisionFlags collision = _controller.Move((horizontalVelocity + Vector3.up * _verticalSpeed) * deltaTime);
            if ((collision & CollisionFlags.Below) != 0 && _verticalSpeed < 0f) _verticalSpeed = 0f;
            if ((collision & CollisionFlags.Above) != 0 && _verticalSpeed > 0f) _verticalSpeed = 0f;
        }

        // 씬 정리 때 불린다. 옵션 알림 구독을 풀고 구성 해제로 표시하며, 관리 상태만 바꾸므로 여러 번 불러도, 파괴된 뒤에도 안전하다.
        // 공유 입력과 오디오는 끄지 않는다. 해제된 모터는 다시 구성되지 않으므로 억제 수와 이동 상태는 비우지 않는다.
        internal void Release()
        {
            if (!_configured) return;
            _configured = false;
            // 구성 때 잡아 둔 옵션 관리자로 푼다. 앱이 먼저 파괴됐어도 관리 상태의 구독 해제라 그대로 부른다.
            _options.Applied -= ApplyOptions;
        }

        // 씬 정리를 거치지 않고 캐릭터가 파괴되는 경우의 백업으로만 Release를 부른다.
        void OnDestroy() => Release();
    }
}

