using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // Bootstrap 씬의 유일한 진입점이라 중복 부착을 막는다. Addressables와 Localization 초기화, GameManager.CreateAsync로 앱 만들기,
    // 저장 관리자에 저장 위치 넣기, Runtime에 오디오 사용 여부 넘기기만 맡는다. 원작처럼 실패 화면 없이 초기화 실패를 한 번 기록만 한다.
    // 게임플레이 데이터는 GameManager 프리팹과 함께 오므로 GameRuntime이 구성할 때 검증한다.
    // 종료 신호와 GameManager 파괴는 같은 호스트의 GameShutdown이, 앱 자원과 Runtime 시작은 GameRuntime이 맡는다.
    // sealed가 아닌 것은 테스트 하위 클래스 TestGameBootstrap이 저장 구성만 대역 저장소로 바꾸기 때문이다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameShutdown))]
    public class GameBootstrap : MonoBehaviour
    {
        // 제품 Bootstrap 씬이 앱 오디오를 켠다. AddComponent로 만든 테스트 호스트도 제품처럼 오디오를 켜도록 C# 기본값을 true로 둔다.
        [SerializeField] bool _enableAudio = true;

        // 초기화를 한 번만 시작한다. 원작처럼 재시도나 실패 화면 없이, 실패는 이 처리기가 Log로 한 번 기록한다.
        // 처리기가 없으면 UniTask 기본 처리기가 Unity 로그로 바로 남겨 Log를 거치지 않는다.
        void Start()
        {
            InitializeAsync(Application.exitCancellationToken).Forget(exception => Log.Instance.Exception(exception, this));
        }

        // Addressables와 Localization 초기화, GameManager 생성, 저장 구성, Begin 순서로 진행한다. Begin은 오디오 사용 여부만
        // Runtime에 넘기고, 게임플레이 데이터 검증은 GameRuntime 구성이 맡는다.
        // GameManager를 만들기 전에만 대기가 있고, 대기 중에 앱 종료가 시작되면 token이 대기를 취소해 아무것도 만들지 않는다.
        // Bootstrap 호스트는 앱 종료 때만 파괴되므로 호스트 파괴는 따로 보지 않는다.
        // 만든 뒤의 종료와 정리는 GameShutdown이, 시작 실패의 정리는 GameRuntime이 스스로 한다.
        async UniTask InitializeAsync(CancellationToken token)
        {
            try
            {
                // 실패 원인은 초기화기가 이미 기록했으므로 고정 문구 예외로 바꿔 넘긴다.
                if (!await AddressablesInitializer.InitializeAsync(token))
                    throw new InvalidOperationException("Addressables 초기화에 실패했습니다.");

                // 메뉴와 제품 UI를 만들기 전에 지원 로캘과 번역 테이블을 준비한다.
                await GameLocalization.InitializeAsync(token);

                // 앱은 여기서만 만든다. 프리팹 동기 로드는 메인 스레드를 멈추므로 CreateAsync가 로드를 기다린 뒤 같은 호출에서 생성한다.
                // 생성 중에 루트의 Awake가 Live를 잡고 DontDestroyOnLoad를 건다. 로드 실패는 Start의 처리기가, 로드 중 앱 종료는 아래 catch가 한 번 기록한다.
                GameManager manager = await GameManager.CreateAsync(token);

                // 저장 관리자는 GameManager 프리팹 자식이라 앱을 만든 뒤에, Runtime이 시작해 저장소를 읽기 전에 구성한다.
                ConfigureSaves(GameManager.Saves);

                // Runtime은 Begin을 받아야 자기 Start에서 시작하므로 생성과 Begin 사이에 대기를 두지 않는다.
                // 저장소는 저장 관리자가 이미 받았으므로 오디오 사용 여부만 넘긴다.
                // UI는 GameManager 프리팹 안에 중첩돼 루트와 함께 DontDestroyOnLoad로 남으므로, 둘 씬을 따로 넘기지 않는다.
                manager.Runtime.Begin(_enableAudio);
            }
            // 예외 경계: 앱 종료로 멈춘 시작도 한 번 기록한다. 취소는 여기서 받아 끝내고, 그 밖의 시작 실패는 Start의 처리기가 기록한다.
            catch (OperationCanceledException exception)
            {
                Log.Instance.Exception(exception, this);
            }
        }

        // 원작 SavesSystem처럼 사용자 영구 저장 폴더의 슬롯 파일과 PlayerPrefs 슬롯 선택을 쓴다.
        // 테스트 하위 클래스는 사용자 파일과 PlayerPrefs를 건드리지 않도록 대역 저장소로 재정의한다.
        private protected virtual void ConfigureSaves(SaveManager saves) => saves.ConfigureProduct(Application.persistentDataPath);
    }
}
