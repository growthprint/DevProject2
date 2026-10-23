#nullable enable
using System.Collections.Generic;

namespace Game.App
{
    // 원작 DecomposeDatabase.Decompose의 아이템 부분을 작업 사본에서 계산한다. 나누기, 부품과 내용물 돌려주기, 결과 넣기,
    // 원래 아이템 지우기를 원작 순서대로 한다. 확인(분해식, 수량 범위, 놓인 가방)은 명령(SessionInventoryCommands.TryDecompose)이 먼저 한다.
    internal sealed partial class GameplayInventory
    {
        // id는 sources가 고른 가방(소지품, 기지면 창고)의 맨 위 칸 아이템이고, count는 1 이상 그 겹친 수 이하다.
        // results는 결과 배수를 곱해 최대 스택 단위로 나눈 새 노드다. 원작 Split처럼 일부만 분해하면 남은 묶음은 제자리에서 수만 줄인다.
        // 묶음 전체면 원작처럼 그 아이템의 칸 부품과 내부 가방 내용물을 SendToPlayer 순서(소지품, 그다음 기지면 창고)로 돌려보낸다.
        // 결과도 같은 순서로 넣고, 원래 아이템은 원작처럼 결과를 넣은 뒤에 칸에서 지워 그 칸을 결과가 쓰지 않는다.
        // 들어가지 못한 그래프는 left 끝에 차례로 붙고, 호출자가 창고 임시 보관함에 넣는다(사용자 결정 2026-10-20).
        // 그래서 규칙 거절은 없고, 컨테이너가 없는 구조 결함만 발견한 도우미가 Defect로 보고한 Invalid가 돌아온다.
        internal InventoryResult Decompose(string id, int count, ItemSources sources, bool toStorage, IReadOnlyList<ItemInstanceState> results,
            InventoryRules rules, List<ItemOwnershipState> left)
        {
            ItemInstanceState target = Child(id);
            bool whole = count >= target.Quantity;
            if (!whole) SetQuantity(target, target.Quantity - count);
            else
            {
                // 노드는 불변이라 돌려보내는 동안 이 목록은 바뀌지 않는다. 원작처럼 칸 순서, 그다음 내부 가방 칸 번호 순서다.
                IReadOnlyList<ItemSlotState> slots = target.Slots;
                for (int index = 0; index < slots.Count; index++)
                {
                    InventoryResult returned = ReturnOwned(slots[index].InstanceId, toStorage, rules, left);
                    if (!returned.IsOk) return returned;
                }
                if (target.Inventory != null)
                    foreach (ItemPositionState entry in Ordered(target.Inventory))
                    {
                        InventoryResult returned = ReturnOwned(entry.InstanceId, toStorage, rules, left);
                        if (!returned.IsOk) return returned;
                    }
            }
            for (int index = 0; index < results.Count; index++)
            {
                ItemInstanceState result = results[index];
                InventoryResult delivered = Deliver(result, rules.MaxStack(result), true, toStorage, out ItemOwnershipState? rest);
                if (!delivered.IsOk) return delivered;
                if (rest != null) left.Add(rest);
            }
            // 부품과 내용물은 이미 떼었으므로 남은 하위 트리는 아이템 하나다. 명령이 놓인 가방을 확인했으므로 Remove는 늘 찾는다.
            return whole ? Remove(id, sources, out _) : InventoryResult.Ok;
        }

        // 원작 Detach와 SendToPlayer다. 그래프 안의 부품 하나를 하위 트리째 떼어 소지품, 그다음 창고로 보내고 남으면 left에 붙인다.
        InventoryResult ReturnOwned(string id, bool toStorage, InventoryRules rules, List<ItemOwnershipState> left)
        {
            InventoryResult returned = DeliverOwned(id, true, toStorage, rules, out ItemOwnershipState? rest);
            if (returned.IsOk && rest != null) left.Add(rest);
            return returned;
        }
    }
}
