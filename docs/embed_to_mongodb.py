"""
BalloonFlow — Design DB Embedding Script
Parses all design YAML files and stores them in MongoDB design_base collection
as a reference example for future puzzle game projects.
"""
import os, sys, yaml, hashlib
from datetime import datetime

sys.stdout = __import__('io').TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

from pymongo import MongoClient

# ─── Config ───
MONGO_URI = "mongodb+srv://jisupark_db_user:MK6mD10A2ccO7lsl@db.qwzflco.mongodb.net/?retryWrites=true&w=majority&appName=DB"
DB_NAME = "aigame"
COLLECTION = "design_base"
BASE = r"E:\AI\projects\BalloonFlow\design_workflow"
PROJECT = "BalloonFlow"
GENRE = "Puzzle"

# ─── YAML Files to embed ───
YAML_FILES = [
    # (file_path, domain, system_name, data_type)
    ("concept.yaml", "InGame", "ConceptDoc", "config"),
    ("layer1/game_design.yaml", "InGame", "GameDesignDoc", "config"),
    ("layer2/system_spec.yaml", "InGame", "SystemSpec", "spec"),
    ("layer2/build_order.yaml", "InGame", "BuildOrder", "flow"),
    ("systems/ingame.yaml", "InGame", "InGameSystem", "spec"),
    ("systems/outgame.yaml", "OutGame", "OutGameSystem", "spec"),
    ("systems/bm.yaml", "BM", "BMSystem", "spec"),
    ("systems/ux.yaml", "UX", "UXSystem", "spec"),
    ("systems/content.yaml", "Content", "ContentSystem", "spec"),
    ("systems/balance.yaml", "Balance", "BalanceSystem", "spec"),
    ("systems/meta.yaml", "Meta", "MetaSystem", "spec"),
    ("systems/social.yaml", "Social", "SocialSystem", "spec"),
    ("balance/economy.yaml", "Balance", "Economy", "formula"),
    ("balance/difficulty_curve.yaml", "Balance", "DifficultyCurve", "formula"),
    ("content/beat_chart.yaml", "Content", "BeatChart", "content_data"),
    ("content/level_design.yaml", "Content", "LevelDesign", "rule"),
    ("content/progression.yaml", "Content", "Progression", "content_data"),
    ("content/gimmick_spec.yaml", "Content", "GimmickSpec", "spec"),
    ("bm/monetization.yaml", "BM", "Monetization", "table"),
    ("liveops/operations.yaml", "LiveOps", "Operations", "config"),
]


def load_yaml(path):
    full = os.path.join(BASE, path)
    if not os.path.exists(full):
        return None
    with open(full, 'r', encoding='utf-8') as f:
        return yaml.safe_load(f)


def make_design_id(project, domain, system):
    return f"{project}_{domain}_{system}".lower().replace(" ", "_")


def extract_summary(data, filepath):
    """Extract a meaningful summary from YAML data."""
    if isinstance(data, dict):
        # Try common summary fields
        for key in ['summary', 'description', 'one_liner', 'monetization_model']:
            if key in data:
                val = data[key]
                if isinstance(val, str):
                    return val.strip()[:500]
        # Try nested
        for key in ['core_fun', 'vision']:
            if key in data and isinstance(data[key], dict):
                for subkey in ['one_liner', 'description']:
                    if subkey in data[key]:
                        return str(data[key][subkey]).strip()[:500]
    return f"Design document: {os.path.basename(filepath)}"


def extract_systems_list(data):
    """Extract system names from various YAML structures."""
    systems = []
    if isinstance(data, dict):
        # Direct systems list
        if 'systems' in data:
            val = data['systems']
            if isinstance(val, list):
                systems.extend([str(s) for s in val if isinstance(s, str)])
            elif isinstance(val, dict):
                for domain_name, info in val.items():
                    if isinstance(info, dict) and 'systems' in info:
                        systems.extend([str(s) for s in info['systems'] if isinstance(s, str)])
        # confirmed_systems
        if 'confirmed_systems' in data and isinstance(data['confirmed_systems'], list):
            systems.extend([str(s) for s in data['confirmed_systems'] if isinstance(s, str)])
    return systems


def sanitize_keys(obj):
    """Recursively convert all dict keys to strings (MongoDB requires string keys)."""
    if isinstance(obj, dict):
        return {str(k): sanitize_keys(v) for k, v in obj.items()}
    elif isinstance(obj, list):
        return [sanitize_keys(item) for item in obj]
    return obj


def build_document(filepath, domain, system, data_type, data):
    design_id = make_design_id(PROJECT, domain, system)
    summary = extract_summary(data, filepath)
    systems = extract_systems_list(data)
    data = sanitize_keys(data)  # Ensure all keys are strings

    return {
        "designId": design_id,
        "project": PROJECT,
        "domain": domain,
        "genre": GENRE,
        "system": system,
        "source": "internal_produced",
        "version": "2.0.0",
        "score": 0.4,
        "data_type": data_type,
        "balance_area": "economy" if domain == "Balance" else None,
        "source_file": filepath,
        "content": {
            "summary": summary,
            "raw_yaml": data,  # Store full YAML content
            "systems_list": systems,
        },
        "design_analysis": {
            "design_intent": f"BalloonFlow {domain} design - reference example for puzzle games",
            "context": "Queue sort puzzle game with circular rail mechanic, holder overflow failure",
            "strengths": [
                "Complete 100-level design with 5 packages",
                "Circular rail mechanic with holder return tension",
                "5 gimmicks with staggered introduction",
                "Escalating continue costs (900/1900/2900)",
            ],
            "concerns": [],
            "db_recommendation": "include",
            "reasoning": "Production-quality puzzle game design, validated through cross-checks",
        },
        "provides": [],
        "requires": [],
        "tags": [f"puzzle", "queue_sort", "balloonflow", domain.lower()],
        "versions": [{
            "version": "2.0.0",
            "phase": "pre_launch",
            "data": f"Round 2 validated design: {system}",
            "note": "Cross-validated, BM/LiveOps deferred",
        }],
        "feedback_history": [],
        "timestamp": datetime.utcnow().isoformat(),
    }


def main():
    print(f"Connecting to MongoDB Atlas ({DB_NAME})...")
    client = MongoClient(MONGO_URI)
    db = client[DB_NAME]
    collection = db[COLLECTION]

    saved = 0
    skipped = 0
    errors = 0

    for filepath, domain, system, data_type in YAML_FILES:
        data = load_yaml(filepath)
        if data is None:
            print(f"  [SKIP] {filepath} — file not found")
            skipped += 1
            continue

        doc = build_document(filepath, domain, system, data_type, data)
        design_id = doc['designId']

        try:
            result = collection.update_one(
                {"designId": design_id},
                {"$set": doc},
                upsert=True
            )
            action = "updated" if result.matched_count > 0 else "inserted"
            print(f"  [{action.upper()}] {design_id} ({domain}/{system})")
            saved += 1
        except Exception as e:
            print(f"  [ERROR] {design_id}: {e}")
            errors += 1

    client.close()
    print(f"\nDone! Saved: {saved}, Skipped: {skipped}, Errors: {errors}")
    print(f"Collection: {DB_NAME}.{COLLECTION}")


if __name__ == "__main__":
    main()
