#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 PerkTreeManager처럼 특성 트리 진행을 소유한다. GameManager 프리팹 자식의 컴포넌트이고 GameManager.Perks로 찾는다.
    // 저장할 때 Perks 구역을 써 넣고 기록을 불러오면 읽어 간다. 트리 정의는 GameplayDataSettings.Progression이 갖고, 이 소유자는 (트리 ID, 특성 이름)별
    // 해금, 연구 중, 연구 시작 UTC만 갖는다. 연구 시작과 확정은 GameplayCommands.Perks를 거친다. 메인 스레드에서만 쓴다.
    // 테스트가 시간원만 바꾸도록 Game.Development 하위 클래스(TestPerkTreeManager)에 열어 둔다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public class PerkTreeManager : SessionSectionOwner
    {
        // 값이 바뀌었다는 신호만 보낸다. 원작 Perk.OnPerkUnlockConfirmed 자리다. 파일 읽기, 연구 시작, 확정 뒤에 내고 시계 역행 보정에는 내지 않는다.
        // 월드가 듣고 PlayerVitals.SyncPerkStats로 스탯을 맞추며, 3-K2 화면이 다시 그린다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 불러온 트리별 기록이다. 정의에 없는 트리와 특성도 지우지 않고 그대로 다시 쓴다. 기록 전에는 새 게임 값(빈 목록)이다.
        IReadOnlyList<PerkTreeSaveState> _trees = CampaignRecordsState.Empty.Perks;
        // 해금된 특성 동작의 합이다. 읽는 쪽이 매 프레임 물을 수 있어, 기록이 바뀔 때 표시만 하고 다음 읽기에서 한 번 다시 센다.
        readonly Dictionary<string, float> _statBaseDeltas = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly List<int> _shopItems = new List<int>();
        int _storageCapacityBonus;
        bool _effectsDirty = true;

        // 연구 대기를 세는 실제 UTC다. 원작 DateTime.UtcNow 자리이며, 테스트 하위 클래스만 바꾼다.
        private protected virtual DateTime UtcNow => DateTime.UtcNow;

        // 원작 RequirePerkUnlocked와 QuestTask_UnlockPerk처럼 트리 ID와 특성 GameObject 이름으로 찾고, 기록이 없으면 거짓이다.
        // 저장 값만 읽어 정의를 보지 않는다. 호출자: 퀘스트 특성 과제 조회(QuestManager.ConfigureWorldQueries), 제조식과 수리, 분해 조건, 특성 화면.
        public bool IsUnlocked(string treeId, string perkName) => TryGetEntry(treeId, perkName, out PerkSaveEntry? entry) && entry.Unlocked;

        // 원작 Perk.Unlocking이다. 화면이 진행 막대와 "활성화" 버튼을 고른다. 호출자: 3-K2 특성 상세 화면.
        public bool IsUnlocking(string treeId, string perkName) => TryGetEntry(treeId, perkName, out PerkSaveEntry? entry) && entry.Unlocking;

        // 원작 ModifyCharacterStatsBase 합처럼 해금된 특성의 스탯 키별 Base 증가량이다. 호출자: PlayerVitals.SyncPerkStats.
        internal IReadOnlyDictionary<string, float> StatBaseDeltas
        {
            get
            {
                EnsureEffects();
                return _statBaseDeltas;
            }
        }

        // 원작 AddPlayerStorage 합이다. 창고 칸 수는 기본 칸 수에 이 값을 더한다. 호출자: GameplayCommands.Perks 창고 맞추기.
        internal int StorageCapacityBonus
        {
            get
            {
                EnsureEffects();
                return _storageCapacityBonus;
            }
        }

        // 해금된 특성의 UnlockStockShopItem 상품 ID다. 겹치지 않는다. 호출자: GameplayCommands.RestorePerkEffects(월드 진입 재적용).
        internal IReadOnlyList<int> UnlockedShopItems
        {
            get
            {
                EnsureEffects();
                return _shopItems;
            }
        }

        // 불변 목록이라 복사하지 않고 그대로 넘긴다.
        private protected override void WriteSection(GameSaveData data) => data.SetPerks(_trees);

        // 불러온 기록으로 바꾼 뒤 알린다. 기록은 코덱이 검증한 불변 값이다.
        private protected override void ReadSection(GameSaveData data)
        {
            _trees = data.Perks;
            // 불러온 기록의 해금으로 동작 합을 다시 세도록 표시한다.
            _effectsDirty = true;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 원작 PerkTreeManager.GetPerkTree다. 트리가 10개라 색인 없이 훑는다. 호출자: 이 소유자, 3-K2 트리 화면과 진입점.
        internal static bool TryGetTree(string treeId, [NotNullWhen(true)] out PerkTreeDefinition? tree)
        {
            IReadOnlyList<PerkTreeDefinition> trees = GameplayDataSettings.Progression.PerkTrees;
            for (int index = 0; index < trees.Count; index++)
            {
                if (trees[index].Id != treeId) continue;
                tree = trees[index];
                return true;
            }
            tree = null;
            return false;
        }

        // 트리와 그 안의 특성 번호를 함께 찾는다. 확인 멤버마다 같은 순서로 트리부터 본다. 트리가 없으면 번호는 -1이다.
        static bool TryGetPerk(string treeId, string perkName, [NotNullWhen(true)] out PerkTreeDefinition? tree, out int index)
        {
            index = -1;
            return TryGetTree(treeId, out tree) && tree.TryGetPerkIndex(perkName, out index);
        }

        // 원작 PerkTree.AreAllParentsUnlocked다. 관계 그래프에 노드가 없는 특성은 원작처럼 늘 거짓이고, 부모가 없는 특성은 참이다.
        // 원작은 화면만 보지만 이 소유자의 연구 시작도 같은 확인을 한다. 정의에 없으면 거짓이다. 호출자: 연구 시작 확인, 특성 화면.
        internal bool AreAllParentsUnlocked(string treeId, string perkName) =>
            TryGetPerk(treeId, perkName, out PerkTreeDefinition? tree, out int index) && AreAllParentsUnlocked(tree, index);

        // 원작 Perk.GetRemainingTime이다. 연구 중이 아니거나 정의에 없으면 0이고, 지난 시간이 요구 시간을 넘으면 0이다.
        // 시작 시각이 미래면 원작처럼 지금으로 당겨 기록을 고친다. 호출자: 특성 화면의 남은 시간(d hh:mm:ss.fff).
        internal TimeSpan GetRemaining(string treeId, string perkName)
        {
            if (!TryGetEntry(treeId, perkName, out PerkSaveEntry? entry) || !entry.Unlocking) return TimeSpan.Zero;
            if (!TryGetPerk(treeId, perkName, out PerkTreeDefinition? tree, out int index)) return TimeSpan.Zero;
            TimeSpan left = tree.Perks[index].RequireTime - Elapsed(treeId, entry);
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }

        // 원작 Perk.GetProgress01이다. 요구 시간이 0 이하면 1이다. 원작처럼 연구 중이 아니면 남은 시간이 0이라 1이다. 호출자: 특성 진행 막대.
        internal float GetProgress01(string treeId, string perkName)
        {
            if (!TryGetPerk(treeId, perkName, out PerkTreeDefinition? tree, out int index)) return 1f;
            double total = tree.Perks[index].RequireTime.TotalSeconds;
            if (total <= 0.0) return 1f;
            return 1f - (float)(GetRemaining(treeId, perkName).TotalSeconds / total);
        }

        // 원작 SubmitItemsAndBeginUnlocking의 확인 부분이다. 관문, 정의, 해금 안 됨, 연구 중 아님, 부모 모두 해금, 레벨 ≥ 요구 레벨을 본다.
        // 원작은 연구 중에도 다시 지불하고 대기를 새로 시작하지만 화면이 버튼을 숨겨 보이는 결과가 같아, 여기서 거절한다. 거절은 기록 없이 false다.
        // 비용 확인과 지불은 호출자(GameplayCommands.TryBeginPerkResearch)가 맡는다.
        internal bool CanBeginResearch(string treeId, string perkName, int level, [NotNullWhen(true)] out PerkDefinition? perk)
        {
            perk = null;
            if (!AcceptsCommands) return false;
            if (!TryGetPerk(treeId, perkName, out PerkTreeDefinition? tree, out int index)) return false;
            if (TryGetEntry(treeId, perkName, out PerkSaveEntry? entry) && (entry.Unlocked || entry.Unlocking)) return false;
            if (!AreAllParentsUnlocked(tree, index) || level < tree.Perks[index].RequireLevel) return false;
            perk = tree.Perks[index];
            return true;
        }

        // 원작처럼 연구 중으로 바꾸고 시작 시각을 지금 UTC로 남긴 뒤 알린다. 호출 쪽이 같은 호출에서 CanBeginResearch와 지불을 통과한 뒤에만 부르므로 실패하지 않는다.
        internal void BeginResearch(string treeId, string perkName)
        {
            Replace(treeId, new PerkSaveEntry(perkName, false, true, UtcNow.Ticks));
            ObserverDispatch.InvokeEach(Changed);
        }

        // 원작 ConfirmUnlock의 확인이다. 관문, 정의, 연구 중, 지난 시간 ≥ 요구 시간을 본다. 원작처럼 시간이 다 돼도 자동으로 확정하지 않고
        // 화면의 "활성화" 버튼이 확정한다. 시작 시각이 미래면 지금으로 당겨 기록을 고치므로 대기가 처음부터 다시 시작된다. 거절은 기록 없이 false다.
        internal bool CanConfirm(string treeId, string perkName, [NotNullWhen(true)] out PerkDefinition? perk)
        {
            perk = null;
            if (!AcceptsCommands) return false;
            if (!TryGetPerk(treeId, perkName, out PerkTreeDefinition? tree, out int index)) return false;
            if (!TryGetEntry(treeId, perkName, out PerkSaveEntry? entry) || !entry.Unlocking) return false;
            if (Elapsed(treeId, entry) < tree.Perks[index].RequireTime) return false;
            perk = tree.Perks[index];
            return true;
        }

        // 원작처럼 해금하고 연구 중을 끈 뒤 알린다. 시작 시각은 원작 저장처럼 그대로 둔다. 동작 합은 다음 읽기에서 다시 센다.
        // 호출 쪽이 같은 호출에서 CanConfirm과 다른 소유자 확인을 통과한 뒤에만 부르므로 실패하지 않는다.
        internal void Confirm(string treeId, string perkName)
        {
            // CanConfirm이 같은 호출에서 기록을 찾았으므로 늘 있다.
            TryGetEntry(treeId, perkName, out PerkSaveEntry? entry);
            Replace(treeId, new PerkSaveEntry(perkName, true, false, entry!.BeginUtcTicks));
            _effectsDirty = true;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 원작 PerkTree.AreAllParentsUnlocked 본문이다. 연결 목록은 특성마다 그래프의 첫 노드로 들어오는 연결만 담는다(가져오기 도구).
        bool AreAllParentsUnlocked(PerkTreeDefinition tree, int index)
        {
            if (!tree.Perks[index].HasNode) return false;
            IReadOnlyList<PerkConnection> connections = tree.Connections;
            for (int i = 0; i < connections.Count; i++)
                if (connections[i].To == index && !IsUnlocked(tree.Id, tree.Perks[connections[i].From].Name)) return false;
            return true;
        }

        // 원작 UnlockingBeginTime 읽기다. 시작 시각이 지금보다 미래면 지금으로 당겨 기록을 고치고 한 번 남긴다. 그래서 같은 역행을 두 번 남기지 않는다.
        // 원작처럼 알림은 내지 않고, 고친 값은 다음 저장 지점이 쓴다. 남은 시간과 확정 확인이 함께 쓴다.
        TimeSpan Elapsed(string treeId, PerkSaveEntry entry)
        {
            DateTime now = UtcNow;
            // 저장 값 검사가 틱을 DateTime 범위로 거른 값이다.
            var begin = new DateTime(entry.BeginUtcTicks, DateTimeKind.Utc);
            if (RealTimeRules.ClampToNow(ref begin, now))
            {
                Replace(treeId, new PerkSaveEntry(entry.Name, entry.Unlocked, entry.Unlocking, begin.Ticks));
                RealTimeRules.ReportTimeTravel("특성 연구 " + treeId + "/" + entry.Name, this);
            }
            return now - begin;
        }

        // 트리 10개, 트리당 특성 44개 이하라 색인 없이 훑는다. 이름 비교는 원작처럼 대소문자를 가린다.
        bool TryGetEntry(string treeId, string perkName, [NotNullWhen(true)] out PerkSaveEntry? entry)
        {
            IReadOnlyList<PerkTreeSaveState> trees = _trees;
            for (int t = 0; t < trees.Count; t++)
            {
                if (trees[t].TreeId != treeId) continue;
                IReadOnlyList<PerkSaveEntry> perks = trees[t].Perks;
                for (int p = 0; p < perks.Count; p++)
                {
                    if (perks[p].Name != perkName) continue;
                    entry = perks[p];
                    return true;
                }
                break;
            }
            entry = null;
            return false;
        }

        // 기록은 불변이라 바꿀 트리만 새로 만든 새 목록으로 바꾼다. 같은 이름은 바꾸고 없으면 끝에 붙여, 트리 ID와 트리 안의 이름이 겹치지 않는다.
        // 그래서 믿을 수 있는 입력용 생성자를 쓴다. 정의에 없는 트리와 특성 기록도 그대로 남는다.
        void Replace(string treeId, PerkSaveEntry next)
        {
            IReadOnlyList<PerkTreeSaveState> trees = _trees;
            var replaced = new List<PerkTreeSaveState>(trees.Count + 1);
            bool found = false;
            for (int t = 0; t < trees.Count; t++)
            {
                if (trees[t].TreeId != treeId)
                {
                    replaced.Add(trees[t]);
                    continue;
                }
                found = true;
                IReadOnlyList<PerkSaveEntry> perks = trees[t].Perks;
                var entries = new List<PerkSaveEntry>(perks.Count + 1);
                bool same = false;
                for (int p = 0; p < perks.Count; p++)
                {
                    bool match = perks[p].Name == next.Name;
                    entries.Add(match ? next : perks[p]);
                    same |= match;
                }
                if (!same) entries.Add(next);
                replaced.Add(new PerkTreeSaveState(treeId, entries));
            }
            if (!found) replaced.Add(new PerkTreeSaveState(treeId, new[] { next }));
            _trees = replaced;
        }

        // 해금된 특성의 동작을 원작 PerkBehaviour 종류별로 모은다. 정의에 없는 트리와 특성은 기록만 지키고 효과는 없다.
        // 제조식(4-C1 제조 소유자 몫), 업적(시스템 없음), 정원과 블랙마켓(범위 밖)은 모으지 않는다.
        void EnsureEffects()
        {
            if (!_effectsDirty) return;
            _effectsDirty = false;
            _statBaseDeltas.Clear();
            _shopItems.Clear();
            _storageCapacityBonus = 0;
            IReadOnlyList<PerkTreeSaveState> trees = _trees;
            for (int t = 0; t < trees.Count; t++)
            {
                if (!TryGetTree(trees[t].TreeId, out PerkTreeDefinition? tree)) continue;
                IReadOnlyList<PerkSaveEntry> perks = trees[t].Perks;
                for (int p = 0; p < perks.Count; p++)
                {
                    if (!perks[p].Unlocked || !tree.TryGetPerkIndex(perks[p].Name, out int index)) continue;
                    IReadOnlyList<PerkEffect> effects = tree.Perks[index].Effects;
                    for (int e = 0; e < effects.Count; e++) Collect(effects[e]);
                }
            }
        }

        // 원작 ModifyCharacterStatsBase는 percentage와 상관없이 Base에 value를 더하므로 값만 합친다.
        void Collect(PerkEffect effect)
        {
            switch (effect.Kind)
            {
                case PerkEffectKind.Stats:
                    IReadOnlyList<PerkStatEntry> stats = effect.Stats;
                    for (int s = 0; s < stats.Count; s++)
                    {
                        _statBaseDeltas.TryGetValue(stats[s].Key, out float sum);
                        _statBaseDeltas[stats[s].Key] = sum + stats[s].Value;
                    }
                    break;
                case PerkEffectKind.PlayerStorage:
                    _storageCapacityBonus += effect.IntValue;
                    break;
                case PerkEffectKind.ShopItem:
                    if (!_shopItems.Contains(effect.IntValue)) _shopItems.Add(effect.IntValue);
                    break;
            }
        }
    }
}
