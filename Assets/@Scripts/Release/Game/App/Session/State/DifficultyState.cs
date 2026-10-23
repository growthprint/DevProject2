#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원본 RuleIndex 숫자를 그대로 쓴다. 저장 파일에 이 숫자가 들어가므로 화면 표시 순서와 따로 고정한다.
    public enum DifficultyRuleIndex
    {
        Standard = 0,
        Custom = 1,
        Easy = 11,
        ExtraEasy = 12,
        StandardChallenge = 13,
        Hard = 21,
        ExtraHard = 22,
        Rage = 23
    }

    // 프리셋 변경이 이미 저장된 진행에 영향을 주지 않도록 규칙 전체를 값으로 고정한다.
    public sealed class DifficultyState
    {
        // 원본 프리셋이나 프리팹 슬라이더 범위 안의 화면 값처럼 코드와 빌드 데이터가 만든 값용 생성자다. 검증하지 않으며,
        // 저장 파일에서 읽은 값은 TryCreate로 만든다.
        public DifficultyState(DifficultyRuleIndex ruleIndex, float damageFactorToPlayer,
            float enemyHealthFactor, bool spawnDeadBody, bool fogOfWar, bool advancedDebuffMode,
            int saveDeadbodyCount, float recoilMultiplier, float enemyReactionTimeFactor,
            float enemyAttackTimeSpaceFactor, float enemyAttackTimeFactor)
        {
            RuleIndex = ruleIndex;
            DamageFactorToPlayer = damageFactorToPlayer;
            EnemyHealthFactor = enemyHealthFactor;
            SpawnDeadBody = spawnDeadBody;
            FogOfWar = fogOfWar;
            AdvancedDebuffMode = advancedDebuffMode;
            SaveDeadbodyCount = saveDeadbodyCount;
            RecoilMultiplier = recoilMultiplier;
            EnemyReactionTimeFactor = enemyReactionTimeFactor;
            EnemyAttackTimeSpaceFactor = enemyAttackTimeSpaceFactor;
            EnemyAttackTimeFactor = enemyAttackTimeFactor;
        }

        // TryCreate가 쓰는 검사이며 어떤 입력에도 예외를 던지지 않는다. 배율은 모두 음수가 아닌 유한값이어야 한다.
        // 세 bool 규칙은 어떤 값이든 유효하므로 인수로 받지 않는다.
        static bool Check(DifficultyRuleIndex ruleIndex, float damageFactorToPlayer, float enemyHealthFactor, int saveDeadbodyCount,
            float recoilMultiplier, float enemyReactionTimeFactor, float enemyAttackTimeSpaceFactor, float enemyAttackTimeFactor) =>
            Enum.IsDefined(typeof(DifficultyRuleIndex), ruleIndex) &&
            StateValues.IsFiniteNonNegative(damageFactorToPlayer) && StateValues.IsFiniteNonNegative(enemyHealthFactor) &&
            saveDeadbodyCount >= 0 && StateValues.IsFiniteNonNegative(recoilMultiplier) &&
            StateValues.IsFiniteNonNegative(enemyReactionTimeFactor) && StateValues.IsFiniteNonNegative(enemyAttackTimeSpaceFactor) &&
            StateValues.IsFiniteNonNegative(enemyAttackTimeFactor);

        // 저장 파일에서 읽은 값용 생성 경로다. 규칙에 맞지 않으면 예외 없이 false를 돌려준다.
        public static bool TryCreate(DifficultyRuleIndex ruleIndex, float damageFactorToPlayer,
            float enemyHealthFactor, bool spawnDeadBody, bool fogOfWar, bool advancedDebuffMode,
            int saveDeadbodyCount, float recoilMultiplier, float enemyReactionTimeFactor,
            float enemyAttackTimeSpaceFactor, float enemyAttackTimeFactor,
            [NotNullWhen(true)] out DifficultyState? value)
        {
            value = Check(ruleIndex, damageFactorToPlayer, enemyHealthFactor, saveDeadbodyCount, recoilMultiplier,
                    enemyReactionTimeFactor, enemyAttackTimeSpaceFactor, enemyAttackTimeFactor)
                ? new DifficultyState(ruleIndex, damageFactorToPlayer, enemyHealthFactor, spawnDeadBody, fogOfWar,
                    advancedDebuffMode, saveDeadbodyCount, recoilMultiplier, enemyReactionTimeFactor, enemyAttackTimeSpaceFactor,
                    enemyAttackTimeFactor)
                : null;
            return value != null;
        }

        // 아래 값은 원본 난이도 규칙의 수치를 그대로 담는다. 배율은 모두 0 이상의 유한값이다.
        public DifficultyRuleIndex RuleIndex { get; }
        public float DamageFactorToPlayer { get; }
        public float EnemyHealthFactor { get; }
        // 원작 DeadBodyManager.ShouldSpawnDeadBody처럼 레벨을 열 때 유실물 묘비를 세울지 정한다. 사망 기록은 이 값과 상관없이 남는다.
        public bool SpawnDeadBody { get; }
        public bool FogOfWar { get; }
        public bool AdvancedDebuffMode { get; }
        // 남겨 둘 유실물의 최대 개수다. 넘치면 가장 오래된 유실물부터 사라진다.
        public int SaveDeadbodyCount { get; }
        public float RecoilMultiplier { get; }
        public float EnemyReactionTimeFactor { get; }
        public float EnemyAttackTimeSpaceFactor { get; }
        public float EnemyAttackTimeFactor { get; }

        // 원본에서 확인한 일곱 프리셋과 사용자 지정(Custom)의 기본값을 만든다. 저장된 규칙을 읽을 때는 프리셋을 다시 적용하지 않는다.
        public static DifficultyState CreatePreset(DifficultyRuleIndex ruleIndex) => ruleIndex switch
        {
            // 연달아 오는 세 bool 인수는 순서를 헷갈리기 쉬워 이름을 붙인다.
            DifficultyRuleIndex.ExtraEasy =>
                new DifficultyState(ruleIndex, .4f, .4f, spawnDeadBody: true, fogOfWar: false, advancedDebuffMode: false,
                    2, .2f, 2f, 2f, .5f),
            DifficultyRuleIndex.Easy =>
                new DifficultyState(ruleIndex, .6f, .6f, spawnDeadBody: true, fogOfWar: true, advancedDebuffMode: false,
                    2, .5f, 1.5f, 1.5f, .5f),
            DifficultyRuleIndex.Standard =>
                new DifficultyState(ruleIndex, .8f, .8f, spawnDeadBody: true, fogOfWar: true, advancedDebuffMode: false,
                    1, 1f, 1.1f, 1.1f, .9f),
            DifficultyRuleIndex.StandardChallenge =>
                new DifficultyState(ruleIndex, 1f, 1f, spawnDeadBody: true, fogOfWar: true, advancedDebuffMode: false,
                    1, 1f, 1f, 1f, 1f),
            DifficultyRuleIndex.Hard =>
                new DifficultyState(ruleIndex, 1f, 1f, spawnDeadBody: false, fogOfWar: true, advancedDebuffMode: false,
                    1, 1f, 1f, 1f, 1f),
            DifficultyRuleIndex.ExtraHard =>
                new DifficultyState(ruleIndex, 1.5f, 1f, spawnDeadBody: false, fogOfWar: true, advancedDebuffMode: false,
                    1, 1f, 1f, 1f, 1f),
            DifficultyRuleIndex.Rage =>
                new DifficultyState(ruleIndex, 1.6f, .4f, spawnDeadBody: false, fogOfWar: true, advancedDebuffMode: true,
                    1, 1f, .75f, .75f, 1f),
            DifficultyRuleIndex.Custom =>
                new DifficultyState(ruleIndex, 1f, 1f, spawnDeadBody: true, fogOfWar: true, advancedDebuffMode: false,
                    1, 1f, 1f, 1f, 1f),
            // 호출자는 열거형 상수만 넘긴다. switch 식은 모든 값을 다뤄야 하므로 정의 밖 값은 열거형 기본값인 Standard 프리셋으로 만든다.
            _ => CreatePreset(DifficultyRuleIndex.Standard)
        };
    }
}
