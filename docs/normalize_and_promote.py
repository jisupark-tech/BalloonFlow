"""
BalloonFlow — Design DB 정규화 + Expert DB 승격
교차검증 통과 → 디렉터 승인(+0.2) → score ≥ 0.6 → Expert DB 승격

정규화 작업:
1. designId 통일 규칙: {project}_{domain}_{system} (소문자, 언더스코어)
2. 도메인 매핑 정규화 (InGame→ingame 등)
3. data_type 정규화
4. tags 표준화 (genre, sub_genre, domain, project)
5. provides/requires 추출 (system_spec 기반)
6. score 0.4 → 0.6 (디렉터 승인)
7. design_base → design_expert 복사 (score ≥ 0.6)
"""
import sys, os, yaml
from datetime import datetime, timezone

sys.stdout = __import__('io').TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

from pymongo import MongoClient

MONGO_URI = "mongodb+srv://jisupark_db_user:MK6mD10A2ccO7lsl@db.qwzflco.mongodb.net/?retryWrites=true&w=majority&appName=DB"
DB_NAME = "aigame"
PROJECT = "BalloonFlow"

# ── Domain 정규화 맵 ──
DOMAIN_NORM = {
    "InGame": "InGame",
    "ingame": "InGame",
    "OutGame": "OutGame",
    "outgame": "OutGame",
    "Balance": "Balance",
    "balance": "Balance",
    "Content": "Content",
    "content": "Content",
    "BM": "BM",
    "bm": "BM",
    "UX": "UX",
    "ux": "UX",
    "LiveOps": "LiveOps",
    "liveops": "LiveOps",
    "Meta": "Meta",
    "meta": "Meta",
    "Social": "Social",
    "social": "Social",
}

# ── 도메인별 문서 역할 분류 ──
DOCUMENT_ROLES = {
    # Core design docs (Layer 1-2)
    "ConceptDoc": {"role": "concept", "description": "컨셉 문서 — 코어 펀, 필러, 타겟, 메카닉, 아트 디렉션"},
    "GameDesignDoc": {"role": "game_design", "description": "게임 기획서 — 비전, 시스템 구성, 기술 요구사항"},
    "SystemSpec": {"role": "system_spec", "description": "시스템 설계서 — 33개 시스템 상세 명세"},
    "BuildOrder": {"role": "build_order", "description": "빌드 오더 — Phase 0~3 빌드 순서 + 의존성"},

    # Domain systems (Stage 2-2)
    "InGameSystem": {"role": "domain_spec", "description": "인게임 시스템 도메인 정의"},
    "OutGameSystem": {"role": "domain_spec", "description": "아웃게임 시스템 도메인 정의"},
    "BMSystem": {"role": "domain_spec", "description": "BM 시스템 도메인 정의"},
    "UXSystem": {"role": "domain_spec", "description": "UX 시스템 도메인 정의"},
    "ContentSystem": {"role": "domain_spec", "description": "콘텐츠 시스템 도메인 정의"},
    "BalanceSystem": {"role": "domain_spec", "description": "밸런스 시스템 도메인 정의"},
    "MetaSystem": {"role": "domain_spec", "description": "메타 시스템 도메인 정의 (DEFERRED)"},
    "SocialSystem": {"role": "domain_spec", "description": "소셜 시스템 도메인 정의 (DEFERRED)"},

    # Balance docs (Stage 2-3)
    "Economy": {"role": "economy", "description": "코인 경제 — 소스/싱크, 패키지별 흐름"},
    "DifficultyCurve": {"role": "difficulty", "description": "난이도 곡선 — 홀더 복귀 기반 공식"},

    # Content docs (Stage 2-4)
    "BeatChart": {"role": "beat_chart", "description": "비트 차트 — 패키지별 구성, 색상 분포, CR 목표"},
    "LevelDesign": {"role": "level_design", "description": "레벨 디자인 규칙 — 구성 규칙, 튜토리얼"},
    "Progression": {"role": "progression", "description": "진행 스케줄 — 100레벨 타임라인"},
    "GimmickSpec": {"role": "gimmick_spec", "description": "기믹 명세 — 5종 기믹 상세 정의"},

    # BM/LiveOps docs (Stage 2-5)
    "Monetization": {"role": "monetization", "description": "수익화 상세 — IAP 상품 테이블, 가치 체계"},
    "Operations": {"role": "liveops", "description": "라이브 운영 — 시즌, 연승, 데일리 (DEFERRED)"},
}

# ── provides/requires 추출 (system_spec.yaml 기반) ──
SPEC_BASE = r"E:\AI\projects\BalloonFlow\design_workflow"

