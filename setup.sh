#!/bin/bash
# BalloonFlow Git 초기 설정 스크립트
# clone 후 한 번만 실행하세요: bash setup.sh

echo "BalloonFlow Git 설정 시작..."

# 1. pre-commit hook 활성화
git config core.hooksPath .githooks
echo "[1/2] pre-commit hook 설정 완료"

# 2. Level Data 변경 감지 무시 (woohyun 제외)
AUTHOR_EMAIL=$(git config user.email)
if [ "$AUTHOR_EMAIL" != "woohyun.lim@aimed.xyz" ]; then
    git update-index --skip-worktree BalloonFlow/Assets/Resources/LevelDatabase.asset
    git update-index --skip-worktree BalloonFlow/Assets/Resources/LevelDatabase.asset.meta
    git update-index --skip-worktree BalloonFlow/Assets/EditorData/LevelDatabase_AI.asset
    git update-index --skip-worktree BalloonFlow/Assets/EditorData/LevelDatabase_AI.asset.meta
    git update-index --skip-worktree BalloonFlow/Assets/EditorData/LevelDatabase_Transform.asset
    git update-index --skip-worktree BalloonFlow/Assets/EditorData/LevelDatabase_Transform.asset.meta
    echo "[2/2] Level Data 변경 감지 무시 설정 완료"
else
    echo "[2/2] woohyun 계정 — Level Data 변경 감지 유지"
fi

echo ""
echo "설정 완료!"
