#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 월드에서 실제로 포착한 생존 수치다. 확인되지 않은 최대치나 기본값을 지어내지 않는다.
    public sealed class PlayerState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public PlayerState(float health, float stamina, float energy, float hydration)
        {
            Health = health;
            Stamina = stamina;
            Energy = energy;
            Hydration = hydration;
        }

        // TryCreate가 쓰는 검사다. 네 수치가 모두 음수가 아닌 유한값이어야 한다.
        static bool Check(float health, float stamina, float energy, float hydration) =>
            StateValues.IsFiniteNonNegative(health) && StateValues.IsFiniteNonNegative(stamina) &&
            StateValues.IsFiniteNonNegative(energy) && StateValues.IsFiniteNonNegative(hydration);

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려준다.
        public static bool TryCreate(float health, float stamina, float energy, float hydration,
            [NotNullWhen(true)] out PlayerState? value)
        {
            value = Check(health, stamina, energy, hydration) ? new PlayerState(health, stamina, energy, hydration) : null;
            return value != null;
        }

        public float Health { get; }
        // 저장 형식을 맞추려고 남기는 값이다. 월드의 실제 스태미나는 모터가 갖고, 월드에 들어올 때 최대치로 시작한다.
        public float Stamina { get; }
        public float Energy { get; }
        public float Hydration { get; }
    }
}
