#nullable enable annotations
using Game.Core;
using Game.GAS;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Game.App
{
    // 프롤로그와 캠페인 월드가 함께 쓰는 플레이어 준비다. 이동 모터, 플레이어 GAS 시스템의 아바타 연결, 같은 루트의 지도 연결,
    // 원본 게임 카메라, 움직임 감소 옵션 구독을 이 순서로 확보하고 Release가 역순으로 되돌린다. 씬 컨트롤러가 레벨 초기화 때 만들고
    // LateUpdate에서 Tick하며, OnDestroy에서 Release한다.
    internal sealed class OriginalPlayerRig
    {
        // 같은 루트의 지도 컨텍스트를 찾고, 정리 실패를 기록할 때 문맥으로 쓰는 씬 컨트롤러다.
        readonly Component _owner;
        readonly GameRuntime _runtime;
        // 원본 GameCamera의 방아쇠 입력(조준 오프셋 보간 속도)을 읽는 Player/Attack 액션이다. 켜고 끄는 것은 앱이 맡는다.
        readonly InputAction _attack = null!;
        // 생성자가 확보하는 순서대로 채운다. 생성 도중 실패하면 채워진 것만 Release가 되돌린다.
        readonly OriginalPlayerMotor _motor = null!;
        // 생존 수치 소유자의 플레이어 GAS 시스템에 연결한 아바타다. 연결이 끝난 뒤에만 채워, Release가 그때만 연결을 푼다.
        readonly GameObject? _avatar;
        readonly SceneMapContext? _map;
        readonly OriginalGameCamera _gameCamera = null!;
        // 움직임 감소 옵션 알림을 구독한 앱의 옵션 관리자다. Release는 앱이 먼저 사라졌을 수 있어 정적 속성 대신 이 참조로 푼다.
        readonly GameOptionsManager? _options;
        bool _released;

        // 씬 컨트롤러의 직렬화 참조와 준비 문맥으로 레벨 초기화 때 만든다. 확보 도중 예외가 나면 확보한 것을 되돌린 뒤 예외를
        // 그대로 넘겨 레벨 초기화를 멈춘다. 그래서 호출자는 완성된 객체만 받는다. playerCharacter는 플레이어 GAS 스탯을 채울 원본 정의다.
        // 입력 실행 사본, 오디오 관리자, 옵션 관리자는 씬 컨트롤러가 레벨 초기화 때 앱에서 한 번 읽어 넘긴다.
        internal OriginalPlayerRig(Component owner, SceneGameplayContext context, Camera camera, RuntimeAnimatorController locomotion,
            Volume? focusVolume, OriginalItemDefinition playerCharacter, InputActionAsset actions, GameAudioManager audio, GameOptionsManager options)
        {
            _owner = owner;
            _runtime = context.Runtime;
            bool prepared = false;
            try
            {
                // 입력은 저장한 키 설정을 따르는 앱의 입력 실행 사본에서 찾고, 오디오는 앱 오디오 관리자로 낸다.
                OriginalPlayerMotor motor = OriginalPlayerMotor.Attach(context.Character, locomotion, _runtime, actions, camera, audio, options);
                _motor = motor;
                // 모터가 첫 Update 전에 어트리뷰트를 읽도록 바로 아바타를 연결한다. 스태미나는 월드에 들어와
                // 연결할 때마다 최대치로 시작한다. 연결이 끝나면 먼저 기록해, 모터 Bind가 실패해도 Release가 연결을 푼다.
                GameObject avatar = motor.gameObject;
                PlayerVitals vitals = _runtime.Vitals;
                AbilitySystem abilities = vitals.BindAvatar(avatar, playerCharacter);
                _avatar = avatar;
                // 대시는 아바타 연결이 준 어빌리티라 같은 묶음의 정의로 모터가 시작하고 끝낸다.
                motor.Bind(abilities, vitals.AbilitySet.Dash!);
                // 같은 루트의 지도 컨텍스트는 컴포넌트 준비 순서에 기대지 않도록 모터를 만든 직후 여기서 직접 연결한다.
                // 지도 블러도 모터와 게임 카메라가 쓰는 같은 월드 카메라에 건다.
                if (owner.TryGetComponent(out SceneMapContext map))
                {
                    _map = map;
                    map.Bind(motor.transform, motor, camera);
                }
                // 방아쇠 입력도 모터와 같은 입력 실행 사본에서 찾아 재바인딩을 함께 따른다. 경로는 공용 상수다.
                _attack = actions.FindAction(GameInputPaths._playerAttack, true);
                // 게임 카메라는 캐릭터 위 0.7m의 VolumeProxy, 공간 Volume 카메라 암, 초점을 맡고 첫 Tick에서 암 위치로 바로 놓인다.
                // 초점 Volume이 없으면 초점과 그 Volume의 블렌드 가중치를, 프로필에 DoF가 없으면 초점 거리만 건너뛰고 준비는 그대로 끝난다.
                _gameCamera = new OriginalGameCamera(camera, motor.transform, focusVolume);
                // 움직임 감소 옵션은 확정값을 한 번 적용하고 이후 적용 알림으로 갱신한다. 매 프레임 옵션 사본을 만들지 않기 위해서다.
                // 옵션 관리자는 구독 전에 필드에 잡아 두어 Release가 같은 관리자에서 푼다.
                _options = options;
                ApplyOptions(options.Current);
                options.Applied += ApplyOptions;
                prepared = true;
            }
            finally
            {
                if (!prepared) Release();
            }
        }

        // 씬 컨트롤러와 전투 월드가 읽는 이동 모터와 게임 카메라다. 생성에 성공한 객체만 쓰이므로 늘 채워져 있다.
        internal OriginalPlayerMotor Motor => _motor;
        internal OriginalGameCamera GameCamera => _gameCamera;

        // 플레이어 이동 뒤 같은 프레임에 게임 카메라를 진행한다. 입력 활성은 원본 InputManager.InputActived처럼 Runtime이
        // 게임플레이 입력을 받고, 창에 포커스가 있고, 대사가 닫혀 있을 때다. 모터가 먼저 파괴되었으면 진행하지 않는다.
        // ADS는 원본 GameCamera가 입력 대신 CharacterMainControl.IsInAdsInput을 읽듯 모터의 조준 상태를 쓴다.
        // 그래서 아이템 사용이나 입력 억제로 모터가 조준을 끊으면 카메라 ADS도 함께 풀린다.
        internal void Tick(bool dialogueOpen)
        {
            if (_motor == null) return;
            bool inputActive = _runtime.AcceptsGameplayInput && Application.isFocused && !dialogueOpen;
            _gameCamera.Tick(new OriginalCameraInput(_motor.AimScreenPosition, ads: inputActive && _motor.IsAiming,
                trigger: inputActive && _attack.IsPressed(), inputActive: inputActive));
        }

        // 원본 멀미 방지 옵션(움직임 감소)을 게임 카메라에 전달한다. 게임 카메라를 만든 뒤에만 구독한다.
        void ApplyOptions(GameOptions options) => _gameCamera.ReduceMotion = options.ReduceCameraMotion;

        // 생성자가 확보한 것을 옵션 구독, 게임 카메라, 지도, 아바타 연결, 모터 순으로 되돌리며 여러 번 불러도 된다.
        // 단계마다 Cleanup.Run이 실패를 기록하고 나머지를 계속하므로 던지지 않는다.
        // 지도와 모터 해제는 관리 상태만 바꾸므로 이미 파괴된 컴포넌트에도 참조 검사로 부른다.
        internal void Release()
        {
            if (_released) return;
            _released = true;
            // 생성 도중 실패했으면 구독 전이라 옵션 관리자가 없다. 파괴된 관리자도 C# 이벤트가 이 핸들러를 들고 있으므로
            // Unity null 비교 대신 참조만 보고 푼다.
            GameOptionsManager? options = _options;
            if (!ReferenceEquals(options, null)) Cleanup.Run(() => options.Applied -= ApplyOptions, _owner);
            if (_gameCamera != null) Cleanup.Run(_gameCamera.Dispose, _owner);
            if (!ReferenceEquals(_map, null)) Cleanup.Run(_map.Release, _owner);
            // 씬보다 오래 사는 GAS 시스템에 레벨 효과가 남지 않도록 모터보다 먼저 연결을 푼다. 아바타가 먼저 파괴됐어도
            // 같은 참조로 푼다. 생존 수치 소유자가 먼저 파괴됐으면 시스템도 함께 해제됐으므로 건너뛴다.
            GameObject? avatar = _avatar;
            PlayerVitals vitals = _runtime.Vitals;
            if (!ReferenceEquals(avatar, null) && vitals != null) Cleanup.Run(() => vitals.UnbindAvatar(avatar), _owner);
            if (!ReferenceEquals(_motor, null)) Cleanup.Run(_motor.Release, _owner);
        }
    }
}
