#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Economy.Cost처럼 돈과 아이템 항목을 함께 담는 저작 데이터다. 확인과 지불은 소유자 명령만 하도록 여기에는 두지 않는다.
    // 생성자는 제작 도구와 코드가 정한 믿을 수 있는 값만 받고, 수량 규칙은 데이터 테스트가 확인한다.
    [Serializable]
    public struct Cost
    {
        // 원작 Cost.ItemEntry다. 원작 수량은 long이지만 아이템 수량이 int라 int로 둔다.
        [Serializable]
        public struct ItemEntry
        {
            [SerializeField] int _id;
            [SerializeField] int _amount;

            public ItemEntry(int id, int amount)
            {
                _id = id;
                _amount = amount;
            }

            public int Id => _id;
            public int Amount => _amount;
        }

        [SerializeField] long _money;
        [SerializeField] ItemEntry[]? _items;

        public Cost(long money, ItemEntry[] items)
        {
            _money = money;
            _items = items;
        }

        public long Money => _money;

        // 기본값 구조체는 배열이 null이라 빈 목록으로 읽는다.
        public IReadOnlyList<ItemEntry> Items => _items ?? Array.Empty<ItemEntry>();

        // 원작 Cost.IsFree처럼 돈이 0 이하이고 아이템 항목이 하나도 없을 때만 무료다.
        public bool IsFree => _money <= 0 && (_items == null || _items.Length == 0);
    }

    // 원작 EconomyManager.Pay의 돈 나누기 규칙이다. 계좌와 아이템은 소유자가 달라 지불 명령이 이 계획으로 둘을 먼저 확인한 뒤 차례로 적용한다.
    internal static class CostRules
    {
        // 매 프레임 불릴 수 있는 지불 확인(GameplayCommands.CanPay)이 배열 없이 돈만 나누도록 TryPlan에서 떼어 낸 규칙이다.
        // 계좌를 쓸 수 있으면 계좌에서 먼저 내고 모자란 만큼만 현금으로 낸다. 낼 현금이 없으면 fromCash는 0이고, int를 넘으면 낼 수 없어 false다.
        internal static bool TrySplit(Cost cost, long account, bool accountAvailable, out long fromAccount, out int fromCash)
        {
            long money = cost.Money;
            fromAccount = accountAvailable && money > 0 ? Math.Min(account, money) : 0;
            long cash = money - fromAccount;
            fromCash = 0;
            if (cash > int.MaxValue)
            {
                fromAccount = 0;
                return false;
            }
            if (cash > 0) fromCash = (int)cash;
            return true;
        }

        // 계좌를 쓸 수 있으면 계좌에서 먼저 내고 모자란 만큼만 현금 아이템으로 낸다. items는 비용 아이템 뒤에 현금 몫을 붙인 목록이다.
        // 아이템을 먼저 빼고 계좌를 나중에 빼므로 현금 몫을 아이템과 함께 세어야 일부만 빠지지 않는다. 현금 몫이 int를 넘으면 낼 수 없어 false다.
        // 소비 명령이 목록을 받아야 해서 배열을 만든다. 그래서 실제로 내는 TryPay만 이 계획을 쓴다.
        internal static bool TryPlan(Cost cost, long account, bool accountAvailable, int cashItemId, out long fromAccount,
            out IReadOnlyList<(int id, int amount)> items)
        {
            // 돈 나누기는 CanPay와 같은 TrySplit 한 곳에서 정해 두 명령의 판정이 어긋나지 않게 한다.
            if (!TrySplit(cost, account, accountAvailable, out fromAccount, out int fromCash))
            {
                items = Array.Empty<(int id, int amount)>();
                return false;
            }
            IReadOnlyList<Cost.ItemEntry> entries = cost.Items;
            var planned = new (int id, int amount)[entries.Count + (fromCash > 0 ? 1 : 0)];
            for (int index = 0; index < entries.Count; index++) planned[index] = (entries[index].Id, entries[index].Amount);
            // TrySplit이 현금 몫을 int 범위로 확인해 넘기므로 그대로 붙인다.
            if (fromCash > 0) planned[entries.Count] = (cashItemId, fromCash);
            items = planned;
            return true;
        }
    }
}
