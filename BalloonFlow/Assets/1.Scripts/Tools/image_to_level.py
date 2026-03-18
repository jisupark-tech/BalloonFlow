"""
BalloonFlow — Image → Level Conversion Pipeline
8-step pipeline: image → parse → color map → parameters → gimmick → queue → validate → export

Usage:
    python image_to_level.py --image cat.png --level-id 42 [--cols 15] [--rows 20] [--output levels/]
    python image_to_level.py --batch images/ --start-level 1 --output levels/

Dependencies:
    pip install Pillow numpy scikit-learn
"""

import argparse
import json
import math
import os
import sys
from pathlib import Path
from typing import Optional

import numpy as np
from PIL import Image

# ─────────────────────────────────────────────
# 28-Color Palette (BalloonFlow project palette)
# ─────────────────────────────────────────────
PALETTE_28 = [
    (255, 0, 0),       # 1  Red
    (0, 128, 0),       # 2  Green
    (0, 0, 255),       # 3  Blue
    (255, 255, 0),     # 4  Yellow
    (255, 165, 0),     # 5  Orange
    (128, 0, 128),     # 6  Purple
    (255, 192, 203),   # 7  Pink
    (0, 255, 255),     # 8  Cyan
    (165, 42, 42),     # 9  Brown
    (128, 128, 128),   # 10 Gray
    (0, 255, 0),       # 11 Lime
    (0, 0, 128),       # 12 Navy
    (255, 127, 80),    # 13 Coral
    (64, 224, 208),    # 14 Turquoise
    (255, 0, 255),     # 15 Magenta
    (75, 0, 130),      # 16 Indigo
    (240, 230, 140),   # 17 Khaki
    (250, 128, 114),   # 18 Salmon
    (0, 128, 128),     # 19 Teal
    (245, 222, 179),   # 20 Wheat
    (220, 20, 60),     # 21 Crimson
    (144, 238, 144),   # 22 LightGreen
    (100, 149, 237),   # 23 CornflowerBlue
    (255, 218, 185),   # 24 PeachPuff
    (46, 139, 87),     # 25 SeaGreen
    (218, 112, 214),   # 26 Orchid
    (210, 105, 30),    # 27 Chocolate
    (70, 130, 180),    # 28 SteelBlue
]

# Gimmick unlock levels (from GimmickManager.cs 2026-03-17)
GIMMICK_UNLOCK = {
    "hidden": 11,
    "chain": 21,
    "pinata": 31,
    "spawner_t": 41,
    "pin": 61,
    "lock_key": 81,
    "surprise": 101,
    "wall": 121,
    "spawner_o": 141,
    "pinata_box": 161,
    "ice": 201,
    "frozen_dart": 241,
    "color_curtain": 281,
}

# Rail capacity tiers
CAPACITY_TIERS = [50, 100, 150, 200]
CAPACITY_DART_THRESHOLDS = [30, 60, 100, float("inf")]


# ═══════════════════════════════════════════════
# STEP 1: Image Preparation
# ═══════════════════════════════════════════════

def step1_prepare(image_path: str, target_cols: int, target_rows: int,
                  max_colors: int = 11) -> np.ndarray:
    """Load image and preprocess. Returns RGBA ndarray (rows x cols x 4)."""
    img = Image.open(image_path).convert("RGBA")
    w, h = img.size

    is_pixel_art = (w <= target_cols * 2 and h <= target_rows * 2)

    if is_pixel_art and w == target_cols and h == target_rows:
        print(f"  [STEP 1A] Pixel art detected ({w}x{h}), skipping preprocessing")
        return np.array(img)

    # STEP 1-B: General image preprocessing
    print(f"  [STEP 1B] Resizing {w}x{h} → {target_cols}x{target_rows}")
    img_resized = img.resize((target_cols, target_rows), Image.Resampling.NEAREST)

    # Color quantization
    arr = np.array(img_resized)
    opaque_mask = arr[:, :, 3] > 128
    opaque_pixels = arr[opaque_mask][:, :3]

    if len(opaque_pixels) == 0:
        print("  [WARN] No opaque pixels found")
        return np.array(img_resized)

    unique_colors = np.unique(opaque_pixels.reshape(-1, 3), axis=0)
    num_unique = len(unique_colors)
    print(f"  [STEP 1B] {num_unique} unique colors found")

    if num_unique > max_colors:
        print(f"  [STEP 1B] Quantizing {num_unique} → {max_colors} colors (K-Means)")
        from sklearn.cluster import KMeans
        kmeans = KMeans(n_clusters=max_colors, random_state=42, n_init=10)
        kmeans.fit(opaque_pixels)
        centers = kmeans.cluster_centers_.astype(np.uint8)
        labels = kmeans.predict(opaque_pixels)

        # Replace colors
        idx = 0
        for r in range(target_rows):
            for c in range(target_cols):
                if arr[r, c, 3] > 128:
                    arr[r, c, :3] = centers[labels[idx]]
                    idx += 1

    # Noise cleanup: isolated 1-pixel clusters → merge with neighbors
    arr = _cleanup_noise(arr)

    return arr


