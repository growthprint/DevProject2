#nullable enable annotations
using Game.App.World;

namespace Game.App
{
    // 한 씬의 게임플레이 준비에 필요한 앱 의존성과 그 씬이 만든 캐릭터, 월드를 한 값으로 묶는다.
    // SceneContext가 레벨 초기화 때 만들고, 씬 컨트롤러가 레벨 초기화 처리에서 읽는다. 씬 수명 밖에 보관하지 않는다.
    public sealed class SceneGameplayContext
    {
        // 앱 의존성은 복사하지 않고 Runtime의 앱 수명 값을 그대로 읽는다.
        readonly GameRuntime _runtime;

        // SceneContext만 만들고 레벨 초기화가 확인하거나 만든 값만 넘긴다. 카탈로그 키가 없는 씬은 catalog가,
        // 펫 프리팹이 없는 씬은 pet이 null이다.
        internal SceneGameplayContext(GameRuntime runtime, GameCharacterBinding character, WorldObjectService world,
            OriginalGameplayCatalog? catalog, PetCharacter? pet)
        {
            _runtime = runtime;
            Character = character;
            World = world;
            Catalog = catalog;
            Pet = pet;
        }

        // 명령과 상태 알림을 맡는 앱 Runtime이다. 오디오, 입력 사본, 옵션 같은 앱 시스템은 GameManager가 내준다.
        public GameRuntime Runtime => _runtime;
        // 앱 수명 공용 HUD다. 한 번에 한 씬 컨트롤러만 표시를 붙이고, 붙인 동안에만 바꾼다.
        public SharedHudView Hud => _runtime.Hud;
        // 앱 수명 GameplayUI다. 공용 HUD와 같은 규칙으로 표시를 붙인 동안에만 바꾼다.
        public OriginalGameplayView GameplayView => _runtime.GameplayView;
        // 이 씬이 만든 플레이어 연결부다.
        public GameCharacterBinding Character { get; }
        // 이 씬에서 월드 객체를 등록하고 생성하는 진입점이다.
        public WorldObjectService World { get; }
        // 이 씬의 서랍에 로드한 원본 게임플레이 카탈로그다. 씬에 카탈로그 키가 없으면 null이다.
        public OriginalGameplayCatalog? Catalog { get; }
        // 원작 LevelManager.PetCharacter처럼 이 씬이 만든 펫이다. 씬 컨트롤러가 묶고 풀며, 개집이 대기를 정한다. 없으면 null이다.
        public PetCharacter? Pet { get; }
    }
}
