# DevProject: Escape From Duckov 모작 (Unity 6)

*Escape From Duckov*의 6개 씬(메인 메뉴, 프롤로그, 커스터마이즈, 튜토리얼, 기지, Ground Zero)을 Unity 6로 재현한 개인 프로젝트의 **코드 스냅숏**입니다(2026-10-23).

> 비상업 · 학습 · 포트폴리오용 재현입니다. 원작의 에셋과 IP는 원저작자에게 있으며, 이 저장소에는 원작 에셋이 없습니다.

## 저장소 구성

- 런타임 C# 코드(`Assets/@Scripts/Release`)와 GAS 프레임워크(`Assets/@Scripts/GAS/Runtime`), asmdef, 패키지 목록만 들어 있습니다.
- 씬, 프리팹, 셰이더, 에셋, 테스트, 에디터 도구는 포함하지 않아 이 저장소만으로는 빌드되지 않습니다.

## 어셈블리 구조

| 어셈블리 | 역할 |
|---|---|
| `Game.App` | 게임 본체: 부트스트랩, 세션, 씬, 게임플레이, UI |
| `Game.GAS` | 게임을 모르는 Gameplay Ability System 프레임워크 |
| `Game.Core` | Addressables, 저장소, 비동기 작업, 스포너 같은 엔진 연결 서비스 |
| `Game.Rendering` | URP 렌더러 기능과 볼륨 |
| `DevProject.Serialization` | JSON 직렬화 공용 설정 |

의존 방향은 `Game.App` → `Game.GAS` → `Game.Core` 한 방향입니다.

## 주요 구현과 읽을 곳

- **GAS (태그, 효과, 어빌리티)**: [GameplayTag.cs](Assets/@Scripts/GAS/Runtime/Tags/GameplayTag.cs), [ActiveEffectContainer.cs](Assets/@Scripts/GAS/Runtime/Effects/ActiveEffectContainer.cs)
- **저장 (스레드 풀 직렬화, 슬롯 잠금, 원자적 교체)**: [GameSaveSlotFiles.cs](Assets/@Scripts/Release/Game/App/Session/Saves/GameSaveSlotFiles.cs), [LocalFileStorage.cs](Assets/@Scripts/Release/Game/Core/Storage/LocalFileStorage.cs)
- **레벨 초기화와 오브젝트 풀**: [SceneContext.cs](Assets/@Scripts/Release/Game/App/Scenes/SceneContext.cs), [Spawner.cs](Assets/@Scripts/Release/Game/Core/Spawning/Spawner.cs)
- **URP 17 Render Graph 이식**: [OriginalHbaoRendererFeature.cs](Assets/@Scripts/Release/Game/Rendering/OriginalHbaoRendererFeature.cs), [OriginalLayerLightFeature.cs](Assets/@Scripts/Release/Game/Rendering/OriginalLayerLightFeature.cs)
- **GPU 잔디 굽기 (AsyncGPUReadback 묶음)**: [OriginalGrassBakeBatch.cs](Assets/@Scripts/Release/Game/App/Gameplay/Grass/OriginalGrassBakeBatch.cs)
- **Burst 애니메이션 잡**: [OriginalMagicBlend.cs](Assets/@Scripts/Release/Game/App/Gameplay/OriginalMagicBlend.cs)

## 사용 기술

Unity 6 · C# · URP 17 (Render Graph) · UniTask · Addressables · Localization · Input System · Burst · Playables · Cinemachine · AI Navigation · Timeline · uGUI · TextMeshPro · FMOD · Newtonsoft JSON
