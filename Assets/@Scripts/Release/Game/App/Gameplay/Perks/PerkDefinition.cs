#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 Perk 컴포넌트와 그 PerkRequirement, 관계 그래프의 노드 위치, 같은 GameObject의 동작 목록을 한데 둔다.
    // 해금 상태는 데이터가 아니라 저장 소유자가 가진다.
    [Serializable]
    public sealed class PerkDefinition
    {
        // 원작 특성 GameObject 이름이다. 원작 저장 항목과 제조식 requirePerk가 표시 이름 키가 아닌 이 이름으로 찾는다.
        [SerializeField] string _name = string.Empty;
        [SerializeField] string _displayName = string.Empty;
        [SerializeField] bool _hasDescription;
        [SerializeField] Sprite? _icon;
        // 원작 DisplayQuality 정수 값이다. 아이콘 뒤 품질 그림자를 고른다.
        [SerializeField] int _quality;
        [SerializeField] bool _defaultUnlocked;
        [SerializeField] bool _lockInDemo;
        [SerializeField] int _requireLevel;
        [SerializeField] Cost _cost;
        // 원작 requireTime과 같은 TimeSpan 틱이다. 연구 대기는 실제 UTC 시간으로 센다.
        [SerializeField] long _requireTime;
        // 관계 그래프에 이 특성의 노드가 있는지다. 원작은 노드가 없는 특성의 부모 확인을 늘 거짓으로 본다.
        [SerializeField] bool _hasNode;
        // 원작 노드의 cachedPosition이다. 화면은 여기에 layoutFactor를 곱하고 y를 뒤집는다.
        [SerializeField] Vector2 _position;
        [SerializeField] PerkEffect[] _effects = Array.Empty<PerkEffect>();

        internal string Name => _name;
        internal string DisplayNameKey => _displayName;
        // 원작처럼 설명이 없는 특성은 빈 키이고, 있으면 원작 설명 키 형식을 따른다. 동작 설명은 화면이 동작마다 따로 붙인다.
        internal string DescriptionKey => _hasDescription ? GameLocalization.DescriptionKey(_displayName) : string.Empty;
        internal Sprite? Icon => _icon;
        internal int Quality => _quality;
        internal bool DefaultUnlocked => _defaultUnlocked;
        internal bool LockInDemo => _lockInDemo;
        internal int RequireLevel => _requireLevel;
        internal Cost Cost => _cost;
        internal TimeSpan RequireTime => TimeSpan.FromTicks(_requireTime);
        internal bool HasNode => _hasNode;
        internal Vector2 Position => _position;
        internal IReadOnlyList<PerkEffect> Effects => _effects;
    }
}
