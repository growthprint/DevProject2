#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 InteractableLootbox다. 저작 상자는 고정 내용(원본 아이템 ID와 수량)을, 적 시체는 대여마다 받은 전리품을 갖는다.
    // 기지 창고도 원작처럼 같은 상자 클래스이며 플레이어 창고를 연다. 칸마다 SourceId와 칸 번호로 만든 사건 키로 한 번만 집는다.
    [DisallowMultipleComponent]
    public sealed class InteractableLootbox : InteractableBase, IPoolable
    {
        [SerializeField] int[] _itemTypes = Array.Empty<int>();
        [SerializeField] int[] _counts = Array.Empty<int>();
        // 원작 기지 창고 상자다. 참이면 상자 칸 대신 플레이어 창고를 연다.
        [SerializeField] bool _playerStorage;
        // 원작 LootBoxLoader.inventorySize다. 상자 화면은 이만큼 칸을 그리고, 내용이 더 많으면 내용 수만큼 그린다. 0이면 내용 수다.
        [SerializeField] int _capacity;

        // 같은 인덱스가 한 칸의 아이템과 수량이다. 무작위 상자는 월드 초기화가 SetContents로 한 번 정한 뒤 바꾸지 않는다.
        public IReadOnlyList<int> ItemTypes => _itemTypes;
        public IReadOnlyList<int> Counts => _counts;
        internal int Capacity => Math.Max(_capacity, _itemTypes.Length);
        internal bool PlayerStorage => _playerStorage;

        internal override void OnInteractFinished(IInteractionHost host) => host.OpenLoot(this);

        // 제작 도구와 시체 대여가 함께 쓰는 구성이다. 반환 때도 같은 메서드로 비워 필드 목록이 어긋나지 않게 한다.
        internal void Configure(string sourceId, Collider? interactCollider, string nameKey, Vector3 markerOffset, float interactTime,
            int[] types, int[] counts, bool playerStorage)
        {
            ConfigureInteraction(sourceId, interactCollider, nameKey, markerOffset, interactTime);
            _itemTypes = types; _counts = counts; _playerStorage = playerStorage; _capacity = 0;
        }

        // 제작 도구가 원본 inventorySize를 적는다.
        internal void ConfigureCapacity(int capacity) => _capacity = capacity;

        // 원작 LootBoxLoader.Setup이 채운 내용이다. 같은 출격이면 같은 시드라 다시 불러도 칸 번호와 루팅 키가 맞는다.
        internal void SetContents(int[] types, int[] counts, int capacity)
        {
            _itemTypes = types; _counts = counts; _capacity = capacity;
        }

        // 대여 직후에는 할 일이 없다. RuntimePointSpawner가 활성화 전에 구성 콜백으로 Configure를 부른다.
        void IPoolable.OnSpawn() { }

        // 풀 반환 때 대여별 값을 비워 다음 대여가 앞 시체의 전리품을 물려받지 않게 한다.
        void IPoolable.OnDespawn() =>
            Configure("", null, "", Vector3.zero, 0f, Array.Empty<int>(), Array.Empty<int>(), false);
    }
}
