#!/usr/bin/env python3
"""flavor-stats: measure V2a (banned-lexicon rate) and V2b (content-word repetition)
over a fusion corpus's flavor lines (concept.flavor of every *.record.json).

Baseline tool for prompt v4 (docs/specs/2026-07-11-prompt-v4.md). The banned and stopword
lists are FROZEN and mirrored in src/Arena/FlavorEval.cs (C# is the single source; the
Arena.Tests banned-list sync test asserts this file carries the same tokens).

usage: python scripts/flavor-stats.py <fusionsDir> [--threshold-v2a 10] [--threshold-v2b 15]
"""
import json
import re
import sys
from pathlib import Path

# FROZEN banned lexicon (v4 spec V2a). Matching: case-insensitive whole word, optional plural 's'.
BANNED = [
    "threshold", "border", "veil", "whisper", "essence", "ancient", "forgotten",
    "eternal", "realm", "shroud", "twilight", "betwixt", "unseen", "beyond",
]

# FROZEN stopword list (v4 spec V2b): content word = alphabetic token, len >= 3, not in this list.
STOPWORDS = {
    "the", "and", "that", "this", "these", "those", "its", "was", "were", "are",
    "been", "being", "has", "have", "had", "does", "did", "will", "would", "can",
    "could", "may", "might", "shall", "should", "must", "not", "nor", "than", "then",
    "when", "where", "while", "who", "whom", "whose", "which", "what", "how", "why",
    "for", "with", "into", "over", "under", "out", "off", "onto", "from", "upon",
    "about", "above", "below", "between", "through", "until", "again", "once", "here",
    "there", "all", "any", "both", "each", "few", "more", "most", "other", "some",
    "such", "only", "own", "same", "too", "very", "just", "still", "yet", "ever",
    "never", "also", "but", "his", "her", "their", "your", "our", "one", "two",
}


def words(line: str) -> list[str]:
    return [w for w in re.findall(r"[a-z]+", line.lower()) if len(w) >= 3 and w not in STOPWORDS]


def has_banned(line: str) -> list[str]:
    low = line.lower()
    return [t for t in BANNED if re.search(rf"\b{t}s?\b", low)]


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    fdir = Path(sys.argv[1])
    t_v2a = float(sys.argv[sys.argv.index("--threshold-v2a") + 1]) if "--threshold-v2a" in sys.argv else 10.0
    t_v2b = float(sys.argv[sys.argv.index("--threshold-v2b") + 1]) if "--threshold-v2b" in sys.argv else 15.0

    lines: list[tuple[str, str]] = []  # (record name, flavor)
    for p in sorted(fdir.glob("*.record.json")):
        rec = json.loads(p.read_text(encoding="utf-8"))
        flavor = (rec.get("concept") or {}).get("flavor", "").strip()
        if flavor:
            lines.append((p.stem, flavor))
    if not lines:
        print(f"no flavor lines found in {fdir}")
        return 2

    n = len(lines)
    banned_hits = [(name, fl, hits) for name, fl in lines if (hits := has_banned(fl))]
    v2a = 100.0 * len(banned_hits) / n

    line_words = [(name, set(words(fl))) for name, fl in lines]
    counts: dict[str, int] = {}
    for _, ws in line_words:
        for w in ws:
            counts[w] = counts.get(w, 0) + 1
    top = sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))
    v2b_word, v2b_count = top[0]
    v2b = 100.0 * v2b_count / n

    print(f"corpus: {fdir}  flavor lines: {n}")
    print(f"V2a banned-lexicon rate: {v2a:.1f}%  ({len(banned_hits)}/{n} lines)  threshold <= {t_v2a:.0f}%  [{'PASS' if v2a <= t_v2a else 'FAIL'}]")
    for name, fl, hits in banned_hits:
        print(f"    {name}: [{', '.join(hits)}] \"{fl}\"")
    print(f"V2b max content-word repetition: {v2b:.1f}%  ('{v2b_word}' in {v2b_count}/{n} lines)  threshold <= {t_v2b:.0f}%  [{'PASS' if v2b <= t_v2b else 'FAIL'}]")
    print("    top repeated content words: " + ", ".join(f"{w}={c}" for w, c in top[:8]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
