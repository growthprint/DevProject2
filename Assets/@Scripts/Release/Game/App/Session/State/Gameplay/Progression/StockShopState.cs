#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 상점 인스턴스별 마지막 재고 갱신 시각을 원본 DateTime.ToBinary 값으로 보존한다.
    public sealed class StockShopState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        // 재고 목록은 따로 한 번 복사하고, null 목록은 빈 목록으로 둔다.
        // 원작은 상인 ID로 재고를 나눠 쓰지만 프로젝트는 같은 상인 프로필의 상점도 따로 기록하므로 인스턴스 키를 받는다(사용자 결정 D1).
        public StockShopState(string shopKey, string merchantId, long lastRefreshBinary, IReadOnlyList<ShopStockState> stocks)
        {
            ShopKey = shopKey; MerchantId = merchantId; LastRefreshBinary = lastRefreshBinary; Stocks = ReadOnlyLists.CopyOrEmpty(stocks);
        }

        // TryCreate가 쓰는 검사다. 갱신 시각은 DateTime으로 되돌릴 수 있는 이진값이어야 한다.
        // 판정만 하고 복사하지 않으며, 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        // 상점 키도 상인 ID처럼 빈 값을 거부한다. 키 중복은 목록을 가진 진행 기록이 본다.
        static bool Check(string shopKey, string merchantId, long lastRefreshBinary, IReadOnlyList<ShopStockState>? stocks)
        {
            if (string.IsNullOrWhiteSpace(shopKey)) return false;
            if (string.IsNullOrWhiteSpace(merchantId)) return false;
            if (!IsRefreshBinary(lastRefreshBinary))
                return false;
            return ReadOnlyLists.AreUnique(stocks, state => state.OriginalItemId);
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        // 상점 키를 첫 인자로 받는다. 키가 없던 옛 항목은 코덱이 필드 누락으로 거부한다.
        public static bool TryCreate(string shopKey, string merchantId, long lastRefreshBinary, IReadOnlyList<ShopStockState> stocks,
            [NotNullWhen(true)] out StockShopState? value)
        {
            value = Check(shopKey, merchantId, lastRefreshBinary, stocks)
                ? new StockShopState(shopKey, merchantId, lastRefreshBinary, stocks) : null;
            return value != null;
        }

        // 상점과 암시장의 마지막 갱신 시각(DateTime.ToBinary 값)을 복원할 수 있는지 판정한다. 종류 비트와 시간대 보정 규칙을
        // 흉내 내지 않고 실제로 변환해 본다. 저장을 읽을 때와 드문 명령에서만 쓴다.
        internal static bool IsRefreshBinary(long value)
        {
            try
            {
                _ = DateTime.FromBinary(value);
                return true;
            }
            // 예외 경계: BCL DateTime.FromBinary가 범위 밖 이진값에 던지는 ArgumentException만 거절 결과로 번역한다.
            catch (ArgumentException)
            {
                return false;
            }
        }

        // 씬의 StockShop._shopKey에 저작한 상점 인스턴스 키다. 원작 상인 프로필은 MerchantId로 남아 재고 품목 ID를 맞춘다.
        public string ShopKey { get; }
        public string MerchantId { get; }
        public long LastRefreshBinary { get; }
        public IReadOnlyList<ShopStockState> Stocks { get; }
    }
}
