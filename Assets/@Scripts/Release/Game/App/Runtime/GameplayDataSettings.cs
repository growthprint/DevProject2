#nullable enable
using System;
using System.Collections.Generic;
using DuckCharacter;
using Game.Core;
using Game.GAS;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 원작 Duckov.Utilities.GameplayDataSettings처럼 게임 전체가 정적 구역으로 읽는 게임플레이 데이터 묶음이다.
    // 구역과 정적 속성 이름은 원작을 따르고, 프로젝트에 대응 데이터가 없는 원작 구역은 두지 않는다.
    // 원작은 Resources로 읽지만 여기서는 GameManager 프리팹이 직렬화 필드로 참조하고 Awake에서 등록한다.
    [CreateAssetMenu(menuName = "Settings/Gameplay Data Settings")]
    public sealed class GameplayDataSettings : ScriptableObject
    {
        // 원작 PrefabsData다. 캐릭터 프리팹과, 월드가 쓰는 효과, 바닥 아이템, 시체, 무덤의 생성 규칙을 앱 수명으로 둔다.
        // 원작 이름이 있는 규칙은 원작 이름을 쓰고, 총구 섬광과 생체 피격과 사망과 탄도는 원작 대응이 없어 프로젝트 이름을 쓴다.
        [Serializable]
        public sealed class PrefabsData
        {
            [SerializeField] GameCharacterBinding? _characterPrefab = null;
            [SerializeField] SpawnRule? _bulletHitObsticleFx = null;
            // 원작 필드 순서대로 탄 피격 효과와 시체 사이에 둔다.
            [SerializeField] SpawnRule? _pickupAgentPrefab = null;
            [SerializeField] SpawnRule? _lootBoxPrefab = null;
            [SerializeField] SpawnRule? _lootBoxPrefabTomb = null;
            [SerializeField] SpawnRule? _alertFxPrefab = null;
            [SerializeField] SpawnRule? _muzzleFlash = null;
            [SerializeField] SpawnRule? _organicImpact = null;
            [SerializeField] SpawnRule? _organicDeath = null;
            [SerializeField] SpawnRule? _bulletTrail = null;
            // 원작 LevelManager의 ExplosionManager.normalFxPfb(ExplosionFx)다. 원작 PrefabsData에는 없지만 앱 수명 효과라 여기에 둔다.
            [SerializeField] SpawnRule? _explosionFx = null;
            // 원작 handheldAgentPrefab(Agent_Handheld)이다. 손에 드는 에이전트가 따로 없는 수류탄 같은 아이템의 아이콘을 오른손에 보인다.
            [SerializeField] SpawnRule? _handheldAgentPrefab = null;
            // 원작 LevelManager의 SkillInWorldHUD다. 원작 PrefabsData에는 없지만 모든 월드가 쓰는 조준 표시라 여기에 둔다.
            [SerializeField] SpawnRule? _skillHud = null;

            // 원작 characterPrefab 자리인 플레이어 캐릭터 묶음이다. 시작 검증이 연결을 확인하므로 '!'로 null 경고를 닫는다.
            public GameCharacterBinding CharacterPrefab => _characterPrefab!;
            // 총알이 벽이나 바닥에 맞을 때의 효과다.
            public SpawnRule? BulletHitObsticleFx => _bulletHitObsticleFx;
            // 원작 pickupAgentPrefab 자리인 바닥 아이템 지점이다. 가방이 줄어 넘친 아이템을 이 규칙으로 바닥에 떨군다.
            public SpawnRule? PickupAgentPrefab => _pickupAgentPrefab;
            // 적이 죽으면 남기는 루팅 시체와, 플레이어가 죽은 자리의 무덤이다. 원작 lootBoxPrefab과 lootBoxPrefab_Tomb 자리다.
            public SpawnRule? LootBoxPrefab => _lootBoxPrefab;
            public SpawnRule? LootBoxPrefab_Tomb => _lootBoxPrefabTomb;
            // 적 근접 공격 직전의 경고 섬광이다.
            public SpawnRule? AlertFxPrefab => _alertFxPrefab;
            public SpawnRule? MuzzleFlash => _muzzleFlash;
            public SpawnRule? OrganicImpact => _organicImpact;
            public SpawnRule? OrganicDeath => _organicDeath;
            public SpawnRule? BulletTrail => _bulletTrail;
            // 드럼통이 터질 때 폭발 중심에 내는 효과다.
            public SpawnRule? ExplosionFx => _explosionFx;
            public SpawnRule? HandheldAgentPrefab => _handheldAgentPrefab;
            public SpawnRule? SkillHud => _skillHud;
        }

        // 원작 ItemAssetsData다. 플레이어 캐릭터 아이템과 현금 아이템의 원본 형식 ID를 둔다.
        [Serializable]
        public sealed class ItemAssetsData
        {
            [SerializeField] int _defaultCharacterItemTypeID = 0;
            // 원작과 같은 값으로 두는 현금 아이템 ID다. 아직 읽는 기능이 없고, 은행 잔고는 현금 아이템과 따로 보관한다.
            [SerializeField] int _cashItemTypeID = 451;
            // 원작 ItemAssetsCollection처럼 앱 수명으로 두는 아이템 정의 카탈로그다. 이어하기 읽기는 씬이 카탈로그를 로드하기 전에
            // 저장 아이템을 대조해야 해서 씬 흐름이 이 참조로 먼저 로드한다. 씬의 SceneContext와 같은 에셋을 가리켜야 한다.
            [SerializeField] AssetReferenceT<OriginalGameplayCatalog> _itemCatalog = null!;

            // 플레이어 능력치를 담은 카탈로그 아이템 정의의 ID다.
            public int DefaultCharacterItemTypeID => _defaultCharacterItemTypeID;
            public int CashItemTypeID => _cashItemTypeID;
            public AssetReferenceT<OriginalGameplayCatalog> ItemCatalog => _itemCatalog;
        }

        // 원작 LayersData다. 판정 코드가 이 마스크로 충돌체를 찾는다. 초기값은 원작 에셋 값이고 레이어 번호도 원작과 같다.
        [Serializable]
        public sealed class LayersData
        {
            [SerializeField] LayerMask _damageReceiverLayerMask = 8;
            [SerializeField] LayerMask _wallLayerMask = 532544;
            [SerializeField] LayerMask _groundLayerMask = 128;
            [SerializeField] LayerMask _halfObsticleLayer = 16384;
            [SerializeField] LayerMask _fowBlockLayers = 2891776;
            [SerializeField] LayerMask _fowBlockLayersWithThermal = 794624;
            [SerializeField] LayerMask _blockBulletLayers = 8388608;

            // DamageReceiver(3)다. 총알과 근접이 캐릭터를 이 레이어로만 맞힌다.
            public LayerMask DamageReceiverLayerMask => _damageReceiverLayerMask;
            // Wall(6), Door(13), Wall_FowBlock(19)다.
            public LayerMask WallLayerMask => _wallLayerMask;
            public LayerMask GroundLayerMask => _groundLayerMask;
            public LayerMask HalfObsticleLayer => _halfObsticleLayer;
            // AI 시야를 막는 Door(13), FowBlock(18), Wall_FowBlock(19), FowDontBlockThermal(21)이다. Wall(6)은 시야를 막지 않는다.
            public LayerMask FowBlockLayers => _fowBlockLayers;
            public LayerMask FowBlockLayersWithThermal => _fowBlockLayersWithThermal;
            public LayerMask BlockBulletLayers => _blockBulletLayers;
        }

        // 원작 BuffsData다. 생존 상태에 따라 붙이고 떼는 버프를 GAS 효과로 둔다.
        [Serializable]
        public sealed class BuffsData
        {
            [SerializeField] GameplayEffectDefinition? _thirsty = null;
            [SerializeField] GameplayEffectDefinition? _starve = null;
            [SerializeField] GameplayEffectDefinition? _baseBuff = null;

            public GameplayEffectDefinition? Thirsty => _thirsty;
            // 원작 1032_Buff_Starve처럼 이동 저하와 주기 피해를 함께 맡는 기아 버프다.
            public GameplayEffectDefinition? Starve => _starve;
            public GameplayEffectDefinition? BaseBuff => _baseBuff;
        }

        // 원작 UIStyleData 자리다. 피해 숫자 모양(원작 elementDamagePopTextLook의 물리 항목)과 치명 아이콘(critPopSprite)만 둔다.
        // 원작 물리 항목은 색 흰색, 일반 크기 1, 치명 크기 1.2이고, 치명이어도 색은 그대로다.
        [Serializable]
        public sealed class UIStyleData
        {
            [SerializeField] Color _damagePopColor = Color.white;
            // 원작 critSize다. 일반 크기는 원작도 1이라 따로 두지 않는다.
            [SerializeField] float _damagePopCritSize = 1.2f;
            // 원작 critPopSprite다. 비어 있으면 원작 PopTextEntity처럼 치명 숫자도 아이콘 없이 띄운다.
            [SerializeField] Sprite? _critPopSprite = null;

            public Color DamagePopColor => _damagePopColor;
            public float DamagePopCritSize => _damagePopCritSize;
            public Sprite? CritPopSprite => _critPopSprite;
        }

        // 원작 GameplayDataSettings에는 없는 구역이다. 원작 PerkTreeManager.perkTrees, EndowmentManager.entries,
        // MapSelectionView 항목처럼 레벨마다 같은 진행 데이터를 앱 수명으로 둔다. 가져오기 도구가 원작 순서대로 채운다.
        [Serializable]
        public sealed class ProgressionData
        {
            [SerializeField] PerkTreeDefinition[] _perkTrees = Array.Empty<PerkTreeDefinition>();
            [SerializeField] EndowmentDefinition[] _endowments = Array.Empty<EndowmentDefinition>();
            [SerializeField] MapSelectionData? _mapSelection = null;

            internal IReadOnlyList<PerkTreeDefinition> PerkTrees => _perkTrees;
            // 원작 entries 순서다. 번호로 찾는 쪽은 각 정의의 Index를 본다.
            internal IReadOnlyList<EndowmentDefinition> Endowments => _endowments;
            // 제품 데이터에는 늘 연결돼 있고, 그 연결은 EditMode 저작 시험(QuestAndMapScreenAuthoringTests)이 본다. 그래서 지도 선택 화면이 null을 다루지 않는다.
            internal MapSelectionData MapSelection => _mapSelection!;
        }

        // 직렬화 필드는 원작 필드 순서를 따른다.
        [SerializeField] PrefabsData _prefabs = new PrefabsData();
        [SerializeField] ItemAssetsData _itemAssets = new ItemAssetsData();
        [SerializeField] LayersData _layers = new LayersData();
        // 원작 SceneManagementData 자리인 논리 씬 표다. 제작 도구가 ComponentAuthoring.ConfigureScene으로 채운다.
        [SerializeField] SceneTable _sceneManagement = new SceneTable();
        [SerializeField] BuffsData _buffs = new BuffsData();
        [SerializeField] UIStyleData _uiStyleData = new UIStyleData();
        [SerializeField] InputActionAsset? _inputActions = null;
        // 원작 buildingDataCollection 자리인 건물 정의 27개다. 제로존 퀘스트 과제도 건물 정의를 보므로 앱 수명으로 둔다.
        [SerializeField] BuildingDataCollection? _buildingDataCollection = null;
        // 원작 CustomFaceData 자리인 외형 파츠 라이브러리다. 새 게임 기본 외형도 이 라이브러리의 DefaultPreset이다.
        [SerializeField] DuckAvatarPartLibrary? _customFaceData = null;
        // 원작 craftingFormulas, decomposeDatabase 자리다. 크기가 작고 기지와 제로존이 함께 쓰므로 직접 참조한다.
        [SerializeField] CraftingFormulaCollection? _craftingFormulas = null;
        [SerializeField] DecomposeDatabase? _decomposeDatabase = null;
        // 원작에 없는 구역이라 원작 필드 뒤에 둔다.
        [SerializeField] ProgressionData _progression = new ProgressionData();

        static GameplayDataSettings? _default;

        // 원작 Default처럼 등록된 에셋을 읽는다. 원작과 달리 스스로 로드하지 않아, 등록을 빠뜨린 코드가 바로 드러난다.
        // GameManager.Awake가 앱 시작과 씬 코드보다 먼저 등록하므로 읽을 때 등록 여부를 검사하지 않는다.
        static GameplayDataSettings Default => _default!;

        // 프로젝트 공용 입력 에셋이다. 키 설정을 적용한 실행 사본은 GameManager.InputActions이고, 씬 코드는 그 사본을 읽는다.
        internal static InputActionAsset InputActions => Default._inputActions!;
        public static DuckAvatarPartLibrary CustomFaceData => Default._customFaceData!;
        // 원작 정적 속성 이름이다. 시작 검증이 세 연결을 확인하므로 '!'로 null 경고를 닫는다.
        public static BuildingDataCollection BuildingDataCollection => Default._buildingDataCollection!;
        public static CraftingFormulaCollection CraftingFormulas => Default._craftingFormulas!;
        public static DecomposeDatabase DecomposeDatabase => Default._decomposeDatabase!;
        public static PrefabsData Prefabs => Default._prefabs;
        public static ItemAssetsData ItemAssets => Default._itemAssets;
        public static LayersData Layers => Default._layers;
        public static SceneTable SceneManagement => Default._sceneManagement;
        public static BuffsData Buffs => Default._buffs;
        public static UIStyleData UIStyle => Default._uiStyleData;
        // 특성 트리, 재능, 지도 선택 항목이다. 소유자와 화면이 이 정의를 읽고 상태는 저장 소유자가 가진다.
        public static ProgressionData Progression => Default._progression;

        // Domain Reload를 끄면 정적 값이 Play 세션을 넘어 남으므로 세션 시작마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _default = null;

        // GameManager.Awake가 자기 프리팹이 참조한 에셋을 등록한다. 테스트와 제작 도구는 같은 경로로 직접 등록한다.
        internal static void Register(GameplayDataSettings settings)
        {
            _default = settings;
        }

        // 등록한 쪽이 사라질 때 부른다. 프리팹 반납 뒤 풀린 에셋을 가리키지 않게 하되, 다른 쪽이 등록한 값은 건드리지 않는다.
        internal static void Unregister(GameplayDataSettings? settings)
        {
            if (ReferenceEquals(_default, settings))
                _default = null;
        }

        // 앱 시작이 자원을 만들기 전에, 제작 도구가 설정을 저장한 뒤에 부르는 검증이다. 첫 결함을 한 번 보고하고 false를 돌려준다.
        // Editor와 DEBUG 빌드는 그 보고가 예외라 호출자가 다시 기록하지 않는다.
        internal static bool TryValidate()
        {
            GameplayDataSettings? settings = _default;
            if (settings == null)
                return Log.Instance.Fail(new InvalidOperationException("GameplayDataSettings가 등록되지 않았습니다."));
            return settings.TryValidateReferences();
        }

        bool TryValidateReferences()
        {
            // 씬 표는 첫 누락을 직접 보고하므로 여기서는 다시 기록하지 않는다.
            if (!_sceneManagement.TryValidate(this))
                return false;
            if (_customFaceData == null)
                return Log.Instance.Fail(new InvalidOperationException("외형 파츠 라이브러리가 연결되지 않았습니다."), this);
            if (_customFaceData.DefaultPreset == null)
                return Log.Instance.Fail(new InvalidOperationException("기본 외형이 없습니다."), this);
            if (!_customFaceData.DefaultPreset.TryValidate())
                return Log.Instance.Fail(new InvalidOperationException("기본 외형 값이 올바르지 않습니다."), this);
            if (_inputActions == null)
                return Log.Instance.Fail(new InvalidOperationException("공용 입력 액션이 연결되지 않았습니다."), this);
            if (_prefabs.CharacterPrefab == null)
                return Log.Instance.Fail(new InvalidOperationException("캐릭터 프리팹이 연결되지 않았습니다."), this);
            // 바닥 아이템도 시체와 무덤처럼 전투 월드가 런타임 지점으로 빌리는 규칙이라 함께 확인한다.
            if (_prefabs.LootBoxPrefab == null || _prefabs.LootBoxPrefab_Tomb == null || _prefabs.PickupAgentPrefab == null)
                return Log.Instance.Fail(new InvalidOperationException("시체, 무덤, 바닥 아이템 생성 규칙이 연결되지 않았습니다."), this);
            if (_buffs.Thirsty == null || _buffs.Starve == null || _buffs.BaseBuff == null)
                return Log.Instance.Fail(new InvalidOperationException("생존 버프 효과가 연결되지 않았습니다."), this);
            // 정적 속성이 '!'로 읽으므로 여기서 막는다. 원작 건물·제조·분해 가져오기 메뉴가 연결한다.
            if (_buildingDataCollection == null || _craftingFormulas == null || _decomposeDatabase == null)
                return Log.Instance.Fail(new InvalidOperationException("건물, 제조식, 분해식 데이터가 연결되지 않았습니다."), this);
            return true;
        }
    }
}