def _cleanup_noise(arr: np.ndarray) -> np.ndarray:
    """Remove 1-pixel isolated color clusters by merging with nearest neighbor."""
    rows, cols = arr.shape[:2]
    for r in range(rows):
        for c in range(cols):
            if arr[r, c, 3] <= 128:
                continue
            color = tuple(arr[r, c, :3])
            has_neighbor = False
            for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
                nr, nc = r + dr, c + dc
                if 0 <= nr < rows and 0 <= nc < cols and arr[nr, nc, 3] > 128:
                    if tuple(arr[nr, nc, :3]) == color:
                        has_neighbor = True
                        break
            if not has_neighbor:
                # Merge with most common neighbor
                neighbor_colors = []
                for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
                    nr, nc = r + dr, c + dc
                    if 0 <= nr < rows and 0 <= nc < cols and arr[nr, nc, 3] > 128:
                        neighbor_colors.append(tuple(arr[nr, nc, :3]))
                if neighbor_colors:
                    from collections import Counter
                    most_common = Counter(neighbor_colors).most_common(1)[0][0]
                    arr[r, c, :3] = most_common
    return arr


# ═══════════════════════════════════════════════
# STEP 2: Image Parsing (pixel → grid)
# ═══════════════════════════════════════════════

def step2_parse(arr: np.ndarray) -> dict:
    """Parse RGBA array into grid with pixel data."""
    rows, cols = arr.shape[:2]
    pixels = []
    for r in range(rows):
        for c in range(cols):
            if arr[r, c, 3] <= 128:
                pixels.append({"row": r, "col": c, "rgb": "EMPTY"})
            else:
                rgb = "#{:02X}{:02X}{:02X}".format(*arr[r, c, :3])
                pixels.append({"row": r, "col": c, "rgb": rgb})

    print(f"  [STEP 2] Parsed {rows}x{cols} grid, "
          f"{sum(1 for p in pixels if p['rgb'] != 'EMPTY')} balloons, "
          f"{sum(1 for p in pixels if p['rgb'] == 'EMPTY')} empty")

    return {"field_rows": rows, "field_columns": cols, "pixels": pixels}


# ═══════════════════════════════════════════════
# STEP 3: Color Mapping (RGB → palette 28)
# ═══════════════════════════════════════════════

def step3_color_map(parsed: dict) -> dict:
    """Map pixel RGB to nearest palette color. Returns field grid + color_list."""
    rows = parsed["field_rows"]
    cols = parsed["field_columns"]
    field = [[0] * cols for _ in range(rows)]
    palette_np = np.array(PALETTE_28, dtype=np.float64)
    used_ids = set()

    for px in parsed["pixels"]:
        r, c = px["row"], px["col"]
        if px["rgb"] == "EMPTY":
            field[r][c] = 0
            continue
        rgb = _hex_to_rgb(px["rgb"])
        pid = _nearest_palette(rgb, palette_np)
        field[r][c] = pid
        used_ids.add(pid)

    color_list = sorted(used_ids)
    num_colors = len(color_list)

    if num_colors > 11:
        print(f"  [STEP 3] ERROR: {num_colors} colors > 11 max. Need color reduction.")
        sys.exit(1)

    print(f"  [STEP 3] Mapped to {num_colors} palette colors: {color_list}")
    return {"num_colors": num_colors, "color_list": color_list, "field": field,
            "field_rows": rows, "field_columns": cols}


def _hex_to_rgb(hex_str: str) -> tuple:
    h = hex_str.lstrip("#")
    return tuple(int(h[i:i+2], 16) for i in (0, 2, 4))


