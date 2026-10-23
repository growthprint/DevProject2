#nullable enable
using System;
using System.Collections.Generic;

namespace Game.GAS
{
    // 점으로 나눈 이름(예: State.Action.Dash)을 정수 하나로 바꾼 태그다. 비교와 부모 검사가 정수 연산이라 매 프레임 써도 할당이 없다.
    // 이름 표는 늘기만 하고 메인 스레드에서만 읽고 쓴다. 같은 이름은 어느 에셋에서 왔든 같은 태그라서 에셋이 두 번 로드돼도 비교가 깨지지 않는다.
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        // 0번은 None이라 이름 표의 0번 칸은 비워 둔다. 부모 표의 값도 태그 번호이고, 최상위 태그의 부모는 0이다.
        static readonly List<string> _names = new List<string> { "" };
        static readonly List<int> _parents = new List<int> { 0 };
        static readonly Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.Ordinal);

        readonly int _id;

        internal GameplayTag(int id) => _id = id;

        public static GameplayTag None => default;
        public bool IsValid => _id != 0;
        public string Name => _names[_id];
        public GameplayTag Parent => new GameplayTag(_parents[_id]);
        internal int Id => _id;
        // 카운트 표가 배열 크기를 정할 때 쓴다. 태그가 새로 생기면 커진다.
        internal static int Count => _names.Count;

        // UE MatchesTag처럼 이 태그가 other이거나 other의 자식이면 true다. A.B는 A에 맞지만 A는 A.B에 맞지 않는다.
        public bool MatchesTag(GameplayTag other)
        {
            if (other._id == 0) return false;
            for (int id = _id; id != 0; id = _parents[id])
                if (id == other._id) return true;
            return false;
        }

        public bool MatchesTagExact(GameplayTag other) => _id != 0 && _id == other._id;

        // 코드 상수용 진입점이다. 상수 이름은 형식에 맞아야 하고, 틀리면 TryParse가 남긴 None을 돌려준다.
        public static GameplayTag Request(string name)
        {
            TryParse(name, out GameplayTag tag);
            return tag;
        }

        // 에셋과 외부 입력용 진입점이다. 이름이 형식에 맞으면 부모 태그까지 함께 등록하고, 아니면 기록 없이 false다.
        public static bool TryParse(string? name, out GameplayTag tag)
        {
            tag = None;
            if (!IsWellFormed(name)) return false;
            tag = new GameplayTag(Intern(name!));
            return true;
        }

        // 등록하지 않고 이미 있는 태그만 찾는다. 제작 검사처럼 표를 늘리면 안 되는 곳이 쓴다.
        public static bool TryFind(string? name, out GameplayTag tag)
        {
            tag = None;
            if (name == null || !_ids.TryGetValue(name, out int id)) return false;
            tag = new GameplayTag(id);
            return true;
        }

        // 부모 이름을 먼저 등록해 부모 번호가 늘 자식보다 작게 한다. 이름 조각은 처음 등록할 때 한 번만 만든다.
        static int Intern(string name)
        {
            if (_ids.TryGetValue(name, out int id)) return id;
            int dot = name.LastIndexOf('.');
            int parent = dot < 0 ? 0 : Intern(name.Substring(0, dot));
            id = _names.Count;
            _names.Add(name);
            _parents.Add(parent);
            _ids.Add(name, id);
            return id;
        }

        // 빈 조각("A..B", ".A", "A.")과 공백, 제어 문자를 거른다. 대소문자는 구분한다.
        static bool IsWellFormed(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            bool segmentStart = true;
            foreach (char character in name!)
            {
                if (character == '.')
                {
                    if (segmentStart) return false;
                    segmentStart = true;
                    continue;
                }
                if (char.IsWhiteSpace(character) || char.IsControl(character)) return false;
                segmentStart = false;
            }
            return !segmentStart;
        }

        public bool Equals(GameplayTag other) => _id == other._id;
        public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);
        public override int GetHashCode() => _id;
        public static bool operator ==(GameplayTag left, GameplayTag right) => left._id == right._id;
        public static bool operator !=(GameplayTag left, GameplayTag right) => left._id != right._id;
        public override string ToString() => Name;
    }
}