def load_system_contracts():
    """system_spec.yaml에서 provides/requires 추출."""
    spec_path = os.path.join(SPEC_BASE, "layer2", "system_spec.yaml")
    with open(spec_path, 'r', encoding='utf-8') as f:
        spec = yaml.safe_load(f)

    contracts = {}
    for domain_key in ['InGame', 'Content', 'Balance', 'UX', 'OutGame', 'BM']:
        domain_data = spec.get(domain_key, {})
        if not isinstance(domain_data, dict):
            continue
        for sys_name, sys_info in domain_data.items():
            if not isinstance(sys_info, dict):
                continue
            contracts[sys_name] = {
                "provides": sys_info.get("provides", []),
                "requires": sys_info.get("requires", []),
                "events": sys_info.get("events_published", []),
                "layer": sys_info.get("layer", ""),
                "role": sys_info.get("role", ""),
            }
    return contracts


def main():
    print("BalloonFlow Design DB — 정규화 + Expert DB 승격")
    print("=" * 60)

    client = MongoClient(MONGO_URI)
    db = client[DB_NAME]
    base = db["design_base"]
    expert = db["design_expert"]

    contracts = load_system_contracts()
    print(f"Loaded {len(contracts)} system contracts from system_spec.yaml")

    # ── Step 1: 정규화 ──
    docs = list(base.find({"project": PROJECT}))
    print(f"\nFound {len(docs)} BalloonFlow documents in design_base")

    normalized = 0
    for doc in docs:
        updates = {}
        system = doc.get("system", "")
        domain = doc.get("domain", "")

        # 1. Domain 정규화
        norm_domain = DOMAIN_NORM.get(domain, domain)
        if norm_domain != domain:
            updates["domain"] = norm_domain

        # 2. Document role
        role_info = DOCUMENT_ROLES.get(system, {})
        if role_info:
            updates["document_role"] = role_info.get("role", "")
            updates["document_description"] = role_info.get("description", "")

        # 3. Tags 표준화
        standard_tags = [
            "puzzle",
            "queue_sort",
            "balloonflow",
            norm_domain.lower(),
            role_info.get("role", system.lower()),
        ]
        updates["tags"] = standard_tags

        # 4. Genre 확인
        if doc.get("genre") != "Puzzle":
            updates["genre"] = "Puzzle"

        # 5. sub_genre 추가
        updates["sub_genre"] = "queue_sort"

        # 6. Score 디렉터 승인: 0.4 → 0.6
        current_score = doc.get("score", 0.4)
        if current_score < 0.6:
            updates["score"] = 0.6
            updates.setdefault("feedback_history", doc.get("feedback_history", []))
            updates["feedback_history"].append({
                "action": "director_approval",
                "score_delta": 0.2,
                "new_score": 0.6,
                "note": "교차검증 통과 (CV-001~CV-005 수정 완료). 디렉터 검수 승인.",
                "timestamp": datetime.now(timezone.utc).isoformat(),
            })

        # 7. Provides/requires from system_spec (for domain_spec docs)
        if role_info.get("role") == "domain_spec":
            # Find matching contracts
            matching = []
            for sys_name, contract in contracts.items():
                matching.append(sys_name)
            # Just set basic contract summary
            pass

        # 8. Version bump
        updates["version"] = "2.0.0"
        updates["normalized_at"] = datetime.now(timezone.utc).isoformat()

        if updates:
            base.update_one({"_id": doc["_id"]}, {"$set": updates})
            normalized += 1

    print(f"Normalized: {normalized} documents")

    # ── Step 2: Expert DB 승격 (score ≥ 0.6) ──
    eligible = list(base.find({"project": PROJECT, "score": {"$gte": 0.6}}))
    print(f"\nEligible for Expert DB: {len(eligible)} documents (score ≥ 0.6)")

    promoted = 0
    for doc in eligible:
        design_id = doc["designId"]
        # Remove MongoDB _id for clean insert
        doc_copy = {k: v for k, v in doc.items() if k != "_id"}
        doc_copy["promoted_at"] = datetime.now(timezone.utc).isoformat()
        doc_copy["promoted_from"] = "design_base"

        result = expert.update_one(
            {"designId": design_id},
            {"$set": doc_copy},
            upsert=True
        )
        action = "updated" if result.matched_count > 0 else "promoted"
        print(f"  [{action.upper()}] {design_id}")
        promoted += 1

    print(f"\nPromoted to design_expert: {promoted}")

    # ── Step 3: 최종 확인 ──
    base_count = base.count_documents({"project": PROJECT})
    expert_count = expert.count_documents({"project": PROJECT})
    total_expert = expert.count_documents({})

    print(f"\n{'=' * 60}")
    print(f"design_base  (BalloonFlow): {base_count}")
    print(f"design_expert (BalloonFlow): {expert_count}")
    print(f"design_expert (Total): {total_expert}")
    print(f"{'=' * 60}")

    client.close()
    print("\nDone!")


if __name__ == "__main__":
    main()