def _nearest_palette(rgb: tuple, palette_np: np.ndarray) -> int:
    """Find nearest palette color by Euclidean distance. Lower ID wins ties."""
    dists = np.sqrt(np.sum((palette_np - np.array(rgb, dtype=np.float64)) ** 2, axis=1))
    return int(np.argmin(dists)) + 1  # palette IDs are 1-based


# ═══════════════════════════════════════════════
# STEP 4: Level Parameter Mapping
# ═══════════════════════════════════════════════

def step4_parameters(color_data: dict, level_id: int) -> dict:
    """Apply Beat Chart rules to determine level parameters."""
    pkg = (level_id - 1) // 20 + 1
    pos = (level_id - 1) % 20 + 1

    # Determine purpose_type from position rules
    purpose_type = _determine_purpose(level_id, pos)

    # Queue columns: tutorial=2, rest=3, normal=3-4, hard/superhard=3-5
    queue_columns = _determine_queue_cols(purpose_type, pkg)

    # Dart capacity range based on purpose
    dart_cap = _dart_capacity_range(purpose_type)

    # Target clear rate
    target_cr = _target_cr(purpose_type)

    params = {
        "level_id": level_id,
        "pkg": pkg,
        "pos": pos,
        "purpose_type": purpose_type,
        "num_colors": color_data["num_colors"],
        "color_list": color_data["color_list"],
        "field_rows": color_data["field_rows"],
        "field_columns": color_data["field_columns"],
        "field": color_data["field"],
        "queue_columns": queue_columns,
        "dart_capacity_range": dart_cap,
        "target_cr": target_cr,
    }
    print(f"  [STEP 4] Lv.{level_id} PKG{pkg}/pos{pos} type={purpose_type} "
          f"queue={queue_columns} cr={target_cr}%")
    return params


def _determine_purpose(level_id: int, pos: int) -> str:
    # Gimmick intro levels = tutorial
    for gimmick, unlock_lv in GIMMICK_UNLOCK.items():
        if level_id == unlock_lv:
            return "tutorial"
    if pos in (5, 10, 15, 20):
        return "rest"
    if pos in (4, 14):
        return "hard"
    if pos in (9, 19):
        return "superhard"
    return "normal"


def _determine_queue_cols(purpose: str, pkg: int) -> int:
    if purpose == "tutorial":
        return 2  # Hard Rule
    if purpose == "rest":
        return 3
    # Scale with package progression
    if pkg <= 3:
        return 3
    if pkg <= 8:
        return 4 if purpose in ("hard", "superhard") else 3
    return 5 if purpose == "superhard" else 4


def _dart_capacity_range(purpose: str) -> dict:
    ranges = {
        "tutorial": {"min": 5, "max": 15},
        "rest": {"min": 10, "max": 20},
        "normal": {"min": 10, "max": 30},
        "hard": {"min": 5, "max": 15},
        "superhard": {"min": 5, "max": 10},
    }
    return ranges.get(purpose, {"min": 10, "max": 30})


def _target_cr(purpose: str) -> int:
    """Target clear rate percentage."""
    cr_map = {
        "tutorial": 92,
        "rest": 90,
        "normal": 60,
        "hard": 40,
        "superhard": 25,
    }
    return cr_map.get(purpose, 60)


# ═══════════════════════════════════════════════
# STEP 5: Gimmick Overlay
# ═══════════════════════════════════════════════

