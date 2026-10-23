#nullable enable
using System.Collections.Generic;

namespace Game.App
{
    // 특성 연구 시작과 확정, 월드 진입 재적용 명령이다(원작 Perk.SubmitItemsAndBeginUnlocking, ConfirmUnlock, PerkBehaviour).
    // 특성 기록은 PerkTreeManager가, 비용은 지불 명령이, 상품 해금은 EconomyManager가, 창고 칸 수는 세션 소지품이 소유한다.
    // 모든 소유자를 먼저 확인한 뒤 차례로 적용하므로, 비용 없이 연구가 시작되거나 동작 없이 특성이 해금되지 않는다.
    // 스탯 동작은 소유자 값만 바뀌고, 월드가 PerkTreeManager.Changed에서 PlayerVitals.SyncPerkStats로 맞춘다. 메인 스레드에서만 부른다.
    internal static partial class GameplayCommands
    {
        // 원작 SubmitItemsAndBeginUnlocking이다. 특성 확인(레벨 포함) → 비용 지불 → 연구 시작(지금 UTC) 순서다.
        // 비용은 원작 Cost.Pay() 기본 인자처럼 계좌를 쓰고, 아이템은 가방, 창고(기지), 펫 가방에서 낸다. 어느 확인이든 실패하면 기록 없이 false다.
        // 호출자: 3-K2 특성 상세 화면의 시작 버튼.
        internal static bool TryBeginPerkResearch(GameSessionManager session, EconomyManager economy, ExperienceManager experience,
            PerkTreeManager perks, string treeId, string perkName)
        {
            if (!perks.CanBeginResearch(treeId, perkName, experience.Level, out PerkDefinition? perk)) return false;
            // 비용은 지금 씬의 소유 출처에서 낸다. 레이드 중에는 창고 아이템을 쓰지 않는다.
            if (!TryPay(session, economy, perk.Cost, ItemSourceRules.Owned(session), true)) return false;
            // 같은 호출 안에서 CanBeginResearch를 통과했고 지불 알림의 구독자는 Runtime 명령 관문을 닫지 않으므로 연구는 늘 시작된다.
            perks.BeginResearch(treeId, perkName);
            return true;
        }

        // 원작 ConfirmUnlock과, 해금 상태 변경 때 도는 동작(AddPlayerStorage, UnlockStockShopItem, UnlockFormula)을 한 전이로 묶는다.
        // 특성 확인과 동작마다 그 소유자의 관문을 먼저 보고, 창고 → 상품 → 제조식 → 특성 순서로 적용한 뒤 퀘스트 특성 과제에 알린다.
        // 이미 해금했거나 확인 대기 중인 상품, 이미 해금한 제조식은 원작 Unlock처럼 조용히 넘긴다. 호출자: 특성 상세 화면의 활성화 버튼.
        internal static bool TryConfirmPerk(GameSessionManager session, EconomyManager economy, QuestManager quests, CraftingManager crafting,
            OriginalGameplayCatalog catalog, PerkTreeManager perks, string treeId, string perkName)
        {
            if (!perks.CanConfirm(treeId, perkName, out PerkDefinition? perk)) return false;
            IReadOnlyList<PerkEffect> effects = perk.Effects;
            int storageBonus = 0;
            bool unlocksShopItems = false;
            bool unlocksFormulas = false;
            for (int index = 0; index < effects.Count; index++)
            {
                if (effects[index].Kind == PerkEffectKind.PlayerStorage) storageBonus += effects[index].IntValue;
                else if (effects[index].Kind == PerkEffectKind.ShopItem) unlocksShopItems = true;
                else if (effects[index].Kind == PerkEffectKind.Formula) unlocksFormulas = true;
            }
            // 경제 해금은 확인과 적용이 한 멤버라, 0원 지출 확인(관문만 본다)으로 경제 관문이 열렸는지 먼저 본다.
            // 제조식 해금은 관문이 닫히면 조용히 넘기므로, 동작 없이 특성만 해금되지 않게 그 관문도 먼저 본다.
            if (unlocksShopItems && !economy.CanSpend(0)) return false;
            if (unlocksFormulas && !crafting.AcceptsCommands) return false;
            // 창고 확장은 세션 관문을 보고, 창고가 없는 구조 결함만 실패한다. 그 실패는 소지품 쪽이 보고하고 아직 아무것도 바뀌지 않았다.
            if (storageBonus > 0 && !session.Inventory.TryExpandStorage(catalog, StorageCapacity(perks) + storageBonus)) return false;
            // 같은 호출 안에서 경제 관문을 확인했으므로 해금은 거절되지 않는다. 이미 있는 상품은 false라 결과를 보지 않는다.
            for (int index = 0; index < effects.Count; index++)
                if (effects[index].Kind == PerkEffectKind.ShopItem) economy.TryUnlock(effects[index].IntValue, false);
            // 원작 UnlockFormula.OnUnlocked다. 키는 원작처럼 "<트리 ID>/<특성 GameObject 이름>"이다.
            if (unlocksFormulas) crafting.UnlockPerkFormulas(PerkKey(treeId, perkName));
            perks.Confirm(treeId, perkName);
            // 원작 Perk.onUnlockStateChanged다. 끝낼 과제가 없으면 퀘스트 소유자가 아무것도 바꾸지 않는다.
            quests.NotifyPerkUnlocked(treeId, perkName);
            return true;
        }

