#nullable enable
namespace Game.GAS
{
    // 원작 ItemStatsSystem.ModifierType과 같은 값이고, 값이 곧 기본 적용 순서(Order)다.
    // Override는 원작에 없고 UE GAS에서 가져왔다. 순서가 가장 늦어 앞의 계산을 모두 덮는다.
    public enum AttributeModifierOp
    {
        // 그대로 더한다.
        Add = 0,
        // 같은 순서끼리 합친 뒤 max(0, 1 + 합)을 한 번 곱한다. 원작 "*+".
        PercentageAdd = 100,
        // 하나마다 max(0, 1 + 값)을 곱한다. 원작 "*".
        PercentageMultiply = 200,
        // 값을 그대로 대입한다.
        Override = 300
    }
}