def step5_gimmick_overlay(params: dict) -> dict:
    """Place gimmicks based on level rules. Returns gimmick data."""
    level_id = params["level_id"]
    purpose = params["purpose_type"]
    field = params["field"]
    rows = params["field_rows"]
    cols = params["field_columns"]

    # Available gimmicks for this level
    available = [g for g, lv in GIMMICK_UNLOCK.items() if level_id >= lv]

    # Tutorial level → only the newly introduced gimmick
    if purpose == "tutorial":
        new_gimmick = None
        for g, lv in GIMMICK_UNLOCK.items():
            if lv == level_id:
                new_gimmick = g
                break
        if new_gimmick:
            available = [new_gimmick]

    # Number of gimmick types based on purpose (max 5, Hard Rule)
    max_gimmick_types = min(5, len(available))
    if purpose == "tutorial":
        num_types = 1
    elif purpose == "rest":
        num_types = min(1, max_gimmick_types)
    elif purpose == "normal":
        num_types = min(2, max_gimmick_types)
    elif purpose == "hard":
        num_types = min(3, max_gimmick_types)
    elif purpose == "superhard":
        num_types = min(4, max_gimmick_types)
    else:
        num_types = min(2, max_gimmick_types)

    # Select gimmicks (prioritize recently unlocked)
    selected = available[-num_types:] if num_types > 0 else []

    # Separate field vs queue gimmicks
    field_gimmick_types = {"pinata", "pin", "surprise", "wall", "pinata_box", "ice", "color_curtain", "lock_key", "frozen_dart"}
    queue_gimmick_types = {"hidden", "chain", "spawner_t", "spawner_o"}

    field_gimmicks = []
    queue_gimmicks = []
    spawners = []

    # Collect non-empty positions for field gimmick placement
    balloon_positions = []
    for r in range(rows):
        for c in range(cols):
            if field[r][c] != 0:
                balloon_positions.append((r, c, field[r][c]))

    empty_positions = []
    for r in range(rows):
        for c in range(cols):
            if field[r][c] == 0:
                empty_positions.append((r, c))

    rng = np.random.RandomState(level_id)  # deterministic per level

    for gimmick in selected:
        if gimmick in field_gimmick_types and balloon_positions:
            fg = _place_field_gimmick(gimmick, balloon_positions, empty_positions, field, rng)
            if fg:
                if gimmick.startswith("spawner"):
                    spawners.extend(fg)
                else:
                    field_gimmicks.extend(fg)
        elif gimmick in queue_gimmick_types:
            queue_gimmicks.append(gimmick)

    gimmick_data = {
        "field_gimmicks": field_gimmicks,
        "spawners": spawners,
        "queue_gimmick_types": queue_gimmicks,
        "active_types": selected,
    }

    print(f"  [STEP 5] Gimmicks: {selected} "
          f"(field={len(field_gimmicks)}, spawners={len(spawners)}, queue={queue_gimmicks})")

    params["gimmicks"] = gimmick_data
    return params


def _place_field_gimmick(gtype: str, balloon_pos: list, empty_pos: list,
                          field: list, rng) -> list:
    """Place 1-3 instances of a field gimmick. Returns list of placement dicts."""
    results = []
    count = rng.randint(1, 4)  # 1-3 instances

    if gtype == "wall" and empty_pos:
        for _ in range(min(count, len(empty_pos))):
            idx = rng.randint(0, len(empty_pos))
            r, c = empty_pos[idx]
            results.append({"type": "wall", "row": r, "col": c, "size": "1x1"})

    elif gtype == "pinata" and balloon_pos:
        for _ in range(min(count, len(balloon_pos))):
            idx = rng.randint(0, len(balloon_pos))
            r, c, color = balloon_pos[idx]
            results.append({"type": "pinata", "row": r, "col": c,
                            "color": color, "life": rng.randint(3, 8)})

    elif gtype == "surprise" and balloon_pos:
        for _ in range(min(count, len(balloon_pos))):
            idx = rng.randint(0, len(balloon_pos))
            r, c, _ = balloon_pos[idx]
            results.append({"type": "surprise_balloon", "row": r, "col": c})

    elif gtype == "ice" and balloon_pos:
        for _ in range(min(count, len(balloon_pos))):
            idx = rng.randint(0, len(balloon_pos))
            r, c, _ = balloon_pos[idx]
            results.append({"type": "ice", "row": r, "col": c,
                            "life": rng.randint(1, 4)})

    elif gtype == "pin" and balloon_pos:
        idx = rng.randint(0, len(balloon_pos))
        r, c, color = balloon_pos[idx]
        length = rng.randint(2, 5)
        results.append({"type": "pin", "row": r, "col": c,
                         "color": color, "length": length})

    elif gtype == "pinata_box" and balloon_pos:
        idx = rng.randint(0, len(balloon_pos))
        r, c, _ = balloon_pos[idx]
        results.append({"type": "pinata_box", "row": r, "col": c,
                         "size": "2x2", "life": rng.randint(5, 12)})

    elif gtype == "color_curtain" and balloon_pos:
        idx = rng.randint(0, len(balloon_pos))
        r, c, color = balloon_pos[idx]
        results.append({"type": "color_curtain", "row": r, "col": c,
                         "color": color, "counter": rng.randint(2, 6)})

    elif gtype == "lock_key" and balloon_pos:
        idx = rng.randint(0, len(balloon_pos))
        r, c, color = balloon_pos[idx]
        results.append({"type": "lock_key", "row": r, "col": c, "color": color})

    elif gtype in ("spawner_t", "spawner_o") and empty_pos:
        idx = rng.randint(0, len(empty_pos))
        r, c = empty_pos[idx]
        transparent = (gtype == "spawner_t")
        directions = ["north", "south", "east", "west"]
        results.append({"row": r, "col": c,
                         "direction": directions[rng.randint(0, 4)],
                         "counter": rng.randint(3, 8),
                         "transparent": transparent})

    return results


