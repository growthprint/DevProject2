#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 낚시 행동(Action_FishingV2)이 플레이어와 월드에 맡기는 일이다. 원작은 행동이 CharacterMainControl을 직접 다루지만, 제품은
    // 전투 월드(WorldInteractions)가 구현하고 시험은 가짜를 쓴다. 행동은 이 인터페이스만 알고 컨트롤러와 세션을 모른다.
    internal interface IFishingHost
    {
        // 원작 CurrentHoldItemAgent다. 손에 든 무기 칸 아이템의 인스턴스 ID와 원본 ID, 그 아이템 Bait 칸에 꽂힌 미끼의 원본 ID(없으면 -1)다.
        // 손에 든 무기가 없으면 false다.
        bool TryHeldItem(out string itemId, out int originalItemId, out int baitItemId);
        // 원작 캐릭터 수치 FishingTime(고리 시간)과 FishingQualityFactor(행운)다.
        float FishingTime { get; }
        float FishingQualityFactor { get; }
        // 원작 TimeOfDayController.AtNight와 CurrentWeather(원작 Weather 정수)다.
        bool AtNight { get; }
        int Weather { get; }
        // 원작 UnityEngine.Random.Range(0, 1)이다. 기다림 시간과 물고기 추첨이 쓴다.
        float Random01();
        // 원작 ItemAssetsCollection.GetMetaData(fishID).tags다. 정의가 없으면 null이다.
        IReadOnlyList<string>? FishTags(int fishId);
        // 원작 ItemAssetsCollection.Search(태그 하나, 제외 태그, 품질 하나)다.
        IReadOnlyList<int> SearchFish(string requireTag, HashSet<string> excludeTags, int quality);
        // 원작 물고기 아이템 수치 FishingDifficulty다. 없으면 0이다.
        float FishingDifficulty(int fishId);
        // 원작 SpawnFish의 미끼 소모다. 낚싯대 Bait 칸의 내용물을 겹치면 하나, 아니면 통째로 없앤다. 쓰지 못하면 false다.
        bool TryConsumeBait(string rodId);
        // 원작 CatchFish와 RequireHasFished.SetHasFished다. 물고기를 새로 만들어 줍고(가방이 차면 발밑에 떨군다), 낚시 경험을 기록하고,
        // 0.65초 뒤 "획득 문구 + 물고기 이름!" 알림을 띄운다. gotFishTextKey는 획득 문구의 번역 키다.
        void CatchFish(int fishId, string gotFishTextKey);
        // 원작 characterController.PopText와 NotificationText.Push다. 키는 번역 키다.
        void PopText(string textKey);
        void Notify(string textKey);
        // 원작 CanMove, CanRun, CanControlAim, CanUseHand, CanEditInventory가 모두 false인 행동 동안 쥐는 억제와 행동 태그다.
        // 돌려받은 값을 Dispose하면 모두 한 번에 반납한다.
        IDisposable HoldFishingAction();
    }
}
