#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 LevelManager의 레벨 초기화와 종료 알림을 받는 씬 컨트롤러의 공통 수명이다. 초기화와 정리를 각각 한 번만
    // 하위 클래스에 맡긴다. 다른 알림을 받는 하위 클래스는 Awake와 OnDestroy를 재정의하고 base를 부른다. 직렬화 필드는 두지 않는다.
    public abstract class SceneLevelController : MonoBehaviour
    {
        // 레벨 초기화 알림과 Start가 둘 다 초기화를 부를 수 있어, 처음 한 번만 진행했는지 기록한다. 실패한 초기화도 다시 하지 않는다.
        bool _levelInitStarted;
        // 레벨 종료 알림과 OnDestroy가 둘 다 레벨 정리를 부를 수 있어, 정리를 이미 했는지 기록한다.
        bool _levelReleased;

        // 게임플레이 루트가 씬 로드 때부터 켜져 있어 Awake가 레벨 초기화보다 먼저 온다.
        // 초기화 알림과 씬을 내리기 직전의 레벨 종료 알림을 여기서 구독하고 OnDestroy에서 푼다.
        protected virtual void Awake()
        {
            SceneContext.LevelInitialized += InitializeLevel;
            SceneContext.LevelEnding += ReleaseLevel;
        }

        // 초기화 알림이 Start보다 먼저 끝났으면 여기서 초기화한다. 원본이 LevelManager.LevelInited를 확인하는 것과 같다.
        protected virtual void Start()
        {
            if (SceneContext.LevelInited) InitializeLevel();
        }

        // 알림 구독을 푼다. 레벨 종료 알림 없이 파괴되는 앱 종료 같은 경우를 위해 레벨 정리를 부르며, 이미 했으면 무동작이다.
        protected virtual void OnDestroy()
        {
            SceneContext.LevelInitialized -= InitializeLevel;
            SceneContext.LevelEnding -= ReleaseLevel;
            ReleaseLevel();
        }

        // SceneContext가 캐릭터와 월드를 준비한 뒤 한 번만 하위 클래스의 레벨 초기화를 부른다.
        private protected void InitializeLevel()
        {
            if (_levelInitStarted) return;
            _levelInitStarted = true;
            OnInitializeLevel();
        }

        // 원작 onStartedLoadingScene 구독자처럼 씬을 내리기 직전에 한 번만 하위 클래스의 레벨 정리를 부른다.
        private protected void ReleaseLevel()
        {
            if (_levelReleased) return;
            _levelReleased = true;
            OnReleaseLevel();
        }

        // 레벨 초기화 체인이라 실패하면 던져 초기화를 멈춘다. 초기화 루트의 catch, 알림 격리, Unity 가운데 하나가 한 번 기록한다.
        protected abstract void OnInitializeLevel();

        // 레벨 초기화가 확보한 것을 되돌린다. 단계마다 Cleanup.Run으로 계속하며 던지지 않는다.
        protected abstract void OnReleaseLevel();
    }
}
