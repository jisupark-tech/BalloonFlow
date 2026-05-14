#!/bin/sh
# Lock sensitive files via skip-worktree.
# jisu (관리자) 자리에서는 자동 unlock — ProjectSettings/.gitignore/SDK 변경 가능.
# 그 외 사용자 자리에서는 자동 lock — SourceTree 변경 목록에서 사라짐 + commit 시도도 pre-commit hook 차단.

set -u

GIT_ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$GIT_ROOT" || exit 0

EMAIL=$(git config user.email)
if [ "$EMAIL" = "jisu.park@gameberry.co.kr" ]; then
    ACTION="--no-skip-worktree"
    LABEL="unlock"
else
    ACTION="--skip-worktree"
    LABEL="lock"
fi

# 보호 영역 패턴 — .githooks/pre-commit 의 jisu 전용 영역과 일치
PATTERNS=".gitignore BalloonFlow/.gitignore BalloonFlow/ProjectSettings BalloonFlow/Assets/Firebase BalloonFlow/Assets/ExternalDependencyManager BalloonFlow/Assets/GeneratedLocalRepo BalloonFlow/Assets/Plugins BalloonFlow/Assets/MaxSdk BalloonFlow/Assets/AppLovin BalloonFlow/Assets/GoogleMobileAds BalloonFlow/Assets/AppsFlyer BalloonFlow/Assets/FacebookSDK BalloonFlow/Assets/google-services.json BalloonFlow/Assets/StreamingAssets/google-services-desktop.json"

git ls-files -- $PATTERNS 2>/dev/null | while IFS= read -r f; do
    [ -z "$f" ] && continue
    git update-index "$ACTION" -- "$f" 2>/dev/null || true
done

echo "[lock-sensitive-files] $LABEL ($EMAIL)"