        // 원작 UnlockStockShopItem.Start와 레벨 초기화 때의 동작 재적용이다. 해금된 특성의 상품과 제조식이 빠졌으면 확인 없이 해금하고,
        // 창고 칸 수를 기본 칸 수 + 창고 확장 합까지 넓힌다. 이미 맞으면 바꾸지 않아 두 번 불러도 같다. 옛 저장과 다른 슬롯 불러오기도 이 경로로 맞춘다.
        // 관문이 닫혔으면 소유자들이 기록 없이 넘긴다. 호출자: 월드 첫 초기화(OriginalGameplayController.TryInitializeWorld).
        internal static void RestorePerkEffects(GameSessionManager session, EconomyManager economy, PerkTreeManager perks, CraftingManager crafting,
            OriginalGameplayCatalog catalog)
        {
            IReadOnlyList<int> items = perks.UnlockedShopItems;
            for (int index = 0; index < items.Count; index++) economy.TryUnlock(items[index], false);
            session.Inventory.TryExpandStorage(catalog, StorageCapacity(perks));
            // 원작 PerkBehaviour가 레벨 초기화마다 해금된 특성의 UnlockFormula를 다시 부르는 자리다. 트리 정의를 훑어 Formula 동작만 본다.
            IReadOnlyList<PerkTreeDefinition> trees = GameplayDataSettings.Progression.PerkTrees;
            for (int tree = 0; tree < trees.Count; tree++)
            {
                IReadOnlyList<PerkDefinition> treePerks = trees[tree].Perks;
                for (int index = 0; index < treePerks.Count; index++)
                    if (HasEffect(treePerks[index], PerkEffectKind.Formula) && perks.IsUnlocked(trees[tree].Id, treePerks[index].Name))
                        crafting.UnlockPerkFormulas(PerkKey(trees[tree].Id, treePerks[index].Name));
            }
        }

        // 원작 UnlockFormula의 matchKey(Master.Master.ID + "/" + Master.name)다. 제조식 requirePerk와 같은 형식이다.
        static string PerkKey(string treeId, string perkName) => treeId + "/" + perkName;

        static bool HasEffect(PerkDefinition perk, PerkEffectKind kind)
        {
            IReadOnlyList<PerkEffect> effects = perk.Effects;
            for (int index = 0; index < effects.Count; index++)
                if (effects[index].Kind == kind) return true;
            return false;
        }

        // 원작 PlayerStorage.Capacity다. 해금된 창고 확장 특성 17개의 합은 890이다.
        static int StorageCapacity(PerkTreeManager perks) => GameplayInventory._storageCapacity + perks.StorageCapacityBonus;
    }
}
