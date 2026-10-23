#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 수치를 원본 키 그대로 보관한다. 그래야 무기마다 다른 값을 임의의 공통 기본값으로 대신하지 않는다.
    [Serializable]
    public sealed class OriginalItemStat
    {
        [SerializeField] string _key = "";
        [SerializeField] float _value;
        // 원작 Stat.display다. 아이템 상세 화면은 이 플래그가 켜진 수치만 원본 순서로 보인다.
        [SerializeField] bool _display;
        public string Key => _key;
        public float Value => _value;
        internal bool Display => _display;
    }

    // 원작 ModifierTarget이다. Parent는 꽂힌 칸의 주인 아이템, Character는 그 아이템을 가진 캐릭터를 바꾼다.
    public enum OriginalModifierTarget { Self, Parent, Character }

    // 원작 Polarity다. 아이템 상세 화면이 modifier 값을 좋은 쪽, 나쁜 쪽 색으로 칠할 때 쓴다.
    public enum OriginalStatPolarity { Negative = -1, Neutral = 0, Positive = 1 }

    // 원작 ModifierDescription 하나다. 연산 값은 원작 ModifierType과 같은 GAS AttributeModifierOp(0, 100, 200)로 둔다.
    [Serializable]
    public sealed class OriginalItemModifier
    {
        [SerializeField] OriginalModifierTarget _target = OriginalModifierTarget.Parent;
        // 원작 enableInInventory다. 꽂히지 않고 가방에 있어도 효과를 내는지이며, 총 부착물은 모두 꺼져 있다.
        [SerializeField] bool _enableInInventory;
        [SerializeField] string _key = "";
        [SerializeField] bool _display;
        [SerializeField] AttributeModifierOp _type;
        [SerializeField] float _value;
        [SerializeField] bool _overrideOrder;
        [SerializeField] int _order;
        internal OriginalModifierTarget Target => _target;
        internal bool EnableInInventory => _enableInInventory;
        internal string Key => _key;
        internal bool Display => _display;
        internal AttributeModifierOp Type => _type;
        internal float Value => _value;
        // 원작 Modifier.Order다. 순서를 덮어쓰지 않으면 연산 값이 곧 적용 순서다.
        internal int Order => _overrideOrder ? _order : (int)_type;
    }

    // 원작 Slot 하나다. 칸 키, 받는 태그(requireTags 모두), 막는 태그(excludeTags 하나라도), 표시 이름 키를 둔다.
    // 같은 종류 금지(forbidItemsWithSameID)는 원작 캐릭터 토템 칸에만 있어 아이템 칸 데이터에는 두지 않는다.
    [Serializable]
    public sealed class OriginalItemSlot
    {
        [SerializeField] string _key = "";
        [SerializeField] string[] _requireTags = Array.Empty<string>();
        [SerializeField] string[] _excludeTags = Array.Empty<string>();
        // 원작 overrideDisplayNameKey다. 비어 있으면 원작 Slot.DisplayName처럼 첫 requireTag의 Tag_ 키를 쓴다.
        [SerializeField] string _nameKey = "";
        // 원작 Slot.slotIcon이다. 빈 칸에만 보이고, 총 부착 칸은 대부분 아이콘이 없다.
        [SerializeField] Sprite? _icon;
        internal string Key => _key;
        internal Sprite? Icon => _icon;
        // 표시 이름 키다. 받는 태그도 덮어쓴 키도 없으면 원작처럼 "?"를 보이도록 빈 문자열이다.
        // 태그 이름 키는 상세 화면 태그 줄과 같은 규칙을 쓰도록 GameLocalization이 만든다.
        internal string LabelKey => _nameKey.Length > 0 ? _nameKey : _requireTags.Length > 0 ? GameLocalization.TagNameKey(_requireTags[0]) : "";

        // 원작 TagCollection.Check다. 받는 태그를 모두 갖고 막는 태그를 하나도 갖지 않아야 한다. 태그는 원본 Tag 자산 이름으로 비교한다.
        internal bool Accepts(OriginalItemDefinition item)
        {
            foreach (string tag in _requireTags)
                if (!item.HasTag(tag)) return false;
            foreach (string tag in _excludeTags)
                if (item.HasTag(tag)) return false;
            return true;
        }
    }

    // 원작 상수(Constants) 가운데 display가 켜진 항목이다. 상세 화면이 Var_ 키 이름과 원작 GetValueDisplayString 문자열을 보인다.
    // 원작 문자열 상수는 ToPlainText로 번역하므로(구경 SMG는 "S") 그런 값은 번역 키로 두고 _localized를 켠다.
    [Serializable]
    public sealed class OriginalItemConstant
    {
        [SerializeField] string _key = "";
        [SerializeField] string _text = "";
        [SerializeField] bool _localized;
        internal string Key => _key;
        internal string Text => _text;
        internal bool Localized => _localized;
    }

    // 원작 Tag 자산의 표시 정보다. 상세 화면 태그 줄은 show가 켜진 태그만 그 배경색으로 보인다.
    [Serializable]
    public sealed class OriginalTagInfo
    {
        [SerializeField] string _name = "";
        [SerializeField] bool _show;
        [SerializeField] Color _color = Color.black;
        internal string Name => _name;
        internal bool Show => _show;
        internal Color Color => _color;
    }

    // 원작 StatInfoDatabase 항목이다. 수치 표시 형식과 좋은 방향을 키마다 둔다. 없는 키는 원작처럼 "0.##"과 Neutral이다.
    [Serializable]
    public sealed class OriginalStatInfo
    {
        [SerializeField] string _key = "";
        [SerializeField] OriginalStatPolarity _polarity;
        [SerializeField] string _format = "";
        internal string Key => _key;
        internal OriginalStatPolarity Polarity => _polarity;
        internal string Format => _format.Length > 0 ? _format : OriginalGameplayCatalog._defaultStatFormat;
    }

    // 원본 아이템 정의다. 프로젝트의 장비 표시 ID와는 따로 관리한다.
    [Serializable]
    public sealed class OriginalItemDefinition
    {
        [SerializeField] int _id;
        [SerializeField] string _nameKey = "";
        // 원본 아이템 프리팹 GUID다. Editor의 ComponentAuthoring과 OriginalGameplayContentBuilder만 직렬화로 읽고 쓰며
        // 런타임 C#은 읽지 않으므로, 이 필드에서만 CS0414 경고를 끈다.
#pragma warning disable CS0414
        [SerializeField] string _sourceGuid = "";
#pragma warning restore CS0414
        [SerializeField] string _caliber = "";
        [SerializeField] string _slot = "";
        [SerializeField] int _defaultCount = 1;
        [SerializeField] int _maxStack = 1;
        [SerializeField] int _containerCapacity = -1;
        [SerializeField] float _weight;
        [SerializeField] float _durability;
        [SerializeField] float _useTime;
        [SerializeField] float _heal;
        [SerializeField] float _energy;
        [SerializeField] float _water;
        [SerializeField] bool _gun;
        [SerializeField] bool _melee;
        [SerializeField] bool _bullet;
        [SerializeField] bool _automatic;
        // 원작 ItemSetting_Gun의 reloadMode가 singleBullet(1)인지와 autoReload다. singleBullet 총(산탄총)은 한 번에 1발씩 가득 찰 때까지
        // 장전하고, autoReload 총(활)은 마지막 탄을 쏜 직후 장전한다. 적 AI 장전이 읽는다.
        [SerializeField] bool _singleBulletReload;
        [SerializeField] bool _autoReload;
        // 원본 사망 보호 태그를 별도로 보존한다.
        [SerializeField] bool _retainedOnDeath;
        // 원본 Item.Sticky가 보는 'Sticky' 태그다. 인스턴스 값이 아니라 아이템 정의의 태그라 정의 플래그로 보존한다.
        [SerializeField] bool _sticky;
        [SerializeField] string _shootKey = "";
        [SerializeField] string _reloadKey = "";
        [SerializeField] Sprite? _icon;
        [SerializeField] GameObject? _visual;
        [SerializeField] OriginalItemStat[] _stats = Array.Empty<OriginalItemStat>();
        // 캐릭터 칸에 꽂혀 있는 동안 플레이어에게 붙는 장비 효과다. 원작 modifier 중 Character 대상만 담고, 없으면 null이다.
        // Editor 제작기가 GE_Equip_<원본 ID> 에셋을 만들어 연결한다.
        [SerializeField] GameplayEffectDefinition? _equipEffect;
        // 몸에 보이는 장비 외형 ID(DuckEquipParts)다. 외형이 없는 아이템은 -1이며, 이 값으로 외형을 찾지 않는다.
        [SerializeField] int _equipmentId = -1;
        // 원작 Inventory.Sort의 정렬 키다. 첫 태그 이름과 그 Tag 자산의 priority, 아이템 order를 원본 프리팹에서 그대로 옮긴다.
        // 태그가 없으면 원작 GetFirstTag의 null처럼 빈 이름과 priority -1이다.
        [SerializeField] string _firstTag = "";
        [SerializeField] int _firstTagPriority = -1;
        [SerializeField] int _order;
        // 원작 ItemMetaData의 tags와 quality다. 무작위 상자(원작 LootBoxLoader)가 태그와 품질로 아이템을 찾는다.
        // 태그는 원본 Tag 자산 이름을 원본 순서로 둔다.
        [SerializeField] string[] _tags = Array.Empty<string>();
        [SerializeField] int _quality;
        // 원작 Item.value(가격)와 상수 RepairLossRatio다. 수리 가격과 수리 손실 계산이 읽는다.
        // 쓰는 곳은 카탈로그 전체 제작(WriteItemDefinition) 하나뿐이라, 다른 제작 메뉴가 이 값을 덮지 않는다.
        // 원작 코드는 상수가 없으면 0.14를 쓰므로 초기값도 0.14다.
        [SerializeField] int _value;
        [SerializeField] float _repairLossRatio = 0.14f;
        // 원작 SlotCollection의 칸 목록이다. 총 부착 칸처럼 아이템 안에 다른 아이템을 꽂는 칸이며, 캐릭터 칸은 OriginalCharacterSlots가 맡는다.
        [SerializeField] OriginalItemSlot[] _itemSlots = Array.Empty<OriginalItemSlot>();
        // 원작 ModifierDescriptionCollection이다. 부착물의 Parent modifier는 꽂힌 총의 수치를 바꾸고, 상세 화면은 display가 켜진 것을 보인다.
        [SerializeField] OriginalItemModifier[] _modifiers = Array.Empty<OriginalItemModifier>();
        // 상세 화면이 보이는 원작 상수다. 구경처럼 실행 규칙이 쓰는 값은 따로 둔 필드가 맡는다.
        [SerializeField] OriginalItemConstant[] _constants = Array.Empty<OriginalItemConstant>();
        public int Id => _id;
        public string NameKey => _nameKey;
        public string Caliber => _caliber;
        public string Slot => _slot;
        public int DefaultCount => _defaultCount;
        // 컨테이너 용량은 공개하지 않고 CreateItem만 읽는다.
        public int MaxStack => _maxStack;
        public float Weight => _weight;
        public float Durability => _durability;
        public float UseTime => _useTime;
        public float Heal => _heal;
        public float Energy => _energy;
        public float Water => _water;
        public bool IsGun => _gun;
        public bool IsMelee => _melee;
        public bool IsBullet => _bullet;
        public bool Automatic => _automatic;
        // 적 AI 장전이 읽는 장전 방식이다.
        internal bool SingleBulletReload => _singleBulletReload;
        internal bool AutoReload => _autoReload;
        public bool RetainedOnDeath => _retainedOnDeath;
        // 원본처럼 사망 때 장착 여부와 상관없이 플레이어에게 남는다.
        public bool Sticky => _sticky;
        public string ShootKey => _shootKey;
        public string ReloadKey => _reloadKey;
        public Sprite? Icon => _icon;
        public GameObject? Visual => _visual;
        public GameplayEffectDefinition? EquipEffect => _equipEffect;
        public int EquipmentId => _equipmentId;
        public string FirstTag => _firstTag;
        public int FirstTagPriority => _firstTagPriority;
        public int Order => _order;
        internal IReadOnlyList<string> Tags => _tags;
        internal int Quality => _quality;

        // 원작 Tag 자산 이름으로 태그 하나를 가졌는지 본다. 상점, 수리, 제조, 열쇠, 소지품 규칙과 칸 판정이 같은 서수 비교를 쓴다.
        // 태그 목록은 짧아 할당 없이 훑는다. 화면이 다시 그릴 때마다 불릴 수 있다.
        internal bool HasTag(string tag) => Array.IndexOf(_tags, tag) >= 0;
        // 수리 규칙과 상점 가격이 읽는 정의 값이다.
        public int Value => _value;
        public float RepairLossRatio => _repairLossRatio;
        internal IReadOnlyList<OriginalItemSlot> ItemSlots => _itemSlots;
        internal IReadOnlyList<OriginalItemModifier> Modifiers => _modifiers;
        internal IReadOnlyList<OriginalItemConstant> Constants => _constants;
        internal IReadOnlyList<OriginalItemStat> Stats => _stats;
        // 원작 Item.Description의 번역 키다. 원작은 표시 이름 키 뒤에 _Desc를 붙이고, 그 규칙은 GameLocalization 한곳에 있다.
        internal string DescriptionKey => GameLocalization.DescriptionKey(_nameKey);

        // 아이템 칸 키를 원본 순서로 둔 목록이다. 인벤토리 작업 사본이 카탈로그 형식 없이 칸을 고르도록 처음 쓸 때 한 번 만든다.
        internal IReadOnlyList<string> ItemSlotKeys
        {
            get
            {
                if (_itemSlotKeys == null)
                {
                    _itemSlotKeys = new string[_itemSlots.Length];
                    for (int index = 0; index < _itemSlots.Length; index++) _itemSlotKeys[index] = _itemSlots[index].Key;
                }
                return _itemSlotKeys;
            }
        }
        [NonSerialized] string[]? _itemSlotKeys;

        // 그 키의 아이템 칸을 찾는다. 원본 칸 키는 아이템 안에서 겹치지 않는다.
        internal bool TryGetItemSlot(string key, [NotNullWhen(true)] out OriginalItemSlot? slot)
        {
            for (int index = 0; index < _itemSlots.Length; index++)
                if (_itemSlots[index].Key == key)
                {
                    slot = _itemSlots[index];
                    return true;
                }
            slot = null;
            return false;
        }
        // 원작 ItemFilter처럼 태그 하나를 갖고 제외 태그는 하나도 갖지 않는지 본다. 태그 비교는 원본 Tag 자산 이름으로 한다.
        // 원작은 빈 태그 목록에서 null 태그를 뽑으면 태그 조건을 건너뛰므로, 빈 이름은 어떤 아이템이든 맞는다.
        internal bool Matches(string requireTag, HashSet<string> excludeTags)
        {
            bool required = requireTag.Length == 0;
            foreach (string tag in _tags)
            {
                if (excludeTags.Contains(tag)) return false;
                if (tag == requireTag) required = true;
            }
            return required;
        }
        // 원본 공격 속도인 ShootSpeed와 AttackSpeed의 하한이다. AttackInterval과 플레이어 반동 시간이 같은 하한을 쓴다.
        // 반동 시간은 수치가 없을 때의 기본값이 달라 AttackInterval을 쓰지 못하고 이 값을 직접 읽으므로 internal로 둔다.
        internal const float _minimumAttackSpeed = .1f;
        // 근접 무기에 AttackRange 수치가 없을 때의 원본 기본 사거리다. 사거리 규칙이 MeleeRange 한 곳에만 있도록 밖에 공개하지 않는다.
        const float _defaultAttackRange = 1.4f;
        // 총의 원본 발사 속도 수치 키다. 읽는 곳마다 없을 때의 기본값이 달라 접근자 대신 키만 함께 쓴다.
        internal const string _shootSpeedStat = "ShootSpeed";
        // 플레이어와 적이 같은 원본 규칙으로 다음 공격 시각을 정한다. 총은 ShootSpeed, 그 밖은 AttackSpeed를 쓰고 수치가 없으면 1이다.
        internal float AttackInterval => 1f / Mathf.Max(_minimumAttackSpeed, Stat(_gun ? _shootSpeedStat : "AttackSpeed", 1f));
        // 플레이어와 적의 근접 판정이 같은 사거리를 쓴다. 음수나 NaN 사거리는 호출자가 제곱 거리로 바꿀 때 걸러 낸다.
        internal float MeleeRange => Stat("AttackRange", _defaultAttackRange);
        // 원본 ItemAgent_Gun.ReloadTime처럼 총의 장전 시간을 캐릭터 ReloadSpeedGain으로 나눈다. 증가량 0이면 정의 값 그대로다.
        internal float ReloadTime(float reloadSpeedGain) => Stat("ReloadTime") / (1f + reloadSpeedGain);
        // 총의 탄창 용량 수치다. 장전 시작 판정과 세션 장전 명령이 같은 키를 읽도록 한곳에 둔다.
        internal int MagazineCapacity => (int)Stat("Capacity");
        // 원작 PostStartReloadSound와 PostReloadSuccessSound의 FMOD 경로다. 장전 키가 없으면 null이라 호출자는 소리를 내지 않는다.
        // 경로 규칙을 정의 한곳에 두고, 장전마다 문자열을 만들지 않도록 처음 읽을 때 한 번 만든다.
        internal string? ReloadStartSound => _reloadKey.Length > 0 ? (_reloadStartSound ??= ReloadSound("_start")) : null;
        internal string? ReloadEndSound => _reloadKey.Length > 0 ? (_reloadEndSound ??= ReloadSound("_end")) : null;
        const string _reloadSoundPrefix = "event:/SFX/Combat/Gun/Reload/";
        string ReloadSound(string suffix) => _reloadSoundPrefix + _reloadKey.ToLowerInvariant() + suffix;
        [NonSerialized] string? _reloadStartSound, _reloadEndSound;
        // 무기의 원본 기본 피해다. 총알 한 발 피해와 근접 피해가 같은 키를 읽도록 한곳에 둔다.
        internal float BaseDamage => Stat("Damage");
        // 원본 ItemAgent_MeleeWeapon처럼 근접 무기 피해에 공격자의 근접 피해 배율을 곱한다.
        internal float MeleeDamage(float multiplier) => BaseDamage * multiplier;
        // 총의 원본 산탄 수다. 수치가 없으면 1발이며, 하한과 정수 변환은 읽는 쪽이 원본 식대로 한다.
        internal float ShotCount => Stat("ShotCount", 1f);
        // 원본 ItemAgent_Gun.BulletDistance가 캐릭터 배율을 곱하기 전의 총 사거리 수치다.
        internal float BulletDistance => Stat("BulletDistance");
        // 원본 CA_Attack처럼 휘두른 뒤 근접 피해를 판정하기까지의 시간이다. 플레이어와 적의 근접 공격이 함께 읽는다.
        internal float DealDamageTime => Stat("DealDamageTime");
        // Stat이 처음 불릴 때 만드는 키별 값 색인이다. 직렬화하지 않아 로드된 정의에서는 null로 시작하고, 메인 스레드에서만 읽는다.
        // Editor에서 _stats가 바뀌면 카탈로그 OnValidate가 비운다. 실행 중에는 _stats 배열을 제자리에서 바꾸지 않는다.
        [NonSerialized] Dictionary<string, float>? _statIndex;
#if UNITY_EDITOR
        // SerializedObject 쓰기는 같은 정의 인스턴스에 값만 덮어써 Stat 색인, 칸 키 목록, 장전 소리 경로를 낡게 만든다.
        // 그래서 카탈로그 OnValidate가 이 메서드로 모두 비운다. Editor 콜백에서만 부르므로 UNITY_EDITOR 안에 둔다.
        internal void ClearStatIndex()
        {
            _statIndex = null;
            _itemSlotKeys = null;
            _reloadStartSound = null;
            _reloadEndSound = null;
        }
#endif
        // 수치를 색인에서 찾는다. 같은 키가 여러 번 있으면 _stats에서 먼저 나온 값을, 없으면 fallback을 돌려준다.
        // 직렬화 데이터에는 null 원소나 null 키가 없지만, 색인은 그런 항목을 던지지 않고 건너뛴다.
        public float Stat(string key, float fallback = 0f)
        {
            _statIndex ??= BaseStats();
            return _statIndex.TryGetValue(key, out float result) ? result : fallback;
        }

        // 원본 기본 수치의 키별 색인을 새로 만든다. Stat 색인과 modifier를 반영한 사본이 같은 규칙으로 시작한다.
        Dictionary<string, float> BaseStats()
        {
            var index = new Dictionary<string, float>(_stats.Length, StringComparer.Ordinal);
            foreach (OriginalItemStat value in _stats)
                if (value != null && value.Key != null) index.TryAdd(value.Key, value.Value);
            return index;
        }

        // 원작 Stat.Recalculate처럼 받은 modifier를 키마다 기본 수치에 적용한 정의 사본이다. 계산은 같은 원작 공식을 쓰는 GAS AttributeAggregator가 한다.
        // 원작은 없는 키의 수치를 0으로 만들고 거기에 적용한다. 사본은 Stat 색인만 다르고 다른 필드는 이 정의와 같다. modifier가 없으면 이 정의 그대로다.
        internal OriginalItemDefinition WithModifiers(IReadOnlyList<OriginalItemModifier> modifiers)
        {
            if (modifiers.Count == 0) return this;
            var aggregators = new Dictionary<string, AttributeAggregator>(StringComparer.Ordinal);
            for (int index = 0; index < modifiers.Count; index++)
            {
                OriginalItemModifier modifier = modifiers[index];
                if (!aggregators.TryGetValue(modifier.Key, out AttributeAggregator? aggregator))
                    aggregators.Add(modifier.Key, aggregator = new AttributeAggregator());
                aggregator.Add(index, 0, modifier.Type, modifier.Order, modifier.Value);
            }
            Dictionary<string, float> stats = BaseStats();
            foreach (KeyValuePair<string, AttributeAggregator> pair in aggregators)
                stats[pair.Key] = pair.Value.Evaluate(stats.TryGetValue(pair.Key, out float baseValue) ? baseValue : 0f);
            var copy = (OriginalItemDefinition)MemberwiseClone();
            copy._statIndex = stats;
            return copy;
        }

        // 아이템을 지급할 때 정의의 컨테이너 용량과 내구도로 새 아이템을 만든다. 정의는 빌드 데이터라 런타임에 검증하지 않고
        // EditMode 카탈로그 테스트가 용량과 최대 중첩을 확인한다. 호출자는 새 GUID와 1 이상 최대 중첩 이하의 수량만 넘긴다.
        internal ItemInstanceState CreateItem(string id, int count)
        {
            ItemContainerState? inventory = _containerCapacity >= 0
                ? new ItemContainerState(_containerCapacity, Array.Empty<ItemPositionState>(), Array.Empty<int>()) : null;
            return GameplayInventory.Create(id, _id, count, inventory, _durability > 0 ? _durability : (float?)null);
        }
    }

    // 원본 추출본으로 만든 읽기 전용 아이템 정의 카탈로그다. 원본 게임 DLL은 실행하지 않는다.
    // 씬은 이 공유 정의를 참조하고, 플레이어가 가진 아이템은 세션에만 저장한다.
    public sealed class OriginalGameplayCatalog : ScriptableObject
    {
        [SerializeField] OriginalItemDefinition[] _items = Array.Empty<OriginalItemDefinition>();
        // 원작 Tag 자산의 표시 정보와 원작 StatInfoDatabase다. 아이템 상세 화면의 태그 줄과 수치 형식, 색이 읽는다.
        [SerializeField] OriginalTagInfo[] _tagInfos = Array.Empty<OriginalTagInfo>();
        [SerializeField] OriginalStatInfo[] _statInfos = Array.Empty<OriginalStatInfo>();
        // 원작 StatInfoDatabase.Get이 없는 키에 쓰는 기본 표시 형식이다.
        internal const string _defaultStatFormat = "0.##";
        // 원본 적이 Prepare에서 받는 GAS 효과 묶음(AS_Enemy)이다. 적은 피해 효과만 쓴다.
        // Editor 제작기 OriginalGameplayContentBuilder가 고정 경로의 에셋을 연결하고, 없으면 제작을 멈춘다.
        [SerializeField] CharacterAbilitySet? _enemyAbilitySet;
        internal CharacterAbilitySet? EnemyAbilitySet => _enemyAbilitySet;
        // 원작 ItemSetting_Skill이 붙은 아이템 가운데 손에 들고 던질 수 있게 옮긴 스킬이다. 몇 개뿐이라 정의마다 두지 않고 따로 둔다.
        [SerializeField] OriginalItemSkill[] _itemSkills = Array.Empty<OriginalItemSkill>();

        // 원작 IsSkill 아이템인지와 그 스킬 수치를 찾는다. 목록이 몇 개뿐이라 색인 없이 훑는다.
        internal bool TryGetSkill(int itemId, [NotNullWhen(true)] out OriginalItemSkill? skill)
        {
            foreach (OriginalItemSkill value in _itemSkills)
                if (value.ItemId == itemId) { skill = value; return true; }
            skill = null;
            return false;
        }

        // 첫 조회 때 만드는 ID 색인이다.
        Dictionary<int, OriginalItemDefinition>? _lookup;

#if UNITY_EDITOR
        // 제작기 ComponentAuthoring이나 인스펙터가 _items를 바꾸면 ID 색인과 정의마다의 Stat 색인을 비워 다음 조회 때 다시 만든다.
        // Editor 콜백이라 UNITY_EDITOR로 감싼다.
        void OnValidate()
        {
            _lookup = null;
            // 태그와 품질이 바뀌면 무작위 상자 검색 결과도 낡으므로 함께 비운다. 상세 화면 표시 정보 색인도 함께 비운다.
            _searches = null;
            _tagLookup = null;
            _statLookup = null;
            if (_items == null) return;
            foreach (OriginalItemDefinition item in _items)
                item?.ClearStatIndex();
        }
#endif

        // 체력, 스태미나, 에너지, 수분, 무게의 플레이어 최대치를 담은 원본 캐릭터 정의를 찾는다. ID는 원작 ItemAssets의 defaultCharacterItemTypeID다.
        // 씬 카탈로그는 이 정의를 늘 갖고 씬 데이터 테스트가 확인하므로, 여기도 호출자도 실패를 기록하지 않는다.
        public bool TryGetPlayerCharacter([NotNullWhen(true)] out OriginalItemDefinition? definition) =>
            TryGet(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID, out definition);

        // 씬 카탈로그가 늘 갖는 원본 플레이어 캐릭터 정의다. 씬 데이터 테스트가 확인하므로 조회 결과를 보지 않는다.
        internal OriginalItemDefinition PlayerCharacter
        {
            get
            {
                TryGetPlayerCharacter(out OriginalItemDefinition? definition);
                return definition!;
            }
        }

        // 정의 ID 조회의 유일한 경로다. 던지지도 기록하지도 않는다. 저장 아이템은 읽을 때 카탈로그와 대조하므로 세션 아이템의 정의는 늘 있다.
        // 대조 규칙: Docs/game-save.md#item-catalog-check
        public bool TryGet(int id, [NotNullWhen(true)] out OriginalItemDefinition? definition)
        {
            // 색인은 첫 조회 때 한 번 만들고, Editor에서 정의가 바뀌면 OnValidate가 비운다.
            _lookup ??= BuildLookup(_items);
            return _lookup.TryGetValue(id, out definition);
        }

        // 정의가 늘 있는 ID의 정의다. 호출자는 저장 읽기의 카탈로그 대조나 저작 데이터가 보장한 ID만 넘기므로 조회 결과를 보지 않는다.
        // 없을 수 있는 ID는 TryGet으로 묻는다. 대조 규칙: Docs/game-save.md#item-catalog-check
        internal OriginalItemDefinition DefinitionOf(int id)
        {
            TryGet(id, out OriginalItemDefinition? definition);
            return definition!;
        }

        // 원작 MasterKeysManager.AllPossibleKeys가 훑는 ItemAssetsCollection.entries 전체다. 제작기가 원본 entries 순서로 정의를 쓰므로,
        // 열쇠 도감 화면(MasterKeysIndexScreen)이 이 순서 그대로 등록할 수 있는 열쇠를 고른다. 정의 배열은 빌드 데이터라 호출자는 읽기만 한다.
        internal IReadOnlyList<OriginalItemDefinition> Items => _items;

        // 원작 Item.Stats처럼 인스턴스 칸에 꽂힌 아이템의 Parent modifier를 반영한 정의다. 부착물을 단 총의 수치가 이 값이다.
        // 원작 ModifierDescriptionCollection처럼 내구도를 쓰는 부착물은 내구도가 0 이하면 효과를 내지 않는다. 모르는 내구도는 가득 찬 것으로 본다.
        // 꽂힌 것이 없으면 카탈로그 정의 그대로다.
        internal OriginalItemDefinition Effective(ItemOwnershipState items, ItemInstanceState item)
        {
            // 그래프의 아이템과 칸의 부품은 저장 읽기 대조를 거쳐 정의가 늘 있으므로 DefinitionOf로 읽는다.
            OriginalItemDefinition definition = DefinitionOf(item.OriginalItemId);
            IReadOnlyList<ItemSlotState> slots = item.Slots;
            if (slots.Count == 0) return definition;
            var applied = new List<OriginalItemModifier>();
            for (int index = 0; index < slots.Count; index++)
            {
                // 검증된 그래프의 칸이 가리키는 아이템이라 늘 있다.
                ItemInstanceState part = items.Find(slots[index].InstanceId)!;
                OriginalItemDefinition partDefinition = DefinitionOf(part.OriginalItemId);
                if (partDefinition.Durability > 0 && part.AbsoluteDurability is float durability && durability <= 0f) continue;
                IReadOnlyList<OriginalItemModifier> modifiers = partDefinition.Modifiers;
                for (int modifier = 0; modifier < modifiers.Count; modifier++)
                    if (modifiers[modifier].Target == OriginalModifierTarget.Parent) applied.Add(modifiers[modifier]);
            }
            return definition.WithModifiers(applied);
        }

        // 원작 Tag 자산의 표시 정보를 이름으로 찾는다. 상세 화면은 정보가 없는 태그를 보이지 않는다.
        internal bool TryGetTag(string name, [NotNullWhen(true)] out OriginalTagInfo? tag)
        {
            if (_tagLookup == null)
            {
                _tagLookup = new Dictionary<string, OriginalTagInfo>(_tagInfos.Length, StringComparer.Ordinal);
                foreach (OriginalTagInfo info in _tagInfos) _tagLookup.TryAdd(info.Name, info);
            }
            return _tagLookup.TryGetValue(name, out tag);
        }

        // 원작 StatInfoDatabase.Get이다. 없는 키는 기본 형식 "0.##"과 Neutral로 본다.
        internal (string Format, OriginalStatPolarity Polarity) StatInfo(string key)
        {
            if (_statLookup == null)
            {
                _statLookup = new Dictionary<string, OriginalStatInfo>(_statInfos.Length, StringComparer.Ordinal);
                foreach (OriginalStatInfo info in _statInfos) _statLookup.TryAdd(info.Key, info);
            }
            return _statLookup.TryGetValue(key, out OriginalStatInfo? found)
                ? (found.Format, found.Polarity) : (_defaultStatFormat, OriginalStatPolarity.Neutral);
        }
        // 태그와 수치 표시 정보의 첫 조회 때 만드는 색인이다. Editor에서 목록이 바뀌면 OnValidate가 비운다.
        Dictionary<string, OriginalTagInfo>? _tagLookup;
        Dictionary<string, OriginalStatInfo>? _statLookup;

        // 원작 ItemAssetsCollection.Search다. 태그 하나와 품질 하나로 찾고, 없으면 원작처럼 품질을 하나씩 낮춰 0까지 다시 찾는다.
        // 원작과 달리 외형 프리팹이 없는 총은 뺀다. 들면 총구가 없어 쏠 수 없기 때문이다. 같은 조건은 처음 찾은 결과를 다시 쓴다.
        // caliber는 원작 ItemFilter.caliber다. 적 총알(CharacterRandomPreset.AddBullet)만 주무기 구경을 넘기고, 비면 구경을 보지 않는다.
        internal IReadOnlyList<int> Search(string requireTag, HashSet<string> excludeTags, int quality, string caliber = "")
        {
            var excluded = new List<string>(excludeTags);
            excluded.Sort(StringComparer.Ordinal);
            string key = requireTag + "|" + quality + "|" + caliber + "|" + string.Join(",", excluded);
            _searches ??= new Dictionary<string, int[]>(StringComparer.Ordinal);
            if (_searches.TryGetValue(key, out int[] cached)) return cached;
            int[] found = Array.Empty<int>();
            for (int current = quality; current >= 0 && found.Length == 0; current--)
            {
                var ids = new List<int>();
                foreach (OriginalItemDefinition item in _items)
                    if (IsSearchCandidate(item, current, requireTag, excludeTags, caliber)) ids.Add(item.Id);
                found = ids.ToArray();
            }
            _searches.Add(key, found);
            return found;
        }

        // 그 품질에서 태그와 구경 조건에 맞고, 들 수 없는 외형 없는 총이 아닌 아이템이 무작위 상자 후보다.
        static bool IsSearchCandidate(OriginalItemDefinition item, int quality, string requireTag, HashSet<string> excludeTags, string caliber) =>
            item.Quality == quality && (caliber.Length == 0 || item.Caliber == caliber) && item.Matches(requireTag, excludeTags)
            && !(item.IsGun && item.Visual == null);
        // Search 결과 캐시다. 카탈로그는 실행 중 바뀌지 않으므로 앱 수명 동안 두고, Editor에서 정의가 바뀌면 OnValidate가 비운다.
        Dictionary<string, int[]>? _searches;

        // TryGet이 쓰는 ID 색인을 만든다. 정의 목록은 직렬화 배열이라 null이 없고, 중복 ID는 제작 도구 ComponentAuthoring이 쓰기 전에 거부한다.
        static Dictionary<int, OriginalItemDefinition> BuildLookup(OriginalItemDefinition[] items)
        {
            var built = new Dictionary<int, OriginalItemDefinition>(items.Length);
            foreach (OriginalItemDefinition item in items)
                built.TryAdd(item.Id, item);
            return built;
        }
    }
}