# ═══════════════════════════════════════════════
# STEP 6: Queue Generation
# ═══════════════════════════════════════════════

def step6_queue(params: dict) -> dict:
    """Generate holder queue based on field balloons + difficulty."""
    field = params["field"]
    rows = params["field_rows"]
    cols = params["field_columns"]
    purpose = params["purpose_type"]
    dart_range = params["dart_capacity_range"]
    gimmicks = params.get("gimmicks", {})

    # Count balloons per color
    color_counts = {}
    for r in range(rows):
        for c in range(cols):
            cid = field[r][c]
            if cid > 0:
                color_counts[cid] = color_counts.get(cid, 0) + 1

    # Add pinata life to dart requirements
    for fg in gimmicks.get("field_gimmicks", []):
        if fg["type"] == "pinata" and "color" in fg:
            color_counts[fg["color"]] = color_counts.get(fg["color"], 0) + fg.get("life", 3)
        if fg["type"] == "pin" and "color" in fg:
            color_counts[fg["color"]] = color_counts.get(fg["color"], 0) + fg.get("length", 2)

    total_needed = sum(color_counts.values())

    # Surplus ratio based on difficulty (higher = easier)
    surplus_map = {
        "tutorial": 1.50,
        "rest": 1.40,
        "normal": 1.20,
        "hard": 1.08,
        "superhard": 1.03,
    }
    surplus = surplus_map.get(purpose, 1.20)
    total_darts = max(total_needed, math.ceil(total_needed * surplus))

    # Generate holders: split into magazine sizes (5-unit steps, 5-50 range)
    queue = []
    holder_id = 0
    queue_gimmick_types = gimmicks.get("queue_gimmick_types", [])
    rng = np.random.RandomState(params["level_id"] + 1000)

    for cid, needed in sorted(color_counts.items()):
        # Allocate darts for this color with surplus
        color_darts = math.ceil(needed * surplus)
        color_darts = max(5, math.ceil(color_darts / 5) * 5)  # round UP to 5-unit

        remaining = color_darts
        while remaining > 0:
            mag = min(remaining, _pick_magazine_size(purpose, rng, dart_range))
            mag = max(5, (mag // 5) * 5)
            if mag > 50:
                mag = 50
            if mag == 0:
                mag = 5
            remaining -= mag

            # Apply queue gimmick randomly
            gimmick = None
            if queue_gimmick_types and rng.random() < 0.25:
                gimmick = queue_gimmick_types[rng.randint(0, len(queue_gimmick_types))]

            queue.append({
                "color": cid,
                "darts": mag,
                "gimmick": gimmick,
            })
            holder_id += 1

    # Shuffle queue for difficulty (better shuffle = harder to optimize)
    if purpose in ("hard", "superhard"):
        rng.shuffle(queue)
    else:
        # Partial shuffle: group by color but randomize within
        rng.shuffle(queue)

    # Calculate rail capacity
    total_queue_darts = sum(h["darts"] for h in queue)
    rail_capacity = _calc_rail_capacity(total_queue_darts)

    print(f"  [STEP 6] Queue: {len(queue)} holders, {total_queue_darts} total darts "
          f"(needed={total_needed}), rail_capacity={rail_capacity}")

    params["queue"] = queue
    params["rail_capacity"] = rail_capacity
    params["total_darts"] = total_queue_darts
    params["total_needed"] = total_needed
    return params


def _pick_magazine_size(purpose: str, rng, dart_range: dict) -> int:
    """Pick magazine size based on difficulty."""
    lo = max(5, dart_range["min"])
    hi = min(50, dart_range["max"])
    return rng.randint(lo // 5, hi // 5 + 1) * 5


def _calc_rail_capacity(total_darts: int) -> int:
    """Match RailManager.CalculateCapacity logic."""
    for i, threshold in enumerate(CAPACITY_DART_THRESHOLDS):
        if total_darts <= threshold:
            return CAPACITY_TIERS[i]
    return CAPACITY_TIERS[-1]


# ═══════════════════════════════════════════════
# STEP 7: Validation
# ═══════════════════════════════════════════════

def step7_validate(params: dict) -> dict:
    """Run Hard Rule + Soft Rule + Balance checks."""
    errors = []
    warnings = []
    level_id = params["level_id"]

    # ── Hard Rules ──
    qc = params["queue_columns"]
    if qc < 2 or qc > 5:
        errors.append(f"queue_columns={qc} out of range [2,5]")

    nc = params["num_colors"]
    if nc < 2 or nc > 11:
        errors.append(f"num_colors={nc} out of range [2,11]")

    active = params.get("gimmicks", {}).get("active_types", [])
    if len(active) > 5:
        errors.append(f"gimmick types={len(active)} exceeds max 5")

    # Check gimmick unlock levels
    for g in active:
        unlock = GIMMICK_UNLOCK.get(g, 0)
        if level_id < unlock:
            errors.append(f"gimmick '{g}' used at Lv.{level_id} but unlocks at Lv.{unlock}")

    # Tutorial levels must have queue=2
    if params["purpose_type"] == "tutorial" and qc != 2:
        errors.append(f"tutorial level must have queue_columns=2, got {qc}")

    # Magazine sizes must be 5-50 in 5-unit steps
    for i, h in enumerate(params.get("queue", [])):
        d = h["darts"]
        if d < 5 or d > 50 or d % 5 != 0:
            errors.append(f"holder[{i}] darts={d} invalid (must be 5-50 in 5-unit steps)")

    # Spawner transparency consistency
    spawners = params.get("gimmicks", {}).get("spawners", [])
    if spawners:
        transp_vals = set(s["transparent"] for s in spawners)
        if len(transp_vals) > 1:
            errors.append("mixed transparent/opaque spawners in same level")

    # Chain validation (if present in queue gimmicks)
    # Chain link count 2-4 only (no 5+)
    # (Chain details would need more data; basic check here)

    # ── Soft Rules ──
    # These would ideally check across multiple levels in batch mode
    # For single-level, we note recommendations

    if params["purpose_type"] == "superhard" and params["target_cr"] > 50:
        warnings.append(f"superhard with CR={params['target_cr']}% seems too easy")

    if params["purpose_type"] == "rest" and params["target_cr"] < 80:
        warnings.append(f"rest level with CR={params['target_cr']}% seems too hard")

    # ── Balance Check ──
    total_darts = params.get("total_darts", 0)
    total_needed = params.get("total_needed", 0)
    if total_needed > 0:
        ratio = total_darts / total_needed
        if ratio < 1.0:
            errors.append(f"dart ratio={ratio:.2f} < 1.0 - level is impossible to clear")
        elif ratio > 2.0:
            warnings.append(f"dart ratio={ratio:.2f} > 2.0 - level may be too easy")

    # Report
    status = "PASS" if not errors else "FAIL"
    print(f"  [STEP 7] Validation: {status} "
          f"({len(errors)} errors, {len(warnings)} warnings)")
    for e in errors:
        print(f"    ERROR: {e}")
    for w in warnings:
        print(f"    WARN:  {w}")

    params["validation"] = {"status": status, "errors": errors, "warnings": warnings}
    return params


# ═══════════════════════════════════════════════
# STEP 8: Level Library Export
# ═══════════════════════════════════════════════

def step8_export(params: dict, output_dir: str, source_path: str) -> str:
    """Export final level JSON compatible with LevelConfig."""
    gimmicks = params.get("gimmicks", {})

    level_json = {
        "level_id": params["level_id"],
        "pkg": params["pkg"],
        "pos": params["pos"],
        "purpose_type": params["purpose_type"],
        "num_colors": params["num_colors"],
        "color_list": params["color_list"],
        "field_rows": params["field_rows"],
        "field_columns": params["field_columns"],
        "field": params["field"],
        "queue_columns": params["queue_columns"],
        "queue": params.get("queue", []),
        "gimmicks": {
            "field_gimmicks": gimmicks.get("field_gimmicks", []),
            "spawners": gimmicks.get("spawners", []),
        },
        "dart_capacity_range": params["dart_capacity_range"],
        "target_cr": params["target_cr"],
        "rail_capacity": params.get("rail_capacity", 0),
        "pixel_art_source": os.path.basename(source_path),
        "validation": params.get("validation", {}),
    }

    os.makedirs(output_dir, exist_ok=True)
    filename = f"level_{params['level_id']:03d}.json"
    filepath = os.path.join(output_dir, filename)
    with open(filepath, "w", encoding="utf-8") as f:
        json.dump(level_json, f, indent=2, ensure_ascii=False)

    print(f"  [STEP 8] Exported -> {filepath}")
    return filepath


# ═══════════════════════════════════════════════
# Pipeline Orchestrator
# ═══════════════════════════════════════════════

def run_pipeline(image_path: str, level_id: int, target_cols: int = 15,
                 target_rows: int = 20, output_dir: str = "levels",
                 max_colors: int = 11) -> Optional[str]:
    """Run full 8-step pipeline for a single image."""
    print(f"\n{'='*60}")
    print(f"  BalloonFlow Image->Level Pipeline - Lv.{level_id}")
    print(f"  Input: {image_path}")
    print(f"{'='*60}")

    # STEP 1
    arr = step1_prepare(image_path, target_cols, target_rows, max_colors)

    # STEP 2
    parsed = step2_parse(arr)

    # STEP 3
    color_data = step3_color_map(parsed)

    # STEP 4
    params = step4_parameters(color_data, level_id)

    # STEP 5
    params = step5_gimmick_overlay(params)

    # STEP 6
    params = step6_queue(params)

    # STEP 7
    params = step7_validate(params)

    if params["validation"]["status"] == "FAIL":
        print(f"\n  *** VALIDATION FAILED - Level NOT exported ***")
        print(f"  Fix errors and re-run.")
        return None

    # STEP 8
    filepath = step8_export(params, output_dir, image_path)

    print(f"\n  DONE - Lv.{level_id} exported successfully\n")
    return filepath


def run_batch(image_dir: str, start_level: int, target_cols: int = 15,
              target_rows: int = 20, output_dir: str = "levels",
              max_colors: int = 11) -> list:
    """Run pipeline for all images in a directory."""
    image_extensions = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}
    images = sorted([
        f for f in Path(image_dir).iterdir()
        if f.suffix.lower() in image_extensions
    ])

    if not images:
        print(f"No images found in {image_dir}")
        return []

    print(f"\nBatch mode: {len(images)} images, starting at Lv.{start_level}")
    results = []
    batch_errors = []
    batch_warnings = []

    for i, img_path in enumerate(images):
        level_id = start_level + i
        filepath = run_pipeline(str(img_path), level_id, target_cols, target_rows,
                                output_dir, max_colors)
        if filepath:
            results.append(filepath)

    # Batch soft rule checks (cross-level)
    print(f"\n{'='*60}")
    print(f"  Batch Summary: {len(results)}/{len(images)} levels exported")
    if batch_errors:
        print(f"  Batch errors: {len(batch_errors)}")
    print(f"{'='*60}\n")

    return results


# ═══════════════════════════════════════════════
# CLI
# ═══════════════════════════════════════════════

def main():
    parser = argparse.ArgumentParser(
        description="BalloonFlow Image → Level Conversion Pipeline")
    parser.add_argument("--image", help="Single image path")
    parser.add_argument("--batch", help="Directory of images for batch conversion")
    parser.add_argument("--level-id", type=int, default=1, help="Level ID (single mode)")
    parser.add_argument("--start-level", type=int, default=1, help="Starting level ID (batch)")
    parser.add_argument("--cols", type=int, default=15, help="Target grid columns")
    parser.add_argument("--rows", type=int, default=20, help="Target grid rows")
    parser.add_argument("--max-colors", type=int, default=11, help="Max colors after quantization")
    parser.add_argument("--output", default="levels", help="Output directory")
    args = parser.parse_args()

    if args.image:
        run_pipeline(args.image, args.level_id, args.cols, args.rows,
                     args.output, args.max_colors)
    elif args.batch:
        run_batch(args.batch, args.start_level, args.cols, args.rows,
                  args.output, args.max_colors)
    else:
        parser.print_help()
        print("\nExamples:")
        print("  python image_to_level.py --image cat.png --level-id 42")
        print("  python image_to_level.py --batch assets/pixel_art/ --start-level 1 --output levels/")


if __name__ == "__main__":
    main()
