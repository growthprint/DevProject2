#nullable enable

namespace Game.App
{
    // 원작 Teams 번호, Team.IsEnemy, 탄과 근접의 같은 팀 통과 규칙이다. 상태가 없어 탄도, 근접 판정, 적 표적 탐색이 함께 쓴다.
    // 적 프리셋의 팀은 원작 enum 값 그대로 직렬화 정수(_team)로 저작되므로 정수로 다룬다.
    // 필드 이름 규칙을 지키면서 원작 팀 이름 그대로 읽히도록 상수 대신 속성으로 둔다(GameLayers와 같은 방식).
    internal static class TeamRules
    {
        // 원작 Teams 값이다. 2는 원작에도 없는 번호다.
        internal static int Player => 0;
        internal static int Scav => 1;
        internal static int Usec => 3;
        internal static int Bear => 4;
        internal static int Middle => 5;
        internal static int Lab => 6;
        internal static int All => 7;
        internal static int Wolf => 8;

        // 원작 Team.IsEnemy 그대로다. 중립(Middle)은 아무도 적으로 보지 않고, All은 자기 팀과 중립까지 모두 적으로 본다.
        // 그 밖의 팀은 대상이 중립이 아니고 자기와 다른 팀이면 적으로 본다. 원작에 없는 번호도 이 마지막 규칙을 따른다.
        internal static bool IsEnemy(int self, int target)
        {
            if (self == Middle) return false;
            if (self == All) return true;
            return target != Middle && self != target;
        }

        // 원작 Projectile.cs:316과 ItemAgent_MeleeWeapon.cs:162의 거르기다. 같은 팀이면 지나가되 대상이 All이면 맞는다.
        // IsEnemy를 쓰지 않으므로 중립도 맞고, 표적이 아닌 다른 팀도 경로에 있으면 맞는다. 쏜 자신은 호출자가 따로 거른다.
        internal static bool BulletPasses(int shooterTeam, int targetTeam) => shooterTeam == targetTeam && targetTeam != All;
    }
}
