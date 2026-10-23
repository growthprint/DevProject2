#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 아이템 칸 하나에 보일 값을 소유 그래프와 카탈로그에서 읽는다. 인벤토리, 상점, 등록, 수리, 분해, 개조 화면이 같은 규칙을 쓴다.
    // 칸 ID는 검증된 소유 그래프가 가리키는 것이라 Find는 실패하지 않고, 그래프의 아이템은 저장 읽기 검사로 늘 카탈로그 정의가 있다.
    internal static class ItemCellValues
    {
        // 칸에 보일 아이콘, 이름, 수량이다. 빈 칸(id가 null)이면 비운다.
        internal static void Describe(ItemOwnershipState items, OriginalGameplayCatalog catalog, string? id, out Sprite? icon, out string name,
            out int count)
        {
            icon = null;
            name = "";
            count = 0;
            if (!TryDescribe(items, catalog, id, out ItemInstanceState item, out OriginalItemDefinition definition)) return;
            icon = definition.Icon;
            name = GameLocalization.Text(definition.NameKey);
            count = item.Quantity;
        }

        // 칸의 아이템과 그 정의를 찾는다. 빈 칸(id가 null)이면 false이고 두 값은 비어 있다.
        internal static bool TryDescribe(ItemOwnershipState items, OriginalGameplayCatalog catalog, string? id, out ItemInstanceState item,
            out OriginalItemDefinition definition)
        {
            if (id == null)
            {
                item = null!;
                definition = null!;
                return false;
            }
            item = items.Find(id)!;
            definition = catalog.DefinitionOf(item.OriginalItemId);
            return true;
        }
    }
}
