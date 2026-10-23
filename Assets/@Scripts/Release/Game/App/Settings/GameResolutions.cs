#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원본 ResolutionSetter와 DuckovResolution의 해상도 목록, 기본값, 화면비 보정 규칙이다.
    // 규칙 함수는 장치 목록을 인수로 받아 테스트할 수 있다. 실제 장치는 Device와 Current만 읽는다.
    internal static class GameResolutions
    {
        // 원본 GetResolutions가 장치 목록에 더하는 높이다. 너비는 최대 해상도의 화면비로 계산한다.
        static readonly int[] _extraHeights = { 1080, 900, 720, 540 };
        // 원본 해상도 목록이 허용하는 최소 화면비다.
        const float _minAspect = 1.4f;
        // 원본 UpdateResolutionAndScreenMode가 저장 해상도를 넓히지 않고 그대로 적용하는 최소 화면비다.
        const float _applyMinAspect = 1.3666667f;

        // 원본 MaxResolution이다. 장치 목록의 마지막인 가장 큰 해상도를 쓰되, 현재 해상도의 너비가 더 크면 현재를 쓴다.
        // 화면비가 1.4 미만이면 높이는 두고 16:9 너비로 넓힌다.
        internal static Vector2Int MaxResolution(IReadOnlyList<Vector2Int> device, Vector2Int current)
        {
            Vector2Int largest = device.Count > 0 ? device[device.Count - 1] : current;
            Vector2Int result = current.x <= largest.x ? largest : current;
            if ((float)result.x / result.y < _minAspect) result.x = WidthFor16By9(result.y);
            return result;
        }

        // 원본 GetResolutions다. 장치 목록에 추가 높이 4개를 더해 중복을 거르고, 화면비 1.4 미만은 버린다.
        // 테두리 없음이면 최대 해상도와 화면비가 다른 항목도 버린다. 높이, 너비 순으로 큰 것부터 정렬한다.
        internal static List<Vector2Int> Choices(IReadOnlyList<Vector2Int> device, Vector2Int current, bool borderless)
        {
            Vector2Int max = MaxResolution(device, current);
            var candidates = new List<Vector2Int>(device);
            foreach (int height in _extraHeights)
                candidates.Add(new Vector2Int((int)((float)max.x * height / max.y), height));
            var result = new List<Vector2Int>();
            // 결과 목록을 매번 처음부터 훑지 않도록, 넣은 값을 집합에도 담아 중복을 검사한다.
            var added = new HashSet<Vector2Int>();
            foreach (Vector2Int value in candidates)
            {
                if (added.Contains(value) || (float)value.x / value.y < _minAspect) continue;
                if (borderless && !FitsRatio(value, max)) continue;
                result.Add(value);
                added.Add(value);
            }
            result.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : b.x.CompareTo(a.x));
            return result;
        }

        // 원본 DuckovResolution.CheckRotioFit: 후보 화면비로 최대 해상도 너비를 환산한 높이가 2픽셀 안이면 같은 화면비다.
        static bool FitsRatio(Vector2Int value, Vector2Int max) =>
            Mathf.Abs(max.y - (float)value.y / value.x * max.x) <= 2f;

        // 원본 UpdateResolutionAndScreenMode: 저장 해상도의 화면비가 1.3666667 미만이면 높이를 유지한 16:9 너비로 넓혀
        // 적용한다. 저장값은 바꾸지 않는다.
        internal static Vector2Int ApplySize(int width, int height)
        {
            // 원본은 적용할 때와 목록을 만들 때 기준 화면비가 달라, 목록의 _minAspect와 섞지 않는다.
            if ((float)width / height < _applyMinAspect) width = WidthFor16By9(height);
            return new Vector2Int(width, height);
        }

        // 높이를 유지한 16:9 너비다. 원본 식 RoundToInt(height * 16 / 9)도 정수 나눗셈이 먼저라 소수점을 버리므로 같은 버림을 쓴다.
        static int WidthFor16By9(int height) => height * 16 / 9;

        // 옵션 파일이 없을 때 원본은 장치 목록의 마지막인 가장 큰 해상도를 기본값으로 쓴다.
        // 그 값이 옵션 검증을 통과하지 못하면 GameOptions 기본값을 쓴다.
        internal static GameOptions CreateDefaultOptions()
        {
            var options = new GameOptions();
            Vector2Int[] device = Device();
            Vector2Int size = device.Length > 0 ? device[device.Length - 1] : Current;
            var candidate = options.Clone();
            candidate.Width = size.x;
            candidate.Height = size.y;
            return candidate.TryValidate() ? candidate : options;
        }

        // 현재 장치의 해상도 목록을 읽는다. Unity가 오름차순으로 주므로 마지막이 가장 크다.
        internal static Vector2Int[] Device()
        {
            Resolution[] resolutions = Screen.resolutions;
            var result = new Vector2Int[resolutions.Length];
            for (int index = 0; index < resolutions.Length; index++)
                result[index] = new Vector2Int(resolutions[index].width, resolutions[index].height);
            return result;
        }

        // 원본 MaxResolution이 비교하는 현재 모니터 해상도다.
        internal static Vector2Int Current
        {
            get
            {
                Resolution resolution = Screen.currentResolution;
                return new Vector2Int(resolution.width, resolution.height);
            }
        }
    }
}
