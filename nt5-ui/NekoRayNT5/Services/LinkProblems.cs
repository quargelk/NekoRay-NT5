using System;
using System.Collections.Generic;
using System.Globalization;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // What could not be used in a list of share links. A line that looks like a link but cannot be used
    // is counted and, for the first MaxDetails of them, described as "Link N: reason" (N = the number of
    // the non-empty line), so that a long text never turns into thousands of entries. Lines that are not
    // links at all (plain text, headings, comments) are only counted: they are not parse errors.
    internal sealed class LinkProblems
    {
        public const int MaxDetails = 20;

        private readonly List<string> _details = new List<string>();
        private readonly List<int> _numbers = new List<int>();
        private readonly List<string> _reasons = new List<string>();
        private int _failed;
        private int _notLinks;

        // lines that look like share links but cannot be used
        public int Count
        {
            get { return _failed; }
        }

        // non-empty lines that are not share links; they are skipped
        public int NotLinks
        {
            get { return _notLinks; }
        }

        // "Link N: reason" for the first MaxDetails links that cannot be used
        public IList<string> Details
        {
            get { return _details.AsReadOnly(); }
        }

        public void AddFailed(int number, string reason)
        {
            _failed++;
            if (_details.Count < MaxDetails)
            {
                _details.Add(Lang.F("Link {0}: {1}", number.ToString(CultureInfo.InvariantCulture), reason));
                _numbers.Add(number);
                _reasons.Add(reason);
            }
        }

        public void AddNotLink()
        {
            _notLinks++;
        }

        public void Clear()
        {
            _details.Clear();
            _numbers.Clear();
            _reasons.Clear();
            _failed = 0;
            _notLinks = 0;
        }

        // At most maxLines lines for a message: links with the same reason together
        // ("Links 2, 5, 9: reason"), then how many more were not described.
        public List<string> Summarize(int maxLines)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<int>> byReason = new Dictionary<string, List<int>>();
            for (int i = 0; i < _reasons.Count; i++)
            {
                List<int> numbers;
                if (!byReason.TryGetValue(_reasons[i], out numbers))
                {
                    numbers = new List<int>();
                    byReason.Add(_reasons[i], numbers);
                    order.Add(_reasons[i]);
                }
                numbers.Add(_numbers[i]);
            }
            List<string> lines = new List<string>();
            int described = 0;
            for (int i = 0; i < order.Count && lines.Count < maxLines; i++)
            {
                List<int> numbers = byReason[order[i]];
                string[] shown = new string[Math.Min(numbers.Count, 5)];
                for (int k = 0; k < shown.Length; k++)
                {
                    shown[k] = numbers[k].ToString(CultureInfo.InvariantCulture);
                }
                string list = string.Join(", ", shown);
                if (numbers.Count > shown.Length)
                {
                    lines.Add(Lang.F("Links {0} and {1} more: {2}", list, numbers.Count - shown.Length, order[i]));
                }
                else
                {
                    lines.Add(Lang.F(numbers.Count == 1 ? "Link {0}: {1}" : "Links {0}: {1}", list, order[i]));
                }
                described += numbers.Count;
            }
            if (_failed > described)
            {
                lines.Add(Lang.F("... and {0} more link(s) that cannot be used", _failed - described));
            }
            return lines;
        }
    }
}
