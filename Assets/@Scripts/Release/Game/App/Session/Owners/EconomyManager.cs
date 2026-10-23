#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 EconomyManager처럼 계좌 돈, 상품 해금과 확인 대기, 상점 재고 기록을 소유한다. GameManager 프리팹 자식의 컴포넌트이고
    // GameManager.Economy로 찾는다. Economy 구역 보관자다. 저장할 때 economy와 shops 필드를 써 넣고, 기록을 불러오면 읽어 간다.
    // 해금은 이 소유자만 바꾸므로 TryUnlock과 TryConfirmUnlock 한 멤버씩으로 둔다.
    // 계좌 돈은 여러 소유자에 걸친 명령이 다른 상태를 바꾸기 전에 확인해야 해서 확인·적용 쌍으로 바꾼다. 확인은 명령 관문을 보고, 적용은 같은 호출에서 확인을 통과한 값만 받아 실패하지 않는다.
    // 상점 재고 기록은 상점 인스턴스 키(StockShop._shopKey)마다 하나를 두고, 그 상점만 TryGetShop과 TrySetShop으로 읽고 쓴다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class EconomyManager : SessionSectionOwner
    {
        // 값이 바뀌었다는 신호만 보낸다. 지불, 환급, 해금, 불러오기 때 내며 2-P2 상점 화면의 잔고와 해금 표시가 듣는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 불러온 기록의 값이다. null은 그 하위 상태를 포착하지 않은 옛 저장 값이라 바꾸기 전까지 그대로 null로 되돌려 쓴다.
        // 세션은 늘 FileLoaded로 기록을 받은 뒤에 저장하므로, 기록 전의 null이 파일에 쓰이지 않는다.
        EconomyPersistenceState? _economy;
        IReadOnlyList<StockShopState>? _shops;

        // 원작 EconomyManager.Money다. 포착하지 않은 옛 저장 값(null)은 0원이다. 호출자: GameplayCommands 지불, 2-P2 상점 화면의 잔고 표시.
        public long Money => _economy != null ? _economy.Money : 0;

        // 원작 IsUnlocked다. 원작 기본 해금 목록은 비어 있어 해금 목록만 본다. 호출자: 2-P1 상점 구매의 해금 확인, 2-P2 상점 화면.
        public bool IsUnlocked(int itemId) => Contains(Unlocked, itemId);

        // 원작 IsWaitingForUnlockConfirm이다. 호출자: 2-P2 상점 화면의 "해금" 버튼.
        public bool IsWaitingForUnlockConfirm(int itemId) => Contains(Awaiting, itemId);

        // 원작 Unlock이 조용히 넘기는 경우(이미 해금이거나 확인 대기)다. 해금 명령과 퀘스트 보상 표시가 같은 판정을 쓴다.
        public bool IsUnlockedOrWaiting(int itemId) => IsUnlocked(itemId) || IsWaitingForUnlockConfirm(itemId);

        IReadOnlyList<int> Unlocked => _economy != null ? _economy.UnlockedItems : Array.Empty<int>();
        IReadOnlyList<int> Awaiting => _economy != null ? _economy.AwaitingConfirmation : Array.Empty<int>();

        // 둘 다 불변 값이라 복사하지 않고 그대로 넘긴다.
        private protected override void WriteSection(GameSaveData data) => data.SetEconomy(_economy, _shops);

        // 불러온 기록으로 바꾼 뒤 알린다. 기록은 코덱이 검증한 불변 값이다.
        private protected override void ReadSection(GameSaveData data)
        {
            _economy = data.Economy;
            _shops = data.Shops;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 여러 소유자에 걸친 명령이 다른 상태를 바꾸기 전에 확인하는 조건이다. 관문이 닫혔으면 기록 없이 거절한다.
        // 금액은 호출자의 돈 나누기(CostRules.TrySplit)가 늘 0과 잔고 사이로 만들고 특성 확정은 0을 넘기므로 범위를 다시 보지 않는다.
        // 호출자: GameplayCommands.TryPay, CanPay, TryConfirmPerk.
        public bool CanSpend(long amount) => AcceptsCommands;

        // 호출 쪽이 같은 호출에서 CanSpend로 확인한 양만 넘기므로 늘 빠져 결과를 돌려주지 않는다. 0은 바뀐 것이 없어 알리지 않는다.
        public void Spend(long amount)
        {
            if (amount == 0) return;
            Replace(Money - amount, Unlocked, Awaiting);
        }

        // 원작 EconomyManager.Add의 확인이다. 관문이 닫혔거나 long을 넘치면 기록 없이 거절한다.
        // 금액은 저작된 보상과 판매 값이라 음수가 오지 않는다. 호출자: 퀘스트 돈 보상, 상점 판매 대금.
        public bool CanAdd(long amount) => AcceptsCommands && amount <= long.MaxValue - Money;

        // 호출 쪽이 같은 호출에서 CanAdd로 확인한 양만 넘기므로 늘 더해진다. 0은 바뀐 것이 없어 알리지 않는다.
        public void Add(long amount)
        {
            if (amount == 0) return;
            Replace(Money + amount, Unlocked, Awaiting);
        }

        // 원작 Unlock(id, needConfirm)이다. needConfirm이면 확인 대기, 아니면 바로 해금 목록 끝에 넣는다. 이 소유자만 바꾸므로 확인과 적용을
        // 한 멤버로 둔다. 확인을 건너뛴 호출자가 목록에 같은 ID를 두 번 넣어 저장을 실패시키지 못하게 하려는 것이다.
        // 관문이 닫혔거나, 이미 해금했거나 확인 대기 중이면 원작처럼 조용히 넘겨 기록 없이 false다. 보상 쪽은 false여도 받음 처리를 막지 않는다.
        // 호출자: 2-Q1 퀘스트 보상(QuestReward_UnlockStockItem).
        public bool TryUnlock(int itemId, bool needConfirm)
        {
            if (!AcceptsCommands || IsUnlockedOrWaiting(itemId)) return false;
            if (needConfirm) Replace(Money, Unlocked, ReadOnlyLists.Appended(Awaiting, itemId));
            else Replace(Money, ReadOnlyLists.Appended(Unlocked, itemId), Awaiting);
            return true;
        }

        // 원작 ConfirmUnlock이다. 확인 대기에서 빼고 해금 목록 끝에 넣는다. TryUnlock처럼 이 소유자만 바꾸므로 확인과 적용을 한 멤버로 둔다.
        // 관문이 닫혔거나 확인 대기 중이 아니면 기록 없이 false다. 호출자: 2-P2 상점 화면의 "해금" 버튼(2-P1 명령을 거친다).
        public bool TryConfirmUnlock(int itemId)
        {
            if (!AcceptsCommands || !IsWaitingForUnlockConfirm(itemId)) return false;
            IReadOnlyList<int> awaiting = Awaiting;
            var rest = new List<int>(awaiting.Count);
            for (int index = 0; index < awaiting.Count; index++)
                if (awaiting[index] != itemId) rest.Add(awaiting[index]);
            Replace(Money, ReadOnlyLists.Appended(Unlocked, itemId), rest);
            return true;
        }

        // 원작 StockShop.Load가 읽는 StockShop_<merchantID> 기록 자리다. 같은 상인 프로필의 상점도 따로 기록하도록 상점 키로 찾는다(사용자 결정 D1).
        // 기록이 없거나 포착하지 않은 옛 저장(null)이면 false다. 읽기만 해 관문을 보지 않는다. 호출자: StockShop 열기와 구매.
        public bool TryGetShop(string shopKey, [NotNullWhen(true)] out StockShopState? state)
        {
            IReadOnlyList<StockShopState>? shops = _shops;
            if (shops != null)
                for (int index = 0; index < shops.Count; index++)
                    if (shops[index].ShopKey == shopKey)
                    {
                        state = shops[index];
                        return true;
                    }
            state = null;
            return false;
        }

        // 원작 StockShop.Save다. 같은 키의 기록을 바꾸고 없으면 끝에 붙여 키가 겹치지 않는다. 이 소유자만 목록을 바꾸므로 확인과 적용을 한 멤버로 둔다.
        // 관문이 닫혔으면 기록 없이 false다. 재고 표시는 상점 컴포넌트의 Changed가 알리므로 여기서는 알리지 않는다.
        // 호출자: StockShop 새로고침, GameplayCommands.TryBuyShopItem(같은 호출의 지불이 관문을 확인한 뒤라 늘 성공한다).
        public bool TrySetShop(StockShopState state)
        {
            if (!AcceptsCommands) return false;
            IReadOnlyList<StockShopState> shops = _shops ?? Array.Empty<StockShopState>();
            var next = new List<StockShopState>(shops.Count + 1);
            bool replaced = false;
            for (int index = 0; index < shops.Count; index++)
            {
                bool same = shops[index].ShopKey == state.ShopKey;
                next.Add(same ? state : shops[index]);
                replaced |= same;
            }
            if (!replaced) next.Add(state);
            _shops = next;
            return true;
        }

        // 새 값으로 바꾸고 알린다. 확인을 통과한 값이라 잔고는 0 이상이고 두 목록은 겹치지 않아, 믿을 수 있는 입력용 생성자를 쓴다.
        // 포착하지 않은 옛 저장 값(null)도 이때부터 포착한 값이 된다.
        void Replace(long money, IReadOnlyList<int> unlocked, IReadOnlyList<int> awaiting)
        {
            _economy = new EconomyPersistenceState(money, unlocked, awaiting);
            ObserverDispatch.InvokeEach(Changed);
        }

        static bool Contains(IReadOnlyList<int> values, int value)
        {
            for (int index = 0; index < values.Count; index++)
                if (values[index] == value) return true;
            return false;
        }
    }
}
