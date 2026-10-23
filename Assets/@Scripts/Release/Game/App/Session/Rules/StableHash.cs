#nullable enable

namespace Game.App
{
    // 문자열의 앞부분만 비교하는 비교자가 같은 범위로 해시를 만들 때 쓴다. string.GetHashCode는 문자열 전체를 섞어 쓸 수 없다.
    internal static class StableHash
    {
        // text의 앞 length 글자를 FNV-1a 32비트로 섞는다. 곱셈 넘침은 FNV-1a 계산의 일부라 unchecked로 둔다.
        internal static int Fnv1a(string text, int length)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int index = 0; index < length; index++) hash = (hash ^ text[index]) * 16777619;
                return (int)hash;
            }
        }
    }
}
